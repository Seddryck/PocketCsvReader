param(
    [string]$InputFile = "$PSScriptRoot/results.csv",
    [string]$OutputFile = "$PSScriptRoot/summary.csv",
    [string]$ReportFile = "$PSScriptRoot/README.md"
)
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$rows = Import-Csv -LiteralPath $InputFile
if ($rows.Count -ne 144) { throw "Expected 144 samples; found $($rows.Count)" }
function Mean($samples, $property) {
    $values = @($samples | ForEach-Object { [double]::Parse($_.$property, $culture) })
    ($values | Measure-Object -Average).Average
}
function Number([double]$value, [string]$format = 'F3') { $value.ToString($format, $culture) }
$summary = foreach ($case in ($rows | Group-Object Format,Projected,Raw,Size)) {
    $before = @($case.Group | Where-Object Label -eq 'before')
    $after = @($case.Group | Where-Object Label -eq 'after')
    if ($before.Count -ne 3 -or $after.Count -ne 3) { throw "Incomplete case: $($case.Name)" }
    if (@($case.Group.InputHash | Sort-Object -Unique).Count -ne 1) { throw "Input mismatch: $($case.Name)" }
    $cpuBefore = Mean $before CpuMs
    $cpuAfter = Mean $after CpuMs
    $elapsedBefore = Mean $before ElapsedMs
    $elapsedAfter = Mean $after ElapsedMs
    [pscustomobject]@{
        Format = $before[0].Format; Projected = $before[0].Projected; Raw = $before[0].Raw; Size = $before[0].Size
        CpuBeforeMs = Number $cpuBefore; CpuAfterMs = Number $cpuAfter
        CpuChangePercent = Number (100 * ($cpuAfter / $cpuBefore - 1)) 'F2'
        ElapsedBeforeMs = Number $elapsedBefore; ElapsedAfterMs = Number $elapsedAfter
        ElapsedChangePercent = Number (100 * ($elapsedAfter / $elapsedBefore - 1)) 'F2'
        AllocatedBefore = Number (Mean $before AllocatedBytes) 'F0'
        AllocatedAfter = Number (Mean $after AllocatedBytes) 'F0'
    }
}
$summary | Export-Csv -LiteralPath $OutputFile -NoTypeInformation
$table = @(
    '<!-- results:start -->'
    '| Shape | Access | CPU before → after (ms) | CPU change | Elapsed before → after (ms) | Allocated before → after (bytes) |'
    '| --- | --- | ---: | ---: | ---: | ---: |'
    foreach ($row in ($summary | Where-Object { $_.Format -eq 'ndjson' -and $_.Projected -eq 'True' } | Sort-Object Size,Raw)) {
        $access = if ($row.Raw -eq 'True') { 'raw' } else { 'three values' }
        "| $($row.Size) | $access | $($row.CpuBeforeMs) → $($row.CpuAfterMs) | $($row.CpuChangePercent)% | $($row.ElapsedBeforeMs) → $($row.ElapsedAfterMs) | $($row.AllocatedBefore) → $($row.AllocatedAfter) |"
    }
    '<!-- results:end -->'
) -join "`r`n"
$report = Get-Content -LiteralPath $ReportFile -Raw
if ($report -notmatch '(?s)<!-- results:start -->.*?<!-- results:end -->') { throw 'Report is missing results markers' }
$report = [regex]::Replace($report, '(?s)<!-- results:start -->.*?<!-- results:end -->', [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $table })
[IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportFile), $report)
$summary | Format-Table -AutoSize
