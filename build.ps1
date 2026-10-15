param(
    [string]$RevitApiPath = 'C:\Program Files\Autodesk\Revit 2022',
    [string]$MSBuildPath,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$IncludeExperimental
)
$ErrorActionPreference = 'Stop'
if (-not $MSBuildPath) {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $locator)) { throw 'Install Visual Studio 2022 with .NET desktop build tools.' }
    $MSBuildPath = & $locator -latest -products '*' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $MSBuildPath -or -not (Test-Path -LiteralPath $MSBuildPath)) { throw 'MSBuild not found.' }
foreach ($name in 'RevitAPI.dll', 'RevitAPIUI.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $RevitApiPath $name))) { throw ('Missing Revit 2022 API: ' + $name) }
}
$output = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$results = @()
foreach ($project in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Recurse -Filter *.csproj | Sort-Object FullName) {
    $projectName = $project.Directory.Name
    if (-not $IncludeExperimental -and $projectName -in 'CreateCeil','FamilyContainerModel') { continue }
    $logFile = Join-Path $output ($projectName + '.log')
    & $MSBuildPath $project.FullName /t:Rebuild /nologo /v:minimal "/p:Configuration=$Configuration" /p:Platform=AnyCPU /p:PlatformTarget=x64 "/p:RevitApiPath=$RevitApiPath" *> $logFile
    $code = $LASTEXITCODE
    $results += [pscustomobject]@{ Project=$projectName; ExitCode=$code; Log=$projectName+'.log' }
    Write-Output ($projectName + ': ' + $(if ($code -eq 0) {'PASS'} else {'FAIL'}))
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'build-results.json') -Encoding utf8
if ($results.Where({$_.ExitCode -ne 0}).Count) { throw 'Build failures: see artifacts/build-results.json and project logs.' }
$package = Join-Path $output 'package'
$pluginFolder = Join-Path $package 'ARV'
New-Item -ItemType Directory -Path $pluginFolder -Force | Out-Null
foreach ($project in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Recurse -Filter *.csproj) {
    if (-not $IncludeExperimental -and $project.Directory.Name -in 'CreateCeil','FamilyContainerModel') { continue }
    [xml]$xml = Get-Content -LiteralPath $project.FullName
    $assemblyName = @($xml.Project.PropertyGroup.AssemblyName | Where-Object {$_})[0]
    $dll = Join-Path $project.Directory.FullName ("bin\$Configuration\$assemblyName.dll")
    if (-not (Test-Path -LiteralPath $dll)) { throw ('Build output missing: '+$dll) }
    Copy-Item -LiteralPath $dll -Destination $pluginFolder -Force
}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src\ARV_Ribbon\Images') -Filter *.png | Copy-Item -Destination $pluginFolder -Force
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets\installed-icons') -Filter *.png | Copy-Item -Destination $pluginFolder -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ARV_Ribbon.addin') -Destination $package -Force
Write-Output ('Staged package: '+$package)
