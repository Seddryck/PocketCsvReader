param([string]$InputFile = "$PSScriptRoot/results.csv", [string]$OutputFile = "$PSScriptRoot/summary.csv")
$ErrorActionPreference = 'Stop'
$rows = Import-Csv -LiteralPath $InputFile
if ($rows.Count -ne 144) { throw "Expected 144 samples; found $($rows.Count)" }
$summary = foreach ($case in ($rows | Group-Object Format,Projected,Raw,Size)) {
    $before = @($case.Group | Where-Object Label -eq 'before')
    $after = @($case.Group | Where-Object Label -eq 'after')
    if ($before.Count -ne 3 -or $after.Count -ne 3) { throw "Incomplete case: $($case.Name)" }
    if (@($case.Group.InputHash | Sort-Object -Unique).Count -ne 1) { throw "Input mismatch: $($case.Name)" }
    function Mean($samples, $property) { ($samples | Measure-Object -Property $property -Average).Average }
    $cpuBefore = Mean $before CpuMs
    $cpuAfter = Mean $after CpuMs
    $elapsedBefore = Mean $before ElapsedMs
    $elapsedAfter = Mean $after ElapsedMs
    [pscustomobject]@{
        Format = $before[0].Format; Projected = $before[0].Projected; Raw = $before[0].Raw; Size = $before[0].Size
        CpuBeforeMs = [math]::Round($cpuBefore, 3); CpuAfterMs = [math]::Round($cpuAfter, 3)
        CpuChangePercent = [math]::Round(100 * ($cpuAfter / $cpuBefore - 1), 2)
        ElapsedBeforeMs = [math]::Round($elapsedBefore, 3); ElapsedAfterMs = [math]::Round($elapsedAfter, 3)
        ElapsedChangePercent = [math]::Round(100 * ($elapsedAfter / $elapsedBefore - 1), 2)
        AllocatedBefore = [math]::Round((Mean $before AllocatedBytes), 0)
        AllocatedAfter = [math]::Round((Mean $after AllocatedBytes), 0)
    }
}
$summary | Export-Csv -LiteralPath $OutputFile -NoTypeInformation
$summary | Format-Table -AutoSize
