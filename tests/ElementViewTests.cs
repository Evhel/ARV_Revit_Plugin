using System;
using System.Linq;
using ARV.ElementViews;

internal static class ElementViewTests
{
    static bool Throws(Action f) {try {f();return false;}catch(ArgumentException){return true;}catch(InvalidOperationException){return true;}}
    static bool Near(double a,double b)=>Math.Abs(a-b)<1e-8;
    internal static void Run(Action<bool,string> check)
    {
        var columns=Enumerable.Range(0,50).Select(i=>new ElementCandidate{Id=i,Z=i,X=50-i,Y=i,Value="К"+(i%17)}).ToArray();
        var unique=ElementViewMath.Select(columns,true);
        check(unique.Count==17 && unique.All(c=>c.Id<17),"50 columns -> 17 marks, lowest instance selected");
        check(ElementViewMath.Select(columns,false).Count==50,"unchecked filter processes all 50 instances");
        var tied=new[]{new ElementCandidate{Id=1,Z=0,X=2,Y=100,Value="A"},new ElementCandidate{Id=2,Z=0,X=1,Y=2,Value="A"},new ElementCandidate{Id=3,Z=0,X=1,Y=3,Value="A"}};
        check(ElementViewMath.Select(tied,true).Single().Id==3,"same elevation: minimum X then maximum Y");
        tied[1].Z=-1;check(ElementViewMath.Select(tied,true).Single().Id==2,"Z has priority over Y");
        check(ElementViewMath.Select(new[]{new ElementCandidate{Id=1,Value=""},new ElementCandidate{Id=2,Value=" "}},true).Count==0,"empty marks excluded when filter is enabled");
        check(ElementViewMath.Select(new[]{new ElementCandidate{Id=1,Value="А"},new ElementCandidate{Id=2,Value="а"}},true).Count==2,"case-sensitive parameter values");
        check(ElementViewMath.Select(new[]{new ElementCandidate{Id=4,Value="A"},new ElementCandidate{Id=3,Value="A"}},true).Single().Id==3,"element ID provides stable tie break");
        check(ElementViewMath.Name("План_","К-1","_АР")=="План_К-1_АР","prefix + parameter value + suffix");
        check(ElementViewMath.Name("", "A:B?C", "")=="A_B_C","invalid view name characters sanitized");
        check(Throws(()=>ElementViewMath.Name("", "", "")),"empty names rejected");
        check(Throws(()=>ElementViewMath.Name(new string('x',240),"MARK","")),"long names rejected rather than losing parameter value");
        check(ElementViewMath.Select(new[]{new ElementCandidate{Id=1,Value="100 мм",GroupKey="D:1"},new ElementCandidate{Id=2,Value="100 мм",GroupKey="D:1.001"}},true).Count==2,"rounded display values must not merge different numeric values");
        check(ElementViewMath.Select(new[]{new ElementCandidate{Id=1,Value="Тип 1",GroupKey="E:100"},new ElementCandidate{Id=2,Value="Тип 1",GroupKey="E:101"}},true).Count==2,"same referenced name must not merge different element IDs");
        var points=(from x in new[]{10.0,12.0} from y in new[]{20.0,22.0} from z in new[]{3.0,9.0} select new Point3(x,y,z)).ToArray();
        var axisX=new Point3(0,1,0);var axisY=new Point3(-1,0,0);
        foreach(string kind in new[]{"Front","Back","Left","Right","Plan"})
        {
            var v=ElementViewMath.Volume(points,axisX,axisY,kind,2,0.1);
            check(Near(v.Up.Dot(v.Direction),0)&&Near(v.Right.Dot(v.Up),0)&&Near(v.Right.Cross(v.Up).Dot(v.Direction),1),"orthonormal section box "+kind);
            check(v.Max.X>v.Min.X && v.Max.Y>v.Min.Y && v.Max.Z>v.Min.Z,"nonzero section extents "+kind);
            check(points.All(p=>(p-v.Origin).Dot(v.Right)>=v.Min.X && (p-v.Origin).Dot(v.Right)<=v.Max.X && (p-v.Origin).Dot(v.Up)>=v.Min.Y && (p-v.Origin).Dot(v.Up)<=v.Max.Y),"crop contains projected element "+kind);
            if(kind!="Plan") check(points.All(p=>(p-v.Origin).Dot(v.Direction)>0 && (p-v.Origin).Dot(v.Direction)<v.Max.Z),"side view is outside element and contains full depth "+kind);
        }
        var front=ElementViewMath.Volume(points,axisX,axisY,"Front",0,.1);
        var back=ElementViewMath.Volume(points,axisX,axisY,"Back",0,.1);
        check(Near(front.Direction.Dot(back.Direction),-1) && Near(front.Direction.X,-1),"front/back follow rotated local axes");
        foreach(var offset in new[]{0.0,2.0,6.0})
        {var v=ElementViewMath.Volume(points,axisX,axisY,"Plan",offset,.1);check(Near(v.Origin.Z,3+offset)&&Near(v.Direction.Z,-1),"horizontal section plane at requested bottom offset "+offset);}
        check(Throws(()=>ElementViewMath.Volume(points,axisX,axisY,"Plan",7,.1)),"offset above top rejected");
        check(Throws(()=>ElementViewMath.Volume(points,axisX,axisY,"Plan",-1,.1)),"negative offset rejected");
        check(Throws(()=>ElementViewMath.Volume(points,axisX,axisY,"Front",0,0)),"zero crop margin rejected");
        check(ElementViewMath.Select(new[]{new ElementCandidate{Id=1,Value=""},new ElementCandidate{Id=2,Value="A"}},false).Count==2,"disabled filter retains empty values");
        var asymmetric=ElementViewMath.Volume(points,axisX,axisY,"Front",0,.2,.3,.4,.8);
        check(Near(asymmetric.Max.Y-asymmetric.Min.Y,6+.3+.4),"upper and lower section margins independent");
        check(Near(asymmetric.Max.X-asymmetric.Min.X,2+.4),"side margin applied to both horizontal sides");
        var planA=ElementViewMath.Volume(points,axisX,axisY,"Plan",2,.2,.3,.4,.8);
        var planB=ElementViewMath.Volume(points,axisX,axisY,"Plan",2,2,3,4,.8);
        check(Near(planA.Max.X-planA.Min.X,2+1.6)&&Near(planA.Max.Y-planA.Min.Y,2+1.6),"plan crop uses its own margin on four sides");
        check(Near(planA.Max.Z,planB.Max.Z)&&Near(planA.Min.X,planB.Min.X)&&Near(planA.Max.Y,planB.Max.Y),"section margins cannot change plan geometry");
        check(Throws(()=>ElementViewMath.Volume(points,axisX,axisY,"Front",0,.2,-.3,.4,.8)),"negative individual margin rejected");
        var kept=ElementViewMath.PackPreserving(new[]{new PaperRect(0,0,2,1),new PaperRect(0,0,20,10),new PaperRect(0,0,2,1)},4.2,2.2,.1,5);
        check(kept.Count==3 && kept[1].X==5 && kept[2].X<4.2,"oversize view kept outside paper without losing later small views");
        var overflow=ElementViewMath.PackPreserving(new[]{new PaperRect(0,0,2,3),new PaperRect(0,0,2,4)},4.2,1,.1,5);
        check(overflow.Count==2 && overflow.All(rect=>rect.X==5) && overflow[1].Y>=overflow[0].Y+overflow[0].Height+.09,"all-too-tall views kept outside without overlap");
        var sizes=new[]{new PaperRect(0,0,2,1),new PaperRect(0,0,2,1),new PaperRect(0,0,2,1)};
        var positions=ElementViewMath.Pack(sizes,4.2,2.2,.1);
        check(positions.Count==3 && Near(positions[2].X,0)&&Near(positions[2].Y,1.1),"sheet packing wraps rows with gap");
        check(positions.All(a=>a.X>=0 && a.Y>=0 && a.X+a.Width<=4.2 && a.Y+a.Height<=2.2),"measured view/label bounds remain inside sheet");
        check(Throws(()=>ElementViewMath.Pack(sizes,1,3,.1)),"too-wide viewport rejected without overlap");
        check(Throws(()=>ElementViewMath.Pack(sizes,4.2,1,.1)),"too-short sheet rejected without partial placement");
    }
}
