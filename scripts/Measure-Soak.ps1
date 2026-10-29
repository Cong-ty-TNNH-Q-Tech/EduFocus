#requires -Version 5.1
<#
.SYNOPSIS
Samples a running EduFocus process for a soak run and reports CPU, RAM,
handles and thread growth against the BA Document §9.3 budgets.

.DESCRIPTION
Run on a representative clean VM with the app already open in the mode under
test. The default run lasts eight hours and samples every 30 seconds. Results
are written as CSV, JSON and Markdown under -OutputDirectory. The script never
changes the app, the database, the hosts file or any Windows setting.

.EXAMPLE
.\scripts\Measure-Soak.ps1 -Mode Protection -DurationHours 8 -OutputDirectory artifacts\soak-protection

.EXAMPLE
.\scripts\Measure-Soak.ps1 -Mode Idle -DurationHours 8 -OutputDirectory artifacts\soak-idle
#>
[CmdletBinding()]
param(
    [string]$ProcessName = "EduFocus",
    [int]$ProcessId = 0,
    [ValidateRange(0.001, 168)][double]$DurationHours = 8,
    [ValidateRange(1, 3600)][int]$IntervalSeconds = 30,
    [ValidateSet("Idle", "Protection", "Attention")][string]$Mode = "Protection",
    [string]$OutputDirectory = "artifacts/soak",
    [double]$MaxWorkingSetMb = 0,
    [double]$MaxCpuPercent = 0,
    [int]$MaxHandleGrowth = 100,
    [double]$MaxMemoryGrowthMbPerHour = 2,
    [switch]$AllowRestarts
)

$ErrorActionPreference = "Stop"
$culture = [System.Globalization.CultureInfo]::InvariantCulture

function Get-TargetProcess {
    if ($ProcessId -gt 0) {
        return Get-Process -Id $ProcessId -ErrorAction Stop
    }

    $candidates = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Sort-Object StartTime
    if (-not $candidates) {
        throw "Process '$ProcessName' is not running. Start EduFocus in Mode '$Mode' first."
    }

    return $candidates | Select-Object -First 1
}

function Get-SlopePerHour {
    param([double[]]$X, [double[]]$Y)

    $count = $X.Count
    if ($count -lt 2) { return 0 }

    $meanX = ($X | Measure-Object -Average).Average
    $meanY = ($Y | Measure-Object -Average).Average
    $numerator = 0.0
    $denominator = 0.0

    for ($index = 0; $index -lt $count; $index++) {
        $dx = $X[$index] - $meanX
        $numerator += $dx * ($Y[$index] - $meanY)
        $denominator += $dx * $dx
    }

    if ($denominator -eq 0) { return 0 }
    return ($numerator / $denominator) * 3600
}

function Get-Percentile {
    param([double[]]$Values, [double]$Percentile)

    if ($Values.Count -eq 0) { return 0 }
    $sorted = $Values | Sort-Object
    $index = [math]::Ceiling($sorted.Count * $Percentile) - 1
    if ($index -lt 0) { $index = 0 }
    return $sorted[$index]
}

function Get-Median {
    param([double[]]$Values)

    if ($Values.Count -eq 0) { return 0 }
    $sorted = $Values | Sort-Object
    $middle = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

switch ($Mode) {
    "Idle" { $workingSetBudget = 120.0; $cpuBudget = 1.0 }
    "Protection" { $workingSetBudget = 200.0; $cpuBudget = 3.0 }
    "Attention" { $workingSetBudget = 350.0; $cpuBudget = 10.0 }
}

if ($MaxWorkingSetMb -gt 0) { $workingSetBudget = $MaxWorkingSetMb }
if ($MaxCpuPercent -gt 0) { $cpuBudget = $MaxCpuPercent }

$outputRoot = $OutputDirectory
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $outputRoot = Join-Path (Get-Location).Path $OutputDirectory
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$machine = Get-CimInstance Win32_ComputerSystem
$diskName = "unknown"
$diskMedia = "unknown"
if (Get-Command Get-PhysicalDisk -ErrorAction SilentlyContinue) {
    $disk = Get-PhysicalDisk | Select-Object -First 1
    if ($disk) {
        $diskName = $disk.FriendlyName
        $diskMedia = $disk.MediaType
    }
}

$dotnetVersion = $null
try { $dotnetVersion = (& dotnet --version 2>$null) } catch { $dotnetVersion = $null }

$process = Get-TargetProcess
$appVersion = $null
$appPath = $null
try {
    $appVersion = $process.MainModule.FileVersionInfo.ProductVersion
    $appPath = $process.MainModule.FileName
} catch {
    $appVersion = "unavailable (access denied)"
    $appPath = "unavailable (access denied)"
}

$coreCount = [Environment]::ProcessorCount
$startedAt = Get-Date
$deadline = $startedAt.AddHours($DurationHours)
$records = New-Object System.Collections.Generic.List[object]
$restartCount = 0
$missingSamples = 0
$previousCpu = $process.TotalProcessorTime
$previousAt = Get-Date
$lastPid = $process.Id
$sampleIndex = 0

Write-Host ("Soak run: mode={0}, process={1} (PID {2}), duration={3} h, interval={4} s" -f `
    $Mode, $process.ProcessName, $process.Id, $DurationHours, $IntervalSeconds)

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds $IntervalSeconds
    $now = Get-Date

    if ($ProcessId -eq 0) {
        $process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
            Sort-Object StartTime |
            Select-Object -First 1
    } else {
        $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    }

    if (-not $process) {
        $missingSamples++
        if ((-not $AllowRestarts) -and $missingSamples -ge 3) {
            throw "Process disappeared during the soak run."
        }
        continue
    }

    if ($sampleIndex -gt 0 -and $process.Id -ne $lastPid) {
        $restartCount++
        $previousCpu = $process.TotalProcessorTime
        $previousAt = $now
    }

    $lastPid = $process.Id
    $elapsedSeconds = [math]::Max(($now - $previousAt).TotalSeconds, 0.001)
    $cpuDelta = ($process.TotalProcessorTime - $previousCpu).TotalSeconds
    $cpuPercent = ($cpuDelta / $elapsedSeconds / $coreCount) * 100.0
    $previousCpu = $process.TotalProcessorTime
    $previousAt = $now

    $record = [pscustomobject]@{
        Timestamp = $now.ToString("o", $culture)
        ElapsedSeconds = [math]::Round(($now - $startedAt).TotalSeconds, 1)
        WorkingSetMb = [math]::Round($process.WorkingSet64 / 1MB, 2)
        PrivateMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        Handles = $process.HandleCount
        Threads = $process.Threads.Count
        CpuPercent = [math]::Round($cpuPercent, 2)
    }
    $records.Add($record)
    $sampleIndex++

    if ($sampleIndex % 10 -eq 0) {
        Write-Host ("[{0}] samples={1}, RAM={2} MB, handles={3}, CPU={4} %" -f `
            $now.ToString("HH:mm:ss"), $sampleIndex, $record.WorkingSetMb, $record.Handles, $record.CpuPercent)
    }
}

if ($records.Count -lt 2) {
    throw "Not enough samples were collected; increase DurationHours or decrease IntervalSeconds."
}

$workingSets = [double[]]($records | ForEach-Object { $_.WorkingSetMb })
$handles = [double[]]($records | ForEach-Object { $_.Handles })
$cpus = [double[]]($records | ForEach-Object { $_.CpuPercent })
$times = [double[]]($records | ForEach-Object { $_.ElapsedSeconds })

$sampledCpus = $cpus
if ($cpus.Count -gt 1) {
    $sampledCpus = [double[]]($cpus[1..($cpus.Count - 1)])
}

$headEnd = [math]::Min(2, $workingSets.Count - 1)
$tailStart = [math]::Max(0, $workingSets.Count - 3)
$firstWorkingSet = Get-Median ([double[]]($workingSets[0..$headEnd]))
$lastWorkingSet = Get-Median ([double[]]($workingSets[$tailStart..($workingSets.Count - 1)]))
$firstHandles = Get-Median ([double[]]($handles[0..$headEnd]))
$lastHandles = Get-Median ([double[]]($handles[$tailStart..($handles.Count - 1)]))
$handleGrowth = $lastHandles - $firstHandles
$workingSetGrowth = $lastWorkingSet - $firstWorkingSet
$memorySlope = Get-SlopePerHour $times $workingSets
$handleSlope = Get-SlopePerHour $times $handles
$averageCpu = ($sampledCpus | Measure-Object -Average).Average
$peakCpu = ($sampledCpus | Measure-Object -Maximum).Maximum
$p95WorkingSet = Get-Percentile $workingSets 0.95
$peakWorkingSet = ($workingSets | Measure-Object -Maximum).Maximum

$failures = New-Object System.Collections.Generic.List[string]
if (-not $AllowRestarts -and $restartCount -gt 0) {
    $failures.Add("The process restarted $restartCount time(s) during the soak.")
}
if ($averageCpu -gt $cpuBudget) {
    $failures.Add("Average CPU $([math]::Round($averageCpu, 2))% exceeded the $cpuBudget% budget.")
}
if ($peakWorkingSet -gt $workingSetBudget) {
    $failures.Add("Peak working set $([math]::Round($peakWorkingSet, 1)) MB exceeded the $workingSetBudget MB budget.")
}
if ($handleGrowth -gt $MaxHandleGrowth) {
    $failures.Add("Handle count grew by $handleGrowth, above the $MaxHandleGrowth limit.")
}
if ($memorySlope -gt $MaxMemoryGrowthMbPerHour) {
    $failures.Add("Working set slope $([math]::Round($memorySlope, 2)) MB/h exceeded $MaxMemoryGrowthMbPerHour MB/h.")
}

$resultText = "PASS"
if ($failures.Count -gt 0) {
    $resultText = "FAIL"
}

$summary = [pscustomobject]@{
    Result = $resultText
    Mode = $Mode
    StartedAt = $startedAt.ToString("o", $culture)
    DurationHours = $DurationHours
    IntervalSeconds = $IntervalSeconds
    Samples = $records.Count
    RestartCount = $restartCount
    AverageCpuPercent = [math]::Round($averageCpu, 2)
    PeakCpuPercent = [math]::Round($peakCpu, 2)
    FirstWorkingSetMb = [math]::Round($firstWorkingSet, 2)
    LastWorkingSetMb = [math]::Round($lastWorkingSet, 2)
    WorkingSetGrowthMb = [math]::Round($workingSetGrowth, 2)
    PeakWorkingSetMb = [math]::Round($peakWorkingSet, 2)
    P95WorkingSetMb = [math]::Round($p95WorkingSet, 2)
    WorkingSetSlopeMbPerHour = [math]::Round($memorySlope, 2)
    FirstHandles = [math]::Round($firstHandles, 1)
    LastHandles = [math]::Round($lastHandles, 1)
    HandleGrowth = [math]::Round($handleGrowth, 1)
    HandleSlopePerHour = [math]::Round($handleSlope, 2)
    Budgets = [pscustomobject]@{
        WorkingSetMb = $workingSetBudget
        CpuPercent = $cpuBudget
        HandleGrowth = $MaxHandleGrowth
        MemoryGrowthMbPerHour = $MaxMemoryGrowthMbPerHour
    }
    Machine = [pscustomobject]@{
        OS = $os.Caption
        Version = $os.Version
        Build = $os.BuildNumber
        Cpu = $cpu.Name.Trim()
        Cores = $cpu.NumberOfCores
        LogicalProcessors = $cpu.NumberOfLogicalProcessors
        RamGb = [math]::Round($machine.TotalPhysicalMemory / 1GB, 1)
        Disk = $diskName
        DiskMedia = $diskMedia
        Dotnet = $dotnetVersion
    }
    App = [pscustomobject]@{
        ProductVersion = $appVersion
        Path = $appPath
        Pid = $process.Id
        LogicalCoresUsedForCpuPercent = $coreCount
    }
    Failures = $failures.ToArray()
    CsvPath = Join-Path $outputRoot "soak-samples.csv"
    JsonPath = Join-Path $outputRoot "soak-summary.json"
    MarkdownPath = Join-Path $outputRoot "soak-summary.md"
}

$records | Export-Csv -Path $summary.CsvPath -NoTypeInformation -Encoding UTF8
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $summary.JsonPath -Encoding UTF8

$markdown = @(
    "# EduFocus soak summary",
    "",
    "- Result: **$($summary.Result)**",
    "- Mode: $Mode",
    "- Duration: $DurationHours h ($($records.Count) samples every $IntervalSeconds s)",
    "- Machine: $($summary.Machine.OS) $($summary.Machine.Version), $($summary.Machine.Cpu), $($summary.Machine.RamGb) GB RAM, $($summary.Machine.DiskMedia) disk",
    "- App: $appVersion",
    "",
    "| Metric | Value | Budget |",
    "|---|---|---|",
    "| Average CPU | $($summary.AverageCpuPercent)% | $cpuBudget% |",
    "| Peak CPU | $($summary.PeakCpuPercent)% | - |",
    "| Working set first/last | $($summary.FirstWorkingSetMb) / $($summary.LastWorkingSetMb) MB | $workingSetBudget MB |",
    "| Peak working set | $($summary.PeakWorkingSetMb) MB | $workingSetBudget MB |",
    "| Working set slope | $($summary.WorkingSetSlopeMbPerHour) MB/h | $MaxMemoryGrowthMbPerHour MB/h |",
    "| Handles first/last | $($summary.FirstHandles) / $($summary.LastHandles) | +$MaxHandleGrowth |",
    "| Restarts | $restartCount | 0 |",
    "",
    $(if ($failures.Count -eq 0) { "All thresholds were met." } else { "Failures:`n" + (($failures | ForEach-Object { "- $_" }) -join "`n") }),
    "",
    "Attach this file, the JSON summary and the CSV to the release checklist artifact."
)
$markdown | Set-Content -Path $summary.MarkdownPath -Encoding UTF8

Write-Host ("Result: {0}; CSV: {1}; JSON: {2}" -f $summary.Result, $summary.CsvPath, $summary.JsonPath)
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    exit 1
}

exit 0
