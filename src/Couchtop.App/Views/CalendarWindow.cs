using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Couchtop.Core.Platform;

namespace Couchtop.App.Views;

/// <summary>The clock popup from the Couchtop Bar: big time, today's date and a month calendar.</summary>
public sealed class CalendarWindow : Window
{
    private readonly AppHost _host;

    public CalendarWindow(AppHost host, MainWindow main)
    {
        _host = host;

        Title = "Couchtop Clock";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        SetResourceReference(FontFamilyProperty, "AppFont");

        var now = DateTime.Now;
        var stack = new StackPanel { Width = 420 };
        var time = ViewKit.Text(now.ToString(host.Settings.Current.Clock24Hour ? "HH:mm" : "h:mm tt"), 72, FontWeights.ExtraBold, "ClockBrush", wrap: false, align: TextAlignment.Center);
        stack.Children.Add(time);
        stack.Children.Add(ViewKit.Text(now.ToString("dddd, d MMMM yyyy"), 26, FontWeights.Bold, "SubtleTextBrush", wrap: false, align: TextAlignment.Center));

        var calendar = new Calendar
        {
            Margin = new Thickness(0, 18, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            DisplayDate = now,
            SelectedDate = now,
            FontSize = 18,
        };
        calendar.SetResourceReference(ForegroundProperty, "TextBrush");
        stack.Children.Add(calendar);

        var board = ViewKit.Pill("Message Board", () =>
        {
            Close();
            main.OpenBuiltInView(new MessageBoardView(host, main));
        }, 260, "SmallPill");
        board.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(board);

        var panel = new Border { Child = stack, Padding = new Thickness(28, 22, 28, 20) };
        panel.SetResourceReference(FrameworkElement.StyleProperty, "AppPanel");
        Content = panel;

        Deactivated += (_, _) => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }

    /// <summary>Shows the popup above the Couchtop Bar, tucked into the bottom-right corner of the screen.</summary>
    public void ShowPopup()
    {
        var monitor = Monitors.Choose(Monitors.Enumerate(), _host.Settings.Current.TargetMonitor);
        Show();
        if (monitor is null) return;
        var scale = monitor.Scale <= 0 ? 1 : monitor.Scale;
        Left = monitor.X / scale + monitor.Width / scale - ActualWidth - 16;
        Top = monitor.Y / scale + monitor.Height / scale - ActualHeight - CouchtopBar.BarHeight - 12;
        Activate();
    }
}
