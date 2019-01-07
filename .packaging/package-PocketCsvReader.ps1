param(
    [parameter(Mandatory=$true)]
    [string]$version
)

$root = (split-path -parent $MyInvocation.MyCommand.Definition)

Write-Host "Calculating dependencies ..."

$dependencies = @{}
$solutionRoot = Join-Path ($root) ".."
$projects = Get-ChildItem $solutionRoot | ?{ $_.PSIsContainer -and $_.Name -like "PocketCsvReader.*" -and $_.Name -notLike "*.Testing*"} | select Name, FullName
foreach($proj in $projects)
{
    $projName = $proj.name
    Write-Host "Looking for dependencies in project $projName ..."
    $path = Join-Path ($proj.FullName) "packages.config"
        
    if(Test-Path $path)
    {
        [xml]$packages = Get-Content $path
        foreach($package in $packages.FirstChild.NextSibling.ChildNodes)
        {
            if (!$dependencies.ContainsKey($package.id)) {$dependencies.add($package.id, "<dependency id=""$($package.id)"" version=""$(($package.allowedVersions, $package.version -ne $null)[0])"" />")}
        }
    }
    
}

Write-Host "Found $($dependencies.Count) dependencies ..."
$depList = $dependencies.Values -join [Environment]::NewLine + "`t`t"

$thisYear = get-date -Format yyyy
Write-Host "Setting copyright until $thisYear"

#For PocketCsvReader
Write-Host "Packaging PocketCsvReader"
$lib = "$root\bin\lib\net461\"
If (Test-Path $lib)
{
	Remove-Item $lib -recurse
}
new-item -Path $lib -ItemType directory
new-item -Path $root\..\.nupkg -ItemType directory -force
Copy-Item $root\..\PocketCsvReader\bin\Debug\PocketCsvReader.dll $lib

Write-Host "Setting .nuspec version tag to $version"

$content = (Get-Content $root\PocketCsvReader.nuspec -Encoding UTF8) 
$content = $content -replace '\$version\$',$version
$content = $content -replace '\$thisYear\$',$thisYear
$content = $content -replace '\$depList\$',$depList

$content | Out-File $root\bin\PocketCsvReader.compiled.nuspec -Encoding UTF8

& NuGet.exe pack $root\bin\PocketCsvReader.compiled.nuspec -Version $version -OutputDirectory $root\..\.nupkg
Write-Host "Package for PocketCsvReader.Framework is ready"