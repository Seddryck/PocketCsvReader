param(
    [string]$InputFile = "$PSScriptRoot/regressions.jsonl",
    [string]$OutputFile = "$PSScriptRoot/regressions-summary.csv",
    [string]$ReportFile = "$PSScriptRoot/README.md"
)
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
function Number([double]$value, [string]$format = 'F3') { $value.ToString($format, $culture) }
$samples = Get-Content -LiteralPath $InputFile | ForEach-Object { $_ | ConvertFrom-Json }
if ($samples.Count -ne 27) { throw "Expected 27 samples; found $($samples.Count)" }
foreach ($mode in ($samples | Group-Object mode)) {
    if (@($mode.Group.inputHash | Sort-Object -Unique).Count -ne 1) { throw "Input mismatch: $($mode.Name)" }
}
$summary = foreach ($case in ($samples | Group-Object mode,label)) {
    if ($case.Count -ne 3) { throw "Incomplete case: $($case.Name)" }
    [pscustomobject]@{
        Mode = $case.Group[0].mode; Label = $case.Group[0].label
        CpuMs = Number (($case.Group | Measure-Object cpuMs -Average).Average)
        ElapsedMs = Number (($case.Group | Measure-Object elapsedMs -Average).Average)
        AllocatedBytes = Number (($case.Group | Measure-Object allocatedBytes -Average).Average) 'F0'
    }
}
$summary | Export-Csv -LiteralPath $OutputFile -NoTypeInformation
$table = @(
    '<!-- regressions:start -->'
    '| Workload | CPU baseline / reviewed / fixed (ms) | Elapsed baseline / reviewed / fixed (ms) | Allocated baseline / reviewed / fixed (bytes) |'
    '| --- | ---: | ---: | ---: |'
    foreach ($mode in @('x','spaces','async-json')) {
        $items = @('baseline','reviewed','fixed') | ForEach-Object { $label = $_; $summary | Where-Object { $_.Mode -eq $mode -and $_.Label -eq $label } }
        "| $mode | $($items.CpuMs -join ' / ') | $($items.ElapsedMs -join ' / ') | $($items.AllocatedBytes -join ' / ') |"
    }
    '<!-- regressions:end -->'
) -join "`r`n"
$report = Get-Content -LiteralPath $ReportFile -Raw
if ($report -notmatch '(?s)<!-- regressions:start -->.*?<!-- regressions:end -->') { throw 'Report is missing regression markers' }
$report = [regex]::Replace($report, '(?s)<!-- regressions:start -->.*?<!-- regressions:end -->', [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $table })
[IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportFile), $report)
$summary | Format-Table -AutoSize
