namespace Meridian.Services;

/// <summary>
/// The authored theme token values: the layered surfaces, the three text
/// tiers, and the selection colours for the dark and light themes. Pure data
/// only — the app carries these exact values in its App.xaml theme
/// dictionaries, and the test suite asserts the two never drift apart (the
/// same arrangement AccentPalette has with the dictionary accent values).
/// Selection is deliberately independent of the accent colour: it is a dark
/// red-gray in the dark theme and a pale red-gray in the light theme, and it
/// never changes when the user picks a different accent. Every text tier
/// holds WCAG AA contrast (4.5:1 or better, via AccentColor.ContrastRatio)
/// on every surface it can sit on, including the selection backgrounds;
/// the tests assert that too.
/// </summary>
public static class ThemeTokens
{
    // ---- Dark theme: near-black layers, one visible step apart. ----
    // Surface is the window background; panels and the sidebar sit one step
    // above it; raised areas (header, pane headers, input fields) one more.
    // The border is a further subtle step above the raised tone, so panel
    // edges stay visible against both the panel and raised tones.
    public static AccentColor DarkSurface { get; } = new(0x10, 0x12, 0x15);
    public static AccentColor DarkPanel { get; } = new(0x17, 0x19, 0x1D);
    public static AccentColor DarkChrome { get; } = new(0x1E, 0x21, 0x26);
    public static AccentColor DarkBorder { get; } = new(0x2C, 0x30, 0x38);
    public static AccentColor DarkTextPrimary { get; } = new(0xFF, 0xFF, 0xFF);
    public static AccentColor DarkTextSecondary { get; } = new(0xC9, 0xCD, 0xD4);
    public static AccentColor DarkTextTertiary { get; } = new(0xAE, 0xB4, 0xBE);

    /// <summary>Dark red-gray selection: muted maroon-gray, never the accent colour.</summary>
    public static AccentColor DarkSelectionBackground { get; } = new(0x4B, 0x36, 0x3B);
    public static AccentColor DarkSelectionPointerOver { get; } = new(0x5C, 0x43, 0x4A);
    public static AccentColor DarkSelectionInactive { get; } = new(0x3A, 0x2C, 0x30);
    public static AccentColor DarkSelectionForeground { get; } = new(0xFF, 0xFF, 0xFF);

    // ---- Light theme: surface values are unchanged from the previous
    // palette; only the selection family is new (pale red-gray, dark text).
    public static AccentColor LightSurface { get; } = new(0xD5, 0xD2, 0xCE);
    public static AccentColor LightPanel { get; } = new(0xC7, 0xC4, 0xBF);
    public static AccentColor LightChrome { get; } = new(0xCD, 0xCA, 0xC5);
    public static AccentColor LightBorder { get; } = new(0xB8, 0xB3, 0xAB);
    public static AccentColor LightTextPrimary { get; } = new(0x00, 0x00, 0x00);
    public static AccentColor LightTextSecondary { get; } = new(0x36, 0x32, 0x2B);
    public static AccentColor LightTextTertiary { get; } = new(0x51, 0x4B, 0x41);

    /// <summary>Pale red-gray selection for the light theme, with dark text on it.</summary>
    public static AccentColor LightSelectionBackground { get; } = new(0xEA, 0xD9, 0xDC);
    public static AccentColor LightSelectionPointerOver { get; } = new(0xDF, 0xC6, 0xCB);
    public static AccentColor LightSelectionInactive { get; } = new(0xF2, 0xE4, 0xE6);
    public static AccentColor LightSelectionForeground { get; } = new(0x00, 0x00, 0x00);
}
