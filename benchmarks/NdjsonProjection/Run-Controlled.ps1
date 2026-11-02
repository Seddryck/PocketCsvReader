param([string]$Before, [string]$After, [string]$Output = "$PSScriptRoot/results.csv", [int]$Pairs = 3)
$ErrorActionPreference = 'Stop'
'Label,Format,Projected,Raw,Size,Rows,Buffer,Operations,ElapsedMs,CpuMs,AllocatedBytes,InputHash' | Set-Content -LiteralPath $Output
foreach ($size in @('small', 'cross', 'large')) {
    foreach ($format in @('ndjson', 'json')) {
        foreach ($projected in @('false', 'true')) {
            foreach ($raw in @('true', 'false')) {
                for ($pair = 0; $pair -lt $Pairs; $pair++) {
                    $order = if ($pair % 2 -eq 0) { @('before', 'after') } else { @('after', 'before') }
                    foreach ($label in $order) {
                        $assembly = if ($label -eq 'before') { $Before } else { $After }
                        $result = & dotnet $assembly $label $format $projected $raw $size
                        if ($LASTEXITCODE -ne 0) { throw "Worker failed: $label $format $projected $raw $size" }
                        $result | Add-Content -LiteralPath $Output
                        Write-Output $result
                    }
                }
            }
        }
    }
}
