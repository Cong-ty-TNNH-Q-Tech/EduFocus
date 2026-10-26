$ErrorActionPreference = 'Stop'
$dataDirectory = Join-Path ([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::CommonApplicationData)) 'EduFocus'

if (-not (Test-Path -LiteralPath $dataDirectory -PathType Container)) {
    throw "EduFocus data directory does not exist: $dataDirectory"
}

try {
    Get-ChildItem -LiteralPath $dataDirectory -Force | Out-Null
    throw 'Standard user unexpectedly listed the EduFocus data directory.'
}
catch [UnauthorizedAccessException] {
    Write-Output 'PASS: standard user cannot list the EduFocus data directory.'
}

$probe = Join-Path $dataDirectory ('.standard-user-acl-probe-' + [Guid]::NewGuid().ToString('N'))
try {
    [System.IO.File]::WriteAllText($probe, 'probe')
    Remove-Item -LiteralPath $probe -Force
    throw 'Standard user unexpectedly wrote into the EduFocus data directory.'
}
catch [UnauthorizedAccessException] {
    Write-Output 'PASS: standard user cannot write to the EduFocus data directory.'
}
