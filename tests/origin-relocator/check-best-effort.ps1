param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path (Split-Path -Parent $PSScriptRoot) -Parent
$taskOut=Join-Path $taskRoot 'artifacts\origin-checks';New-Item -ItemType Directory -Path $taskOut -Force | Out-Null
$taskSource=Get-Content -LiteralPath (Join-Path $taskRoot 'src\OriginRelocator\RelocationEngine.cs') -Raw -Encoding UTF8
function Read-Block($signature){
 $a=$taskSource.IndexOf($signature);$b=$taskSource.IndexOf('{',$a);$depth=0
 for($i=$b;$i -lt $taskSource.Length;$i++){if($taskSource[$i] -eq '{'){$depth++};if($taskSource[$i] -eq '}'){$depth--;if($depth -eq 0){return $taskSource.Substring($a,$i-$a+1)}}}
 throw ('Block missing: '+$signature)
}
$methods=(Read-Block 'private static bool RunTransaction(')+(Read-Block 'internal static void VerifyOriginalElements(')
$wrapper='using System;using System.Collections.Generic;using System.Linq;using Autodesk.Revit.DB;namespace Arv.OriginRelocator {internal static class RelocationEngine {'+$methods+' public static bool Stage(Document d,string n,RelocationReport r,RelocationPlan p,Action body){return RunTransaction(d,n,r,p,body);}}}'
$taskCode=Join-Path $taskOut 'best-effort-engine.cs';[IO.File]::WriteAllText($taskCode,$wrapper,[Text.UTF8Encoding]::new($true))
$taskExe=Join-Path $taskOut 'best-effort-tests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /out:$taskExe (Join-Path $PSScriptRoot 'BestEffortDoubles.cs') (Join-Path $taskRoot 'src\OriginRelocator\RelocationProgress.cs') (Join-Path $taskRoot 'src\OriginRelocator\RelocationDiagnostics.cs') $taskCode
if($LASTEXITCODE -ne 0){throw 'Best effort checks compile failed'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Best effort checks failed'}
