param(
    [string]$Destination = (Join-Path $PSScriptRoot 'models')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$models = @(
    @{
        Name = 'face_detection_yunet_2023mar.onnx'
        Url  = 'https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx'
        Sha256 = '8F2383E4DD3CFBB4553EA8718107FC0423210DC964F9F4280604804ED2552FA4'
    },
    @{
        Name = 'haarcascade_frontalface_default.xml'
        Url  = 'https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_frontalface_default.xml'
        Sha256 = '0F7D4527844EB514D4A4948E822DA90FBB16A34A0BBBBC6ADC6498747A5AAFB0'
    },
    @{
        Name = 'haarcascade_eye.xml'
        Url  = 'https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_eye.xml'
        Sha256 = '71CC64FC305A355DC60067880F6FBBD43DD155BD63EE3844661A1BDA34B2FD8C'
    }
)

New-Item -ItemType Directory -Path $Destination -Force | Out-Null

foreach ($model in $models) {
    $target = Join-Path $Destination $model.Name
    $existingHash = $null
    if (Test-Path -LiteralPath $target) {
        $existingHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    }

    if ($existingHash -eq $model.Sha256) {
        Write-Host "OK      $($model.Name)"
        continue
    }

    Write-Host "GET     $($model.Name)"
    Invoke-WebRequest -Uri $model.Url -OutFile $target -MaximumRedirection 10

    $actualHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($actualHash -ne $model.Sha256) {
        Remove-Item -LiteralPath $target -Force
        throw "Hash mismatch for $($model.Name): expected $($model.Sha256), got $actualHash"
    }

    Write-Host "VERIFY  $($model.Name)"
}

Write-Host "Models ready in $Destination"
