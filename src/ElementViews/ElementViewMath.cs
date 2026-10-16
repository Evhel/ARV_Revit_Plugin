using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ARV.ElementViews
{
    public struct Point3
    {
        public double X, Y, Z;
        public Point3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Point3 operator +(Point3 a, Point3 b) => new Point3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static Point3 operator -(Point3 a, Point3 b) => new Point3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static Point3 operator *(Point3 a, double k) => new Point3(a.X*k,a.Y*k,a.Z*k);
        public double Dot(Point3 b) => X*b.X+Y*b.Y+Z*b.Z;
        public Point3 Cross(Point3 b) => new Point3(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);
        public Point3 Unit() { double length=Math.Sqrt(Dot(this)); if(length<1e-9 || double.IsNaN(length) || double.IsInfinity(length)) throw new ArgumentException("Не определено направление элемента."); return this*(1/length); }
    }
    public sealed class ElementCandidate
    {
        public int Id;
        public double Z, X, Y;
        public string Value, GroupKey;
    }
    public sealed class SectionVolume
    {
        public Point3 Origin, Right, Up, Direction, Min, Max;
    }
    public sealed class PaperRect
    {
        public double X, Y, Width, Height;
        public PaperRect(double x,double y,double width,double height) { X=x;Y=y;Width=width;Height=height; }
    }
    public static class ElementViewMath
    {
        public static List<ElementCandidate> Select(IEnumerable<ElementCandidate> source, bool unique)
        {
            var ordered=source.OrderBy(e=>e.Z).ThenBy(e=>e.X).ThenByDescending(e=>e.Y).ThenBy(e=>e.Id).ToList();
            if (!unique) return ordered;
            var seen=new HashSet<string>(StringComparer.Ordinal);
            return ordered.Where(e=>!string.IsNullOrWhiteSpace(e.Value) && seen.Add(e.GroupKey??e.Value.Trim())).ToList();
        }
        public static string Name(string prefix,string token,string suffix)
        {
            string value=Regex.Replace((prefix??"")+(token??"")+(suffix??""), @"[\\:{}\[\]|;<>?`\r\n\t]", "_").Trim();
            if (value.Length==0) throw new ArgumentException("Пустое название вида или листа.");
            if (value.Length>240) throw new ArgumentException("Название длиннее 240 символов: сократите префикс или суффикс.");
            return value;
        }
        public static SectionVolume Volume(IList<Point3> points, Point3 axisX, Point3 axisY, string kind, double offset, double padding)
        {
            if(padding<=0) throw new ArgumentException("Некорректный отступ.");
            return Volume(points,axisX,axisY,kind,offset,padding,padding,padding,padding);
        }
        public static SectionVolume Volume(IList<Point3> points, Point3 axisX, Point3 axisY, string kind, double offset, double side, double above, double below, double planPadding)
        {
            double padding=kind=="Plan"?planPadding:side;
            if(new[]{side,above,below,planPadding}.Any(v=>v<0||double.IsNaN(v)||double.IsInfinity(v))) throw new ArgumentException("Некорректные отступы.");
            if(points==null || points.Count<2 || padding<0 || double.IsNaN(padding) || double.IsInfinity(padding)) throw new ArgumentException("Некорректные габариты или отступ.");
            var z=new Point3(0,0,1);
            Point3 x=new Point3(axisX.X,axisX.Y,0).Unit();
            Point3 y=z.Cross(x); if(y.Dot(axisY)<0) y=y*(-1);
            Point3 direction, up;
            bool plan=kind=="Plan";
            if(plan) {direction=z*(-1);up=y;}
            else { up=z; switch(kind) {case "Front":direction=y;break;case "Back":direction=y*(-1);break;case "Left":direction=x;break;case "Right":direction=x*(-1);break;default:throw new ArgumentException("Неизвестное направление вида.");} }
            Point3 right=up.Cross(direction).Unit();
            Point3 center=new Point3(points.Average(p=>p.X),points.Average(p=>p.Y),points.Average(p=>p.Z));
            double low=points.Min(p=>p.Z),high=points.Max(p=>p.Z);
            if(plan && (double.IsNaN(offset)||double.IsInfinity(offset)||offset<0||offset>high-low+1e-6)) throw new ArgumentException("Отметка плана находится вне высоты элемента.");
            double depth=Math.Max(plan?planPadding:side,1.0/304.8);
            Point3 origin=plan ? new Point3(center.X,center.Y,low+offset) : center+direction*(points.Min(p=>(p-center).Dot(direction))-depth);
            var local=points.Select(p=>new Point3((p-origin).Dot(right),(p-origin).Dot(up),(p-origin).Dot(direction))).ToList();
            return new SectionVolume { Origin=origin,Right=right,Up=up,Direction=direction,
                Min=new Point3(local.Min(p=>p.X)-padding,local.Min(p=>p.Y)-(plan?planPadding:below),0),
                Max=new Point3(local.Max(p=>p.X)+padding,local.Max(p=>p.Y)+(plan?planPadding:above),Math.Max(depth,local.Max(p=>p.Z)+depth)) };
        }
        public static string SheetTitle(string suffix,string token)
        {
            return ((suffix??"").Trim()+" "+(token??"").Trim()).Trim();
        }
        public static void ValidateLayout(int[] cells,int rows,int columns,int[] numbers)
        {
            if(rows<1||rows>4||columns<1||columns>4||cells==null||cells.Length!=rows*columns)throw new ArgumentException("Недопустимый размер схемы листа.");
            var used=cells.Where(n=>n!=0).ToArray();
            if(used.Any(n=>!numbers.Contains(n)))throw new ArgumentException("В схеме указан выключенный или неизвестный № вида.");
            if(used.Distinct().Count()!=used.Length)throw new ArgumentException("Каждый вид должен встречаться в схеме только один раз.");
            if(numbers.Any(n=>!used.Contains(n)))throw new ArgumentException("Укажите в схеме каждый включённый вид или нажмите «Заполнить по порядку».");
        }
        public static List<PaperRect> PackGrid(IList<PaperRect> sizes,int[] numbers,int[] cells,int rows,int columns,double width,double height,double gap,double overflowX)
        {
            ValidateLayout(cells,rows,columns,numbers);
            if(sizes.Count!=numbers.Length||width<=0||height<=0||gap<0||overflowX<=width)throw new ArgumentException("Недопустимая область компоновки.");
            double cellWidth=(width-(columns-1)*gap)/columns,cellHeight=(height-(rows-1)*gap)/rows,outsideY=0;
            var result=new List<PaperRect>();
            for(int i=0;i<sizes.Count;i++)
            {
                var size=sizes[i];int cell=Array.IndexOf(cells,numbers[i]);
                if(size.Width<=0||size.Height<=0||double.IsNaN(size.Width)||double.IsNaN(size.Height)||double.IsInfinity(size.Width)||double.IsInfinity(size.Height))throw new ArgumentException("Недопустимый размер вида.");
                if(size.Width>cellWidth+1e-8||size.Height>cellHeight+1e-8)
                {result.Add(new PaperRect(overflowX,outsideY,size.Width,size.Height));outsideY+=size.Height+gap;}
                else result.Add(new PaperRect((cell%columns)*(cellWidth+gap)+(cellWidth-size.Width)/2,(cell/columns)*(cellHeight+gap)+(cellHeight-size.Height)/2,size.Width,size.Height));
            }
            return result;
        }
        public static List<PaperRect> PackPreserving(IList<PaperRect> sizes,double width,double height,double gap,double overflowX)
        {
            if(width<=0||height<=0||gap<0||overflowX<=width) throw new ArgumentException("Некорректная рабочая область листа.");
            double x=0,y=0,row=0,outsideY=0;var result=new List<PaperRect>();
            foreach(var size in sizes)
            {
                double nextX=x,nextY=y,nextRow=row;
                if(nextX>0 && nextX+size.Width>width+1e-8) {nextX=0;nextY+=nextRow+gap;nextRow=0;}
                if(size.Width>width+1e-8 || nextY+size.Height>height+1e-8)
                {result.Add(new PaperRect(overflowX,outsideY,size.Width,size.Height));outsideY+=size.Height+gap;continue;}
                result.Add(new PaperRect(nextX,nextY,size.Width,size.Height));x=nextX+size.Width+gap;y=nextY;row=Math.Max(nextRow,size.Height);
            }
            return result;
        }
        public static List<PaperRect> Pack(IList<PaperRect> sizes,double width,double height,double gap)
        {
            if(width<=0||height<=0||gap<0) throw new ArgumentException("Недостаточная рабочая область листа.");
            double x=0,y=0,row=0;var result=new List<PaperRect>();
            foreach(var size in sizes)
            {
                if(size.Width<=0||size.Height<=0||size.Width>width+1e-8) throw new InvalidOperationException("Виды не помещаются на листе. Выберите больший формат или увеличьте знаменатель масштаба.");
                if(x>0 && x+size.Width>width+1e-8) {x=0;y+=row+gap;row=0;}
                if(y+size.Height>height+1e-8) throw new InvalidOperationException("Виды не помещаются на листе. Выберите больший формат или увеличьте знаменатель масштаба.");
                result.Add(new PaperRect(x,y,size.Width,size.Height));x+=size.Width+gap;row=Math.Max(row,size.Height);
            }
            return result;
        }
    }
}
