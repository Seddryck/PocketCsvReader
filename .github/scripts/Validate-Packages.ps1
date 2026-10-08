[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageIds = @(
    'PocketCsvReader',
    'PocketCsvReader.Json',
    'PocketCsvReader.Ndjson',
    'PocketCsvReader.Arrow',
    'PocketCsvReader.FixedWidth',
    'PocketCsvReader.KeyValue',
    'PocketCsvReader.WebLogs'
)

$packageDirectoryPath = (Resolve-Path -LiteralPath $PackageDirectory).Path
$expectedFiles = foreach ($packageId in $packageIds) {
    "$packageId.$Version.nupkg"
    "$packageId.$Version.snupkg"
}

$actualFiles = @(
    Get-ChildItem -LiteralPath $packageDirectoryPath -File |
        Where-Object Extension -In '.nupkg', '.snupkg' |
        Select-Object -ExpandProperty Name
)

$differences = @(Compare-Object $expectedFiles $actualFiles)
if ($differences.Count -ne 0) {
    $details = $differences | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }
    throw "The NuGet package set is incomplete or contains unexpected files:`n$($details -join "`n")"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($packageId in $packageIds) {
    $packagePath = Join-Path $packageDirectoryPath "$packageId.$Version.nupkg"
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)

    try {
        $entryNames = @($archive.Entries | Select-Object -ExpandProperty FullName)
        foreach ($framework in @('net8.0', 'net9.0', 'net10.0')) {
            $assemblyPath = "lib/$framework/$packageId.dll"
            if ($assemblyPath -notin $entryNames) {
                throw "$packagePath does not contain $assemblyPath."
            }
        }

        $nuspecEntry = $archive.Entries | Where-Object FullName -EQ "$packageId.nuspec"
        if ($null -eq $nuspecEntry) {
            throw "$packagePath does not contain $packageId.nuspec."
        }

        $stream = $nuspecEntry.Open()
        $reader = [System.IO.StreamReader]::new($stream)
        try {
            [xml] $nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }

        if ([string] $nuspec.package.metadata.id -cne $packageId) {
            throw "$packagePath contains package ID '$($nuspec.package.metadata.id)' instead of '$packageId'."
        }

        if ([string] $nuspec.package.metadata.version -cne $Version) {
            throw "$packagePath contains version '$($nuspec.package.metadata.version)' instead of '$Version'."
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Host "Validated $($packageIds.Count) NuGet packages and their symbol packages for version $Version."
