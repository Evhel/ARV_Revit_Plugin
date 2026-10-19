param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path (Split-Path -Parent $PSScriptRoot) -Parent
$taskOutputFolder=Join-Path $taskRoot 'artifacts\origin-checks'
New-Item -ItemType Directory -Path $taskOutputFolder -Force | Out-Null
$taskEngine=Get-Content -LiteralPath (Join-Path $taskRoot 'src\OriginRelocator\RelocationEngine.cs') -Raw -Encoding UTF8
$taskStart=$taskEngine.IndexOf('        internal static void VerifyOriginalElements(')
$taskBrace=$taskEngine.IndexOf('{',$taskStart);$taskDepth=0;$taskEnd=-1
for($i=$taskBrace;$i -lt $taskEngine.Length;$i++){
 if($taskEngine[$i] -eq '{'){$taskDepth++}
 if($taskEngine[$i] -eq '}'){$taskDepth--;if($taskDepth -eq 0){$taskEnd=$i+1;break}}
}
if($taskStart -lt 0 -or $taskEnd -lt 0){throw 'Original-element guard not found'}
$taskMethod=$taskEngine.Substring($taskStart,$taskEnd-$taskStart)
$taskGuard='using System;using System.Collections.Generic;using System.Linq;using Autodesk.Revit.DB;namespace Arv.OriginRelocator {internal static class RelocationEngine {'+$taskMethod+'}}'
$taskGuardPath=Join-Path $taskOutputFolder 'invalid-dimension-guard.cs'
[IO.File]::WriteAllText($taskGuardPath,$taskGuard,[Text.UTF8Encoding]::new($true))
$taskExe=Join-Path $taskOutputFolder 'invalid-dimension-tests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /out:$taskExe (Join-Path $taskRoot 'src\OriginRelocator\StrictFailures.cs') (Join-Path $PSScriptRoot 'InvalidDimensionDoubles.cs') $taskGuardPath
if($LASTEXITCODE -ne 0){throw 'Recovery checks compile failed'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Recovery checks failed'}
