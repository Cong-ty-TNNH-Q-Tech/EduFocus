#requires -Version 5.1
<#
.SYNOPSIS
Packages the EduFocus MV3 extension as a store-ready ZIP and, optionally, a CRX.

.DESCRIPTION
Copies only the runtime extension files (no tests, native host files or
developer metadata) into a staging folder, validates the manifest, writes
EduFocus-<version>-browser-extension.zip and prints its SHA-256.

When -PemPath is supplied, the script invokes Chrome or Edge with
--pack-extension to produce a signed CRX. The private key must live outside
this repository: it is never copied, printed or added to artifacts.

.EXAMPLE
./scripts/pack-browser-extension.ps1

.EXAMPLE
./scripts/pack-browser-extension.ps1 -PemPath D:\secure\edufocus-extension.pem
#>
[CmdletBinding()]
param(
    [string]$OutputDir,
    [string]$Version,

    [string]$PemPath,

    [string]$BrowserPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$extensionDir = Join-Path $repoRoot 'browser-extension'

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot 'artifacts'
}

$manifestPath = Join-Path $extensionDir 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Extension manifest not found: '$manifestPath'."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.manifest_version -ne 3) {
    throw 'browser-extension/manifest.json must be Manifest V3.'
}
if ([string]::IsNullOrWhiteSpace($manifest.key)) {
    throw 'manifest.json has no key; the extension ID would change between installs.'
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$manifest.version
}
if ($Version -notmatch '^[0-9]+(\.[0-9]+){0,3}$') {
    throw "Extension version '$Version' is not numeric (expected 1.0.0)."
}

$runtimeFiles = @(
    'manifest.json',
    'background.js',
    'matcher.js',
    'blocked.html',
    'blocked.css',
    'blocked.js',
    'popup.html',
    'popup.css',
    'popup.js'
)
foreach ($file in $runtimeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $extensionDir $file) -PathType Leaf)) {
        throw "Extension runtime file '$file' is missing."
    }
}

$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ('edufocus-extension-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $stageRoot 'EduFocus-browser-extension'
$crxPath = $null

try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    foreach ($file in $runtimeFiles) {
        Copy-Item -LiteralPath (Join-Path $extensionDir $file) -Destination $stage
    }

    $manifest.version = $Version
    $stagedManifest = $manifest | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText(
        (Join-Path $stage 'manifest.json'),
        $stagedManifest,
        (New-Object System.Text.UTF8Encoding($false)))

    if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }

    $zipPath = Join-Path $OutputDir "EduFocus-$Version-browser-extension.zip"
    Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stage,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        foreach ($file in $runtimeFiles) {
            if ($entries -notcontains $file) {
                throw "The ZIP is missing '$file'."
            }
        }
        if ($entries | Where-Object { $_ -match '(^|/)(tests|native-host)/' }) {
            throw 'The ZIP contains test or native host files; only runtime files may ship.'
        }
    }
    finally {
        $archive.Dispose()
    }

    if (-not [string]::IsNullOrWhiteSpace($PemPath)) {
        $pemFull = (Resolve-Path -LiteralPath $PemPath -ErrorAction Stop).Path
        $repoPrefix = $repoRoot.TrimEnd([char]'\') + '\'
        if ($pemFull.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The signing key must be stored outside the repository; artifacts must never contain private keys.'
        }

        if ([string]::IsNullOrWhiteSpace($BrowserPath)) {
            $candidates = @(
                (Join-Path ${env:ProgramFiles} 'Google\Chrome\Application\chrome.exe'),
                (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
                (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
                (Join-Path ${env:ProgramFiles} 'Microsoft\Edge\Application\msedge.exe')
            ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }

            $BrowserPath = $candidates | Select-Object -First 1
        }
        if ([string]::IsNullOrWhiteSpace($BrowserPath)) {
            throw 'Chrome/Edge was not found. Pass -BrowserPath to sign a CRX, or publish the ZIP to the Chrome Web Store.'
        }

        $packedCrx = "$stage.crx"
        Remove-Item -LiteralPath $packedCrx -Force -ErrorAction SilentlyContinue
        $process = Start-Process `
            -FilePath $BrowserPath `
            -ArgumentList @("--pack-extension=$stage", "--pack-extension-key=$pemFull") `
            -Wait `
            -PassThru `
            -NoNewWindow
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $packedCrx -PathType Leaf)) {
            throw "Browser CRX packaging failed (exit code $($process.ExitCode))."
        }

        $crxPath = Join-Path $OutputDir "EduFocus-$Version-browser-extension.crx"
        Move-Item -LiteralPath $packedCrx -Destination $crxPath -Force
    }
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Created '$zipPath' (SHA-256 $zipHash)."
if ($crxPath) {
    $crxHash = (Get-FileHash -LiteralPath $crxPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Created '$crxPath' (SHA-256 $crxHash)."
}
