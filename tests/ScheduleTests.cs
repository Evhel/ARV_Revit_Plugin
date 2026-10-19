using System;
using System.Linq;
using ARV.ElementViews;
using ARV.Common;
public static class ScheduleTests
{
    private static bool Near(double a,double b)=>Math.Abs(a-b)<1e-10;
    private static bool Throws(Action action){try{action();return false;}catch(ArgumentException){return true;}}
    public static void Run(Action<bool,string> check)
    {
        var frame=SheetMargins.Frame(0,0,420/304.8,297/304.8);
        check(Near(frame.Left*304.8,20)&&Near(frame.Top*304.8,292),"inner frame excludes left 20 mm and top 5 mm");
        check(Near(frame.Right*304.8,415)&&Near(frame.Bottom*304.8,5),"inner frame excludes right and bottom 5 mm");
        check(Near(frame.Width*304.8,395)&&Near(frame.Height*304.8,287),"paper margins excluded from usable dimensions");
        check(Near((frame.Left+25/304.8)*304.8,45)&&Near((frame.Top-30/304.8)*304.8,262),"view offsets are additional to built-in frame margins");
        var shifted=SheetMargins.Frame(-2,-3,2,3);
        check(Near(shifted.Left,-2+20/304.8)&&Near(shifted.Top,3-5/304.8)&&Near(shifted.Right,2-5/304.8),"frame follows a shifted sheet origin");
        check(Throws(()=>SheetMargins.Frame(0,0,20/304.8,100/304.8)),"paper too small for mandatory frame rejected");
        check(Throws(()=>SheetMargins.Frame(0,0,double.NaN,1)),"nonfinite paper dimensions rejected");
        var corner=ScheduleLayoutMath.PackRight(new[]{new PaperRect(0,0,.2,.3)},new[]{1},new[]{0,1,0,0,0,0,0,0,0,0},0)[0];
        check(Near((frame.Right+corner.X+corner.Width)*304.8,415)&&Near((frame.Top-ScheduleLayoutMath.TopGapFeet-corner.Y)*304.8,287),"schedule top right is 10 mm below paper top and 5 mm from paper right");
        int[] cells={7,1,0,4,0,0,0,0,0,0};
        var sizes=new[]{new PaperRect(0,0,2,3),new PaperRect(0,0,1,2),new PaperRect(0,0,4,5)};
        var placed=ScheduleLayoutMath.PackRight(sizes,new[]{1,4,7},cells,1);
        check(Near(placed[0].X+placed[0].Width,0)&&Near(placed[0].Y,0),"first schedule anchored exactly at right upper paper corner");
        check(Near(placed[1].X+placed[1].Width,0)&&Near(placed[1].Y,3),"next right schedule touches previous bottom without vertical gap");
        check(Near(placed[2].X+placed[2].Width,-3)&&Near(placed[2].Y,0),"second column starts to the left using widest right table and horizontal gap");
        var zero=ScheduleLayoutMath.PackRight(sizes,new[]{1,4,7},cells,0);
        check(Near(zero[2].X+zero[2].Width,zero[0].X),"zero horizontal gap supported");
        var reverse=ScheduleLayoutMath.PackRight(new[]{sizes[2],sizes[0],sizes[1]},new[]{7,1,4},cells,1);
        check(Near(reverse[0].X,placed[2].X)&&Near(reverse[2].Y,placed[1].Y),"table order follows cells independently of creation order");
        var holes=ScheduleLayoutMath.PackRight(new[]{sizes[0],sizes[1]},new[]{1,4},new[]{0,1,0,0,0,0,0,4,0,0},1);
        check(Near(holes[1].Y,3),"empty schedule rows do not reserve height");
        var single=ScheduleLayoutMath.PackRight(new[]{sizes[0]},new[]{1},new[]{0,0,1,0,0,0,0,0,0,0},1);
        check(Near(single[0].X,-2)&&Near(single[0].Y,0),"single nonempty column starts at paper corner");
        double margin=ScheduleLayoutMath.InvisibleMarginFeet;
        var visible=ScheduleLayoutMath.VisibleBounds(new PaperRect(-margin,-margin,2+2*margin,3+2*margin));
        check(Near(visible.X,0)&&Near(visible.Y,0)&&Near(visible.Width,2)&&Near(visible.Height,3),"invisible native margins excluded before corner alignment");
        check(Near(margin*304.8,25.4/12),"schedule margin converted from official one twelfth inch");
        check(Throws(()=>ScheduleLayoutMath.VisibleBounds(new PaperRect(0,0,margin,margin))),"invalid schedule measurement rejected");
        check(Throws(()=>ScheduleLayoutMath.Validate(new int[9],new int[0])),"schedule grid must have ten cells");
        check(Throws(()=>ScheduleLayoutMath.Validate(new[]{0,1,0,1,0,0,0,0,0,0},new[]{1})),"duplicate schedule number rejected");
        check(Throws(()=>ScheduleLayoutMath.Validate(new int[10],new[]{1})),"missing schedule rejected");
        check(Throws(()=>ScheduleLayoutMath.Validate(new[]{0,2,0,0,0,0,0,0,0,0},new[]{1})),"unselected schedule rejected");
        check(Throws(()=>ScheduleLayoutMath.PackRight(sizes,new[]{1,4,7},cells,-1)),"negative schedule gap rejected");
        check(Throws(()=>ScheduleLayoutMath.PackRight(sizes,new[]{1,4,7},cells,double.NaN)),"nonfinite schedule gap rejected");
        check(Throws(()=>ScheduleLayoutMath.PackRight(new[]{new PaperRect(0,0,double.PositiveInfinity,1)},new[]{1},new[]{0,1,0,0,0,0,0,0,0,0},1)),"invalid native schedule size rejected");
        check(ScheduleLayoutMath.PackRight(new PaperRect[0],new int[0],new int[10],1).Count==0,"empty schedule selection is valid");
        var ten=Enumerable.Range(1,10).ToArray();var tenCells=new int[10];for(int i=0;i<10;i++)tenCells[(i%5)*2+(i<5?1:0)]=ten[i];
        var all=ScheduleLayoutMath.PackRight(ten.Select(n=>new PaperRect(0,0,1,n)).ToArray(),ten,tenCells,.1);
        check(all.Count==10&&Near(all[4].Y,10)&&Near(all[9].Y,30),"ten schedules stack into two five-row columns");
    }
}
