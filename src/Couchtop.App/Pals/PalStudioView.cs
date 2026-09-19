using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Couchtop.App.Views;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>
/// Pal Studio: the Pals channel. A live 3D Pal on a podium in the middle, categories down the left and every
/// choice for the current category on the right. Changes show instantly; nothing is kept until Save.
/// </summary>
public sealed class PalStudioView : UserControl, IScreenView, IPalHost
{
    private static readonly (string Name, AvatarFraming Framing)[] Categories =
    {
        ("Body", AvatarFraming.FullBody),
        ("Face", AvatarFraming.Face),
        ("Eyes", AvatarFraming.Face),
        ("Hair", AvatarFraming.Bust),
        ("Top", AvatarFraming.FullBody),
        ("Bottoms", AvatarFraming.FullBody),
        ("Extras", AvatarFraming.Bust),
        ("Personality", AvatarFraming.FullBody),
    };

    private static readonly PalGesture[] Reactions = { PalGesture.Nod, PalGesture.ThumbsUp, PalGesture.Spin, PalGesture.Cheer, PalGesture.Clap, PalGesture.Wave };

    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly bool _firstTime;
    private readonly AvatarView _stage = new();
    private readonly StackPanel _rail = new();
    private readonly StackPanel _options = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _nameLabel;
    private readonly Stack<PalProfile> _undo = new();
    private readonly DispatcherTimer _rebuild;
    private readonly List<ToggleButton> _railButtons = new();
    private PalProfile _profile;
    private string _category = "Body";
    private bool _dirty;
    private int _reaction;
    private Point? _dragFrom;
    private double _dragFacing;

    public PalStudioView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;
        _firstTime = !host.Pals.HasPal;
        _profile = host.Pals.Profile?.Clone() ?? PalProfile.Random(Environment.TickCount);
        _dirty = _firstTime;

        _rebuild = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(70) };
        _rebuild.Tick += (_, _) =>
        {
            _rebuild.Stop();
            ShowProfile(react: false);
        };

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(620) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ------------------------------------------------ categories
        foreach (var (name, _) in Categories)
        {
            var button = ViewKit.Option(name, name == _category, () => SelectCategory(name));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 0, 12);
            _railButtons.Add(button);
            _rail.Children.Add(button);
        }
        body.Children.Add(_rail);

        // ------------------------------------------------ the stage
        var stageCard = new Border { Margin = new Thickness(30, 0, 30, 0), ClipToBounds = true };
        stageCard.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
        stageCard.SetResourceReference(Border.BorderThicknessProperty, "PanelBorderThickness");
        stageCard.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        stageCard.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        var stageGrid = new Grid();
        // A soft spotlight behind the Pal.
        stageGrid.Children.Add(new Ellipse
        {
            Width = 620,
            Height = 620,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 0, 0),
            Fill = new RadialGradientBrush(Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)),
            IsHitTestVisible = false,
        });
        stageGrid.Children.Add(_stage);
        _stage.Margin = new Thickness(0, 0, 0, 110);
        _stage.Background = Brushes.Transparent;
        _stage.Cursor = Cursors.SizeWE;
        _stage.MouseLeftButtonDown += (_, e) =>
        {
            _dragFrom = e.GetPosition(this);
            _dragFacing = _stage.Facing;
            _stage.CaptureMouse();
        };
        _stage.MouseMove += (_, e) =>
        {
            if (_dragFrom is not { } from) return;
            _stage.Facing = _dragFacing + (e.GetPosition(this).X - from.X) * 0.6;
        };
        _stage.MouseLeftButtonUp += (_, _) =>
        {
            _dragFrom = null;
            _stage.ReleaseMouseCapture();
        };
        _stage.Extras.Children.Add(Podium());

        var under = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 24) };
        _nameLabel = ViewKit.Text(_profile.Name, 44, FontWeights.ExtraBold, "AccentDeepBrush", wrap: false, align: TextAlignment.Center);
        _nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        under.Children.Add(_nameLabel);
        var turns = ViewKit.Horizontal(
            Small("Turn left", () => _stage.Facing -= 45, 150),
            Small("Face me", () => _stage.Facing = 0, 150),
            Small("Turn right", () => _stage.Facing += 45, 150));
        turns.HorizontalAlignment = HorizontalAlignment.Center;
        turns.Margin = new Thickness(0, 8, 0, 0);
        under.Children.Add(turns);
        stageGrid.Children.Add(under);
        stageCard.Child = stageGrid;
        Grid.SetColumn(stageCard, 1);
        body.Children.Add(stageCard);

        // ------------------------------------------------ choices
        _scroll = ViewKit.Scroll(_options);
        Grid.SetColumn(_scroll, 2);
        body.Children.Add(_scroll);

        Content = ViewKit.Scaffold("Pals", _firstTime ? "Make your Pal. They'll hang out with you all over Couchtop." : "Pal Studio", body, Back,
            ViewKit.Pill("Surprise me", Randomize, 250),
            ViewKit.Pill("Undo", Undo, 170),
            ViewKit.Pill(_firstTime ? "Done" : "Save", Save, 190));

        ShowProfile(react: false);
        _stage.SnapFacing(-18);
        _stage.SetFraming(AvatarFraming.FullBody, instant: true);
        BuildOptions();
    }

    public void OnShown()
    {
        _stage.Animator?.Play(_firstTime ? PalGesture.Wave : PalGesture.Cheer);
        _stage.Facing = 0;
    }

    public void OnHidden() { }

    /// <summary>The Studio has its own Pal on the podium, so no corner peek while editing.</summary>
    public bool ShowsPal => true;

    public bool HandleBack()
    {
        Back();
        return true;
    }

    // ---------------------------------------------------------------- actions

    private void Back()
    {
        if (!_dirty)
        {
            _window.ReturnToMenu();
            return;
        }
        _ = AskAsync();

        async Task AskAsync()
        {
            var message = _firstTime ? "Keep this Pal? You can change anything later in the Pals channel." : "Save the changes to your Pal?";
            var choice = await _window.ShowDialogAsync(message, "Save", "Don't save", "Keep editing");
            if (choice == "Save") Save();
            else if (choice == "Don't save") _window.ReturnToMenu();
        }
    }

    private void Save()
    {
        _profile.Name = PalProfile.CleanName(_profile.Name);
        _host.Pals.SaveProfile(_profile);
        _dirty = false;
        _window.ReturnToMenu();
    }

    private void Randomize()
    {
        var keepName = _profile.Name;
        Change(p =>
        {
            var random = PalProfile.Random(Environment.TickCount ^ _undo.Count * 7919);
            random.Name = keepName;
            random.Personality = p.Personality;
            return random;
        }, react: true, gesture: PalGesture.Spin);
        BuildOptions();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _profile = _undo.Pop();
        _dirty = true;
        ShowProfile(react: false);
        BuildOptions();
    }

    private void Change(Func<PalProfile, PalProfile> edit, bool react = true, PalGesture? gesture = null, bool live = false)
    {
        _undo.Push(_profile.Clone());
        if (_undo.Count > 40) TrimUndo();
        _profile = edit(_profile.Clone()).Normalize();
        _dirty = true;
        if (live)
        {
            // Sliders rebuild at most every few frames while dragging.
            _rebuild.Stop();
            _rebuild.Start();
            return;
        }
        ShowProfile(react, gesture);
    }

    private void TrimUndo()
    {
        var keep = _undo.Take(30).Reverse().ToList();
        _undo.Clear();
        foreach (var p in keep) _undo.Push(p);
    }

    private void ShowProfile(bool react, PalGesture? gesture = null)
    {
        _stage.SetProfile(_profile);
        _stage.Model?.WarmUpFaces();
        _nameLabel.Text = _profile.Name;
        _stage.SetFraming(FramingFor(_category));
        if (!react || _stage.Animator is null) return;
        var move = gesture ?? Reactions[_reaction++ % Reactions.Length];
        _stage.Animator.Play(move, PalMood.Excited);
    }

    private static AvatarFraming FramingFor(string category) => Categories.First(c => c.Name == category).Framing;

    public void SelectCategory(string name)
    {
        _category = name;
        for (var i = 0; i < Categories.Length; i++) _railButtons[i].IsChecked = Categories[i].Name == name;
        _stage.SetFraming(FramingFor(name));
        _stage.Facing = name is "Hair" or "Extras" ? -25 : 0;
        BuildOptions();
        _scroll.ScrollToTop();
    }

    // ---------------------------------------------------------------- the options panel

    private void BuildOptions()
    {
        _options.Children.Clear();
        var p = _profile;
        switch (_category)
        {
            case "Body":
                Sliders("Proportions",
                    ("Height", p.Height, v => p2 => { p2.Height = v; return p2; }),
                    ("Build", p.Build, v => p2 => { p2.Build = v; return p2; }),
                    ("Head size", p.HeadSize, v => p2 => { p2.HeadSize = v; return p2; }),
                    ("Legs", p.LegLength, v => p2 => { p2.LegLength = v; return p2; }));
                Swatches("Skin", PalColors.Skin, p.Skin, (x, c) => x.Skin = c);
                break;
            case "Face":
                Choices("Face shape", PalCatalog.FaceShapes, p.FaceShape, (x, id) => x.FaceShape = id);
                Choices("Nose", PalCatalog.NoseStyles, p.NoseStyle, (x, id) => x.NoseStyle = id);
                Choices("Mouth", PalCatalog.MouthStyles, p.MouthStyle, (x, id) => x.MouthStyle = id);
                Choices("Cheeks", PalCatalog.CheekStyles, p.Cheeks, (x, id) => x.Cheeks = id);
                Choices("Facial hair", PalCatalog.FacialHairStyles, p.FacialHair, (x, id) => x.FacialHair = id);
                break;
            case "Eyes":
                Choices("Eyes", PalCatalog.EyeStyles, p.EyeStyle, (x, id) => x.EyeStyle = id);
                Swatches("Eye color", PalColors.Eyes, p.EyeColor, (x, c) => x.EyeColor = c);
                Sliders("Placement",
                    ("Size", p.EyeSize, v => p2 => { p2.EyeSize = v; return p2; }),
                    ("Spacing", p.EyeSpacing, v => p2 => { p2.EyeSpacing = v; return p2; }),
                    ("Height", p.EyeHeight, v => p2 => { p2.EyeHeight = v; return p2; }));
                Choices("Eyebrows", PalCatalog.BrowStyles, p.BrowStyle, (x, id) => x.BrowStyle = id);
                break;
            case "Hair":
                Choices("Hairstyle", PalCatalog.HairStyles, p.HairStyle, (x, id) => x.HairStyle = id);
                Swatches("Hair color", PalColors.Hair, p.HairColor, (x, c) => x.HairColor = c);
                Swatches("Dyed tips", new[] { "none" }.Concat(PalColors.Accent.Skip(1)).ToList(), p.HairTips, (x, c) => x.HairTips = c);
                break;
            case "Top":
                Choices("Top", PalCatalog.TopStyles, p.TopStyle, (x, id) => x.TopStyle = id);
                Swatches("Color", PalColors.Outfit, p.TopColor, (x, c) => x.TopColor = c);
                Choices("Print", PalCatalog.Patterns, p.TopPattern, (x, id) => x.TopPattern = id);
                Swatches("Trim and print color", PalColors.Accent, p.TopAccent, (x, c) => x.TopAccent = c);
                break;
            case "Bottoms":
                if (p.TopStyle != "dress")
                {
                    Choices("Bottoms", PalCatalog.BottomStyles, p.BottomStyle, (x, id) => x.BottomStyle = id);
                    Swatches("Color", PalColors.Bottoms, p.BottomColor, (x, c) => x.BottomColor = c);
                }
                else
                {
                    Note("Your Pal is wearing a dress. Pick another top to choose trousers, shorts or a skirt.");
                }
                Choices("Shoes", PalCatalog.ShoeStyles, p.ShoeStyle, (x, id) => x.ShoeStyle = id);
                Swatches("Shoe color", PalColors.Outfit, p.ShoeColor, (x, c) => x.ShoeColor = c);
                break;
            case "Extras":
                Choices("Hat", PalCatalog.Hats, p.Hat, (x, id) => x.Hat = id);
                if (p.Hat is not ("none" or "crown")) Swatches("Hat color", PalColors.Outfit, p.HatColor, (x, c) => x.HatColor = c);
                Choices("Glasses", PalCatalog.GlassesStyles, p.Glasses, (x, id) => x.Glasses = id);
                if (p.Glasses != "none") Swatches("Frame color", PalColors.Frames, p.GlassesColor, (x, c) => x.GlassesColor = c);
                Choices("Also wearing", PalCatalog.Extras, p.Extra, (x, id) => x.Extra = id);
                if (p.Extra != "none") Swatches("Color", PalColors.Outfit, p.ExtraColor, (x, c) => x.ExtraColor = c);
                Choices("Earrings", PalCatalog.EarringStyles, p.Earrings, (x, id) => x.Earrings = id);
                break;
            case "Personality":
                BuildPersonality();
                break;
        }
    }

    private void BuildPersonality()
    {
        var nameBox = new TextBox { Text = _profile.Name, FontSize = 34, MaxLength = 16, MinWidth = 420, Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left };
        nameBox.SetResourceReference(FontFamilyProperty, "AppFont");
        nameBox.TextChanged += (_, _) =>
        {
            _profile.Name = nameBox.Text;
            _nameLabel.Text = PalProfile.CleanName(nameBox.Text);
            _dirty = true;
        };
        var names = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var suggestion in PalCatalog.SuggestedNames.Take(8))
            names.Children.Add(Small(suggestion, () => nameBox.Text = suggestion, 0));
        Section("Name", nameBox, names);

        var blurb = ViewKit.Text(PalCatalog.PersonalityBlurb(_profile.Personality), 26, brush: "SubtleTextBrush");
        blurb.Margin = new Thickness(0, 12, 0, 0);
        var personalities = ChoicePanel(PalCatalog.Personalities, _profile.Personality, id =>
        {
            Change(x => { x.Personality = id; return x; }, react: true, gesture: id switch
            {
                "chill" => PalGesture.Yawn,
                "sporty" => PalGesture.Stretch,
                "curious" => PalGesture.LookAround,
                "cheeky" => PalGesture.Dance,
                _ => PalGesture.Cheer,
            });
            BuildOptions();
        });
        Section("Personality", personalities, blurb);

        // How the Pal behaves around Couchtop. These apply straight away, even before saving the look.
        var prefs = _host.Pals.Preferences;
        var talk = ChoicePanel(new[]
        {
            new PalOption(nameof(PalChattiness.Chatty), "Chatty"), new PalOption(nameof(PalChattiness.Normal), "Now and then"),
            new PalOption(nameof(PalChattiness.Quiet), "Quiet"), new PalOption(nameof(PalChattiness.Silent), "Never talks"),
        }, prefs.Chattiness.ToString(), id =>
        {
            prefs.Chattiness = Enum.Parse<PalChattiness>(id);
            _host.Pals.SavePreferences();
            BuildOptions();
        });
        var hint = ViewKit.Text("Your Pal never talks over a full-screen game or video, and remarks are spaced out so they don't pile up.", 24, brush: "SubtleTextBrush");
        hint.Margin = new Thickness(0, 12, 0, 0);
        Section("How often they talk", talk, hint);

        var toggles = new StackPanel();
        toggles.Children.Add(Toggle("Hang out on the home screen", prefs.ShowOnHome, v => prefs.ShowOnHome = v));
        toggles.Children.Add(Toggle("Walk around on their own", prefs.Wander, v => prefs.Wander = v));
        toggles.Children.Add(Toggle("Visit my desktop and apps", prefs.DesktopVisits, v => prefs.DesktopVisits = v));
        toggles.Children.Add(Toggle("Pop up in the Couchtop Bar while you use apps", prefs.BarReactions, v => prefs.BarReactions = v));
        Section("Around Couchtop", toggles);
    }

    private UIElement Toggle(string label, bool value, Action<bool> set)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var options = ViewKit.Horizontal(
            ViewKit.Option("On", value, () => { set(true); _host.Pals.SavePreferences(); BuildOptions(); }),
            ViewKit.Option("Off", !value, () => { set(false); _host.Pals.SavePreferences(); BuildOptions(); }));
        DockPanel.SetDock(options, Dock.Right);
        row.Children.Add(options);
        var text = ViewKit.Text(label, 28, FontWeights.Bold);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }

    private void Note(string text) => _options.Children.Add(ViewKit.Card(ViewKit.Text(text, 26, brush: "SubtleTextBrush")));

    private void Section(string title, params UIElement[] content)
    {
        var stack = new StackPanel();
        var heading = ViewKit.Text(title, 32, FontWeights.ExtraBold, wrap: false);
        heading.Margin = new Thickness(0, 0, 0, 14);
        stack.Children.Add(heading);
        foreach (var c in content) stack.Children.Add(c);
        _options.Children.Add(ViewKit.Card(stack, new Thickness(32, 22, 32, 24)));
    }

    private void Choices(string title, IReadOnlyList<PalOption> options, string current, Action<PalProfile, string> apply) =>
        Section(title, ChoicePanel(options, current, id =>
        {
            Change(x => { apply(x, id); return x; });
            BuildOptions();
        }));

    private static WrapPanel ChoicePanel(IReadOnlyList<PalOption> options, string current, Action<string> pick)
    {
        var panel = new WrapPanel();
        foreach (var option in options)
        {
            var id = option.Id;
            var button = ViewKit.Option(option.Label, id == current, () => pick(id));
            button.Margin = new Thickness(0, 0, 10, 10);
            panel.Children.Add(button);
        }
        return panel;
    }

    private void Swatches(string title, IReadOnlyList<string> colors, string current, Action<PalProfile, string> apply)
    {
        var panel = new WrapPanel();
        foreach (var color in colors)
        {
            var value = color;
            var selected = string.Equals(value, current, StringComparison.OrdinalIgnoreCase);
            panel.Children.Add(Swatch(value, selected, () =>
            {
                Change(x => { apply(x, value); return x; }, react: _reaction % 3 == 0);
                _reaction++;
                BuildOptions();
            }));
        }
        Section(title, panel);
    }

    private static Button Swatch(string color, bool selected, Action pick)
    {
        var grid = new Grid { Width = 66, Height = 66 };
        var ring = new Ellipse { StrokeThickness = selected ? 6 : 3 };
        ring.SetResourceReference(Shape.StrokeProperty, selected ? "AccentBrush" : "PanelBorderBrush");
        grid.Children.Add(ring);
        if (color == "none")
        {
            var none = new Ellipse { Margin = new Thickness(9), Fill = Brushes.White };
            grid.Children.Add(none);
            grid.Children.Add(new Line { X1 = 20, Y1 = 46, X2 = 46, Y2 = 20, StrokeThickness = 5, Stroke = new SolidColorBrush(Color.FromRgb(200, 90, 90)), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        else
        {
            var c = AvatarMaterials.Parse(color);
            grid.Children.Add(new Ellipse
            {
                Margin = new Thickness(9),
                Fill = new RadialGradientBrush(AvatarMaterials.Lighten(c, 0.25), c) { GradientOrigin = new Point(0.35, 0.3) },
                Stroke = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                StrokeThickness = 1,
            });
        }
        var button = new Button
        {
            Content = grid,
            Width = 66,
            Height = 66,
            MinWidth = 0,
            MinHeight = 0,
            Margin = new Thickness(0, 0, 12, 12),
            Template = BareTemplate,
            Cursor = Cursors.Hand,
            ToolTip = color == "none" ? "None" : null,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        button.MouseEnter += (_, _) => ((ScaleTransform)button.RenderTransform).ScaleX = ((ScaleTransform)button.RenderTransform).ScaleY = 1.12;
        button.MouseLeave += (_, _) => ((ScaleTransform)button.RenderTransform).ScaleX = ((ScaleTransform)button.RenderTransform).ScaleY = 1;
        button.Click += (_, _) => pick();
        return button;
    }

    private static readonly ControlTemplate BareTemplate = CreateBareTemplate();

    private static ControlTemplate CreateBareTemplate()
    {
        var template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        template.Seal();
        return template;
    }

    private void Sliders(string title, params (string Label, double Value, Func<double, Func<PalProfile, PalProfile>> Edit)[] sliders)
    {
        var stack = new StackPanel();
        foreach (var (label, value, edit) in sliders)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = ViewKit.Text(label, 28, FontWeights.Bold, wrap: false);
            text.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(text);
            var slider = new Slider { Minimum = 0, Maximum = 1, Value = value, VerticalAlignment = VerticalAlignment.Center, SmallChange = 0.05, LargeChange = 0.1 };
            var dragging = false;
            slider.ValueChanged += (_, e) =>
            {
                // One undo step per drag, not one per pixel.
                if (!dragging)
                {
                    _undo.Push(_profile.Clone());
                    dragging = true;
                }
                _profile = edit(e.NewValue)(_profile.Clone()).Normalize();
                _dirty = true;
                _rebuild.Stop();
                _rebuild.Start();
            };
            slider.PreviewMouseUp += (_, _) => dragging = false;
            slider.LostKeyboardFocus += (_, _) => dragging = false;
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            stack.Children.Add(row);
        }
        Section(title, stack);
    }

    private static Button Small(string text, Action onClick, double minWidth) => ViewKit.Pill(text, onClick, minWidth, "SmallPill");

    /// <summary>A round podium under the Pal's feet.</summary>
    private static Model3DGroup Podium()
    {
        var group = new Model3DGroup();
        var top = AvatarMaterials.Soft(Color.FromRgb(250, 251, 252), 0.35, 40);
        var side = AvatarMaterials.Soft(Color.FromRgb(214, 226, 234), 0.1);
        group.Children.Add(AvatarModel.Model(AvatarMeshes.Lathe(new[] { (0.31, -0.05), (0.325, -0.025), (0.32, -0.004), (0.0005, 0.0) }, 48), side));
        group.Children.Add(AvatarModel.Model(AvatarMeshes.Ring(0.3, 0.01, 60, 6), AvatarMaterials.Glow(Color.FromRgb(160, 220, 245)), AvatarModel.At(0, -0.004, 0)));
        group.Children.Add(AvatarModel.Model(AvatarMeshes.DiscZ(0.31, 0.31, 48), top, AvatarModel.At(0, -0.001, 0, pitch: -90)));
        return group;
    }

    /// <summary>Snapshot renders: open a category and settle the Pal.</summary>
    internal void SnapshotPrepare(string category)
    {
        SelectCategory(category);
        _stage.SnapFacing(category is "Hair" or "Extras" ? -25 : 0);
        _stage.SetFraming(FramingFor(category), instant: true);
        _stage.Animator!.Fidgets = false;
        _stage.Step(0.8, 20);
    }
}
