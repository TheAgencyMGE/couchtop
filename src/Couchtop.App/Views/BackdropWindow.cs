using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;

namespace Couchtop.App.Views;

/// <summary>A calm console-style backdrop (stripes, faint wordmark, clock) for monitors other than the menu's.</summary>
public sealed class BackdropWindow : Window
{
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    private static readonly IntPtr HwndBottom = new(1);
    private readonly MonitorDescriptor _monitor;
    private readonly DispatcherTimer _clock;
    private readonly TextBlock _time;

    public BackdropWindow(MonitorDescriptor monitor)
    {
        _monitor = monitor;
        Title = "Couchtop backdrop";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");

        var grid = new Grid();
        var stripes = new Rectangle();
        stripes.SetResourceReference(Shape.FillProperty, "StripeBrush");
        grid.Children.Add(stripes);
        var decor = new ContentControl { Content = "decor", Focusable = false, IsTabStop = false, IsHitTestVisible = false };
        decor.SetResourceReference(ContentControl.ContentTemplateProperty, "BackgroundDecorTemplate");
        grid.Children.Add(decor);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var mark = ViewKit.Text("Couchtop", 140, FontWeights.ExtraBold, "ClockBrush", wrap: false);
        mark.Opacity = 0.35;
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        _time = ViewKit.Text("", 80, FontWeights.Medium, "ClockBrush", wrap: false);
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        _time.Opacity = 0.6;
        stack.Children.Add(mark);
        stack.Children.Add(_time);
        grid.Children.Add(new Viewbox { Child = stack, Margin = new Thickness(200) });
        Content = grid;

        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
        _clock.Tick += (_, _) => UpdateTime();
        UpdateTime();
        _clock.Start();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
            NativeMethods.SetWindowPos(hwnd, HwndBottom, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, NativeMethods.SWP_NOACTIVATE);
        };
        Closed += (_, _) => _clock.Stop();
    }

    private void UpdateTime() => _time.Text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
}
