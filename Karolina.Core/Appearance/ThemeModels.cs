namespace Karolina.Core.Appearance;

public sealed record ThemeFont(string Family, string Source, string Weight = "100 900", string Style = "normal");
public sealed record ThemeEffect(string? Fragment = null, double Intensity = 0.45);

/// <summary>Only appearance data and resources; a theme never supplies executable JavaScript.</summary>
public sealed record ThemeManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Dictionary<string, string> Tokens { get; init; } = [];
    public Dictionary<string, Dictionary<string, string>> ColorModes { get; init; } = [];
    public Dictionary<string, string> Assets { get; init; } = [];
    public Dictionary<string, string> Icons { get; init; } = [];
    public Dictionary<string, string> Components { get; init; } = [];
    public string[] Styles { get; init; } = [];
    public ThemeFont[] Fonts { get; init; } = [];
    public ThemeEffect Effect { get; init; } = new();
}

public sealed record AppearancePreferences
{
    public string ThemeId { get; init; } = "karolina";
    public string Mode { get; init; } = "dark";
    public string Motion { get; init; } = "system";
    public string Effects { get; init; } = "auto";
    public bool MascotVisible { get; init; } = true;
    public double BackgroundOpacity { get; init; } = 0.8;
    public Dictionary<string, string> Tokens { get; init; } = [];
}

public sealed record ThemeDescriptor(ThemeManifest Manifest, bool BuiltIn, string BaseUrl);
