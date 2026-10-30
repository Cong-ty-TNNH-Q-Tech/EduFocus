# Removes the EduFocus native messaging host registration for Chrome and Edge.

[CmdletBinding()]
param(
    [ValidateSet('User', 'Machine')]
    [string]$Scope = 'User'
)

$ErrorActionPreference = 'Stop'

$hostName = 'com.edufocus.native_host'

if ($Scope -eq 'Machine') {
    $manifestDirectory = Join-Path $env:ProgramData 'EduFocus\NativeMessaging'
    $root = 'HKEY_LOCAL_MACHINE'
    $keyRoots = @(
        "$root\Software\Google\Chrome\NativeMessagingHosts\$hostName",
        "$root\Software\Microsoft\Edge\NativeMessagingHosts\$hostName",
        "HKEY_LOCAL_MACHINE\Software\WOW6432Node\Google\Chrome\NativeMessagingHosts\$hostName",
        "HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Edge\NativeMessagingHosts\$hostName"
    )
} else {
    $manifestDirectory = Join-Path $env:LOCALAPPDATA 'EduFocus\NativeMessaging'
    $root = 'HKEY_CURRENT_USER'
    $keyRoots = @(
        "$root\Software\Google\Chrome\NativeMessagingHosts\$hostName",
        "$root\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
    )
}

foreach ($key in $keyRoots) {
    if (Test-Path -Path "Registry::$key") {
        Remove-Item -Path "Registry::$key" -Recurse -Force
        Write-Host "Removed $key"
    }
}

$manifestPath = Join-Path $manifestDirectory "$hostName.json"
if (Test-Path -LiteralPath $manifestPath) {
    Remove-Item -LiteralPath $manifestPath -Force
    Write-Host "Removed $manifestPath"
}

Write-Host "Native host unregistered for $Scope scope."
