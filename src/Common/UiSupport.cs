using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ARV.Common
{
    // Общие средства оформления WPF, без обращения к документу Revit.
    public static class UiSupport
    {
        private const string InvalidFieldKey = "Arv.InvalidField";

        public static void Initialize(Window window)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.SourceInitialized += (sender, args) =>
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                FitToMonitor(window);
                CenterOnOwnerMonitor(window);
            };
            // Повторяем после окончательной компоновки, а не с первоначальной высотой окна.
            window.Loaded += (sender, args) => CenterOnOwnerMonitor(window);
            window.ContentRendered += (sender, args) => CenterOnOwnerMonitor(window);
            window.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler((sender, args) => ClearError(window)));
            window.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler((sender, args) => ClearError(window)));
            window.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((sender, args) => ClearError(window)));
            window.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((sender, args) => ClearError(window)));
        }

        public static void Error(Window window, Control field, string message)
        {
            ClearError(window);
            var label = window.FindName("ValidationMessage") as TextBlock;
            if (label != null) { label.Text = message; label.Visibility = Visibility.Visible; }
            if (field == null) return;
            window.Resources[InvalidFieldKey] = field;
            field.SetResourceReference(Control.BorderBrushProperty, "Arv.Error");
            field.BringIntoView();
            field.Focus();
        }

        private static void ClearError(Window window)
        {
            var field = window.Resources[InvalidFieldKey] as Control;
            if (field != null) field.ClearValue(Control.BorderBrushProperty);
            window.Resources.Remove(InvalidFieldKey);
            var label = window.FindName("ValidationMessage") as TextBlock;
            if (label != null) { label.Text = ""; label.Visibility = Visibility.Collapsed; }
        }

        private static void FitToMonitor(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            var owner = new WindowInteropHelper(window).Owner;
            var monitor = MonitorFromWindow(owner == IntPtr.Zero ? handle : owner, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            var source = PresentationSource.FromVisual(window);
            if (source == null || source.CompositionTarget == null || !GetMonitorInfo(monitor, ref info)) return;
            var transform = source.CompositionTarget.TransformFromDevice;
            var size = transform.Transform(new Vector(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top));
            window.MaxWidth = Math.Max(100, size.X - 24);
            window.MaxHeight = Math.Max(100, size.Y - 24);
            window.MinWidth = Math.Min(window.MinWidth, window.MaxWidth);
            window.MinHeight = Math.Min(window.MinHeight, window.MaxHeight);
            window.Width = Math.Min(window.Width, window.MaxWidth);
            window.Height = Math.Min(window.Height, window.MaxHeight);
        }

        // Все аргументы и результат — экранные координаты в одной системе Win32.
        internal static System.Windows.Rect GetCenteredPixelBounds(System.Windows.Rect work, Size requested)
        {
            double width = Math.Min(requested.Width, work.Width);
            double height = Math.Min(requested.Height, work.Height);
            return new System.Windows.Rect(work.Left + (work.Width - width) / 2,
                work.Top + (work.Height - height) / 2, width, height);
        }

        private static void CenterOnOwnerMonitor(Window window)
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero || helper.Owner == IntPtr.Zero) return;
            IntPtr oldContext = IntPtr.Zero;
            try
            {
                // Только на время Win32-вызовов; DPI-режим процесса Revit не меняем.
                oldContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
                var monitor = MonitorFromWindow(helper.Owner, 2);
                var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                Rect current;
                if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(helper.Handle, out current)) return;
                var bounds = GetCenteredPixelBounds(
                    new System.Windows.Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top),
                    new Size(current.Right - current.Left, current.Bottom - current.Top));
                SetWindowPos(helper.Handle, IntPtr.Zero, (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                    (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x0014); // NOZORDER | NOACTIVATE
            }
            finally
            {
                if (oldContext != IntPtr.Zero) SetThreadDpiAwarenessContext(oldContext);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
