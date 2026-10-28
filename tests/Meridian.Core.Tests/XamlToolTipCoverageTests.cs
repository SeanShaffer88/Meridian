using System.Text.RegularExpressions;

namespace Meridian.Core.Tests;

/// <summary>
/// Guards the App project's XAML against interactive controls with no
/// tooltip. The owner's rule is that every button, menu item, picker,
/// switch and similar control explains itself in plain words — and that
/// destructive or consequential ones state the consequence (Replace
/// overwrites the existing file; Cancel keeps finished files and removes
/// the half-copied one). A control that genuinely gets its tooltip from
/// somewhere the scan cannot see (a style or template) must be named in
/// <see cref="TooltipAllowList"/> with the reason — the list is the only
/// exception route, and it is empty today because every control in the
/// XAML carries its tooltip directly.
/// </summary>
public sealed class XamlToolTipCoverageTests
{
    // Element local names that count as interactive for this scan. Property
    // elements (SplitButton.Flyout, ListView.ContextFlyout, …) resolve to
    // their part after the last dot and are not in the set; neither are
    // purely presentational elements. ListView itself is excluded: the
    // pane lists' tooltips name the folder they show and are set from code
    // where the folder is known.
    private static readonly HashSet<string> InteractiveElements = new(StringComparer.Ordinal)
    {
        "Button", "AppBarButton", "AppBarToggleButton", "ToggleButton",
        "SplitButton", "ToggleSplitButton", "DropDownButton", "ComboBox",
        "MenuFlyoutItem", "ToggleMenuFlyoutItem", "MenuFlyoutSubItem",
        "ToggleSwitch", "Slider", "CheckBox", "RadioButton",
        "HyperlinkButton", "RepeatButton", "Expander", "RatingControl",
        "TextBox",
    };

    /// <summary>
    /// x:Name values allowed to lack a ToolTipService.ToolTip attribute,
    /// each with its reason recorded here. Empty: every interactive control
    /// in the App XAML carries its tooltip directly on the element.
    /// </summary>
    private static readonly HashSet<string> TooltipAllowList = new(StringComparer.Ordinal)
    {
    };

    // XML comments can contain example markup; they are not controls.
    private static readonly Regex XmlComment = new(
        "<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // One XAML element open tag, attributes included, never crossing into a
    // child element. Attribute values in this project's XAML never contain
    // '<' or '>', so [^<>]* is a safe attribute span.
    private static readonly Regex ElementTag = new(
        "<(?<name>[A-Za-z][A-Za-z0-9_.:-]*)\\b(?<attrs>[^<>]*?)(/?)>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex Attribute = new(
        "(?<key>[\\w.:-]+)=\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled);

    [Fact]
    public void AppXaml_EveryInteractiveElement_HasATooltip()
    {
        var appDirectory = FindAppProjectDirectory();
        // Source XAML only: build output folders can hold stale generated copies.
        var xamlFiles = Directory
            .EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .ToList();
        Assert.NotEmpty(xamlFiles);

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in xamlFiles)
        {
            var xaml = XmlComment.Replace(File.ReadAllText(file), "");
            foreach (Match tag in ElementTag.Matches(xaml))
            {
                var localName = tag.Groups["name"].Value.Split('.').Last().Split(':').Last();
                if (!InteractiveElements.Contains(localName)) continue;
                scanned++;
                var attrs = tag.Groups["attrs"].Value;
                if (attrs.Contains("ToolTipService.ToolTip=\"", StringComparison.Ordinal)) continue;

                var attributes = Attribute.Matches(attrs)
                    .ToDictionary(match => match.Groups["key"].Value, match => match.Groups["value"].Value);
                if (attributes.TryGetValue("x:Name", out var name) && TooltipAllowList.Contains(name))
                    continue;
                var identity = name
                    ?? (attributes.TryGetValue("Content", out var content) ? content
                    : attributes.TryGetValue("Text", out var text) ? text
                    : attributes.TryGetValue("Label", out var label) ? label
                    : attributes.TryGetValue("Header", out var header) ? header
                    : "(unnamed)");
                offenders.Add($"{Path.GetFileName(file)}: <{localName}> {identity}");
            }
        }

        Assert.True(
            scanned > 40,
            $"Only {scanned} interactive elements were found in the App XAML; the scan is not working.");
        Assert.True(
            offenders.Count == 0,
            "Interactive elements without a ToolTipService.ToolTip (every control must explain itself "
            + "in plain words; destructive ones must state the consequence): "
            + string.Join(", ", offenders));
    }

    private static string FindAppProjectDirectory()
    {
        // Start from the test assembly's own folder (bin/<config>/net10.0) and
        // walk up until the directory that contains src/Meridian.App is found.
        // (AppContext.BaseDirectory is the test runner's folder, not this one.)
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(XamlToolTipCoverageTests).Assembly.Location)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Meridian.App");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/Meridian.App above the test assembly directory; the XAML tooltip guard cannot run.");
    }
}
