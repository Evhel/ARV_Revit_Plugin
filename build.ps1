param(
    [string]$RevitApiPath = 'C:\Program Files\Autodesk\Revit 2022',
    [string]$MSBuildPath
)
$ErrorActionPreference = 'Stop'
$version = '1.1.1'
if (-not $MSBuildPath) {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $locator)) { throw 'Install Visual Studio 2022 with .NET desktop development.' }
    $MSBuildPath = & $locator -latest -products '*' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $MSBuildPath -or -not (Test-Path -LiteralPath $MSBuildPath)) { throw 'MSBuild not found.' }
foreach ($name in 'RevitAPI.dll', 'RevitAPIUI.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $RevitApiPath $name))) { throw ('Missing Revit 2022 API: ' + $name) }
}
$output = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$log = Join-Path $output 'solution-build.log'
& $MSBuildPath (Join-Path $PSScriptRoot 'ARV_Revit_Plugin.sln') /t:Rebuild /nologo /v:minimal /p:Configuration=Release "/p:RevitApiPath=$RevitApiPath" *> $log
if ($LASTEXITCODE -ne 0) { throw ('Build failed: ' + $log) }
if (Select-String -LiteralPath $log -Pattern ': warning ') { throw ('Build has warnings: ' + $log) }
# This directory contains only generated deployment files. Never touch installed add-ins.
$dist = Join-Path $PSScriptRoot 'dist'
$package = [IO.Path]::GetFullPath((Join-Path $dist 'Revit2022'))
if (-not $package.StartsWith([IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package path.' }
if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Recurse -Force }
$pluginFolder = Join-Path $package 'ARV'
New-Item -ItemType Directory -Path $pluginFolder -Force | Out-Null
$projects = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Recurse -Filter *.csproj
if ($projects.Count -ne 12) { throw 'Expected exactly 12 production projects.' }
foreach ($project in $projects) {
    [xml]$xml = Get-Content -LiteralPath $project.FullName
    $name = @($xml.Project.PropertyGroup.AssemblyName | Where-Object {$_})[0]
    $dll = Join-Path $project.Directory.FullName ("bin\Release\$name.dll")
    if (-not (Test-Path -LiteralPath $dll)) { throw ('Missing output: ' + $dll) }
    Copy-Item -LiteralPath $dll -Destination $pluginFolder -Force
}
$icons = @('ARV.png','StartWall.png','FinishWall.png','HideKat.png','HideSubK.png','NewSheet.png','CopyView.png','ViewsToSheets.png','ViewsCut.png','ViewNumber.png','Worksets.png')
foreach ($icon in $icons) {
    $path = Join-Path $PSScriptRoot ('src\ARV_Ribbon\Images\'+$icon)
    if ($icon -eq 'ARV.png') { $path = Join-Path $PSScriptRoot 'assets\installed-icons\ARV.png' }
    Copy-Item -LiteralPath $path -Destination $pluginFolder
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ARV_Ribbon.addin') -Destination $package
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'INSTALL_RU.md') -Destination $package
$hashes = Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.FullName.Substring($package.Length + 1).Replace('\','/')
}
$hashes | Set-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -Encoding utf8
$zip = Join-Path $dist ("ARV_Revit_Plugin_Revit2022_v$version.zip")
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -Force
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $zip -Leaf) | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output ('PASS: 12 projects; package: ' + $package)
Write-Output ('ZIP: ' + $zip)
