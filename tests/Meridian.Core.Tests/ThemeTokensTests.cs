using System.Text.RegularExpressions;
using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// Guards the darker red theme: the exact token values (near-black dark
/// surfaces, red-gray selection in both themes, light surfaces unchanged),
/// WCAG AA contrast for the text tiers on every surface they can sit on —
/// including the selection backgrounds — the rule that selection never
/// equals an accent preset colour, and the App.xaml theme dictionaries
/// carrying exactly the Core ThemeTokens values (the same drift guard
/// AccentPalette has for the dictionary accent values).
/// </summary>
public sealed class ThemeTokensTests
{
    [Fact]
    public void DarkTokens_AreTheConfirmedDarkerValues()
    {
        Assert.Equal(new AccentColor(0x10, 0x12, 0x15), ThemeTokens.DarkSurface);
        Assert.Equal(new AccentColor(0x17, 0x19, 0x1D), ThemeTokens.DarkPanel);
        Assert.Equal(new AccentColor(0x1E, 0x21, 0x26), ThemeTokens.DarkChrome);
        Assert.Equal(new AccentColor(0x2C, 0x30, 0x38), ThemeTokens.DarkBorder);
        Assert.Equal(new AccentColor(0xFF, 0xFF, 0xFF), ThemeTokens.DarkTextPrimary);
        Assert.Equal(new AccentColor(0xC9, 0xCD, 0xD4), ThemeTokens.DarkTextSecondary);
        Assert.Equal(new AccentColor(0xAE, 0xB4, 0xBE), ThemeTokens.DarkTextTertiary);
    }

    [Fact]
    public void DarkSelection_IsTheConfirmedRedGray()
    {
        Assert.Equal(new AccentColor(0x4B, 0x36, 0x3B), ThemeTokens.DarkSelectionBackground);
        Assert.Equal(new AccentColor(0x5C, 0x43, 0x4A), ThemeTokens.DarkSelectionPointerOver);
        Assert.Equal(new AccentColor(0x3A, 0x2C, 0x30), ThemeTokens.DarkSelectionInactive);
        Assert.Equal(ThemeTokens.DarkTextPrimary, ThemeTokens.DarkSelectionForeground);
    }

    [Fact]
    public void LightSurfaces_AreUnchangedFromThePreviousPalette()
    {
        Assert.Equal(new AccentColor(0xD5, 0xD2, 0xCE), ThemeTokens.LightSurface);
        Assert.Equal(new AccentColor(0xC7, 0xC4, 0xBF), ThemeTokens.LightPanel);
        Assert.Equal(new AccentColor(0xCD, 0xCA, 0xC5), ThemeTokens.LightChrome);
        Assert.Equal(new AccentColor(0xB8, 0xB3, 0xAB), ThemeTokens.LightBorder);
        Assert.Equal(new AccentColor(0x00, 0x00, 0x00), ThemeTokens.LightTextPrimary);
        Assert.Equal(new AccentColor(0x36, 0x32, 0x2B), ThemeTokens.LightTextSecondary);
        Assert.Equal(new AccentColor(0x51, 0x4B, 0x41), ThemeTokens.LightTextTertiary);
    }

    [Fact]
    public void LightSelection_IsThePaleRedGray()
    {
        Assert.Equal(new AccentColor(0xEA, 0xD9, 0xDC), ThemeTokens.LightSelectionBackground);
        Assert.Equal(new AccentColor(0xDF, 0xC6, 0xCB), ThemeTokens.LightSelectionPointerOver);
        Assert.Equal(new AccentColor(0xF2, 0xE4, 0xE6), ThemeTokens.LightSelectionInactive);
        Assert.Equal(ThemeTokens.LightTextPrimary, ThemeTokens.LightSelectionForeground);
    }

    [Fact]
    public void DarkTextTiers_MeetAaContrast_OnEveryDarkSurface()
    {
        var surfaces = new[] { ThemeTokens.DarkSurface, ThemeTokens.DarkPanel, ThemeTokens.DarkChrome };
        foreach (var surface in surfaces)
        {
            AssertAa(ThemeTokens.DarkTextPrimary, surface);
            AssertAa(ThemeTokens.DarkTextSecondary, surface);
            AssertAa(ThemeTokens.DarkTextTertiary, surface);
        }
    }

    [Fact]
    public void DarkTextTiers_MeetAaContrast_OnTheSelectionColours()
    {
        var selections = new[]
        {
            ThemeTokens.DarkSelectionBackground,
            ThemeTokens.DarkSelectionPointerOver,
            ThemeTokens.DarkSelectionInactive
        };
        foreach (var selection in selections)
        {
            AssertAa(ThemeTokens.DarkTextPrimary, selection);
            AssertAa(ThemeTokens.DarkTextSecondary, selection);
            AssertAa(ThemeTokens.DarkSelectionForeground, selection);
        }
    }

    [Fact]
    public void LightTextTiers_MeetAaContrast_OnEveryLightSurface()
    {
        var surfaces = new[] { ThemeTokens.LightSurface, ThemeTokens.LightPanel, ThemeTokens.LightChrome };
        foreach (var surface in surfaces)
        {
            AssertAa(ThemeTokens.LightTextPrimary, surface);
            AssertAa(ThemeTokens.LightTextSecondary, surface);
        }
    }

    [Fact]
    public void LightTextTiers_MeetAaContrast_OnTheSelectionColours()
    {
        var selections = new[]
        {
            ThemeTokens.LightSelectionBackground,
            ThemeTokens.LightSelectionPointerOver,
            ThemeTokens.LightSelectionInactive
        };
        foreach (var selection in selections)
        {
            AssertAa(ThemeTokens.LightTextPrimary, selection);
            AssertAa(ThemeTokens.LightTextSecondary, selection);
            AssertAa(ThemeTokens.LightSelectionForeground, selection);
        }
    }

    [Fact]
    public void SelectionColours_DifferFromEveryAccentPreset()
    {
        var selections = new[]
        {
            ThemeTokens.DarkSelectionBackground, ThemeTokens.DarkSelectionPointerOver, ThemeTokens.DarkSelectionInactive,
            ThemeTokens.LightSelectionBackground, ThemeTokens.LightSelectionPointerOver, ThemeTokens.LightSelectionInactive
        };
        foreach (var preset in AccentPalette.Presets)
        {
            foreach (var selection in selections)
                Assert.NotEqual(preset.Color, selection);
        }
    }

    [Fact]
    public void AppXaml_ThemeDictionaries_CarryExactlyTheTokenValues()
    {
        var dark = ReadThemeDictionaryBrushes("Dark");
        var light = ReadThemeDictionaryBrushes("Light");

        AssertBrush(dark, "MeridianSurfaceBrush", ThemeTokens.DarkSurface);
        AssertBrush(dark, "MeridianPanelBrush", ThemeTokens.DarkPanel);
        AssertBrush(dark, "MeridianChromeBrush", ThemeTokens.DarkChrome);
        AssertBrush(dark, "MeridianBorderBrush", ThemeTokens.DarkBorder);
        AssertBrush(dark, "MeridianTextPrimaryBrush", ThemeTokens.DarkTextPrimary);
        AssertBrush(dark, "MeridianTextSecondaryBrush", ThemeTokens.DarkTextSecondary);
        AssertBrush(dark, "MeridianTextTertiaryBrush", ThemeTokens.DarkTextTertiary);
        AssertBrush(dark, "MeridianSelectionBackgroundBrush", ThemeTokens.DarkSelectionBackground);
        AssertBrush(dark, "MeridianSelectionPointerOverBrush", ThemeTokens.DarkSelectionPointerOver);
        AssertBrush(dark, "MeridianSelectionInactiveBrush", ThemeTokens.DarkSelectionInactive);
        AssertBrush(dark, "MeridianSelectionForegroundBrush", ThemeTokens.DarkSelectionForeground);
        AssertBrush(dark, "MeridianAccentBrush", AccentPalette.DefaultColor);
        AssertBrush(dark, "MeridianActivePaneBorderBrush", AccentPalette.DefaultColor);

        AssertBrush(light, "MeridianSurfaceBrush", ThemeTokens.LightSurface);
        AssertBrush(light, "MeridianPanelBrush", ThemeTokens.LightPanel);
        AssertBrush(light, "MeridianChromeBrush", ThemeTokens.LightChrome);
        AssertBrush(light, "MeridianBorderBrush", ThemeTokens.LightBorder);
        AssertBrush(light, "MeridianTextPrimaryBrush", ThemeTokens.LightTextPrimary);
        AssertBrush(light, "MeridianTextSecondaryBrush", ThemeTokens.LightTextSecondary);
        AssertBrush(light, "MeridianTextTertiaryBrush", ThemeTokens.LightTextTertiary);
        AssertBrush(light, "MeridianSelectionBackgroundBrush", ThemeTokens.LightSelectionBackground);
        AssertBrush(light, "MeridianSelectionPointerOverBrush", ThemeTokens.LightSelectionPointerOver);
        AssertBrush(light, "MeridianSelectionInactiveBrush", ThemeTokens.LightSelectionInactive);
        AssertBrush(light, "MeridianSelectionForegroundBrush", ThemeTokens.LightSelectionForeground);
        var lightAccent = AccentPalette.DeriveLightVariant(AccentPalette.DefaultColor);
        AssertBrush(light, "MeridianAccentBrush", lightAccent);
        AssertBrush(light, "MeridianActivePaneBorderBrush", lightAccent);
    }

    [Fact]
    public void AppXaml_ListSelectionResources_PointAtTheSelectionTokens()
    {
        // The built-in ListView/GridView/TreeView item templates read these
        // system theme resources; in each dictionary they must carry exactly
        // the selection token colours (pressed uses the pointer-over step,
        // disabled the dimmer inactive step).
        AssertItemSelectionKeys(ReadThemeDictionaryBrushes("Dark"),
            ThemeTokens.DarkSelectionBackground, ThemeTokens.DarkSelectionPointerOver, ThemeTokens.DarkSelectionInactive);
        AssertItemSelectionKeys(ReadThemeDictionaryBrushes("Light"),
            ThemeTokens.LightSelectionBackground, ThemeTokens.LightSelectionPointerOver, ThemeTokens.LightSelectionInactive);
    }

    private static void AssertItemSelectionKeys(
        IReadOnlyDictionary<string, AccentColor> brushes, AccentColor selected, AccentColor pointerOver, AccentColor inactive)
    {
        foreach (var control in new[] { "ListViewItem", "GridViewItem", "TreeViewItem" })
        {
            AssertBrush(brushes, control + "BackgroundSelected", selected);
            AssertBrush(brushes, control + "BackgroundSelectedPointerOver", pointerOver);
            AssertBrush(brushes, control + "BackgroundSelectedPressed", pointerOver);
            AssertBrush(brushes, control + "BackgroundSelectedDisabled", inactive);
        }
    }

    private static void AssertAa(AccentColor foreground, AccentColor background) =>
        Assert.True(foreground.ContrastRatio(background) >= 4.5,
            $"{foreground.ToHex()} on {background.ToHex()} is below WCAG AA (4.5:1).");

    private static void AssertBrush(IReadOnlyDictionary<string, AccentColor> brushes, string key, AccentColor expected)
    {
        Assert.True(brushes.TryGetValue(key, out var actual), $"App.xaml is missing the {key} brush.");
        Assert.Equal(expected, actual);
    }

    private static readonly Regex BrushEntry = new(
        "<SolidColorBrush x:Key=\"(?<key>[^\"]+)\" Color=\"#(?<hex>[0-9A-Fa-f]{6})\" />",
        RegexOptions.Compiled);

    private static IReadOnlyDictionary<string, AccentColor> ReadThemeDictionaryBrushes(string themeKey)
    {
        var appXamlPath = Path.Combine(FindAppProjectDirectory(), "App.xaml");
        var xaml = File.ReadAllText(appXamlPath);
        var start = xaml.IndexOf($"<ResourceDictionary x:Key=\"{themeKey}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"App.xaml has no {themeKey} theme dictionary.");
        var end = xaml.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"App.xaml's {themeKey} theme dictionary is not closed.");
        var section = xaml[start..end];
        var brushes = new Dictionary<string, AccentColor>(StringComparer.Ordinal);
        foreach (Match match in BrushEntry.Matches(section))
        {
            Assert.True(AccentColor.TryParse("#" + match.Groups["hex"].Value, out var color));
            brushes[match.Groups["key"].Value] = color;
        }

        Assert.NotEmpty(brushes);
        return brushes;
    }

    private static string FindAppProjectDirectory()
    {
        // Start from the test assembly's own folder (bin/<config>/net10.0) and
        // walk up until the directory that contains src/Meridian.App is found.
        // (AppContext.BaseDirectory is the test runner's folder, not this one.)
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(ThemeTokensTests).Assembly.Location)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Meridian.App");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/Meridian.App above the test assembly directory; the theme-token guard cannot run.");
    }
}
