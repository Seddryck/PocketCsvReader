param(
    [Parameter(Mandatory)][string]$BeforeAssembly,
    [Parameter(Mandatory)][string]$AfterAssembly,
    [Parameter(Mandatory)][string]$OutputPrefix,
    [string]$BeforeLabel = 'main-b359baf',
    [string]$AfterLabel = 'pr393',
    [ValidateSet('CsvUtf8', 'CsvChars', 'CsvCharsOptimized', 'CsvUtf8Optimized')][string]$Parser = 'CsvUtf8',
    [int]$Pairs = 4,
    [int]$Core = 2,
    [int]$WarmupSeconds = 5,
    [int]$Batches = 5,
    [int]$SampleSeconds = 2
)
$ErrorActionPreference = 'Stop'
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$workers = [System.Collections.Generic.List[object]]::new()
$rows = [System.Collections.Generic.List[object]]::new()
for ($pair = 1; $pair -le $Pairs; $pair++) {
    $order = if ($pair % 2 -eq 1) { @('before','after') } else { @('after','before') }
    foreach ($variant in $order) {
        $assembly = if ($variant -eq 'before') { $BeforeAssembly } else { $AfterAssembly }
        $label = if ($variant -eq 'before') { $BeforeLabel } else { $AfterLabel }
        Write-Host "Pair $pair/$Pairs, $variant ($label), $Parser, CPU $Core"
        $output = & dotnet $assembly --controlled-worker $Parser $label $Core $WarmupSeconds $Batches $SampleSeconds
        if ($LASTEXITCODE -ne 0) { throw "Worker failed: $label" }
        $resultLine = @($output | Where-Object { $_.StartsWith('CONTROLLED_RESULT:') })
        if ($resultLine.Count -ne 1) { throw "Expected exactly one worker result: $label" }
        $worker = $resultLine[0].Substring('CONTROLLED_RESULT:'.Length) | ConvertFrom-Json
        $worker | Add-Member Pair $pair
        $worker | Add-Member Variant $variant
        $workers.Add($worker)
        foreach ($property in @('InputHash','ReadIL','ProjectionIL','Framework','Architecture','Affinity','ServerGC','TieredPGO')) {
            if (@($workers | ForEach-Object { $_.$property } | Sort-Object -Unique).Count -ne 1) {
                throw "Control mismatch: $property"
            }
        }
        foreach ($group in @($workers | Group-Object Variant)) {
            if (@($group.Group.ParserMvid | Sort-Object -Unique).Count -ne 1) {
                throw "Parser assembly changed within variant $($group.Name)"
            }
        }
        foreach ($sample in $worker.Samples) {
            $rows.Add([pscustomobject]@{
                Pair=$pair; Variant=$variant; Label=$label; Parser=$Parser; Core=$Core
                Sample=$sample.Sample; Operations=$sample.Operations
                CpuMs=$sample.CpuMs; WallMs=$sample.WallMs; AllocatedBytes=$sample.AllocatedBytes
                Gen0=$sample.Gen0; Gen1=$sample.Gen1; Gen2=$sample.Gen2
            })
        }
        $rows | Export-Csv "$OutputPrefix.csv" -NoTypeInformation
        ConvertTo-Json -InputObject @($workers) -Depth 7 | Set-Content "$OutputPrefix.json"
        Write-Host ('  CPU mean: {0:F3} ms/input; allocation: {1:F0} bytes/input' -f
            ($worker.Samples.CpuMs | Measure-Object -Average).Average,
            ($worker.Samples.AllocatedBytes | Measure-Object -Average).Average)
    }
}
foreach ($property in @('InputHash','ReadIL','ProjectionIL','Framework','Architecture','Affinity','ServerGC','TieredPGO')) {
    if (@($workers | ForEach-Object { $_.$property } | Sort-Object -Unique).Count -ne 1) {
        throw "Control mismatch: $property"
    }
}
if (@($workers.ParserMvid | Sort-Object -Unique).Count -ne 2) {
    throw 'Expected distinct before/after parser assemblies.'
}
$ratios = foreach ($pair in 1..$Pairs) {
    $before = ($rows | Where-Object { $_.Pair -eq $pair -and $_.Variant -eq 'before' }).CpuMs | Measure-Object -Average
    $after = ($rows | Where-Object { $_.Pair -eq $pair -and $_.Variant -eq 'after' }).CpuMs | Measure-Object -Average
    [pscustomobject]@{ Pair=$pair; BeforeCpuMs=$before.Average; AfterCpuMs=$after.Average; Ratio=$after.Average/$before.Average }
}
$ratios | Export-Csv "$OutputPrefix.pairs.csv" -NoTypeInformation
$ratios | Format-Table -AutoSize
Write-Host 'Controls matched: input, harness IL, runtime, architecture, affinity, GC mode and TieredPGO setting.'
