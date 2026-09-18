using System.Globalization;

namespace Couchtop.Core.Pals;

/// <summary>One choice in a Pal Studio category: a stable id for the saved file and a friendly label.</summary>
public sealed record PalOption(string Id, string Label);

/// <summary>
/// Everything that describes a Pal's look. Choices are stored as stable string ids and colors as #RRGGBB, so
/// saved Pals survive new options being added. Sliders are 0..1 with 0.5 as the everyday middle.
/// </summary>
public sealed class PalProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Name { get; set; } = "Pal";

    // Body
    public double Height { get; set; } = 0.5;
    public double Build { get; set; } = 0.5;
    public double HeadSize { get; set; } = 0.5;
    public double LegLength { get; set; } = 0.5;
    public string Skin { get; set; } = "#F2C9A5";

    // Face
    public string FaceShape { get; set; } = "round";
    public string EyeStyle { get; set; } = "round";
    public string EyeColor { get; set; } = "#4A3426";
    public double EyeSize { get; set; } = 0.5;
    public double EyeSpacing { get; set; } = 0.5;
    public double EyeHeight { get; set; } = 0.5;
    public string BrowStyle { get; set; } = "soft";
    public string NoseStyle { get; set; } = "button";
    public string MouthStyle { get; set; } = "smile";
    public string Cheeks { get; set; } = "blush";
    public string FacialHair { get; set; } = "none";

    // Hair
    public string HairStyle { get; set; } = "short";
    public string HairColor { get; set; } = "#4B3224";
    public string HairTips { get; set; } = "none";

    // Outfit
    public string TopStyle { get; set; } = "hoodie";
    public string TopColor { get; set; } = "#35B4E5";
    public string TopAccent { get; set; } = "#FFFFFF";
    public string TopPattern { get; set; } = "plain";
    public string BottomStyle { get; set; } = "jeans";
    public string BottomColor { get; set; } = "#3E5C8A";
    public string ShoeStyle { get; set; } = "sneakers";
    public string ShoeColor { get; set; } = "#F25C54";

    // Accessories
    public string Hat { get; set; } = "none";
    public string HatColor { get; set; } = "#F25C54";
    public string Glasses { get; set; } = "none";
    public string GlassesColor { get; set; } = "#2E2A33";
    public string Extra { get; set; } = "none";
    public string ExtraColor { get; set; } = "#FFC93C";
    public string Earrings { get; set; } = "none";

    // Personality
    public string Personality { get; set; } = "cheerful";

    public PalProfile Clone() => (PalProfile)MemberwiseClone();

    /// <summary>Repairs a hand-edited or older file: unknown ids fall back, colors are validated, sliders clamped.</summary>
    public PalProfile Normalize()
    {
        Name = CleanName(Name);
        Height = Slider(Height);
        Build = Slider(Build);
        HeadSize = Slider(HeadSize);
        LegLength = Slider(LegLength);
        EyeSize = Slider(EyeSize);
        EyeSpacing = Slider(EyeSpacing);
        EyeHeight = Slider(EyeHeight);
        Skin = PalColors.Clean(Skin, "#F2C9A5");
        EyeColor = PalColors.Clean(EyeColor, "#4A3426");
        HairColor = PalColors.Clean(HairColor, "#4B3224");
        TopColor = PalColors.Clean(TopColor, "#35B4E5");
        TopAccent = PalColors.Clean(TopAccent, "#FFFFFF");
        BottomColor = PalColors.Clean(BottomColor, "#3E5C8A");
        ShoeColor = PalColors.Clean(ShoeColor, "#F25C54");
        HatColor = PalColors.Clean(HatColor, "#F25C54");
        GlassesColor = PalColors.Clean(GlassesColor, "#2E2A33");
        ExtraColor = PalColors.Clean(ExtraColor, "#FFC93C");
        FaceShape = PalCatalog.Pick(PalCatalog.FaceShapes, FaceShape);
        EyeStyle = PalCatalog.Pick(PalCatalog.EyeStyles, EyeStyle);
        BrowStyle = PalCatalog.Pick(PalCatalog.BrowStyles, BrowStyle);
        NoseStyle = PalCatalog.Pick(PalCatalog.NoseStyles, NoseStyle);
        MouthStyle = PalCatalog.Pick(PalCatalog.MouthStyles, MouthStyle);
        Cheeks = PalCatalog.Pick(PalCatalog.CheekStyles, Cheeks);
        FacialHair = PalCatalog.Pick(PalCatalog.FacialHairStyles, FacialHair);
        HairStyle = PalCatalog.Pick(PalCatalog.HairStyles, HairStyle);
        HairTips = HairTips == "none" ? "none" : PalColors.Clean(HairTips, "none");
        TopStyle = PalCatalog.Pick(PalCatalog.TopStyles, TopStyle);
        TopPattern = PalCatalog.Pick(PalCatalog.Patterns, TopPattern);
        BottomStyle = PalCatalog.Pick(PalCatalog.BottomStyles, BottomStyle);
        ShoeStyle = PalCatalog.Pick(PalCatalog.ShoeStyles, ShoeStyle);
        Hat = PalCatalog.Pick(PalCatalog.Hats, Hat);
        Glasses = PalCatalog.Pick(PalCatalog.GlassesStyles, Glasses);
        Extra = PalCatalog.Pick(PalCatalog.Extras, Extra);
        Earrings = PalCatalog.Pick(PalCatalog.EarringStyles, Earrings);
        Personality = PalCatalog.Pick(PalCatalog.Personalities, Personality);
        SchemaVersion = CurrentSchemaVersion;
        return this;
    }

    public static string CleanName(string? name)
    {
        var trimmed = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (trimmed.Length > 16) trimmed = trimmed[..16].Trim();
        return trimmed.Length == 0 ? "Pal" : trimmed;
    }

    private static double Slider(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;

    /// <summary>A complete, good-looking random Pal. The same seed always gives the same Pal.</summary>
    public static PalProfile Random(int seed)
    {
        var rng = new Random(seed);
        T One<T>(IReadOnlyList<T> list) => list[rng.Next(list.Count)];
        double Near(double spread = 0.28) => Math.Clamp(0.5 + (rng.NextDouble() * 2 - 1) * spread, 0, 1);

        var hairColor = One(PalColors.Hair);
        var outfit = One(PalColors.Outfit);
        var profile = new PalProfile
        {
            Name = One(PalCatalog.SuggestedNames),
            Height = Near(),
            Build = Near(),
            HeadSize = Near(0.2),
            LegLength = Near(0.2),
            Skin = One(PalColors.Skin),
            FaceShape = One(PalCatalog.FaceShapes).Id,
            EyeStyle = One(PalCatalog.EyeStyles).Id,
            EyeColor = One(PalColors.Eyes),
            EyeSize = Near(0.25),
            EyeSpacing = Near(0.2),
            EyeHeight = Near(0.2),
            BrowStyle = One(PalCatalog.BrowStyles).Id,
            NoseStyle = One(PalCatalog.NoseStyles).Id,
            MouthStyle = One(PalCatalog.MouthStyles).Id,
            Cheeks = One(PalCatalog.CheekStyles).Id,
            FacialHair = rng.NextDouble() < 0.18 ? One(PalCatalog.FacialHairStyles.Skip(1).ToList()).Id : "none",
            HairStyle = One(PalCatalog.HairStyles.Where(h => h.Id != "bald").ToList()).Id,
            HairColor = hairColor,
            HairTips = rng.NextDouble() < 0.2 ? One(PalColors.Accent) : "none",
            TopStyle = One(PalCatalog.TopStyles).Id,
            TopColor = outfit,
            TopAccent = One(PalColors.Accent),
            TopPattern = rng.NextDouble() < 0.45 ? One(PalCatalog.Patterns).Id : "plain",
            BottomStyle = One(PalCatalog.BottomStyles).Id,
            BottomColor = One(PalColors.Bottoms),
            ShoeStyle = One(PalCatalog.ShoeStyles).Id,
            ShoeColor = One(PalColors.Outfit),
            Hat = rng.NextDouble() < 0.35 ? One(PalCatalog.Hats.Skip(1).ToList()).Id : "none",
            HatColor = One(PalColors.Outfit),
            Glasses = rng.NextDouble() < 0.3 ? One(PalCatalog.GlassesStyles.Skip(1).ToList()).Id : "none",
            GlassesColor = One(PalColors.Frames),
            Extra = rng.NextDouble() < 0.3 ? One(PalCatalog.Extras.Skip(1).ToList()).Id : "none",
            ExtraColor = One(PalColors.Accent),
            Earrings = rng.NextDouble() < 0.2 ? One(PalCatalog.EarringStyles.Skip(1).ToList()).Id : "none",
            Personality = One(PalCatalog.Personalities).Id,
        };
        return profile.Normalize();
    }
}

public static class PalCatalog
{
    public static readonly IReadOnlyList<PalOption> FaceShapes = new PalOption[]
    {
        new("round", "Round"), new("oval", "Oval"), new("soft-square", "Soft square"), new("heart", "Heart"), new("wide", "Wide"),
    };

    public static readonly IReadOnlyList<PalOption> EyeStyles = new PalOption[]
    {
        new("round", "Round"), new("sparkle", "Sparkly"), new("almond", "Almond"), new("sleepy", "Sleepy"),
        new("happy", "Happy"), new("dot", "Dot"), new("lashes", "Lashes"), new("focused", "Focused"),
    };

    public static readonly IReadOnlyList<PalOption> BrowStyles = new PalOption[]
    {
        new("soft", "Soft"), new("straight", "Straight"), new("arched", "Arched"), new("thick", "Thick"),
        new("thin", "Thin"), new("determined", "Determined"), new("worried", "Worried"),
    };

    public static readonly IReadOnlyList<PalOption> NoseStyles = new PalOption[]
    {
        new("button", "Button"), new("round", "Round"), new("pointed", "Pointed"), new("small", "Tiny"), new("none", "None"),
    };

    public static readonly IReadOnlyList<PalOption> MouthStyles = new PalOption[]
    {
        new("smile", "Smile"), new("grin", "Big grin"), new("small", "Small"), new("smirk", "Smirk"),
        new("cat", "Cat"), new("open", "Cheery"), new("calm", "Calm"),
    };

    public static readonly IReadOnlyList<PalOption> CheekStyles = new PalOption[]
    {
        new("none", "None"), new("blush", "Blush"), new("freckles", "Freckles"), new("both", "Blush + freckles"), new("mole", "Beauty mark"),
    };

    public static readonly IReadOnlyList<PalOption> FacialHairStyles = new PalOption[]
    {
        new("none", "None"), new("stubble", "Stubble"), new("mustache", "Mustache"), new("goatee", "Goatee"), new("beard", "Beard"),
    };

    public static readonly IReadOnlyList<PalOption> HairStyles = new PalOption[]
    {
        new("short", "Short"), new("swept", "Swept"), new("spiky", "Spiky"), new("curly", "Curly"), new("buzz", "Buzz cut"),
        new("side-part", "Side part"), new("bob", "Bob"), new("long", "Long"), new("ponytail", "Ponytail"),
        new("twin-tails", "Twin tails"), new("bun", "Bun"), new("afro", "Afro"), new("mohawk", "Mohawk"), new("bald", "Bald"),
    };

    public static readonly IReadOnlyList<PalOption> TopStyles = new PalOption[]
    {
        new("hoodie", "Hoodie"), new("tee", "T-shirt"), new("sweater", "Sweater"), new("jacket", "Jacket"),
        new("collared", "Collared shirt"), new("tank", "Tank top"), new("dress", "Dress"),
    };

    public static readonly IReadOnlyList<PalOption> Patterns = new PalOption[]
    {
        new("plain", "Plain"), new("stripes", "Stripes"), new("dots", "Dots"), new("star", "Star"), new("heart", "Heart"),
        new("bolt", "Lightning"), new("controller", "Controller"),
    };

    public static readonly IReadOnlyList<PalOption> BottomStyles = new PalOption[]
    {
        new("jeans", "Jeans"), new("joggers", "Joggers"), new("shorts", "Shorts"), new("skirt", "Skirt"),
    };

    public static readonly IReadOnlyList<PalOption> ShoeStyles = new PalOption[]
    {
        new("sneakers", "Sneakers"), new("boots", "Boots"), new("slip-ons", "Slip-ons"), new("high-tops", "High-tops"),
    };

    public static readonly IReadOnlyList<PalOption> Hats = new PalOption[]
    {
        new("none", "None"), new("cap", "Cap"), new("beanie", "Beanie"), new("bucket", "Bucket hat"), new("headphones", "Headphones"),
        new("crown", "Crown"), new("bow", "Bow"), new("headband", "Headband"), new("wizard", "Wizard hat"),
    };

    public static readonly IReadOnlyList<PalOption> GlassesStyles = new PalOption[]
    {
        new("none", "None"), new("round", "Round"), new("square", "Square"), new("shades", "Shades"), new("star", "Star shades"),
    };

    public static readonly IReadOnlyList<PalOption> Extras = new PalOption[]
    {
        new("none", "None"), new("scarf", "Scarf"), new("backpack", "Backpack"), new("bowtie", "Bow tie"), new("necklace", "Necklace"),
    };

    public static readonly IReadOnlyList<PalOption> EarringStyles = new PalOption[]
    {
        new("none", "None"), new("studs", "Studs"), new("hoops", "Hoops"),
    };

    public static readonly IReadOnlyList<PalOption> Personalities = new PalOption[]
    {
        new("cheerful", "Cheerful"), new("chill", "Chill"), new("sporty", "Sporty"), new("curious", "Curious"), new("cheeky", "Cheeky"),
    };

    public static readonly IReadOnlyList<string> SuggestedNames = new[]
    {
        "Pip", "Juno", "Milo", "Nova", "Remy", "Kit", "Toby", "Lumi", "Sage", "Otto", "Wren", "Bea", "Kai", "Ivy", "Rio", "Momo",
    };

    public static string Pick(IReadOnlyList<PalOption> options, string? id) =>
        options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase))?.Id ?? options[0].Id;

    public static string Label(IReadOnlyList<PalOption> options, string id) => options.FirstOrDefault(o => o.Id == id)?.Label ?? id;

    public static string PersonalityBlurb(string id) => id switch
    {
        "chill" => "Laid back. Takes it slow, yawns a lot, never in a hurry.",
        "sporty" => "Full of energy. Stretches, jogs on the spot, loves a challenge.",
        "curious" => "Nosy in the nicest way. Wants to know what you're up to.",
        "cheeky" => "A bit of a joker. Dances when nobody's looking.",
        _ => "Sunny and upbeat. Happy to see you every time.",
    };
}

/// <summary>Curated palettes for Pal Studio. Every swatch is chosen to look good next to the others.</summary>
public static class PalColors
{
    public static readonly IReadOnlyList<string> Skin = new[]
    {
        "#FBE3D0", "#F6D2B6", "#F2C9A5", "#E8B48C", "#D9A074", "#C68A5E", "#A86F48", "#8C5836", "#6E4329", "#52301E",
    };

    public static readonly IReadOnlyList<string> Hair = new[]
    {
        "#1F1B1C", "#3B2A22", "#4B3224", "#6E4A2F", "#8E5B35", "#B7793F", "#D9A75B", "#EFD28C", "#C9C3BD", "#F4F1EA",
        "#B8452F", "#E07A3A", "#E86F9A", "#9B6BD6", "#4F8FE0", "#3FB6A8",
    };

    public static readonly IReadOnlyList<string> Eyes = new[]
    {
        "#2A211D", "#4A3426", "#6B4A2B", "#3F6D3A", "#3C6FA8", "#6C8FA6", "#7B5AA6", "#A65B3C",
    };

    public static readonly IReadOnlyList<string> Outfit = new[]
    {
        "#F25C54", "#F7A541", "#FFD35A", "#8BD66A", "#3FB6A8", "#35B4E5", "#4F7FE0", "#8D6BE8", "#E86F9A",
        "#FFFFFF", "#D9DEE3", "#6C7680", "#2E3440", "#7A4B35", "#F2E3C6",
    };

    public static readonly IReadOnlyList<string> Bottoms = new[]
    {
        "#3E5C8A", "#2A3B57", "#6E8FB8", "#2E3440", "#6C7680", "#C9B99A", "#7A4B35", "#3F6D3A", "#8C3B3B", "#E8E3D8",
    };

    public static readonly IReadOnlyList<string> Accent = new[]
    {
        "#FFFFFF", "#FFD35A", "#F25C54", "#35B4E5", "#8BD66A", "#E86F9A", "#8D6BE8", "#2E3440",
    };

    public static readonly IReadOnlyList<string> Frames = new[]
    {
        "#2E2A33", "#7A4B35", "#C0A062", "#D9DEE3", "#F25C54", "#35B4E5", "#E86F9A",
    };

    /// <summary>Returns a normalized #RRGGBB string, or <paramref name="fallback"/> when the text isn't a color.</summary>
    public static string Clean(string? value, string fallback)
    {
        var text = value?.Trim() ?? "";
        if (text.StartsWith('#')) text = text[1..];
        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return "#" + text.ToUpperInvariant();
        return fallback;
    }

    public static (byte R, byte G, byte B) Rgb(string hex)
    {
        var clean = Clean(hex, "#808080");
        var value = int.Parse(clean[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
