param([string]$Baseline, [string]$Reviewed, [string]$Fixed, [string]$Output = "$PSScriptRoot/regressions.jsonl")
$ErrorActionPreference = 'Stop'
$paths = @{ baseline=$Baseline; reviewed=$Reviewed; fixed=$Fixed }
$results = [Collections.Generic.List[string]]::new()
foreach ($mode in @('x', 'spaces', 'async-json')) {
    for ($sample=0; $sample -lt 3; $sample++) {
        $order = if ($sample % 2 -eq 0) { @('baseline','reviewed','fixed') } else { @('fixed','reviewed','baseline') }
        foreach ($label in $order) {
            $result = & dotnet $paths[$label] --regression $mode $label
            if ($LASTEXITCODE -ne 0) { throw "Worker failed: $label $mode" }
            $results.Add($result)
            Write-Output $result
        }
    }
}
$results | Set-Content -LiteralPath $Output
