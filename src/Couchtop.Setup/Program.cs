using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Storage;

namespace Couchtop.Setup;

/// <summary>
///   Couchtop.Setup.exe                    install (from the extracted package) or uninstall (from the install folder)
///   --install [--quiet] [--dir PATH] [--no-desktop-shortcut] [--start-at-sign-in] [--no-launch]
///   --uninstall [--quiet] [--purge]
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Log.Initialize(AppPaths.ForCurrentUser().LogDirectory, "setup");
        bool Has(string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        string? Value(string flag)
        {
            var i = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var here = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        var installed = Installer.InstalledDirectory();
        var runningFromInstall = installed is not null && string.Equals(installed.TrimEnd('\\'), here, StringComparison.OrdinalIgnoreCase);
        var uninstall = Has("--uninstall") || (!Has("--install") && runningFromInstall);
        var target = Value("--dir") ?? installed ?? AppPaths.DefaultInstallDirectory;

        if (Has("--quiet"))
        {
            try
            {
                var installer = new Installer();
                var result = uninstall
                    ? installer.Uninstall(installed ?? here, Has("--purge"))
                    : installer.Install(here, target, new InstallOptions(!Has("--no-desktop-shortcut"), Has("--start-at-sign-in"), !Has("--no-launch")));
                Log.Info(result.Message);
                return result.Success ? 0 : 2;
            }
            catch (Exception ex)
            {
                Log.Error("Setup failed", ex);
                return 3;
            }
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new SetupWindow(uninstall, here, installed, target);
        return app.Run(window);
    }
}

public sealed class SetupWindow : Window
{
    private readonly bool _uninstall;
    private readonly string _source;
    private readonly string? _installed;
    private readonly string _target;
    private readonly TextBlock _status;
    private readonly Button _primary;
    private readonly Button _close;
    private readonly ToggleButton? _desktop;
    private readonly ToggleButton? _startup;
    private readonly ToggleButton? _launch;
    private readonly ToggleButton? _purge;

    private static readonly Color Accent = Color.FromRgb(0x35, 0xB4, 0xE5);
    private static readonly Color Text = Color.FromRgb(0x55, 0x63, 0x6B);

    public SetupWindow(bool uninstall, string source, string? installed, string target)
    {
        _uninstall = uninstall;
        _source = source;
        _installed = installed;
        _target = target;

        Title = uninstall ? "Uninstall Couchtop" : "Couchtop Setup";
        Width = 880;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        Background = new LinearGradientBrush(Colors.White, Color.FromRgb(0xE6, 0xEC, 0xEF), 90);

        var stack = new StackPanel { Margin = new Thickness(48, 40, 48, 36) };
        stack.Children.Add(new TextBlock { Text = uninstall ? "Uninstall Couchtop" : "Install Couchtop", FontSize = 38, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Accent) });
        var intro = uninstall
            ? "Couchtop will first make sure Windows Explorer is your desktop again (turning Shell Mode off if it is on), then remove the program."
            : $"Couchtop turns Windows into a playful, console-style channel menu. It installs just for you, no administrator rights needed.\n\nInstall folder: {target}";
        stack.Children.Add(new TextBlock { Text = intro, FontSize = 17, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Text), Margin = new Thickness(0, 12, 0, 20) });

        if (uninstall)
        {
            _purge = Toggle("Also delete my channels, settings and logs", false);
            stack.Children.Add(_purge);
        }
        else
        {
            _desktop = Toggle("Create a desktop shortcut", true);
            _startup = Toggle("Start Couchtop when I sign in (launcher mode, Explorer stays available)", false);
            _launch = Toggle("Open Couchtop when setup finishes", true);
            stack.Children.Add(_desktop);
            stack.Children.Add(_startup);
            stack.Children.Add(_launch);
            stack.Children.Add(new TextBlock
            {
                Text = "Shell Mode mode (replacing Explorer at sign-in) is never turned on by Setup. Enable it later from Settings › Shell Mode after the Shell Safety Test.",
                FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0x9C)), Margin = new Thickness(0, 12, 0, 0),
            });
        }

        _status = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 16), MinHeight = 24 };
        stack.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _primary = Pill(uninstall ? "Uninstall" : installed is null ? "Install" : "Update");
        _close = Pill("Cancel");
        _primary.Click += async (_, _) => await RunAsync();
        _close.Click += (_, _) => Close();
        buttons.Children.Add(_close);
        buttons.Children.Add(_primary);
        stack.Children.Add(buttons);
        Content = stack;
    }

    private static readonly ControlTemplate PillTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ButtonBase">
          <Border x:Name="B" CornerRadius="26" BorderThickness="3" BorderBrush="#BCC5CB" Padding="{TemplateBinding Padding}">
            <Border.Background><LinearGradientBrush StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFFF" Offset="0"/><GradientStop Color="#E2E8EC" Offset="1"/></LinearGradientBrush></Border.Background>
            <ContentPresenter HorizontalAlignment="Left" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="B" Property="BorderBrush" Value="#35B4E5"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="B" Property="BorderBrush" Value="#35B4E5"/></Trigger>
            <Trigger Property="ToggleButton.IsChecked" Value="True"><Setter TargetName="B" Property="BorderBrush" Value="#35B4E5"/><Setter TargetName="B" Property="Background" Value="#DCF3FC"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    private static Button Pill(string text) => new()
    {
        Content = text, Template = PillTemplate, MinWidth = 150, Height = 52, Padding = new Thickness(28, 0, 28, 0), Margin = new Thickness(12, 0, 0, 0),
        FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Text), HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    private static ToggleButton Toggle(string text, bool isChecked) => new()
    {
        Content = "  " + text, IsChecked = isChecked, Template = PillTemplate, Height = 48, Padding = new Thickness(20, 0, 20, 0), Margin = new Thickness(0, 0, 0, 10),
        FontSize = 16, Foreground = new SolidColorBrush(Text), HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private async Task RunAsync()
    {
        _primary.IsEnabled = false;
        _close.IsEnabled = false;
        var installer = new Installer(message => Dispatcher.BeginInvoke(() => _status.Text = message));
        SetupResult result;
        try
        {
            if (_uninstall)
            {
                var purge = _purge?.IsChecked == true;
                result = await Task.Run(() => installer.Uninstall(_installed ?? _source, purge));
            }
            else
            {
                var options = new InstallOptions(_desktop?.IsChecked == true, _startup?.IsChecked == true, _launch?.IsChecked == true);
                result = await Task.Run(() => installer.Install(_source, _target, options));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Setup failed", ex);
            result = new SetupResult(false, "Setup failed: " + ex.Message);
        }

        _status.Text = result.Message;
        _status.Foreground = new SolidColorBrush(result.Success ? Color.FromRgb(0x4F, 0xA8, 0x33) : Color.FromRgb(0xD0, 0x45, 0x5F));
        _close.Content = "Close";
        _close.IsEnabled = true;
        if (!result.Success) _primary.IsEnabled = true;
        if (result.Success) NativeMethods.MessageBox(IntPtr.Zero, result.Message, Title, NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);
    }
}
