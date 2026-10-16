param()
$ErrorActionPreference='Stop'
$taskOutputFolder=Join-Path (Split-Path (Split-Path -Parent $PSScriptRoot) -Parent) 'artifacts\origin-checks'
New-Item -ItemType Directory -Path $taskOutputFolder -Force | Out-Null
$taskRoot=Join-Path (Split-Path (Split-Path -Parent $PSScriptRoot) -Parent) 'src\OriginRelocator'
$taskEngine=Get-Content -LiteralPath (Join-Path $taskRoot 'RelocationEngine.cs') -Raw
function Get-TaskBlock([string]$source,[string]$signature){
    $start=$source.IndexOf($signature); if($start -lt 0){throw "Missing $signature"}
    $brace=$source.IndexOf('{',$start); $depth=0
    for($i=$brace;$i -lt $source.Length;$i++){
        if($source[$i] -eq '{'){$depth++}
        if($source[$i] -eq '}'){$depth--;if($depth -eq 0){return $source.Substring($start,$i-$start+1)}}
    }; throw 'Unclosed block'
}
$methods=(Get-TaskBlock $taskEngine 'internal static IEnumerable<ViewState> OrderViewsForRelocation(')+[Environment]::NewLine+(Get-TaskBlock $taskEngine 'private static int ViewDepth(')
if(-not $taskEngine.Contains('foreach (ViewState state in OrderViewsForRelocation(doc, plan.Views))')){throw 'Production loop does not use tested order'}
$code=@"
using System;
using System.Linq;
using System.Collections.Generic;
namespace Arv.OriginRelocator {
    class ElementId { public readonly int IntegerValue; public ElementId(int id){IntegerValue=id;} public static readonly ElementId InvalidElementId=new ElementId(-1); }
    class Document { public Dictionary<int,View> Items=new Dictionary<int,View>(); public View GetElement(ElementId id){View v;return Items.TryGetValue(id.IntegerValue,out v)?v:null;} }
    class View { public ElementId Id;public Document Document;public ElementId Primary=ElementId.InvalidElementId;public ElementId Parent=ElementId.InvalidElementId;public bool IsCallout;public ElementId GetPrimaryViewId(){return Primary;}public ElementId GetCalloutParentId(){return Parent;} }
    class ViewPlan:View{} class ViewSection:View{} class ViewState { public ElementId Id; public ViewState(View v){Id=v.Id;} }
    static class RelocationEngine { $methods }
    static class Checks {
        static int count;static void Require(bool ok,string message){count++;if(!ok)throw new Exception(message);}
        static T Add<T>(Document doc,int id) where T:View,new(){T v=new T{Id=new ElementId(id),Document=doc};doc.Items[id]=v;return v;}
        // Replay only the order-sensitive bbox behaviour measured by the native order probe.
        // This is not a Revit document or an implementation of native regeneration.
        static double[] Replay(IEnumerable<ViewState> order){bool sectionMoved=false,badCropRegeneration=false;double min=0,max=0;
            foreach(ViewState s in order){if(s.Id.IntegerValue==3234199&&!sectionMoved)badCropRegeneration=true;
                if(s.Id.IntegerValue==3241781){sectionMoved=true;min=badCropRegeneration?161259.20666326574:164999.99999996636;max=badCropRegeneration?165062.66039135258:164999.99999996636;}}
            return new[]{min,max};}
        public static int Main(){Document doc=new Document();ViewPlan parent=Add<ViewPlan>(doc,3234199);ViewSection section=Add<ViewSection>(doc,3241781);
            List<ViewState> input=new List<ViewState>{new ViewState(parent),new ViewState(section)};
            double[] old=Replay(input);Require(Math.Abs(old[0]-165000)>0.1&&Math.Abs(old[1]-165000)>0.1,"Old-order control did not reproduce native bbox errors");
            List<ViewState> ordered=RelocationEngine.OrderViewsForRelocation(doc,input).ToList();double[] fixedPoints=Replay(ordered);
            Require(Math.Abs(fixedPoints[0]-165000)<0.1&&Math.Abs(fixedPoints[1]-165000)<0.1,"Production order retains crop-before-section failure");
            Require(ordered[0].Id.IntegerValue==3241781,"Section must precede parent plan crop");
            ViewPlan primary=Add<ViewPlan>(doc,10),dependent=Add<ViewPlan>(doc,11);dependent.Primary=primary.Id;
            ViewSection baseSection=Add<ViewSection>(doc,20),dependentSection=Add<ViewSection>(doc,21);dependentSection.Primary=baseSection.Id;
            ViewSection callout=Add<ViewSection>(doc,22);callout.IsCallout=true;callout.Parent=dependentSection.Id;
            ViewSection fromPlan=Add<ViewSection>(doc,23);fromPlan.IsCallout=true;fromPlan.Parent=parent.Id;
            var many=new View[]{dependent,callout,primary,dependentSection,parent,fromPlan,section,baseSection}.Select(v=>new ViewState(v)).ToList();
            var ids=RelocationEngine.OrderViewsForRelocation(doc,many).Select(s=>s.Id.IntegerValue).ToList();
            Require(ids.IndexOf(20)<ids.IndexOf(21)&&ids.IndexOf(21)<ids.IndexOf(22),"Section dependency order changed");
            Require(ids.IndexOf(10)<ids.IndexOf(11),"Plan dependency order changed");
            Require(ids.IndexOf(23)<ids.IndexOf(3234199),"Section callout must precede crop of its plan parent");
            Require(ids.IndexOf(22)<ids.IndexOf(10)&&ids.IndexOf(23)<ids.IndexOf(10),"A section was sorted after plan crops");
            Require(ids.Count==many.Count&&ids.Distinct().Count()==many.Count,"View omitted or duplicated");
            var permuted=RelocationEngine.OrderViewsForRelocation(doc,many.AsEnumerable().Reverse()).Select(s=>s.Id.IntegerValue).ToList();
            Require(permuted.IndexOf(20)<permuted.IndexOf(21)&&permuted.IndexOf(21)<permuted.IndexOf(22)&&permuted.IndexOf(23)<permuted.IndexOf(3234199),"Order depends on collector insertion sequence");
            Console.WriteLine("PASS: "+count+" checks on production view order and native-observation bbox replay.");
            Console.WriteLine("LIMIT: managed ordering and observation replay; full native relocation 1.0.20 not tested.");return 0;
        }
    }
}
"@
$taskCode=Join-Path $taskOutputFolder 'view-movement-order-harness.cs';$taskExe=Join-Path $taskOutputFolder 'view-movement-order-harness.exe'
Set-Content -LiteralPath $taskCode -Value $code -Encoding UTF8
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /out:$taskExe $taskCode
if($LASTEXITCODE -ne 0){throw 'Ordering check compile failed'}
$taskOutput=& $taskExe; $taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskOutputFolder 'view-movement-order-result.txt') -Encoding UTF8
$taskOutput
if($taskExit -ne 0){throw 'Ordering check failed'}
