using System;
namespace ARV.Common
{
    // All sheet offsets are measured from this inner drawing frame, not from the paper edge.
    internal static class SheetMargins
    {
        internal const double LeftFeet=20.0/304.8;
        internal const double TopFeet=5.0/304.8;
        internal const double RightFeet=5.0/304.8;
        internal const double BottomFeet=5.0/304.8;
        internal static SheetFrame Frame(double minU,double minV,double maxU,double maxV)
        {
            var frame=new SheetFrame{Left=minU+LeftFeet,Bottom=minV+BottomFeet,Right=maxU-RightFeet,Top=maxV-TopFeet};
            if(double.IsNaN(frame.Width)||double.IsInfinity(frame.Width)||double.IsNaN(frame.Height)||double.IsInfinity(frame.Height)||frame.Width<=0||frame.Height<=0)
                throw new ArgumentException("Размер листа меньше обязательных полей рамки.");
            return frame;
        }
    }
    internal struct SheetFrame
    {
        internal double Left,Bottom,Right,Top;
        internal double Width=>Right-Left;
        internal double Height=>Top-Bottom;
    }
}
