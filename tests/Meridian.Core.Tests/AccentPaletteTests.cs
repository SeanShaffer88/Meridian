using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class AccentPaletteTests
{
    [Fact]
    public void Presets_AreTheDocumentedRow_WithRedDefaultFirst()
    {
        Assert.Equal(9, AccentPalette.Presets.Count);
        Assert.Equal(AccentPalette.Presets.Count, AccentPalette.Presets.Select(preset => preset.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var first = AccentPalette.Presets[0];
        Assert.Equal(AccentPalette.DefaultPresetId, first.Id);
        Assert.Equal("Red", first.Name);
        Assert.Equal(new AccentColor(0xF0, 0x44, 0x44), first.Color);
        Assert.Equal(first.Color, AccentPalette.DefaultColor);
        // The previous default stays available as an ordinary preset, so a
        // saved "Sky" choice keeps meaning exactly what it meant before.
        var sky = AccentPalette.FindPreset("Sky");
        Assert.NotNull(sky);
        Assert.Equal(new AccentColor(0x4C, 0xC2, 0xFF), sky.Color);
        Assert.Equal(sky.Color, AccentPalette.PreviousDefaultColor);
        // The older blue preset also keeps its colour.
        var blue = AccentPalette.FindPreset("Blue");
        Assert.NotNull(blue);
        Assert.Equal(new AccentColor(0x3D, 0x9B, 0xE9), blue.Color);
    }

    [Fact]
    public void Resolve_NoSavedChoice_UsesTheRedDefault()
    {
        // A user who never picked an accent (no preset id stored) gets the
        // shipped red default in the dark theme and its derived variant
        // in the light theme.
        Assert.Equal(new AccentColor(0xF0, 0x44, 0x44),
            AccentPalette.Resolve(AccentMode.Preset, null, null, lightTheme: false, windowsAccent: null));
        Assert.Equal(AccentPalette.DeriveLightVariant(new AccentColor(0xF0, 0x44, 0x44)),
            AccentPalette.Resolve(AccentMode.Preset, null, null, lightTheme: true, windowsAccent: null));
    }

    [Fact]
    public void Resolve_SavedChoices_AreUnchangedByTheNewDefault()
    {
        // Saved preset choices resolve exactly as before the default changed.
        Assert.Equal(new AccentColor(0xF0, 0x44, 0x44),
            AccentPalette.Resolve(AccentMode.Preset, "Red", null, lightTheme: false, windowsAccent: null));
        Assert.Equal(new AccentColor(0x3D, 0x9B, 0xE9),
            AccentPalette.Resolve(AccentMode.Preset, "Blue", null, lightTheme: false, windowsAccent: null));
        // A saved custom colour still beats every preset.
        Assert.Equal(new AccentColor(0x11, 0x22, 0x33),
            AccentPalette.Resolve(AccentMode.Custom, "Sky", "#112233", lightTheme: false, windowsAccent: null));
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
    public void Resolve_UnknownPreset_FallsBackToTheDefault()
    {
        Assert.Equal(AccentPalette.DefaultColor,
            AccentPalette.Resolve(AccentMode.Preset, "NoSuchPreset", null, lightTheme: false, windowsAccent: null));
        Assert.Equal(AccentPalette.DefaultColor,
            AccentPalette.Resolve(AccentMode.Preset, null, null, lightTheme: false, windowsAccent: null));
    }

    [Fact]
    public void Resolve_Custom_BeatsPreset_AndInvalidCustomFallsBackToTheDefault()
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
    public void DeriveLightVariant_DefaultRed_MatchesTheLightThemeDictionaryValue()
    {
        // App.xaml's Light dictionary carries this same derived value; the two
        // must not drift apart.
        Assert.Equal(new AccentColor(0x99, 0x0C, 0x0C), AccentPalette.DeriveLightVariant(AccentPalette.DefaultColor));
    }

    [Fact]
    public void Migrate_PresetSky_BecomesTheRedPreset()
    {
        var (mode, presetId, customHex) = AccentPalette.MigrateLegacyDefaultAccent("Preset", "Sky", null);
        Assert.Equal("Preset", mode);
        Assert.Equal("Red", presetId);
        Assert.Null(customHex);
    }

    [Fact]
    public void Migrate_NullModeWithSkyPreset_BecomesTheRedPreset()
    {
        // Files written before the mode existed carry only a preset id; a
        // stored "Sky" there is what the program itself saved while Sky was
        // the default, not a deliberate choice. The rest of the record is
        // carried through untouched.
        var (mode, presetId, customHex) = AccentPalette.MigrateLegacyDefaultAccent(null, "Sky", "#112233");
        Assert.Null(mode);
        Assert.Equal("Red", presetId);
        Assert.Equal("#112233", customHex);
    }

    [Theory]
    [InlineData("#4CC2FF")]
    [InlineData("#4cc2ff")]
    [InlineData("4CC2FF")]
    public void Migrate_CustomSkyHex_BecomesTheRedPreset(string storedHex)
    {
        // A stored custom colour equal to the old sky-blue default becomes
        // the Red preset and the hex is dropped, whatever letter case or "#"
        // form was saved, so the migrated state matches a fresh install.
        var (mode, presetId, customHex) = AccentPalette.MigrateLegacyDefaultAccent("Custom", "Blue", storedHex);
        Assert.Equal("Preset", mode);
        Assert.Equal("Red", presetId);
        Assert.Null(customHex);
    }

    [Fact]
    public void Migrate_AnyOtherStoredChoice_IsReturnedUnchanged()
    {
        // Another preset is a deliberate choice and is kept exactly.
        Assert.Equal(("Preset", "Blue", (string?)null),
            AccentPalette.MigrateLegacyDefaultAccent("Preset", "Blue", null));
        // Another custom colour is kept, and so is one a single step away
        // from the old default: only an exact match reads as "never changed".
        Assert.Equal(("Custom", (string?)null, "#112233"),
            AccentPalette.MigrateLegacyDefaultAccent("Custom", null, "#112233"));
        Assert.Equal(("Custom", (string?)null, "#4CC2FE"),
            AccentPalette.MigrateLegacyDefaultAccent("Custom", null, "#4CC2FE"));
        // Follow Windows is never rewritten, whatever else is stored.
        Assert.Equal(("FollowWindows", "Sky", "#4CC2FF"),
            AccentPalette.MigrateLegacyDefaultAccent("FollowWindows", "Sky", "#4CC2FF"));
        // Malformed or missing values pass through untouched.
        Assert.Equal(("Garbage", "Sky", (string?)null),
            AccentPalette.MigrateLegacyDefaultAccent("Garbage", "Sky", null));
        Assert.Equal(("Custom", (string?)null, "not-a-color"),
            AccentPalette.MigrateLegacyDefaultAccent("Custom", null, "not-a-color"));
        Assert.Equal(((string?)null, (string?)null, (string?)null),
            AccentPalette.MigrateLegacyDefaultAccent(null, null, null));
    }

    [Fact]
    public void Resolve_PresetSky_ReturnsSkyInBothThemes()
    {
        // Resolve is pure: after the one-time load migration, a stored "Sky"
        // preset id is a deliberate choice and means Sky in both themes.
        Assert.Equal(new AccentColor(0x4C, 0xC2, 0xFF),
            AccentPalette.Resolve(AccentMode.Preset, "Sky", null, lightTheme: false, windowsAccent: null));
        Assert.Equal(AccentPalette.DeriveLightVariant(new AccentColor(0x4C, 0xC2, 0xFF)),
            AccentPalette.Resolve(AccentMode.Preset, "Sky", null, lightTheme: true, windowsAccent: null));
    }

    [Fact]
    public void Resolve_CustomSkyHex_ReturnsThatColourExactly()
    {
        // The same holds for a custom colour equal to the old default: it is
        // a usable colour like any other, never rewritten at resolve time.
        Assert.Equal(new AccentColor(0x4C, 0xC2, 0xFF),
            AccentPalette.Resolve(AccentMode.Custom, null, "#4CC2FF", lightTheme: false, windowsAccent: null));
        Assert.Equal(AccentPalette.DeriveLightVariant(new AccentColor(0x4C, 0xC2, 0xFF)),
            AccentPalette.Resolve(AccentMode.Custom, null, "#4cc2ff", lightTheme: true, windowsAccent: null));
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
