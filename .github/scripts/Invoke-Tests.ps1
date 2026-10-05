[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$tests = @(
    @{ Project = 'PocketCsvReader.Testing/PocketCsvReader.Testing.csproj'; Name = 'PocketCsvReader'; Include = '[PocketCsvReader]*' },
    @{ Project = 'PocketCsvReader.Ndjson.Testing/PocketCsvReader.Ndjson.Testing.csproj'; Name = 'PocketCsvReader.Ndjson'; Include = '[PocketCsvReader*]*' },
    @{ Project = 'PocketCsvReader.FixedWidth.Testing/PocketCsvReader.FixedWidth.Testing.csproj'; Name = 'PocketCsvReader.FixedWidth'; Include = '[PocketCsvReader.FixedWidth]*' },
    @{ Project = 'PocketCsvReader.KeyValue.Testing/PocketCsvReader.KeyValue.Testing.csproj'; Name = 'PocketCsvReader.KeyValue'; Include = '[PocketCsvReader.KeyValue]*' },
    @{ Project = 'PocketCsvReader.WebLogs.Testing/PocketCsvReader.WebLogs.Testing.csproj'; Name = 'PocketCsvReader.WebLogs'; Include = '[PocketCsvReader.WebLogs]*' }
)

New-Item -ItemType Directory -Force artifacts/coverage, artifacts/test-results | Out-Null

foreach ($test in $tests) {
    foreach ($framework in @('net8.0', 'net9.0', 'net10.0')) {
        $coverage = Join-Path $PWD "artifacts/coverage/$($test.Name).$framework.cobertura.xml"
        dotnet test $test.Project `
            --configuration Release `
            --framework $framework `
            --no-build `
            --no-restore `
            --nologo `
            --results-directory artifacts/test-results `
            --logger "trx;LogFileName=$($test.Name).$framework.trx" `
            -p:CollectCoverage=true `
            "-p:Include=$($test.Include)" `
            -p:CoverletOutputFormat=cobertura `
            -p:Threshold=10 `
            -p:ThresholdType=line `
            "-p:CoverletOutput=$coverage"

        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}
