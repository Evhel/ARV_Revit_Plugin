using System;
using System.Collections.Generic;
using System.Linq;
namespace ARV.ElementViews
{
    public static class ScheduleLayoutMath
    {
        // Autodesk development team: invisible schedule margin is 1/12 inch.
        // https://forums.autodesk.com/t5/revit-api-forum/schedulesheetinstance-bounding-box/m-p/7620115
        public const double InvisibleMarginFeet=1.0/144;
        public static PaperRect VisibleBounds(PaperRect box)
        {
            double margin=InvisibleMarginFeet;
            if(box.Width<=2*margin||box.Height<=2*margin)throw new ArgumentException("Невозможно измерить видимую таблицу спецификации.");
            return new PaperRect(box.X+margin,box.Y+margin,box.Width-2*margin,box.Height-2*margin);
        }
        public static void Validate(int[] cells,int[] numbers)
        {
            if(cells==null||cells.Length!=10)throw new ArgumentException("Схема спецификаций должна содержать 2 столбца и 5 строк.");
            var used=cells.Where(n=>n!=0).ToArray();
            if(used.Any(n=>!numbers.Contains(n)))throw new ArgumentException("В схеме спецификаций указан номер пустой строки.");
            if(used.Distinct().Count()!=used.Length)throw new ArgumentException("Каждая спецификация должна быть указана в схеме один раз.");
            if(numbers.Any(n=>!used.Contains(n)))throw new ArgumentException("Укажите в схеме все выбранные спецификации или заполните её по порядку.");
        }
        public static List<PaperRect> PackRight(IList<PaperRect> sizes,int[] numbers,int[] cells,double gap)
        {
            Validate(cells,numbers);
            if(sizes.Count!=numbers.Length||gap<0||double.IsNaN(gap)||double.IsInfinity(gap)||sizes.Any(s=>s.Width<=0||s.Height<=0||double.IsNaN(s.Width)||double.IsNaN(s.Height)||double.IsInfinity(s.Width)||double.IsInfinity(s.Height)))throw new ArgumentException("Некорректные габариты или зазор спецификаций.");
            var result=new PaperRect[sizes.Count];var map=numbers.Select((n,i)=>new{n,i}).ToDictionary(p=>p.n,p=>p.i);
            double right=0;
            foreach(int column in new[]{1,0})
            {
                var indices=Enumerable.Range(0,5).Select(row=>cells[row*2+column]).Where(n=>n!=0).Select(n=>map[n]).ToList();
                if(indices.Count==0)continue;
                double top=0,width=indices.Max(i=>sizes[i].Width);
                foreach(int i in indices){var size=sizes[i];result[i]=new PaperRect(-right-size.Width,top,size.Width,size.Height);top+=size.Height;}
                right+=width+gap;
            }
            return result.ToList();
        }
    }
}
