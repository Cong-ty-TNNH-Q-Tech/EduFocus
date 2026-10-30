#requires -Version 5.1
<#
.SYNOPSIS
Builds the EduFocus MSI from a self-contained publish folder.

.DESCRIPTION
Harvests every file in the publish folder into a generated WiX component
group, rejects secrets and runtime data, then invokes WiX v4 with the UI and
Util extensions. The main executable keeps a fixed component GUID so upgrades
replace the file instead of creating a second install.

.EXAMPLE
./installer/Build-Installer.ps1 -PublishDir publish -Version 2.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputPath,

    [string]$Wix = "wix"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerDir

$Version = $Version.Trim()
if ($Version.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
    $Version = $Version.Substring(1)
}

if ($Version -notmatch '^[0-9]+(\.[0-9]+){1,3}$') {
    throw "Version '$Version' is not a numeric MSI version (expected 1.2.3 or 1.2.3.4)."
}

$publish = [System.IO.Path]::GetFullPath($PublishDir)
if (-not (Test-Path -LiteralPath $publish -PathType Container)) {
    throw "Publish folder '$publish' does not exist."
}

$mainExe = Join-Path $publish "EduFocus.exe"
if (-not (Test-Path -LiteralPath $mainExe -PathType Leaf)) {
    throw "Publish folder '$publish' does not contain EduFocus.exe. Run dotnet publish first."
}

# Product.wxs references this file by a fixed component id for native messaging
# registration custom actions.
$nativeHostExe = Join-Path $publish "native-host\EduFocus.NativeHost.exe"
if (-not (Test-Path -LiteralPath $nativeHostExe -PathType Leaf)) {
    throw "Publish folder '$publish' does not contain native-host\EduFocus.NativeHost.exe. Publish the native host into the payload first."
}

# Product.wxs references the generated service component by a fixed id so the
# MSI owns the Windows service. The name and account must match
# ServiceControlProtocol.ServiceIdentity in EduFocus.Ports.
$serviceExe = Join-Path $publish "EduFocus.Service.exe"
if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
    throw "Publish folder '$publish' does not contain EduFocus.Service.exe. Publish the Windows service into the payload first."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Join-Path $repoRoot "artifacts") ("EduFocus-{0}-win-x64.msi" -f $Version)
}

$outputFull = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFull
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

# Private keys and secret files must never ship, even inside a single file.
$forbiddenExtensions = @(
    '.pfx', '.p12', '.snk', '.key', '.pem', '.cer', '.crt',
    '.jks', '.keystore', '.pvk', '.env'
)
$forbiddenNames = @('secrets.json', 'credentials.json', 'id_rsa', 'id_ed25519')
$scannedExtensions = @(
    '.config', '.json', '.xml', '.txt', '.md', '.yml', '.yaml',
    '.ps1', '.cmd', '.bat', '.ini', '.properties'
)
$privateKeyMarker = '-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----'

# User data never belongs in an installer, even when the publish folder is dirty.
$runtimeExtensions = @('.db', '.db-shm', '.db-wal', '.log', '.bak', '.tmp')

# Build metadata that is not needed at runtime.
$excludedExtensions = @('.pdb', '.lib', '.wixpdb')

function Get-RelativePath([string]$basePath, [string]$fullPath) {
    $prefix = $basePath.TrimEnd([char]'\') + '\'
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "File '$fullPath' is not inside '$basePath'."
    }

    return $fullPath.Substring($prefix.Length)
}

function ConvertTo-XmlText([string]$value) {
    return $value.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
}

$payloadFiles = New-Object System.Collections.Generic.List[object]

foreach ($file in Get-ChildItem -LiteralPath $publish -Recurse -File | Sort-Object FullName) {
    $relative = Get-RelativePath $publish $file.FullName
    $lowerName = $file.Name.ToLowerInvariant()
    $extension = $file.Extension.ToLowerInvariant()

    if (($forbiddenExtensions -contains $extension) -or ($forbiddenNames -contains $lowerName)) {
        throw "Refusing to package '$relative': private keys and secret files must never be in the installer."
    }

    if (($runtimeExtensions -contains $extension) -or ($relative -match '\.db-(shm|wal)$')) {
        throw "Refusing to package '$relative': runtime data must not be published."
    }

    if ($scannedExtensions -contains $extension) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        if (($null -ne $content) -and ($content -match $privateKeyMarker)) {
            throw "Refusing to package '$relative': it contains a PEM private key."
        }
    }

    if ($excludedExtensions -contains $extension) {
        continue
    }

    $payloadFiles.Add([pscustomobject]@{
        FullName = $file.FullName
        Relative = $relative
        Name     = $file.Name
        Length   = $file.Length
    })
}

$mainEntries = @($payloadFiles | Where-Object { $_.Relative -ieq 'EduFocus.exe' })
if ($mainEntries.Count -ne 1) {
    throw "The publish folder must contain exactly one EduFocus.exe at its root."
}

$serviceEntries = @($payloadFiles | Where-Object { $_.Relative -ieq 'EduFocus.Service.exe' })
if ($serviceEntries.Count -ne 1) {
    throw "The publish folder must contain exactly one EduFocus.Service.exe at its root."
}

# The service file is emitted as its own component so ServiceInstall and
# ServiceControl live next to the file in one MSI component.
$serviceFile = $serviceEntries[0]
$payloadFiles.Remove($serviceFile) | Out-Null

$directoryIds = @{ '' = 'INSTALLFOLDER' }
$directoryRecords = New-Object System.Collections.Generic.List[object]
$removeFolderAdded = @{}

function Get-DirectoryId([string]$relativeDirectory) {
    if ([string]::IsNullOrEmpty($relativeDirectory)) {
        return 'INSTALLFOLDER'
    }

    if ($script:directoryIds.ContainsKey($relativeDirectory)) {
        return $script:directoryIds[$relativeDirectory]
    }

    $parentRelative = Split-Path -Parent $relativeDirectory
    if ($null -eq $parentRelative) {
        $parentRelative = ''
    }

    $parentId = Get-DirectoryId $parentRelative
    $id = 'PayloadDir{0:D4}' -f ($script:directoryRecords.Count + 1)

    $script:directoryIds[$relativeDirectory] = $id
    $script:directoryRecords.Add([pscustomobject]@{
        Id       = $id
        Name     = (Split-Path -Leaf $relativeDirectory)
        ParentId = $parentId
    })

    return $id
}

$directoryAssignments = @()
foreach ($file in $payloadFiles) {
    $relativeDirectory = Split-Path -Parent $file.Relative
    if ($null -eq $relativeDirectory) {
        $relativeDirectory = ''
    }

    $directoryAssignments += [pscustomobject]@{
        File   = $file
        DirId  = (Get-DirectoryId $relativeDirectory)
    }
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
[void]$builder.AppendLine('<!-- Generated by installer/Build-Installer.ps1. Do not edit. -->')
[void]$builder.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">')

if ($directoryRecords.Count -gt 0) {
    [void]$builder.AppendLine('  <Fragment>')
    [void]$builder.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')

    function Write-DirectoryChildren([string]$parentId, [int]$indent) {
        $padding = ' ' * $indent
        foreach ($directory in $script:directoryRecords | Where-Object { $_.ParentId -eq $parentId }) {
            $escapedName = ConvertTo-XmlText $directory.Name
            [void]$builder.AppendLine("$padding<Directory Id=`"$($directory.Id)`" Name=`"$escapedName`">")
            Write-DirectoryChildren $directory.Id ($indent + 2)
            [void]$builder.AppendLine("$padding</Directory>")
        }
    }

    Write-DirectoryChildren 'INSTALLFOLDER' 6
    [void]$builder.AppendLine('    </DirectoryRef>')
    [void]$builder.AppendLine('  </Fragment>')
}

[void]$builder.AppendLine('  <Fragment>')
[void]$builder.AppendLine('    <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">')

$componentIndex = 0
foreach ($assignment in $directoryAssignments) {
    $file = $assignment.File
    $componentIndex++

    $componentId = 'PayloadCmp{0:D4}' -f $componentIndex
    $fileId = 'PayloadFile{0:D4}' -f $componentIndex
    $guid = '*'

    if ($file.Relative -ieq 'EduFocus.exe') {
        $componentId = 'MainExe'
        $fileId = 'MainExeFile'
        $guid = 'A1B2C3D4-E5F6-7890-ABCD-EF1234567890'
    }
    elseif ($file.Relative -ieq 'native-host\EduFocus.NativeHost.exe') {
        $componentId = 'NativeHostExe'
        $fileId = 'NativeHostExeFile'
        $guid = 'F1E2D3C4-B5A6-4978-8C9D-0E1F2A3B4C5D'
    }

    $escapedSource = ConvertTo-XmlText $file.FullName
    $escapedName = ConvertTo-XmlText $file.Name
    $directoryAttribute = ''
    if ($assignment.DirId -ne 'INSTALLFOLDER') {
        $directoryAttribute = " Directory=`"$($assignment.DirId)`""
    }

    [void]$builder.AppendLine("      <Component Id=`"$componentId`"$directoryAttribute Guid=`"$guid`">")

    if (($assignment.DirId -ne 'INSTALLFOLDER') -and (-not $removeFolderAdded.ContainsKey($assignment.DirId))) {
        [void]$builder.AppendLine("        <RemoveFolder Id=`"Remove$($assignment.DirId)`" Directory=`"$($assignment.DirId)`" On=`"uninstall`"/>")
        $removeFolderAdded[$assignment.DirId] = $true
    }

    [void]$builder.AppendLine("        <File Id=`"$fileId`" Source=`"$escapedSource`" Name=`"$escapedName`" KeyPath=`"yes`"/>")
    [void]$builder.AppendLine('      </Component>')
}

[void]$builder.AppendLine('    </ComponentGroup>')
[void]$builder.AppendLine('  </Fragment>')

$escapedServiceSource = ConvertTo-XmlText $serviceFile.FullName
[void]$builder.AppendLine('  <Fragment>')
[void]$builder.AppendLine('    <!-- Windows service owner of protection. A fixed GUID keeps one')
[void]$builder.AppendLine('         service row across upgrades; recovery restarts it after a crash. -->')
[void]$builder.AppendLine('    <Component Id="ServiceExe" Directory="INSTALLFOLDER" Guid="7C4F3A21-9B8D-4E6F-A5C4-3D2E1F0A9B8C">')
[void]$builder.AppendLine("      <File Id=`"ServiceExeFile`" Source=`"$escapedServiceSource`" Name=`"EduFocus.Service.exe`" KeyPath=`"yes`"/>")
[void]$builder.AppendLine('      <ServiceInstall Id="EduFocusProtectionService" Name="EduFocusProtection" DisplayName="EduFocus Protection Service" Description="Keeps EduFocus protection active while the EduFocus window is closed." Start="auto" Type="ownProcess" ErrorControl="normal" Account="LocalSystem"/>')
[void]$builder.AppendLine('      <ServiceControl Id="EduFocusProtectionControl" Name="EduFocusProtection" Start="install" Stop="both" Remove="uninstall" Wait="yes"/>')
[void]$builder.AppendLine('      <util:ServiceConfig ServiceName="EduFocusProtection" FirstFailureActionType="restart" SecondFailureActionType="restart" ThirdFailureActionType="restart" RestartServiceDelayInSeconds="60" ResetPeriodInDays="1"/>')
[void]$builder.AppendLine('    </Component>')
[void]$builder.AppendLine('  </Fragment>')
[void]$builder.AppendLine('</Wix>')

$payloadDirectory = Join-Path $installerDir 'obj'
if (-not (Test-Path -LiteralPath $payloadDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null
}

$payloadWxs = Join-Path $payloadDirectory 'Payload.wxs'
[System.IO.File]::WriteAllText($payloadWxs, $builder.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$warnings = $payloadFiles | Where-Object {
    ($_.Length -gt 0) -and
    ($_.Relative -notmatch '\.(exe|dll|onnx|json|xml|dat|bin|md|txt)$') -and
    ($_.Name -notmatch '^(LICENSE|NOTICE)$')
}
foreach ($warning in $warnings) {
    Write-Warning "Shipping '$($warning.Relative)' ($($warning.Length) bytes); confirm it contains no secrets."
}

Write-Host ("Packaging {0} file(s) from '{1}' (version {2})." -f $payloadFiles.Count, $publish, $Version)

$productWxs = Join-Path $installerDir 'Product.wxs'
& $Wix build $productWxs $payloadWxs `
    -ext WixToolset.UI.wixext `
    -ext WixToolset.Util.wixext `
    -arch x64 `
    -d "Version=$Version" `
    -o $outputFull

if ($LASTEXITCODE -ne 0) {
    throw "wix build failed with exit code $LASTEXITCODE."
}

$msi = Get-Item -LiteralPath $outputFull
Write-Host ("Created '{0}' ({1:N0} bytes)." -f $msi.FullName, $msi.Length)
