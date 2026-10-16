param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$command=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'src\AreaReinforcementHosts\Commands.cs'))
# The production filter/Resolve are extracted unchanged; command UI remains manual.
$filterStart=$command.IndexOf('    internal sealed class AreaFilter')
$filterEnd=$command.IndexOf('    internal sealed class HostFilter')
$resolveStart=$command.IndexOf('        internal static AreaReinforcement Resolve')
$resolveEnd=$command.IndexOf('        internal static void Report')
$source='using System;using System.Collections.Generic;using System.Linq;using ARV.AreaReinforcementHosts;using Autodesk.Revit.DB;using Autodesk.Revit.DB.Structure;using Autodesk.Revit.UI.Selection;namespace ARV.AreaReinforcementHosts {internal static class SelectionSupport {'+$command.Substring($resolveStart,$resolveEnd-$resolveStart)+'}'+$command.Substring($filterStart,$filterEnd-$filterStart)+'}'
$processor=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'src\AreaReinforcementHosts\OutsideHostWarnings.cs'))
$doubles=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AreaInteractionDoubles.cs'))
Add-Type -TypeDefinition ($source + [Environment]::NewLine + $processor.Replace('using Autodesk.Revit.DB;','') + [Environment]::NewLine + $doubles.Substring($doubles.IndexOf('namespace Autodesk.Revit.DB {')))
[AreaInteractionRegression]::Run()