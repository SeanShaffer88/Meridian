using System.Text.RegularExpressions;

namespace Meridian.Core.Tests;

/// <summary>
/// Guards the App project against a startup crash class: a XAML control that
/// carries both SelectedIndex and SelectionChanged reports its starting value
/// while InitializeComponent() is still running, before the window's pane
/// state exists. The handler runs with a null pane and crashes the program
/// before it ever opens (this is what made 0.3.0 fail to start: the grouping
/// combos reached UpdateGrouping with no pane). Every such handler — or the
/// method it delegates to — must null-guard its pane parameter, the same way
/// UpdateSort already does. ApplyFilter is checked too: its TextChanged
/// handlers pass the panes the same way, so it is one XAML edit away from
/// the identical crash.
/// </summary>
public sealed class XamlInitEventGuardTests
{
    // One XAML element (open tag), attributes included, never crossing into a
    // child element.
    private static readonly Regex ElementTag = new(
        "<[A-Za-z][^<>]*>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex SelectionChangedAttribute = new(
        "SelectionChanged=\"(?<handler>[^\"]+)\"",
        RegexOptions.Compiled);

    [Fact]
    public void InitTimeSelectionChangedHandlers_GuardAgainstNullPane()
    {
        var appDirectory = FindAppProjectDirectory();
        var xamlPath = Path.Combine(appDirectory, "MainWindow.xaml");
        var codePath = Path.Combine(appDirectory, "MainWindow.xaml.cs");
        Assert.True(File.Exists(xamlPath), $"MainWindow.xaml not found at {xamlPath}; the init-event guard cannot run.");
        Assert.True(File.Exists(codePath), $"MainWindow.xaml.cs not found at {codePath}; the init-event guard cannot run.");

        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);

        var handlerNames = new List<string>();
        foreach (Match tag in ElementTag.Matches(xaml))
        {
            if (!tag.Value.Contains("SelectedIndex=\"", StringComparison.Ordinal)) continue;
            var handler = SelectionChangedAttribute.Match(tag.Value);
            if (handler.Success) handlerNames.Add(handler.Groups["handler"].Value);
        }

        Assert.True(
            handlerNames.Count > 0,
            "No XAML controls with both SelectedIndex and SelectionChanged were found in MainWindow.xaml; the scan is not working.");

        var unguarded = new List<string>();
        foreach (var handlerName in handlerNames)
        {
            var (body, checkedName) = ExtractHandlerOrDelegationBody(code, handlerName);
            if (!body.Contains("is null", StringComparison.Ordinal))
                unguarded.Add($"{handlerName} -> {checkedName} (no 'is null' guard for its pane parameter)");
        }

        Assert.True(
            unguarded.Count == 0,
            "Handlers that can fire during InitializeComponent, before the pane state exists, "
            + "must null-guard the pane (a null pane here crashes the program at startup): "
            + string.Join(", ", unguarded));
    }

    [Fact]
    public void ApplyFilter_GuardsAgainstNullPane()
    {
        var appDirectory = FindAppProjectDirectory();
        var codePath = Path.Combine(appDirectory, "MainWindow.xaml.cs");
        Assert.True(File.Exists(codePath), $"MainWindow.xaml.cs not found at {codePath}; the init-event guard cannot run.");

        var body = ExtractMethodBody(File.ReadAllText(codePath), "ApplyFilter");
        Assert.True(
            body.Contains("pane is null", StringComparison.Ordinal),
            "ApplyFilter must guard against a null pane ('pane is null'): its filter TextChanged handlers "
            + "pass the pane fields the same way the grouping handlers do, so a null pane would crash the program at startup.");
    }

    /// <summary>
    /// Returns the text whose null guard protects the given event handler: if
    /// the handler is an expression-bodied delegation (for example
    /// "=> UpdateGrouping(_left, LeftGroup);"), the target method's body is
    /// returned along with the target's name; otherwise the handler's own
    /// body (block or expression) is returned with the handler's name.
    /// </summary>
    private static (string Body, string CheckedName) ExtractHandlerOrDelegationBody(string code, string handlerName)
    {
        var (kind, text) = ExtractMethodBodyOrExpression(code, handlerName);
        if (kind == "expression")
        {
            var call = Regex.Match(text, @"^\s*(?<target>[A-Za-z_][A-Za-z0-9_]*)\s*\(");
            if (call.Success)
            {
                var targetName = call.Groups["target"].Value;
                return (ExtractMethodBody(code, targetName), targetName);
            }
        }

        return (text, handlerName);
    }

    private static string ExtractMethodBody(string code, string methodName)
    {
        var (kind, text) = ExtractMethodBodyOrExpression(code, methodName);
        Assert.True(
            kind == "block",
            $"Expected {methodName} to have a block body so its null guard can be checked.");
        return text;
    }

    /// <summary>
    /// Locates a method definition and returns ("block", body text without the
    /// outer braces) for a block-bodied method or ("expression", expression
    /// text) for an expression-bodied one.
    /// </summary>
    private static (string Kind, string Text) ExtractMethodBodyOrExpression(string code, string methodName)
    {
        // Anchored at a line start with an access modifier, and the text
        // between the modifier and the method name may not contain '(' or
        // '=' — otherwise a call inside another method's declaration line
        // (an expression-bodied handler delegating to this method) would be
        // mistaken for the definition.
        var definition = Regex.Match(
            code,
            @"^\s*(?:private|internal|public|protected)\s+[^\n;{}()=]*?\b" + Regex.Escape(methodName) + @"\s*\(",
            RegexOptions.Multiline);
        Assert.True(definition.Success, $"Method definition for {methodName} was not found in MainWindow.xaml.cs.");

        // Skip the parameter list (balanced parentheses).
        var index = definition.Index + definition.Length; // just past '('
        var depth = 1;
        while (index < code.Length && depth > 0)
        {
            if (code[index] == '(') depth++;
            else if (code[index] == ')') depth--;
            index++;
        }

        while (index < code.Length && char.IsWhiteSpace(code[index])) index++;
        Assert.True(index < code.Length, $"Method {methodName} has no body in MainWindow.xaml.cs.");

        if (code[index] == '=' && index + 1 < code.Length && code[index + 1] == '>')
        {
            var end = code.IndexOf(';', index + 2);
            Assert.True(end > index, $"Expression-bodied method {methodName} is not terminated in MainWindow.xaml.cs.");
            return ("expression", code[(index + 2)..end]);
        }

        Assert.True(code[index] == '{', $"Method {methodName} has neither a block nor an expression body in MainWindow.xaml.cs.");
        var bodyStart = index + 1;
        depth = 1;
        index++;
        while (index < code.Length && depth > 0)
        {
            if (code[index] == '{') depth++;
            else if (code[index] == '}') depth--;
            index++;
        }

        Assert.True(depth == 0, $"Method {methodName} has unbalanced braces in MainWindow.xaml.cs.");
        return ("block", code[bodyStart..(index - 1)]);
    }

    private static string FindAppProjectDirectory()
    {
        // Start from the test assembly's own folder (bin/<config>/net10.0) and
        // walk up until the directory that contains src/Meridian.App is found.
        // (AppContext.BaseDirectory is the test runner's folder, not this one.)
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(XamlInitEventGuardTests).Assembly.Location)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Meridian.App");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/Meridian.App above the test assembly directory; the init-event guard cannot run.");
    }
}
