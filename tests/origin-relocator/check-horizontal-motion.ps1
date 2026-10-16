param()
$ErrorActionPreference = 'Stop'
$taskOutputFolder=Join-Path (Split-Path (Split-Path -Parent $PSScriptRoot) -Parent) 'artifacts\origin-checks'
New-Item -ItemType Directory -Path $taskOutputFolder -Force | Out-Null
$taskRoot=Join-Path (Split-Path (Split-Path -Parent $PSScriptRoot) -Parent) 'src\OriginRelocator'
$taskEngine = Get-Content -LiteralPath (Join-Path $taskRoot 'RelocationEngine.cs') -Raw
$taskStart = $taskEngine.IndexOf('        internal static XYZ HorizontalOnly(')
$taskEnd = $taskEngine.IndexOf('        private static void Move(', $taskStart)
if ($taskStart -lt 0 -or $taskEnd -lt 0) { throw 'Production horizontal helpers not found.' }
$taskHelpers = $taskEngine.Substring($taskStart,$taskEnd-$taskStart)
$taskTolerance = [regex]::Match($taskEngine, 'public const double Tolerance = ([^;]+);').Groups[1].Value
if (-not $taskTolerance) { throw 'Production tolerance not found.' }
$taskHarness = '
using System;
namespace Arv.OriginRelocator
{
    // Managed vector for isolated arithmetic checks, without a Revit document.
    internal sealed class XYZ
    {
        public readonly double X, Y, Z;
        public static XYZ Zero { get { return new XYZ(0,0,0); } }
        public XYZ(double x,double y,double z) { X=x;Y=y;Z=z; }
        public double DotProduct(XYZ b) { return X*b.X+Y*b.Y+Z*b.Z; }
        public XYZ Multiply(double factor) { return new XYZ(X*factor,Y*factor,Z*factor); }
    }
    internal static class RelocationEngine
    {
        public const double Tolerance = __TOLERANCE__;
__HELPERS__
    }
    internal static class GeometryChecks
    {
        static void Equal(double actual,double expected)
        { if (Math.Abs(actual-expected)>1e-10) throw new Exception("Expected "+expected+"; actual "+actual); }
        static void Vector(XYZ actual,double x,double y)
        { Equal(actual.X,x); Equal(actual.Y,y); Equal(actual.Z,0); }
        static void Reject(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new Exception("Expected vertical-movement rejection.");
        }
        public static int Main()
        {
            Vector(RelocationEngine.HorizontalOnly(new XYZ(10,20,0),"test"),10,20);
            Reject(()=>RelocationEngine.HorizontalOnly(new XYZ(1,2,0.01),"test"));
            Vector(RelocationEngine.HorizontalOnly(new XYZ(1,2,RelocationEngine.Tolerance/10),"test"),1,2);
            Vector(RelocationEngine.HorizontalPlaneMove(new XYZ(1,0,0),4),4,0);
            Vector(RelocationEngine.HorizontalPlaneMove(new XYZ(0.6,0.8,0),5),3,4);
            XYZ inclined=RelocationEngine.HorizontalPlaneMove(new XYZ(0.6,0,0.8),3);
            Vector(inclined,5,0); Equal(inclined.DotProduct(new XYZ(0.6,0,0.8)),3);
            Reject(()=>RelocationEngine.HorizontalPlaneMove(new XYZ(0,0,1),2));
            Vector(RelocationEngine.HorizontalPlaneMove(new XYZ(0,0,1),0),0,0);
            XYZ normal=new XYZ(0.6,0,0.8), delta=new XYZ(10,7,0);
            XYZ plane=RelocationEngine.HorizontalPlaneMove(normal,delta.DotProduct(normal));
            XYZ remainder=new XYZ(delta.X-plane.X,delta.Y-plane.Y,delta.Z-plane.Z);
            Vector(remainder,0,7); Equal(remainder.DotProduct(normal),0);
            XYZ sectionAnnotation=RelocationEngine.InViewOnly(new XYZ(2,0,3),new XYZ(0,1,0),"section");
            Equal(sectionAnnotation.X,2); Equal(sectionAnnotation.Y,0); Equal(sectionAnnotation.Z,3);
            Reject(()=>RelocationEngine.HorizontalOnly(new XYZ(2,0,3),"same vector as model"));
            Vector(RelocationEngine.InViewOnly(new XYZ(2,3,0),new XYZ(0,0,1),"plan"),2,3);
            XYZ tiltedAnnotation=RelocationEngine.InViewOnly(new XYZ(1,4,-3),new XYZ(0,0.6,0.8),"tilted");
            Equal(tiltedAnnotation.X,1); Equal(tiltedAnnotation.Y,4); Equal(tiltedAnnotation.Z,-3);
            Reject(()=>RelocationEngine.InViewOnly(new XYZ(1,2,3),new XYZ(0,1,0),"wrong plane"));
            Reject(()=>RelocationEngine.InViewOnly(new XYZ(1,2,3),new XYZ(0,0,0),"missing plane"));
            Console.WriteLine("PASS: 15 horizontal/in-view motion checks on production helpers from RelocationEngine.cs.");
            Console.WriteLine("These arithmetic checks do not exercise Revit runtime behavior.");
            return 0;
        }
    }
}
'
$taskHarness = $taskHarness.Replace('__TOLERANCE__',$taskTolerance).Replace('__HELPERS__',$taskHelpers)
$taskSourcePath = Join-Path $taskOutputFolder 'horizontal-motion-harness.cs'
$taskExePath = Join-Path $taskOutputFolder 'horizontal-motion-harness.exe'
Set-Content -LiteralPath $taskSourcePath -Value $taskHarness -Encoding UTF8
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 "/out:$taskExePath" $taskSourcePath
if ($LASTEXITCODE -ne 0) { throw "Arithmetic harness compilation failed: $LASTEXITCODE." }
$taskOutput = & $taskExePath
$taskStatus = $LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskOutputFolder 'horizontal-motion-result.txt') -Encoding UTF8
$taskOutput
if ($taskStatus -ne 0) { throw "Horizontal arithmetic checks failed: $taskStatus." }
