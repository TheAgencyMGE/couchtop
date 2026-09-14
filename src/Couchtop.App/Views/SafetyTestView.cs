using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Couchtop.App.Services;
using Couchtop.Core.Safety;

namespace Couchtop.App.Views;

public sealed class SafetyTestView : UserControl, IScreenView
{
    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly Dictionary<string, (Ellipse Dot, TextBlock Glyph, TextBlock Detail)> _rows = new();
    private readonly Button _start;
    private readonly Border _prompt;
    private readonly TextBlock _countdown;
    private readonly TextBlock _summary;
    private CancellationTokenSource? _cts;
    private bool _running;

    public SafetyTestView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(640) });

        var steps = new StackPanel();
        foreach (var (id, title) in SafetyTestRunner.Steps)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dotHost = new Grid { Width = 52, Height = 52, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            var dot = new Ellipse();
            dot.SetResourceReference(Shape.FillProperty, "TrackBrush");
            var glyph = ViewKit.Text("", 30, FontWeights.ExtraBold, "InverseTextBrush", wrap: false, align: TextAlignment.Center);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            dotHost.Children.Add(dot);
            dotHost.Children.Add(glyph);
            row.Children.Add(dotHost);
            var text = new StackPanel();
            text.Children.Add(ViewKit.Text(title, 30, FontWeights.Bold, wrap: false));
            var detail = ViewKit.Text("Not run yet", 22, FontWeights.Normal, "SubtleTextBrush");
            text.Children.Add(detail);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            steps.Children.Add(ViewKit.Card(row, new Thickness(26, 14, 26, 14), new Thickness(0, 0, 20, 12)));
            _rows[id] = (dot, glyph, detail);
        }
        body.Children.Add(ViewKit.Scroll(steps));

        var side = new StackPanel();
        side.Children.Add(ViewKit.Text("Before Couchtop may replace Explorer, this test proves on your PC that every way back to the normal desktop works.", 30, FontWeights.Bold));
        side.Children.Add(Spacer(18));
        side.Children.Add(ViewKit.Text("Nothing is changed for real: registry work happens in a sandbox and Explorer is never stopped. Near the end you'll be asked to press the emergency shortcut. It won't close anything during the test.", 25, FontWeights.Normal, "SubtleTextBrush"));
        side.Children.Add(Spacer(26));
        _start = ViewKit.Pill("Start Test", Start, 420);
        _start.HorizontalAlignment = HorizontalAlignment.Left;
        side.Children.Add(_start);

        var promptStack = new StackPanel();
        promptStack.Children.Add(ViewKit.Text("Press  Ctrl + Alt + Shift + F12  now", 40, FontWeights.ExtraBold, "AccentDeepBrush"));
        _countdown = ViewKit.Text("", 28, FontWeights.Bold, "TextBrush");
        promptStack.Children.Add(_countdown);
        _prompt = new Border { Child = promptStack, CornerRadius = new CornerRadius(30), Padding = new Thickness(30, 22, 30, 22), Margin = new Thickness(0, 24, 0, 0), BorderThickness = new Thickness(5), Visibility = Visibility.Collapsed };
        _prompt.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        _prompt.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        side.Children.Add(_prompt);

        _summary = ViewKit.Text(SummaryForStoredRecord(), 28, FontWeights.Bold);
        _summary.Margin = new Thickness(0, 26, 0, 0);
        side.Children.Add(_summary);

        var sideCard = ViewKit.Card(side, new Thickness(44, 36, 44, 36));
        Grid.SetColumn(sideCard, 2);
        sideCard.VerticalAlignment = VerticalAlignment.Top;
        body.Children.Add(sideCard);

        Content = ViewKit.Scaffold("Shell Safety Test", "Proves recovery works before Shell Mode can be turned on", body, window.ReturnToMenu);
        ShowStoredRecord();
    }

    private static FrameworkElement Spacer(double height) => new Border { Height = height };

    private string SummaryForStoredRecord()
    {
        var gate = _host.EvaluateSafetyGate();
        return gate.Passed ? "Last result: passed. Shell Mode can be enabled in Settings › Shell Mode." : "Shell Mode stays locked until this test passes.";
    }

    private void ShowStoredRecord()
    {
        var record = _host.SafetyStore.Load();
        if (record is null) return;
        foreach (var step in record.Steps) SetRow(step, running: false);
    }

    private void SetRow(SafetyStepResult step, bool running)
    {
        if (!_rows.TryGetValue(step.Id, out var row)) return;
        row.Detail.Text = step.Detail;
        if (running)
        {
            row.Dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
            row.Glyph.Text = "…";
            return;
        }
        row.Dot.SetResourceReference(Shape.FillProperty, step.Passed ? "SuccessBrush" : "DangerBrush");
        row.Glyph.Text = step.Passed ? "✓" : "✕";
    }

    private async void Start()
    {
        if (_running) return;
        _running = true;
        _start.IsEnabled = false;
        _summary.Text = "Testing… this takes about a minute.";
        foreach (var (dot, glyph, detail) in _rows.Values)
        {
            dot.SetResourceReference(Shape.FillProperty, "TrackBrush");
            glyph.Text = "";
            detail.Text = "Waiting";
        }

        _cts = new CancellationTokenSource();
        try
        {
            var runner = new SafetyTestRunner(_host, () => _window.EmergencyHotkeyRegistered);
            var record = await runner.RunAsync(step => SetRow(step, true), step => SetRow(step, false), WaitForHotkeyAsync, _cts.Token);
            var passed = record.Steps.All(s => s.Passed);
            _summary.Text = passed
                ? "All checks passed! You can now turn on Shell Mode in Settings › Shell Mode."
                : "Some checks did not pass, so Shell Mode stays locked. Fix the red items and run the test again.";
            _summary.SetResourceReference(TextBlock.ForegroundProperty, passed ? "SuccessBrush" : "DangerBrush");
            _host.Audio.Play(passed ? SoundEffect.Launch : SoundEffect.Error);
        }
        catch (OperationCanceledException)
        {
            _summary.Text = "The test was cancelled.";
        }
        finally
        {
            _running = false;
            _start.IsEnabled = true;
            _start.Content = "Run Again";
            _window.EmergencyHotkeyInterceptor = null;
            _prompt.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<bool> WaitForHotkeyAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _window.EmergencyHotkeyInterceptor = () =>
        {
            tcs.TrySetResult(true);
            return true;
        };
        _prompt.Visibility = Visibility.Visible;
        _host.Audio.Play(SoundEffect.HomeOpen);
        try
        {
            for (var seconds = 60; seconds > 0; seconds--)
            {
                _countdown.Text = $"{seconds} seconds left";
                var finished = await Task.WhenAny(tcs.Task, Task.Delay(1000, ct));
                if (finished == tcs.Task) return true;
                ct.ThrowIfCancellationRequested();
            }
            return false;
        }
        finally
        {
            _window.EmergencyHotkeyInterceptor = null;
            _prompt.Visibility = Visibility.Collapsed;
        }
    }

    public void OnShown() { }

    public void OnHidden()
    {
        _cts?.Cancel();
        _window.EmergencyHotkeyInterceptor = null;
    }

    public bool HandleBack() => false;
}
