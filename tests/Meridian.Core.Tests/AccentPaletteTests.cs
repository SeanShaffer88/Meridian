using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class AccentPaletteTests
{
    [Fact]
    public void Presets_AreTheDocumentedRow_WithRedDefaultFirst()
    {
        Assert.Equal(8, AccentPalette.Presets.Count);
        Assert.Equal(AccentPalette.Presets.Count, AccentPalette.Presets.Select(preset => preset.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var first = AccentPalette.Presets[0];
        Assert.Equal(AccentPalette.DefaultPresetId, first.Id);
        Assert.Equal("Red", first.Name);
        Assert.Equal(new AccentColor(0xF0, 0x44, 0x44), first.Color);
        Assert.Equal(first.Color, AccentPalette.DefaultColor);
    }

    [Fact]
    public void TryParse_AcceptsHashAndBareHex_InAnyCase()
    {
        Assert.True(AccentColor.TryParse("#F04444", out var withHash));
        Assert.Equal(new AccentColor(0xF0, 0x44, 0x44), withHash);
        Assert.True(AccentColor.TryParse(" 3d9be9 ", out var bare));
        Assert.Equal(new AccentColor(0x3D, 0x9B, 0xE9), bare);
        Assert.Equal("#3D9BE9", bare.ToHex());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#FF04444")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void TryParse_RejectsAnythingElse(string? hex)
    {
        Assert.False(AccentColor.TryParse(hex, out _));
    }

    [Fact]
    public void ContrastRatio_BlackOnWhite_IsTwentyOne()
    {
        var black = new AccentColor(0, 0, 0);
        var white = new AccentColor(0xFF, 0xFF, 0xFF);
        Assert.Equal(21.0, black.ContrastRatio(white), precision: 2);
        Assert.Equal(1.0, white.ContrastRatio(white), precision: 10);
    }

    [Fact]
    public void ParseMode_PrefersTheStoredModeName()
    {
        Assert.Equal(AccentMode.Custom, AccentPalette.ParseMode("Custom", legacyFollowWindowsAccent: true));
        Assert.Equal(AccentMode.Preset, AccentPalette.ParseMode("Preset", legacyFollowWindowsAccent: true));
        Assert.Equal(AccentMode.FollowWindows, AccentPalette.ParseMode("FollowWindows", legacyFollowWindowsAccent: false));
    }

    [Fact]
    public void ParseMode_FallsBackToTheLegacyFollowWindowsFlag()
    {
        Assert.Equal(AccentMode.FollowWindows, AccentPalette.ParseMode(null, legacyFollowWindowsAccent: true));
        Assert.Equal(AccentMode.Preset, AccentPalette.ParseMode(null, legacyFollowWindowsAccent: false));
        Assert.Equal(AccentMode.FollowWindows, AccentPalette.ParseMode("Garbage", legacyFollowWindowsAccent: true));
        Assert.Equal(AccentMode.Preset, AccentPalette.ParseMode("Garbage", legacyFollowWindowsAccent: false));
    }

    [Fact]
    public void Resolve_Preset_ReturnsThePresetColourVerbatimInDarkTheme()
    {
        var resolved = AccentPalette.Resolve(AccentMode.Preset, "Blue", null, lightTheme: false, windowsAccent: null);
        Assert.Equal(new AccentColor(0x3D, 0x9B, 0xE9), resolved);
    }

    [Fact]
    public void Resolve_Preset_UsesTheDerivedVariantInLightTheme()
    {
        var resolved = AccentPalette.Resolve(AccentMode.Preset, "Blue", null, lightTheme: true, windowsAccent: null);
        Assert.Equal(AccentPalette.DeriveLightVariant(new AccentColor(0x3D, 0x9B, 0xE9)), resolved);
        Assert.True(resolved.ContrastRatio(AccentPalette.LightPanelReference) >= 4.75);
    }

    [Fact]
    public void Resolve_UnknownPreset_FallsBackToBrandRed()
    {
        Assert.Equal(AccentPalette.DefaultColor,
            AccentPalette.Resolve(AccentMode.Preset, "NoSuchPreset", null, lightTheme: false, windowsAccent: null));
        Assert.Equal(AccentPalette.DefaultColor,
            AccentPalette.Resolve(AccentMode.Preset, null, null, lightTheme: false, windowsAccent: null));
    }

    [Fact]
    public void Resolve_Custom_BeatsPreset_AndInvalidCustomFallsBackToBrandRed()
    {
        var resolved = AccentPalette.Resolve(AccentMode.Custom, "Blue", "#112233", lightTheme: false, windowsAccent: null);
        Assert.Equal(new AccentColor(0x11, 0x22, 0x33), resolved);
        Assert.Equal(AccentPalette.DefaultColor,
            AccentPalette.Resolve(AccentMode.Custom, "Blue", "not-a-color", lightTheme: false, windowsAccent: null));
    }

    [Fact]
    public void Resolve_FollowWindows_WinsOverPresetAndCustom_AndIsUsedAsIsInBothThemes()
    {
        var windowsAccent = new AccentColor(0x00, 0x67, 0xC0);
        Assert.Equal(windowsAccent,
            AccentPalette.Resolve(AccentMode.FollowWindows, "Blue", "#112233", lightTheme: false, windowsAccent));
        Assert.Equal(windowsAccent,
            AccentPalette.Resolve(AccentMode.FollowWindows, "Blue", "#112233", lightTheme: true, windowsAccent));
    }

    [Fact]
    public void Resolve_FollowWindows_WithoutALiveAccent_BehavesLikeTheDefaultPreset()
    {
        Assert.Equal(AccentPalette.DefaultColor,
            AccentPalette.Resolve(AccentMode.FollowWindows, null, null, lightTheme: false, windowsAccent: null));
        Assert.Equal(AccentPalette.DeriveLightVariant(AccentPalette.DefaultColor),
            AccentPalette.Resolve(AccentMode.FollowWindows, null, null, lightTheme: true, windowsAccent: null));
    }

    [Fact]
    public void DeriveLightVariant_BrandRed_MatchesTheLightThemeDictionaryValue()
    {
        // App.xaml's Light dictionary carries this same derived value; the two
        // must not drift apart.
        Assert.Equal(new AccentColor(0xBF, 0x0F, 0x0F), AccentPalette.DeriveLightVariant(AccentPalette.DefaultColor));
    }

    [Fact]
    public void DeriveLightVariant_EveryPreset_ReachesAaContrastOnTheLightPanel()
    {
        foreach (var preset in AccentPalette.Presets)
        {
            var variant = AccentPalette.DeriveLightVariant(preset.Color);
            Assert.True(variant.ContrastRatio(AccentPalette.LightPanelReference) >= 4.75,
                $"{preset.Name} light variant {variant.ToHex()} is below 4.75:1 on the light panel.");
            Assert.True(new AccentColor(0xFF, 0xFF, 0xFF).ContrastRatio(variant) >= 4.5,
                $"{preset.Name} light variant {variant.ToHex()} cannot carry white foreground content.");
        }
    }

    [Fact]
    public void DeriveLightVariant_AlreadyDarkColour_IsReturnedUnchanged()
    {
        var dark = new AccentColor(0x11, 0x22, 0x33);
        Assert.Equal(dark, AccentPalette.DeriveLightVariant(dark));
    }
}
