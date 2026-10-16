param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'src\PlaceViewsOnSheet\bin\Release\PlaceViewsOnSheet.dll'))
$type=$assembly.GetType('ARV.Common.UiSupport',$true)
$method=$type.GetMethod('GetCenteredPixelBounds',[Reflection.BindingFlags]'Static,NonPublic')
$cases=@(
 @(0,0,1920,1040,540,500),
 @(1920,0,2560,1400,700,670),
 @(-2560,-120,2560,1400,760,740),
 @(40,32,1880,1008,540,480),
 @(0,0,480,320,760,740),
 @(1920,0,1920,1040,810,750),
 @(-3840,0,3840,2080,1080,1000),
 @(0,-1440,2560,1400,620,540)
)
foreach($c in $cases) {
 $area=[Windows.Rect]::new($c[0],$c[1],$c[2],$c[3]);$size=[Windows.Size]::new($c[4],$c[5])
 $bounds=$method.Invoke($null,@($area,$size))
 if ([math]::Abs(($bounds.Left+$bounds.Width/2)-($area.Left+$area.Width/2)) -gt 0.001 -or [math]::Abs(($bounds.Top+$bounds.Height/2)-($area.Top+$area.Height/2)) -gt 0.001) {throw 'Window is not centered'}
 if (-not $area.Contains($bounds)) {throw 'Window extends outside work area'}
}
$source=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'src\Common\UiSupport.cs'))
if ($source -notmatch 'MonitorFromWindow\(helper.Owner, 2\)' -or $source -notmatch 'window.ContentRendered' -or $source -notmatch 'finally' -or $source -notmatch 'SetThreadDpiAwarenessContext\(oldContext\)') {throw 'Missing owner, final-layout hook or DPI context restoration'}
$casesList=Get-Content -LiteralPath (Join-Path $RepositoryRoot 'tests\ui-windows.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach($case in $casesList) {
 [xml]$xml=Get-Content -LiteralPath (Join-Path $RepositoryRoot ('src\'+$case.project+'\'+$case.file)) -Raw -Encoding UTF8
 if($xml.DocumentElement.WindowStartupLocation -ne 'Manual') {throw ('Conflicting startup positioning: '+$case.project)}
}
# Скрытые окна отдельного тестового процесса; окно Revit не используется.
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ArvPlacementProbe {
 [StructLayout(LayoutKind.Sequential)] public struct R { public int Left, Top, Right, Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct M { public int Size; public R Monitor, Work; public uint Flags; }
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr monitor,ref M info);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out R rect);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
"@
$owner=[Windows.Window]::new();$child=[Windows.Window]::new()
try {
 $ownerHandle=[Windows.Interop.WindowInteropHelper]::new($owner).EnsureHandle()
 [Windows.Interop.WindowInteropHelper]::new($child).Owner=$ownerHandle
 [void]$type.GetMethod('Initialize').Invoke($null,@($child))
 $childHandle=[Windows.Interop.WindowInteropHelper]::new($child).EnsureHandle()
 $previous=[ArvPlacementProbe]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
 try {
  $info=[ArvPlacementProbe+M]::new();$info.Size=[Runtime.InteropServices.Marshal]::SizeOf($info)
  if (-not [ArvPlacementProbe]::GetMonitorInfo([ArvPlacementProbe]::MonitorFromWindow($ownerHandle,2),[ref]$info)) {throw 'Monitor info failed'}
  $work=$info.Work
  [void][ArvPlacementProbe]::SetWindowPos($ownerHandle,[IntPtr]::Zero,$work.Left+80,$work.Bottom-280,400,220,0x14)
  [void][ArvPlacementProbe]::SetWindowPos($childHandle,[IntPtr]::Zero,$work.Left+20,$work.Top+20,540,480,0x14)
 } finally {if($previous -ne [IntPtr]::Zero){[void][ArvPlacementProbe]::SetThreadDpiAwarenessContext($previous)}}
 $center=$type.GetMethod('CenterOnOwnerMonitor',[Reflection.BindingFlags]'Static,NonPublic')
 [void]$center.Invoke($null,@($child))
 $previous=[ArvPlacementProbe]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
 try {
  $actual=[ArvPlacementProbe+R]::new()
  if (-not [ArvPlacementProbe]::GetWindowRect($childHandle,[ref]$actual)) {throw 'Hidden window bounds failed'}
  if ([math]::Abs(($actual.Left+$actual.Right)/2.0-($work.Left+$work.Right)/2.0) -gt 1 -or [math]::Abs(($actual.Top+$actual.Bottom)/2.0-($work.Top+$work.Bottom)/2.0) -gt 1) {throw 'Native hidden dialog did not center on owner monitor'}
 } finally {if($previous -ne [IntPtr]::Zero){[void][ArvPlacementProbe]::SetThreadDpiAwarenessContext($previous)}}
} finally {$child.Close();$owner.Close()}
"PASS: 8 centered work-area calculations; $($casesList.Count) dialogs use shared manual placement; native placement verified with hidden test windows and displaced owner. Revit not used; real Revit/mixed-DPI monitor scenarios remain manual."
