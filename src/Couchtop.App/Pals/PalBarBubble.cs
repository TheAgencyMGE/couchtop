using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Couchtop.App.Controls;
using Couchtop.Core.Native;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>
/// While another app is in front, the Pal speaks up in a small bubble above the Couchtop Bar: its face with
/// the matching expression and the line. Never takes focus, never covers a full-screen app, and a click (or a
/// few seconds) sends it away.
/// </summary>
public sealed class PalBarBubble : Window
{
    private readonly AppHost _host;
    private readonly Image _face = new() { Width = 96, Height = 96 };
    private readonly TextBlock _text = new() { FontSize = 24, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, VerticalAlignment = VerticalAlignment.Center };
    private readonly TranslateTransform _slide = new(0, 30);
    private readonly DispatcherTimer _hide;

    public PalBarBubble(AppHost host)
    {
        _host = host;
        Title = "Couchtop Pal";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        SetResourceReference(FontFamilyProperty, "AppFont");

        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var faceFrame = new Grid { Width = 96, Height = 96, Margin = new Thickness(0, 0, 14, 0) };
        var ring = new Ellipse { StrokeThickness = 4 };
        ring.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        ring.SetResourceReference(Shape.FillProperty, "BackgroundBrush");
        faceFrame.Children.Add(ring);
        _face.Clip = new EllipseGeometry(new Point(48, 48), 46, 46);
        faceFrame.Children.Add(_face);
        row.Children.Add(faceFrame);
        row.Children.Add(_text);
        var panel = new Border { Child = row, Padding = new Thickness(14, 12, 26, 12), CornerRadius = new CornerRadius(30), BorderThickness = new Thickness(3), Margin = new Thickness(12) };
        panel.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        panel.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.25 };
        panel.RenderTransform = _slide;
        Content = panel;

        _hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _hide.Tick += (_, _) => Dismiss();
        MouseLeftButtonUp += (_, _) => Dismiss();
        Cursor = Cursors.Hand;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));
    }

    /// <summary>Pops up with the Pal's face and line, its bottom-right corner at <paramref name="anchor"/> (screen units).</summary>
    public void ShowLine(PalReaction reaction, Point anchor)
    {
        if (_host.Pals.Profile is not { } profile || reaction.Text is null) return;
        _face.Source = PalPortrait.Render(profile, 96, 96, AvatarFraming.Face, mood: reaction.Mood);
        _text.Text = reaction.Text;
        if (!IsVisible) Show();
        UpdateLayout();
        Left = anchor.X - ActualWidth;
        Top = anchor.Y - ActualHeight;
        Opacity = 1;
        if (!Anim.Reduced)
        {
            _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = Anim.Springy });
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        _hide.Stop();
        _hide.Interval = TimeSpan.FromSeconds(Math.Clamp(3 + reaction.Text.Length * 0.06, 4, 8));
        _hide.Start();
    }

    public void Dismiss()
    {
        _hide.Stop();
        if (!IsVisible) return;
        if (Anim.Reduced)
        {
            Hide();
            return;
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (!_hide.IsEnabled) Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
