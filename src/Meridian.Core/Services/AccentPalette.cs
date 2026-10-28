using System.Globalization;

namespace Meridian.Services;

/// <summary>How the app's accent colour is chosen.</summary>
public enum AccentMode
{
    /// <summary>One of the built-in preset colours.</summary>
    Preset,

    /// <summary>A colour the user entered as a hex value.</summary>
    Custom,

    /// <summary>The accent colour Windows is currently using.</summary>
    FollowWindows
}

/// <summary>An opaque RGB colour, exchanged as a "#RRGGBB" hex string.</summary>
public readonly record struct AccentColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Accepts "#RRGGBB" or "RRGGBB", in any letter case. Anything else is rejected.</summary>
    public static bool TryParse(string? hex, out AccentColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var text = hex.Trim();
        if (text.StartsWith('#')) text = text[1..];
        if (text.Length != 6) return false;
        if (!byte.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        color = new AccentColor(r, g, b);
        return true;
    }

    /// <summary>WCAG contrast ratio against another colour, from 1 (identical) to 21 (black on white).</summary>
    public double ContrastRatio(AccentColor other)
    {
        var first = RelativeLuminance(R, G, B);
        var second = RelativeLuminance(other.R, other.G, other.B);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    internal static double RelativeLuminance(double r, double g, double b)
    {
        static double Linearize(double channel)
        {
            var scaled = channel / 255.0;
            return scaled <= 0.04045 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }
}

/// <summary>One built-in accent colour choice.</summary>
public sealed record AccentPreset(string Id, string Name, AccentColor Color);

/// <summary>
/// The accent colour system: the preset row, the persisted-choice resolution
/// (preset / custom / follow-Windows precedence), and the rule that derives a
/// colour's light-theme variant. Pure logic only — the app applies the result
/// to its brushes. The light variant keeps the colour's hue and saturation and
/// lowers its lightness in fixed steps until it reaches a 4.75:1 contrast
/// ratio against the light panel surface, so a colour picked for the dark
/// theme stays readable in the light theme without a second hand-tuned value.
/// </summary>
public static class AccentPalette
{
    /// <summary>The light theme's panel surface; light variants are derived against it.</summary>
    public static AccentColor LightPanelReference { get; } = new(0xC7, 0xC4, 0xBF);

    public const string DefaultPresetId = "Red";

    /// <summary>The shipped default accent: the red preset, also the fallback for every invalid choice.</summary>
    public static AccentColor DefaultColor { get; } = new(0xF0, 0x44, 0x44);

    /// <summary>
    /// The previous shipped default (the sky-blue preset, default before the
    /// darker red theme). Kept only so a stored accent equal to it can be
    /// recognised as "never changed from the old default" by the one-time
    /// migration; see <see cref="MigrateLegacyDefaultAccent"/>.
    /// </summary>
    public static AccentColor PreviousDefaultColor { get; } = new(0x4C, 0xC2, 0xFF);

    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("Red", "Red", new AccentColor(0xF0, 0x44, 0x44)),
        new("Sky", "Sky", new AccentColor(0x4C, 0xC2, 0xFF)),
        new("Orange", "Orange", new AccentColor(0xF0, 0x7D, 0x2B)),
        new("Amber", "Amber", new AccentColor(0xF5, 0xB8, 0x3D)),
        new("Green", "Green", new AccentColor(0x3D, 0xA5, 0x6A)),
        new("Teal", "Teal", new AccentColor(0x2B, 0xB3, 0xA3)),
        new("Blue", "Blue", new AccentColor(0x3D, 0x9B, 0xE9)),
        new("Purple", "Purple", new AccentColor(0x9A, 0x6B, 0xE8)),
        new("Pink", "Pink", new AccentColor(0xE5, 0x62, 0xA0))
    ];

    public static AccentPreset? FindPreset(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var preset in Presets)
        {
            if (string.Equals(preset.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)) return preset;
        }

        return null;
    }

    /// <summary>
    /// Reads the persisted mode. Files written before the mode existed carry
    /// only the follow-Windows flag, which stays authoritative for them.
    /// </summary>
    public static AccentMode ParseMode(string? storedMode, bool legacyFollowWindowsAccent) => storedMode switch
    {
        nameof(AccentMode.Custom) => AccentMode.Custom,
        nameof(AccentMode.FollowWindows) => AccentMode.FollowWindows,
        nameof(AccentMode.Preset) => AccentMode.Preset,
        _ => legacyFollowWindowsAccent ? AccentMode.FollowWindows : AccentMode.Preset
    };

    /// <summary>
    /// The effective accent colour for one theme. Precedence: a live Windows
    /// accent wins in FollowWindows mode (used as-is in both themes, matching
    /// how Windows itself presents it); a valid custom colour beats the
    /// preset fallback; an unknown preset or unreadable custom value falls
    /// back to the default red. Resolution is pure: a stored choice means
    /// exactly what it says, in both themes — the one-time switch away from
    /// the previous default happens in the settings load, via
    /// <see cref="MigrateLegacyDefaultAccent"/>, never here. In the light
    /// theme, preset and custom colours are replaced by their derived light
    /// variant.
    /// </summary>
    public static AccentColor Resolve(AccentMode mode, string? presetId, string? customHex, bool lightTheme, AccentColor? windowsAccent)
    {
        AccentColor baseColor;
        var deriveLightVariant = true;
        switch (mode)
        {
            case AccentMode.FollowWindows when windowsAccent is { } live:
                baseColor = live;
                deriveLightVariant = false;
                break;
            case AccentMode.Custom:
                baseColor = AccentColor.TryParse(customHex, out var custom) ? custom : DefaultColor;
                break;
            default:
                baseColor = FindPreset(presetId)?.Color ?? DefaultColor;
                break;
        }

        return lightTheme && deriveLightVariant ? DeriveLightVariant(baseColor) : baseColor;
    }

    /// <summary>
    /// The one-time migration of a stored accent choice away from the
    /// previous shipped default. A stored choice equal to that default — the
    /// "Sky" preset id, or a custom hex equal to the Sky colour — was in
    /// practice never a deliberate choice: it is what the program itself
    /// saved while Sky was the default. Such a value is rewritten to the red
    /// default (the custom form becomes the Red preset, dropping the hex, so
    /// the migrated state is indistinguishable from a fresh install). Every
    /// other value — FollowWindows, any other preset, any other custom
    /// colour, and malformed input — is returned exactly as stored, so a Sky
    /// choice made deliberately after the migration is never rewritten.
    /// Callers run this once, at settings load, and record that they did.
    /// </summary>
    public static (string? Mode, string? PresetId, string? CustomHex) MigrateLegacyDefaultAccent(
        string? mode, string? presetId, string? customHex)
    {
        if (string.Equals(mode, nameof(AccentMode.FollowWindows), StringComparison.Ordinal))
            return (mode, presetId, customHex);

        if (string.Equals(mode, nameof(AccentMode.Custom), StringComparison.Ordinal))
        {
            return AccentColor.TryParse(customHex, out var stored) && stored == PreviousDefaultColor
                ? (nameof(AccentMode.Preset), DefaultPresetId, null)
                : (mode, presetId, customHex);
        }

        // Files written before the mode existed carry only a preset id; a
        // null mode with a preset id is the preset form of the same state.
        var presetForm = string.Equals(mode, nameof(AccentMode.Preset), StringComparison.Ordinal)
            || (mode is null && !string.IsNullOrWhiteSpace(presetId));
        if (presetForm && string.Equals(FindPreset(presetId)?.Id, "Sky", StringComparison.Ordinal))
            return (mode, DefaultPresetId, customHex);

        return (mode, presetId, customHex);
    }

    /// <summary>
    /// Derives the light-theme variant of a colour: same hue and saturation,
    /// lightness lowered in 0.02 steps until the result reaches a 4.75:1
    /// contrast ratio against <see cref="LightPanelReference"/> (or lightness
    /// reaches 0.05). Colours that already pass are returned unchanged.
    /// </summary>
    public static AccentColor DeriveLightVariant(AccentColor color)
    {
        var (hue, lightness, saturation) = ToHls(color.R, color.G, color.B);
        var panelLuminance = AccentColor.RelativeLuminance(
            LightPanelReference.R, LightPanelReference.G, LightPanelReference.B);
        var current = lightness;
        while (current > 0.05)
        {
            var (r, g, b) = FromHls(hue, current, saturation);
            var luminance = AccentColor.RelativeLuminance(r, g, b);
            var lighter = Math.Max(luminance, panelLuminance);
            var darker = Math.Min(luminance, panelLuminance);
            if ((lighter + 0.05) / (darker + 0.05) >= 4.75)
                return new AccentColor(Round(r), Round(g), Round(b));
            current -= 0.02;
        }

        var (fr, fg, fb) = FromHls(hue, current, saturation);
        return new AccentColor(Round(fr), Round(fg), Round(fb));
    }

    private static byte Round(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 255));

    private static (double Hue, double Lightness, double Saturation) ToHls(double r, double g, double b)
    {
        r /= 255.0;
        g /= 255.0;
        b /= 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2.0;
        if (max == min) return (0, lightness, 0);
        var delta = max - min;
        var saturation = lightness > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);
        double hue;
        if (max == r) hue = (g - b) / delta + (g < b ? 6 : 0);
        else if (max == g) hue = (b - r) / delta + 2;
        else hue = (r - g) / delta + 4;
        return (hue / 6.0, lightness, saturation);
    }

    private static (double R, double G, double B) FromHls(double hue, double lightness, double saturation)
    {
        if (saturation == 0)
        {
            var gray = lightness * 255.0;
            return (gray, gray, gray);
        }

        var m2 = lightness <= 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var m1 = 2 * lightness - m2;
        return (
            HueToChannel(m1, m2, hue + 1.0 / 3.0) * 255.0,
            HueToChannel(m1, m2, hue) * 255.0,
            HueToChannel(m1, m2, hue - 1.0 / 3.0) * 255.0);
    }

    private static double HueToChannel(double m1, double m2, double hue)
    {
        hue -= Math.Floor(hue);
        if (hue < 1.0 / 6.0) return m1 + (m2 - m1) * hue * 6.0;
        if (hue < 1.0 / 2.0) return m2;
        if (hue < 2.0 / 3.0) return m1 + (m2 - m1) * (2.0 / 3.0 - hue) * 6.0;
        return m1;
    }
}
