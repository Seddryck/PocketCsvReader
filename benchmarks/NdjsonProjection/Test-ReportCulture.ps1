$ErrorActionPreference = 'Stop'
$folder = Join-Path ([IO.Path]::GetTempPath()) ('ndjson-culture-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$originalCulture = [Threading.Thread]::CurrentThread.CurrentCulture
try {
    $baseline = $null
    foreach ($name in @('en-US', 'fr-BE', '')) {
        [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($name)
        $report = Join-Path $folder 'report.md'
        $summary = Join-Path $folder 'summary.csv'
        '<!-- results:start --><!-- results:end --><!-- regressions:start --><!-- regressions:end -->' | Set-Content -LiteralPath $report
        & "$PSScriptRoot/Summarize.ps1" -OutputFile $summary -ReportFile $report | Out-Null
        & "$PSScriptRoot/Summarize-Regressions.ps1" -OutputFile (Join-Path $folder 'regressions.csv') -ReportFile $report | Out-Null
        $regression = Get-Content -LiteralPath (Join-Path $folder 'regressions.csv') -Raw
        if ($null -eq $baseline) { $baselineRegression = $regression }
        if ($regression -cne $baselineRegression) { throw "Culture changed regression output: $name" }
        $actual = Get-Content -LiteralPath $summary -Raw
        $table = Get-Content -LiteralPath $report -Raw
        if ($null -eq $baseline) { $baseline = $actual; $baselineTable = $table }
        if ($actual -cne $baseline -or $table -cne $baselineTable) { throw "Culture changed output: $name" }
        $rows = Import-Csv -LiteralPath $summary
        foreach ($row in $rows) {
            foreach ($field in @('CpuBeforeMs','CpuAfterMs','CpuChangePercent','ElapsedBeforeMs','ElapsedAfterMs','ElapsedChangePercent')) {
                if ($row.$field -notmatch '^-?\d+\.\d+$') { throw "Invalid invariant number: $($row.$field)" }
            }
            $change = [double]::Parse($row.CpuChangePercent, [Globalization.CultureInfo]::InvariantCulture)
            if ($change -lt -100) { throw 'Impossible reduction in CPU' }
        }
        $raw = Import-Csv "$PSScriptRoot/results.csv" | Where-Object { $_.Format -eq 'ndjson' -and $_.Size -eq 'small' -and $_.Projected -eq 'True' -and $_.Raw -eq 'True' -and $_.Label -eq 'before' }
        $mean = ($raw | ForEach-Object { [double]::Parse($_.CpuMs, [Globalization.CultureInfo]::InvariantCulture) } | Measure-Object -Average).Average
        $result = $rows | Where-Object { $_.Format -eq 'ndjson' -and $_.Size -eq 'small' -and $_.Projected -eq 'True' -and $_.Raw -eq 'True' }
        $expected = $mean.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)
        if ($result.CpuBeforeMs -cne $expected) { throw "Incorrect mean: $($result.CpuBeforeMs) versus $expected" }
        if (!$table.Contains("| small | raw | $expected →")) { throw 'Markdown does not contain the correct mean' }
    }
    Write-Output 'Benchmark report culture tests passed (en-US, fr-BE, invariant).'
}
finally {
    [Threading.Thread]::CurrentThread.CurrentCulture = $originalCulture
    # The unique scratch directory is deliberately retained for inspection; no
    # caller-provided or computed directory is recursively deleted by this test.
}
