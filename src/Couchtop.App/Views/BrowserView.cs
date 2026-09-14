using System.Diagnostics;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Couchtop.Core.Diagnostics;

namespace Couchtop.App.Views;

/// <summary>The Web: WebView2 with a big console-style toolbar along the bottom.</summary>
public sealed class BrowserView : UserControl, IScreenView, IDisposable
{
    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly Grid _webHost = new() { Background = Brushes.White };
    private readonly TextBox _address;
    private readonly FrameworkElement _toolbarContent;
    private readonly Grid _fallback;
    private WebView2? _web;
    private bool _initializing;

    public BrowserView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_webHost);

        _fallback = new Grid { Visibility = Visibility.Collapsed };
        _fallback.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
        var fallbackStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        fallbackStack.Children.Add(ViewKit.Text("The Web needs the Microsoft Edge WebView2 Runtime.", 40, FontWeights.ExtraBold, align: TextAlignment.Center));
        var openDefault = ViewKit.Pill("Open My Default Browser", () =>
        {
            try { using var _ = Process.Start(new ProcessStartInfo("https://duckduckgo.com") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("Default browser failed", ex); }
        }, 520);
        openDefault.HorizontalAlignment = HorizontalAlignment.Center;
        openDefault.Margin = new Thickness(0, 40, 0, 0);
        fallbackStack.Children.Add(openDefault);
        _fallback.Children.Add(new Viewbox { Child = new Grid { Width = 1920, Height = 900, Children = { fallbackStack } } });
        root.Children.Add(_fallback);

        var toolbar = new Border { BorderThickness = new Thickness(0, 5, 0, 0) };
        toolbar.SetResourceReference(Border.BackgroundProperty, "BarBrush");
        toolbar.SetResourceReference(Border.BorderBrushProperty, "BarLineBrush");
        Grid.SetRow(toolbar, 1);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10, 8, 10, 12) };
        bar.Children.Add(ViewKit.Pill("Back", () => { if (_web?.CanGoBack == true) _web.GoBack(); }, 140, "SmallPill"));
        bar.Children.Add(ViewKit.Pill("Forward", () => { if (_web?.CanGoForward == true) _web.GoForward(); }, 160, "SmallPill"));
        bar.Children.Add(ViewKit.Pill("Reload", () => _web?.Reload(), 150, "SmallPill"));
        bar.Children.Add(ViewKit.Pill("Home", GoHomePage, 140, "SmallPill"));
        _address = new TextBox { Width = 820, FontSize = 26, MinHeight = 64, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        _address.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Go(_address.Text);
            e.Handled = true;
        };
        bar.Children.Add(_address);
        bar.Children.Add(ViewKit.Pill("Go", () => Go(_address.Text), 110, "SmallPill"));
        bar.Children.Add(ViewKit.Pill("−", () => Zoom(-0.1), 80, "SmallPill"));
        bar.Children.Add(ViewKit.Pill("+", () => Zoom(0.1), 80, "SmallPill"));
        bar.Children.Add(ViewKit.Pill("Menu", window.ReturnToMenu, 200, "SmallPill"));
        _toolbarContent = bar;
        toolbar.Child = bar;
        root.Children.Add(toolbar);

        Content = root;
        SizeChanged += (_, _) =>
        {
            var scale = Math.Clamp(ActualWidth / 1920.0, 0.55, 2.5);
            _toolbarContent.LayoutTransform = new ScaleTransform(scale, scale);
        };
    }

    public bool WantsSystemCursor => true;

    public async void OnShown()
    {
        if (_web is not null || _initializing) return;
        _initializing = true;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, _host.Paths.WebViewDataDirectory);
            _web = new WebView2();
            _webHost.Children.Add(_web);
            await _web.EnsureCoreWebView2Async(environment);
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                core.Navigate(e.Uri);
            };
            core.SourceChanged += (_, _) =>
            {
                if (_address.IsKeyboardFocused) return;
                var source = core.Source ?? "";
                _address.Text = source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || source == "about:blank" ? "" : source;
            };
            GoHomePage();
        }
        catch (Exception ex)
        {
            Log.Warn("WebView2 is not available", ex);
            _fallback.Visibility = Visibility.Visible;
        }
        finally
        {
            _initializing = false;
        }
    }

    public void OnHidden() => Teardown();
    public bool HandleBack() => false;

    private void GoHomePage()
    {
        if (_web?.CoreWebView2 is null) return;
        if (Uri.TryCreate(_host.Settings.Current.BrowserHome, UriKind.Absolute, out var home) && home.Scheme is "http" or "https")
            _web.CoreWebView2.Navigate(home.ToString());
        else
            _web.NavigateToString(StartPage());
        _address.Text = "";
    }

    private void Go(string input)
    {
        input = input.Trim();
        if (input.Length == 0 || _web?.CoreWebView2 is null) return;
        string target;
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") target = uri.ToString();
        else if (input.Contains('.') && !input.Contains(' ')) target = "https://" + input;
        else target = string.Format(_host.Settings.Current.SearchUrl, Uri.EscapeDataString(input));
        _web.CoreWebView2.Navigate(target);
        _web.Focus();
    }

    private void Zoom(double delta)
    {
        if (_web is null) return;
        _web.ZoomFactor = Math.Clamp(_web.ZoomFactor + delta, 0.5, 3);
    }

    private string StartPage()
    {
        var tiles = new StringBuilder();
        foreach (var bookmark in _host.Settings.Current.Bookmarks)
        {
            var title = WebUtility.HtmlEncode(bookmark.Title);
            var url = WebUtility.HtmlEncode(bookmark.Url);
            var letter = WebUtility.HtmlEncode(bookmark.Title.Length > 0 ? bookmark.Title[..1].ToUpperInvariant() : "?");
            tiles.Append($"<a class=\"tile\" href=\"{url}\"><span class=\"badge\">{letter}</span><span class=\"name\">{title}</span></a>");
        }
        var search = WebUtility.HtmlEncode(_host.Settings.Current.SearchUrl.Replace("{0}", ""));

        // The start page follows the active theme.
        var fallback = Color.FromRgb(0x55, 0x63, 0x6B);
        string C(string key) => ThemeManager.Css(ThemeManager.ColorOf(key, fallback));
        var (bgTop, bgBottom) = ThemeManager.Ends("BackgroundBrush", Colors.White);
        var (buttonTop, buttonBottom) = ThemeManager.Ends("ButtonBrush", Colors.White);
        var (tileTop, tileBottom) = ThemeManager.Ends("PanelBrush", Colors.White);
        var radius = Application.Current.TryFindResource("PillCornerRadius") is CornerRadius r ? Math.Min(40, r.TopLeft) : 40;
        var fontSource = (Application.Current.TryFindResource("AppFont") as FontFamily)?.Source ?? "";
        var font = fontSource.Contains("Rounded Mplus", StringComparison.OrdinalIgnoreCase)
            ? "\"M PLUS Rounded 1c\",\"Segoe UI\""
            : string.Join(",", fontSource.Split(',').Select(f => $"\"{f.Trim()}\""));
        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Web</title>
            <style>
            html,body{margin:0;min-height:100%;font-family:{{font}},sans-serif;color:{{C("TextBrush")}};
              background:linear-gradient({{ThemeManager.Css(bgTop)}},{{ThemeManager.Css(bgBottom)}}) fixed}
            main{max-width:1100px;margin:0 auto;padding:6vh 32px}
            h1{font-size:64px;margin:0 0 8px;color:{{C("AccentBrush")}};font-weight:800}
            p{font-size:22px;margin:0 0 36px;color:{{C("SubtleTextBrush")}}}
            form{display:flex;gap:16px;margin-bottom:48px}
            input{flex:1;font-size:28px;padding:18px 28px;border-radius:{{radius}}px;border:4px solid {{C("ButtonBorderBrush")}};outline:none;font-family:inherit;color:{{C("TextBrush")}};background:{{C("CardBrush")}}}
            input:focus{border-color:{{C("AccentBrush")}}}
            button{font-size:28px;font-weight:700;padding:0 40px;border-radius:{{radius}}px;border:4px solid {{C("ButtonBorderBrush")}};background:linear-gradient({{ThemeManager.Css(buttonTop)}},{{ThemeManager.Css(buttonBottom)}});color:{{C("ButtonTextBrush")}};font-family:inherit;cursor:pointer}
            button:hover{border-color:{{C("AccentBrush")}};color:{{C("AccentDeepBrush")}}}
            .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:24px}
            .tile{display:flex;flex-direction:column;align-items:center;gap:14px;padding:28px 16px;border-radius:{{Math.Min(28, radius)}}px;border:4px solid {{C("PanelBorderBrush")}};
              background:linear-gradient({{ThemeManager.Css(tileTop)}},{{ThemeManager.Css(tileBottom)}});text-decoration:none;color:{{C("TextBrush")}};font-size:24px;font-weight:700;transition:transform .15s,border-color .15s}
            .tile:hover{transform:scale(1.05);border-color:{{C("AccentBrush")}};box-shadow:0 0 0 6px {{C("AccentGlowBrush")}}}
            .badge{width:80px;height:80px;border-radius:{{Math.Min(24, radius)}}px;background:{{C("AccentBrush")}};color:{{C("BadgeRimBrush")}};font-size:44px;display:flex;align-items:center;justify-content:center}
            </style></head><body><main>
            <h1>Internet</h1><p>Search the web or pick a favorite.</p>
            <form action="{{search}}" method="get" onsubmit="event.preventDefault();location.href='{{search}}'+encodeURIComponent(document.getElementById('q').value)">
              <input id="q" name="q" placeholder="Search or type a web address" autofocus><button type="submit">Search</button></form>
            <div class="grid">{{tiles}}</div></main></body></html>
            """;
    }

    private void Teardown()
    {
        if (_web is null) return;
        _webHost.Children.Remove(_web);
        _web.Dispose();
        _web = null;
    }

    public void Dispose() => Teardown();
}
