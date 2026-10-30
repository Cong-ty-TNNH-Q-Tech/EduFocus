# Registers the EduFocus native messaging host and manifest for Chrome and Edge.
# Run from an elevated PowerShell session only when -Scope Machine is used.

[CmdletBinding()]
param(
    [string]$HostPath = (Join-Path $PSScriptRoot '..\artifacts\native-host\EduFocus.NativeHost.exe'),
    [ValidateSet('User', 'Machine')]
    [string]$Scope = 'User'
)

$ErrorActionPreference = 'Stop'

$extensionId = 'lcanbfnejggilcaeinmahlcfimecmjmp'
$hostName = 'com.edufocus.native_host'
$resolvedHost = (Resolve-Path -LiteralPath $HostPath -ErrorAction Stop).Path

if ($Scope -eq 'Machine') {
    $principal = New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Machine scope requires an elevated PowerShell session.'
    }

    $manifestDirectory = Join-Path $env:ProgramData 'EduFocus\NativeMessaging'
    $root = 'HKEY_LOCAL_MACHINE'
} else {
    $manifestDirectory = Join-Path $env:LOCALAPPDATA 'EduFocus\NativeMessaging'
    $root = 'HKEY_CURRENT_USER'
}

New-Item -ItemType Directory -Force -Path $manifestDirectory | Out-Null
$manifestPath = Join-Path $manifestDirectory "$hostName.json"

$manifest = [ordered]@{
    name = $hostName
    description = 'EduFocus browser extension bridge'
    path = $resolvedHost
    type = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
}
$json = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText(
    $manifestPath,
    $json,
    (New-Object System.Text.UTF8Encoding($false)))

$browserKeys = @(
    "$root\Software\Google\Chrome\NativeMessagingHosts\$hostName",
    "$root\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
)

if ($Scope -eq 'Machine') {
    $browserKeys += @(
        "HKEY_LOCAL_MACHINE\Software\WOW6432Node\Google\Chrome\NativeMessagingHosts\$hostName",
        "HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Edge\NativeMessagingHosts\$hostName"
    )
}

foreach ($key in $browserKeys) {
    New-Item -Path "Registry::$key" -Force | Out-Null
    Set-Item -Path "Registry::$key" -Value $manifestPath
}

Write-Host "Native host registered for $Scope scope."
Write-Host "  Host:     $resolvedHost"
Write-Host "  Manifest: $manifestPath"
Write-Host "  Keys:     $($browserKeys -join ', ')"
