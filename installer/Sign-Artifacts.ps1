#Requires -Version 5.1
<#
.SYNOPSIS
    Signs release artifacts with an Authenticode certificate supplied by CI secrets.

.DESCRIPTION
    The certificate is provided as base64-encoded PFX plus a password, normally from
    CODESIGN_PFX_BASE64 and CODESIGN_PFX_PASSWORD. The PFX is written to a temporary
    file, imported into the current user's certificate store, used through signtool and
    then removed together with the temporary file.

    Every signed file is verified with "signtool verify /pa" before the script returns
    true. When no certificate is configured the script returns false, unless
    -RequireSigning is set, in which case it throws so a release cannot silently ship
    unsigned.

    The password is never passed on a command line: signtool selects the certificate by
    thumbprint and no secret value is written to the output.

.PARAMETER Files
    One or more files (EXE, MSI) to sign.

.PARAMETER PfxBase64
    Base64 content of a PFX/PKCS#12 file that contains a code-signing certificate and
    its private key.

.PARAMETER PfxPassword
    Password that protects the PFX file.

.PARAMETER Description
    Description embedded in the signature (defaults to EduFocus).

.PARAMETER RequireSigning
    Fail instead of returning false when no certificate is configured.

.PARAMETER TimestampUrl
    RFC 3161 timestamp server used while signing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files,

    [string]$PfxBase64 = "",

    [string]$PfxPassword = "",

    [string]$Description = "EduFocus",

    [switch]$RequireSigning,

    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $kitsRoot = Join-Path $programFilesX86 "Windows Kits\10\bin"
        if (Test-Path -LiteralPath $kitsRoot) {
            $candidate = Get-ChildItem -LiteralPath $kitsRoot -Directory |
                Sort-Object Name -Descending |
                ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
                Where-Object { Test-Path -LiteralPath $_ } |
                Select-Object -First 1
            if ($candidate) {
                return $candidate
            }
        }
    }

    throw "signtool.exe was not found. Install the Windows SDK signing tools or add signtool.exe to PATH."
}

$resolvedFiles = @()
foreach ($file in $Files) {
    if ([string]::IsNullOrWhiteSpace($file)) {
        throw "A file path passed to -Files is empty."
    }
    $fullPath = [IO.Path]::GetFullPath($file)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "File to sign was not found: '$fullPath'."
    }
    $resolvedFiles += $fullPath
}
if ($resolvedFiles.Count -eq 0) {
    throw "At least one file must be passed to -Files."
}

if ([string]::IsNullOrWhiteSpace($PfxBase64)) {
    if ($RequireSigning) {
        throw "Code signing is required (REQUIRE_CODE_SIGNING=true) but CODESIGN_PFX_BASE64 is not configured."
    }
    Write-Warning "Code signing certificate is not configured; artifacts will remain unsigned."
    return $false
}

if ([string]::IsNullOrEmpty($PfxPassword)) {
    throw "CODESIGN_PFX_PASSWORD is required when CODESIGN_PFX_BASE64 is set."
}

try {
    $pfxBytes = [Convert]::FromBase64String($PfxBase64.Trim())
}
catch {
    throw "CODESIGN_PFX_BASE64 is not valid base64."
}

$signTool = Find-SignTool
$securePassword = ConvertTo-SecureString -String $PfxPassword -AsPlainText -Force
$pfxPath = Join-Path ([IO.Path]::GetTempPath()) ("edufocus-sign-" + [Guid]::NewGuid().ToString("N") + ".pfx")
$existingThumbprints = @(
    Get-ChildItem -Path "Cert:\CurrentUser\My" -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Thumbprint }
)
$certificates = @()

try {
    [IO.File]::WriteAllBytes($pfxPath, $pfxBytes)
    $certificates = @(
        Import-PfxCertificate `
            -FilePath $pfxPath `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -Password $securePassword `
            -ErrorAction Stop
    )

    $signingCertificate = $certificates |
        Where-Object { $_.HasPrivateKey } |
        Select-Object -First 1
    if (-not $signingCertificate) {
        throw "The PFX does not contain a certificate with a private key."
    }

    foreach ($file in $resolvedFiles) {
        & $signTool sign `
            /sha1 $signingCertificate.Thumbprint `
            /fd SHA256 `
            /td SHA256 `
            /tr $TimestampUrl `
            /d $Description `
            $file
        if ($LASTEXITCODE -ne 0) {
            throw "signtool sign failed for '$file' with exit code $LASTEXITCODE."
        }
    }

    foreach ($file in $resolvedFiles) {
        & $signTool verify /pa /v $file
        if ($LASTEXITCODE -ne 0) {
            throw "Signature verification failed for '$file' with exit code $LASTEXITCODE."
        }
    }

    Write-Host "Signed and verified: $($resolvedFiles -join ', ')"
    return $true
}
finally {
    Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue

    foreach ($certificate in $certificates) {
        if ([string]::IsNullOrWhiteSpace($certificate.Thumbprint)) {
            continue
        }
        if ($existingThumbprints -contains $certificate.Thumbprint) {
            continue
        }
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $certificate.Thumbprint) -Force -ErrorAction SilentlyContinue
    }
}
