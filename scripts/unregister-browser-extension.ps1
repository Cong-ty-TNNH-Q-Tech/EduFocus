# Removes the EduFocus native messaging host registration for Chrome and Edge.

[CmdletBinding()]
param(
    [string]$HostPath = (Join-Path $PSScriptRoot '..\artifacts\native-host\EduFocus.NativeHost.exe'),
    [ValidateSet('User', 'Machine')]
    [string]$Scope = 'User'
)

$ErrorActionPreference = 'Stop'

$hostName = 'com.edufocus.native_host'

if ($Scope -eq 'Machine') {
    $principal = New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Machine scope requires an elevated PowerShell session.'
    }

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

$manifestPaths = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
[void]$manifestPaths.Add((Join-Path $manifestDirectory "$hostName.json"))
if (-not [string]::IsNullOrWhiteSpace($HostPath)) {
    $hostCandidate = [System.IO.Path]::GetFullPath($HostPath)
    $hostDir = Split-Path -Parent $hostCandidate
    if (-not [string]::IsNullOrWhiteSpace($hostDir)) {
        [void]$manifestPaths.Add((Join-Path $hostDir "$hostName.json"))
    }
}
if ($Scope -eq 'Machine' -and $env:ProgramFiles) {
    [void]$manifestPaths.Add((Join-Path $env:ProgramFiles "EduFocus\native-host\$hostName.json"))
}

foreach ($key in $keyRoots) {
    if (Test-Path -Path "Registry::$key") {
        $registeredManifest = (Get-Item -Path "Registry::$key").GetValue('')
        if (-not [string]::IsNullOrWhiteSpace($registeredManifest)) {
            [void]$manifestPaths.Add([string]$registeredManifest)
        }
        Remove-Item -Path "Registry::$key" -Recurse -Force
        Write-Host "Removed $key"
    }
}

foreach ($manifestPath in $manifestPaths) {
    if (Test-Path -LiteralPath $manifestPath) {
        Remove-Item -LiteralPath $manifestPath -Force
        Write-Host "Removed $manifestPath"
    }
}

Write-Host "Native host unregistered for $Scope scope."
