param (
    [string]$PackageName, # The name of the NuGet package
    [string[]]$Versions,  # List of versions to download
    [string]$OutputFolder = "./NuGetPackages" # Output folder for organizing dlls
)

# Ensure the output folder exists
if (!(Test-Path -Path $OutputFolder)) {
    New-Item -ItemType Directory -Path $OutputFolder
}

foreach ($version in $Versions) {
    Write-Host "Processing version $version of $PackageName..."

    # Create folder for the specific version
    $versionFolder = Join-Path -Path $OutputFolder -ChildPath $version
    if (!(Test-Path -Path $versionFolder)) {
        New-Item -ItemType Directory -Path $versionFolder
    }

    # Download the package using `nuget.exe`
    $nugetPath = "nuget.exe"
    if (!(Get-Command $nugetPath -ErrorAction SilentlyContinue)) {
        Write-Error "nuget.exe not found. Ensure it's installed and in your PATH."
        break
    }

    # Download the package into a temp folder
    $tempFolder = Join-Path -Path $OutputFolder -ChildPath "temp_$version"
    if (!(Test-Path -Path $tempFolder)) {
        New-Item -ItemType Directory -Path $tempFolder
    }

    Write-Host "Downloading $PackageName version $version..."
    & $nugetPath install $PackageName -Version $version -OutputDirectory $tempFolder -Source "https://api.nuget.org/v3/index.json"

    # Locate the .dll file
    $packageFolder = Get-ChildItem -Path $tempFolder -Directory | Where-Object { $_.Name -like "$PackageName.$version*" }
    if ($packageFolder -eq $null) {
        Write-Error "Failed to find the downloaded package for version $version."
        continue
    }

    $dllPath = Get-ChildItem -Path $packageFolder.FullName -Recurse -Filter "*.dll" | Select-Object -First 1
    if ($dllPath -eq $null) {
        Write-Error "No DLL found for $PackageName version $version."
        continue
    }

    # Move the .dll file to the version folder
    Copy-Item -Path $dllPath.FullName -Destination $versionFolder -Force
    Write-Host "DLL for version $version placed in $versionFolder."

    # Clean up temp folder
    Remove-Item -Recurse -Force $tempFolder
}

Write-Host "Completed processing all versions."
