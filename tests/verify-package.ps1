param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve({
    param($sender, $eventArgs)
    $simple = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name
    $path = Join-Path 'C:\Program Files\Autodesk\Revit 2022' ($simple + '.dll')
    if (Test-Path -LiteralPath $path) { return [Reflection.Assembly]::ReflectionOnlyLoadFrom($path) }
    return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)
})
$package = Join-Path $RepositoryRoot 'dist\Revit2022'
$plugins = Join-Path $package 'ARV'
$zipPath = Join-Path $RepositoryRoot 'dist\ARV_Revit_Plugin_Revit2022_v1.1.1.zip'
$source = [IO.File]::ReadAllText((Join-Path $RepositoryRoot 'src\ARV_Ribbon\Class1.cs'))
$buttons = [regex]::Matches($source, 'Add\(app, "([^"]+)", "([^"]+)", "([^"]+)", "([^"]+)"')
if ($buttons.Count -ne 11) { throw 'Expected 11 ribbon mappings.' }
$assemblies = @{}
$metadata = foreach ($file in Get-ChildItem -LiteralPath $plugins -Filter *.dll | Sort-Object Name) {
    $assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($file.FullName)
    $assemblies[$file.BaseName] = $assembly
    if ($assembly.GetName().Version.ToString() -ne '1.1.1.0') { throw ('Unexpected version: ' + $file.Name) }
    if ($assembly.GetName().ProcessorArchitecture.ToString() -ne 'Amd64') { throw ('Unexpected architecture: ' + $file.Name) }
    $refs = @($assembly.GetReferencedAssemblies() | Where-Object { $_.Name -in 'RevitAPI', 'RevitAPIUI' })
    if ($refs.Count -ne 2 -or @($refs | Where-Object {$_.Version.Major -ne 22}).Count) { throw ('Unexpected Revit references: ' + $file.Name) }
    $commands = @($assembly.GetExportedTypes() | Where-Object { @($_.GetInterfaces().FullName) -contains 'Autodesk.Revit.UI.IExternalCommand' })
    if ($file.BaseName -ne 'ARV_Ribbon' -and $commands.Count -ne 1) { throw ('Unexpected command count: ' + $file.Name) }
    [pscustomobject]@{File=$file.Name;Version=$assembly.GetName().Version.ToString();Architecture=$assembly.GetName().ProcessorArchitecture.ToString();Commands=@($commands.FullName);SHA256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()}
}
if ($metadata.Count -ne 12) { throw 'Expected 12 production DLLs.' }
foreach ($button in $buttons) {
    $name = $button.Groups[2].Value
    $type = $assemblies[$name].GetType($button.Groups[3].Value, $true)
    if (@($type.GetInterfaces().FullName) -notcontains 'Autodesk.Revit.UI.IExternalCommand') { throw ('Not a command: ' + $type.FullName) }
    if (-not $type.GetConstructor([type[]]@())) { throw ('No public default constructor: ' + $type.FullName) }
    $availabilityName = if ($name -eq 'BuildinOpener') {'BuildinOpener.SiteAvailability'} else {'ARV.Common.ProjectCommandAvailability'}
    $availability = $assemblies[$name].GetType($availabilityName, $true)
    if (@($availability.GetInterfaces().FullName) -notcontains 'Autodesk.Revit.UI.IExternalCommandAvailability') { throw 'Availability mapping mismatch.' }
    if (-not (Test-Path -LiteralPath (Join-Path $plugins $button.Groups[4].Value))) { throw 'Missing button icon.' }
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $package 'ARV_Ribbon.addin')
if ($manifest.RevitAddIns.AddIn.FullClassName -ne 'ARV_Ribbon.App') { throw 'Manifest class mismatch.' }
$appType = $assemblies['ARV_Ribbon'].GetType('ARV_Ribbon.App', $true)
if (@($appType.GetInterfaces().FullName) -notcontains 'Autodesk.Revit.UI.IExternalApplication') { throw 'Manifest application mismatch.' }
if ($manifest.RevitAddIns.AddIn.Assembly -ne 'C:\ProgramData\Autodesk\Revit\Addins\2022\ARV\ARV_Ribbon.dll') { throw 'Install path mismatch.' }
foreach ($line in Get-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw 'Invalid checksum line.' }
    $expected=$matches[1];$relative=$matches[2]
    if ((Get-FileHash -LiteralPath (Join-Path $package $relative)).Hash.ToLowerInvariant() -ne $expected) { throw ('Hash mismatch: '+$relative) }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
$zipFiles = 0
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        if ($entry.FullName -match '(^|/)\.\.(/|$)|^[\\/]|:') { throw 'Unsafe archive entry.' }
        $path = Join-Path $package $entry.FullName
        if (-not (Test-Path -LiteralPath $path)) { throw ('Unexpected archive entry: '+$entry.FullName) }
        $stream = $entry.Open();$sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
        finally { $stream.Dispose();$sha.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()) { throw ('Archive hash mismatch: '+$entry.FullName) }
        $zipFiles++
    }
} finally { $archive.Dispose() }
if ($zipFiles -ne @(Get-ChildItem -LiteralPath $package -Recurse -File).Count) { throw 'Incomplete ZIP.' }
if (Get-ChildItem -LiteralPath $plugins -File | Where-Object { $_.Name -match '^RevitAPI|CreateCeil|FamilyContainerModel|ExportNavis' }) { throw 'Excluded binary in package.' }
$result = [pscustomobject]@{Version='1.1.1';ButtonMappings=$buttons.Count;Assemblies=$metadata;ZipFiles=$zipFiles;ZipSHA256=(Get-FileHash -LiteralPath $zipPath).Hash.ToLowerInvariant();NativeExecution='Not run: existing Revit window capture timed out twice.'}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $RepositoryRoot 'docs\release-verification.json') -Encoding UTF8
'PASS: 12 x64 DLLs for Revit API 22; 11 command/availability/icon mappings; manifest; checksums; '+$zipFiles+' ZIP files match the package.'
