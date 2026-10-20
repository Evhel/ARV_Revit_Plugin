param()
$ErrorActionPreference='Stop'
$taskOutputFolder=Join-Path (Split-Path (Split-Path -Parent $PSScriptRoot) -Parent) 'artifacts\origin-checks'
New-Item -ItemType Directory -Path $taskOutputFolder -Force | Out-Null
$taskRoot=Join-Path (Split-Path (Split-Path -Parent $PSScriptRoot) -Parent) 'src\OriginRelocator'
function Get-TaskBlock([string]$source,[string]$signature){
    $taskStart=$source.IndexOf($signature)
    if($taskStart -lt 0){throw "Block missing: $signature"}
    $taskBrace=$source.IndexOf('{',$taskStart);$taskDepth=0
    for($taskIndex=$taskBrace;$taskIndex -lt $source.Length;$taskIndex++){
        if($source[$taskIndex] -eq '{'){$taskDepth++}
        if($source[$taskIndex] -eq '}'){$taskDepth--;if($taskDepth -eq 0){return $source.Substring($taskStart,$taskIndex-$taskStart+1)}}
    }
    throw 'Unclosed block.'
}
$taskSource=Get-Content -LiteralPath (Join-Path $taskRoot 'PresentationState.cs') -Raw
$taskEngine=Get-Content -LiteralPath (Join-Path $taskRoot 'RelocationEngine.cs') -Raw
$taskClasses=@(
    Get-TaskBlock $taskSource 'internal sealed class ViewportLabelState'
    Get-TaskBlock $taskSource 'internal sealed class ViewGraphicState'
) -join [Environment]::NewLine
$taskMethods=@(
    Get-TaskBlock $taskEngine 'internal static XYZ SheetOffset('
    Get-TaskBlock $taskEngine 'internal static XYZ LabelOffsetFromSheet('
    Get-TaskBlock $taskEngine 'internal static XYZ SectionMarkerTranslation('
    Get-TaskBlock $taskEngine 'internal static XYZ HorizontalPlaneMove('
    Get-TaskBlock $taskEngine 'internal static void RestoreViewports('
    Get-TaskBlock $taskEngine 'private static void VerifySheetViewports('
    Get-TaskBlock $taskEngine 'internal static void VerifyViewportPositions('
) -join [Environment]::NewLine
$taskHarness='
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Autodesk.Revit.DB {
    internal class XYZ {
        public readonly double X,Y,Z;
        public static readonly XYZ Zero=new XYZ(0,0,0);
        public XYZ(double x,double y,double z){X=x;Y=y;Z=z;}
        public XYZ Multiply(double k){return new XYZ(X*k,Y*k,Z*k);}
        public double DotProduct(XYZ b){return X*b.X+Y*b.Y+Z*b.Z;}
        public double GetLength(){return Math.Sqrt(DotProduct(this));}
        public static XYZ operator +(XYZ a,XYZ b){return new XYZ(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
        public static XYZ operator -(XYZ a,XYZ b){return new XYZ(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
    }
    internal class ElementId {
        public readonly int IntegerValue; public static readonly ElementId InvalidElementId=new ElementId(-1);
        public ElementId(int id){IntegerValue=id;}
        public override bool Equals(object o){return o is ElementId && ((ElementId)o).IntegerValue==IntegerValue;}
        public override int GetHashCode(){return IntegerValue;}
        public static bool operator ==(ElementId a,ElementId b){return Object.Equals(a,b);}
        public static bool operator !=(ElementId a,ElementId b){return !Object.Equals(a,b);}
    }
    internal enum ViewportRotation {None,Clockwise,Counterclockwise}
    internal class Transform {public XYZ OfPoint(XYZ p){return p;}}
    internal class BoundingBoxXYZ {public XYZ Min,Max;public Transform Transform=new Transform();}
    internal class Outline:IDisposable {public bool IsEmpty;public XYZ MinimumPoint,MaximumPoint;public void Dispose(){}}
    internal class Element {
        public Document Document;public ElementId Id;public BoundingBoxXYZ Box;public ElementId Target=new ElementId(99);
        public BoundingBoxXYZ get_BoundingBox(View view){return Box;}
    }
    internal class View:Element {public XYZ ViewDirection=new XYZ(0,0,1);}
    internal class Viewport:Element {
        public ViewportRotation Rotation;
        public XYZ Center=new XYZ(.5,.75,0),LabelOffset=new XYZ(.1,-.2,0);
        public ElementId ViewId=new ElementId(70),SheetId=new ElementId(80);
        public bool Pinned,HasLabel=true;
        public double LabelLineLength=.3,BodyHalfWidth=.2,BodyHalfHeight=.3,TextWidth=.1;
        public XYZ GetBoxCenter(){return Center;}
        public void SetBoxCenter(XYZ p){if(Pinned)throw new Exception("Pinned viewport moved");Center=p;}
        XYZ Rotate(XYZ p){if(Rotation==ViewportRotation.Clockwise)return new XYZ(p.Y,-p.X,0);if(Rotation==ViewportRotation.Counterclockwise)return new XYZ(-p.Y,p.X,0);return p;}
        public Outline GetLabelOutline(){
            if(!HasLabel)return new Outline {IsEmpty=true};
            XYZ origin=Center+Rotate(new XYZ(-BodyHalfWidth,-BodyHalfHeight,0)+LabelOffset);
            XYZ[] corners=new XYZ[]{new XYZ(0,0,0),new XYZ(LabelLineLength+TextWidth,0,0),new XYZ(0,.05,0),new XYZ(LabelLineLength+TextWidth,.05,0)}
                .Select(x=>origin+Rotate(x)).ToArray();
            return new Outline {MinimumPoint=new XYZ(corners.Min(x=>x.X),corners.Min(x=>x.Y),0),MaximumPoint=new XYZ(corners.Max(x=>x.X),corners.Max(x=>x.Y),0)};
        }
    }
    internal class Document {
        public readonly Dictionary<int,Element> Items=new Dictionary<int,Element>();
        public Element GetElement(ElementId id){Element e;return Items.TryGetValue(id.IntegerValue,out e)?e:null;}
        public void Regenerate(){}
    }
    internal static class ReferenceableViewUtils {public static ElementId GetReferencedViewId(Document d,ElementId id){return d.GetElement(id).Target;}}
}
namespace Arv.OriginRelocator {
    using Autodesk.Revit.DB;
    internal class RelocationPlan {
        public readonly Dictionary<int,XYZ> ViewportCenters=new Dictionary<int,XYZ>();
        public readonly Dictionary<int,bool> Pins=new Dictionary<int,bool>();
        public readonly Dictionary<int,ViewportLabelState> ViewportLabels=new Dictionary<int,ViewportLabelState>();
    }
    internal class RelocationReport {public string SheetNumber(ElementId id){return "Test-sheet";}public readonly List<string> Lines=new List<string>();}
    internal class ElementState {
        public ElementId Id,ViewId;public XYZ[] Before;
        public static ElementState Capture(Element e,View v,bool annotation){return e.Box==null?null:new ElementState {Id=e.Id,ViewId=v.Id,Before=new XYZ[]{e.Box.Min,e.Box.Max}};}
        public static XYZ PointSetCenter(XYZ[] p){return p.Aggregate(XYZ.Zero,(a,b)=>a+b).Multiply(1.0/p.Length);}
        public static XYZ Project(XYZ p,XYZ n){return p-n.Multiply(p.DotProduct(n));}
        public static bool TranslatedPointSetsMatch(XYZ[] b,XYZ[] c,XYZ delta,XYZ n,double t){return b.Length==c.Length && b.Select((x,i)=>Project(x+delta-c[i],n).GetLength()).All(x=>x<=t);}
        public XYZ Residual(Document d,XYZ delta,bool project){return Project(Before[0]+delta-d.GetElement(Id).Box.Min,((View)d.GetElement(ViewId)).ViewDirection);}
        public void Verify(Document d,XYZ delta,bool project){Element e=d.GetElement(Id);if(!TranslatedPointSetsMatch(Before,new XYZ[]{e.Box.Min,e.Box.Max},delta,((View)d.GetElement(ViewId)).ViewDirection,1e-8))throw new Exception("Graphic was not translated");}
    }
__CLASSES__
    internal static class RelocationEngine {
        static bool Check(RelocationReport report, ElementId id, string stage, Action action) {action();return true;}
        static bool TryElement(Document doc, RelocationReport report, ElementId id, string stage, Action action) {action();return true;}

        public const double Tolerance=.1/304.8;
__METHODS__
    }
    internal static class Checks {
        static int count;
        static void Require(bool v,string message){count++;if(!v)throw new Exception(message);}
        static bool Throws(Action a){try{a();return false;}catch{return true;}}
        static Viewport Add(Document d,RelocationPlan p,ViewportRotation rot){
            Viewport v=new Viewport {Document=d,Id=new ElementId(1),Rotation=rot};d.Items[1]=v;
            p.ViewportCenters[1]=v.Center;p.Pins[1]=true;p.ViewportLabels[1]=new ViewportLabelState(v);return v;
        }
        public static int Main(){
            foreach(ViewportRotation rot in new ViewportRotation[]{ViewportRotation.None,ViewportRotation.Clockwise,ViewportRotation.Counterclockwise}){
                Document d=new Document();RelocationPlan p=new RelocationPlan();Viewport v=Add(d,p,rot);
                v.Center=v.Center+new XYZ(10,7,0);v.BodyHalfWidth=2;v.BodyHalfHeight=4;v.LabelLineLength=9;
                RelocationEngine.RestoreViewports(d,p,new RelocationReport());p.ViewportLabels[1].Verify(v);
                Require(v.Pinned && v.LabelLineLength==.3 && RelocationEngine.SheetOffset(v.Center,p.ViewportCenters[1]).GetLength()==0,"Viewport/title restoration failed with rotation "+rot);
            }
            Require((RelocationEngine.SectionMarkerTranslation(new XYZ(5,7,99),new XYZ(0,1,0),4,true)-new XYZ(5,4,0)).GetLength()==0,"Inherited crop motion produced wrong section plane offset");
            Require((RelocationEngine.SectionMarkerTranslation(new XYZ(0,8,99),new XYZ(.6,0,.8),3,true)-new XYZ(5,8,0)).GetLength()<1e-8,"Sloped section correction introduced height movement");
            Document doc=new Document();RelocationPlan plan=new RelocationPlan();Viewport viewport=Add(doc,plan,ViewportRotation.None);
            viewport.LabelOffset=new XYZ(-7,-9,0);plan.ViewportLabels[1]=new ViewportLabelState(viewport);viewport.BodyHalfWidth=5;
            RelocationEngine.RestoreViewports(doc,plan,new RelocationReport());Require(plan.ViewportLabels[1].Matches(viewport),"Manual title position not restored");
            doc=new Document();plan=new RelocationPlan();viewport=Add(doc,plan,ViewportRotation.None);viewport.HasLabel=false;
            Require(Throws(()=>plan.ViewportLabels[1].Restore(doc,viewport)),"Disappeared title accepted");
            viewport.HasLabel=true;viewport.TextWidth=1;
            Require(Throws(()=>{plan.ViewportLabels[1].Restore(doc,viewport);plan.ViewportLabels[1].Verify(viewport);}),"Changed title shape accepted");
            viewport.HasLabel=false;ViewportLabelState noTitle=new ViewportLabelState(viewport);viewport.Center=new XYZ(9,8,0);
            noTitle.Restore(doc,viewport);Require(noTitle.Matches(viewport),"Hidden title invented");
            XYZ normal=new XYZ(0,1,0),move=new XYZ(5,7,99);
            Require((RelocationEngine.SectionMarkerTranslation(move,normal,7,true)-new XYZ(5,7,0)).GetLength()==0,"Parallel section-line component lost or model Z moved");
            Require((RelocationEngine.SectionMarkerTranslation(move,normal,7,false)-new XYZ(0,7,0)).GetLength()==0,"Dependent view shifted parent along its own crop extents");
            doc=new Document();View parent=new View {Id=new ElementId(2),Document=doc};doc.Items[2]=parent;
            Element reference=new Element {Id=new ElementId(3),Document=doc,Box=new BoundingBoxXYZ {Min=new XYZ(0,0,0),Max=new XYZ(1,1,0)}};doc.Items[3]=reference;
            ViewGraphicState graphic=new ViewGraphicState(reference,parent,true);XYZ delta=new XYZ(5,7,0);
            Require((graphic.Residual(doc,delta)-delta).GetLength()==0,"Reference graphics missing full translation");
            reference.Box.Min+=delta;reference.Box.Max+=delta;
            Require(graphic.Residual(doc,delta).GetLength()==0,"Already inherited reference translation applied twice");graphic.Verify(doc,delta);
            reference.Target=new ElementId(100);Require(Throws(()=>graphic.Verify(doc,delta)),"Reference target changed silently");
            reference.Target=new ElementId(99);reference.Box.Max+=new XYZ(2,0,0);
            Require(Throws(()=>graphic.Verify(doc,delta)),"Manual section graphic deformation accepted");
            Console.WriteLine("PASS: "+count+" checks on production title snapshots/restoration, section translation and reference-graphic state.");
            Console.WriteLine("Managed Revit metadata/geometry doubles only; no native title or section marker was moved.");return 0;
        }
    }
}
'
$taskHarness=$taskHarness.Replace('__CLASSES__',$taskClasses).Replace('__METHODS__',$taskMethods)
$taskCodePath=Join-Path $taskOutputFolder 'presentation-preservation-harness.cs'
$taskExePath=Join-Path $taskOutputFolder 'presentation-preservation-harness.exe'
Set-Content -LiteralPath $taskCodePath -Value $taskHarness -Encoding UTF8
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 "/out:$taskExePath" $taskCodePath
if($LASTEXITCODE -ne 0){throw "Presentation check compilation failed: $LASTEXITCODE"}
$taskOutput=& $taskExePath;$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskOutputFolder 'presentation-preservation-result.txt') -Encoding UTF8
$taskOutput
if($taskExit -ne 0){throw "Presentation checks failed: $taskExit"}
