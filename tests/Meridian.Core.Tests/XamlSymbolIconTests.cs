using System.Text.RegularExpressions;

namespace Meridian.Core.Tests;

/// <summary>
/// Guards the App project's XAML against icon names that are not members of
/// Microsoft.UI.Xaml.Controls.Symbol. An unknown name (for example the former
/// "OpenFolder") compiles fine but throws while the window is being created,
/// so the program never opens; this test makes that crash class fail here
/// instead of on a user's machine.
/// </summary>
public sealed class XamlSymbolIconTests
{
    // The complete member list of Microsoft.UI.Xaml.Controls.Symbol
    // (Windows App SDK). There is deliberately no "OpenFolder" member.
    private static readonly HashSet<string> ValidSymbols = new(StringComparer.Ordinal)
    {
        "Previous", "Next", "Play", "Pause", "Edit", "Save", "Clear", "Delete",
        "Remove", "Add", "Cancel", "Accept", "More", "Redo", "Undo", "Home",
        "Up", "Forward", "Back", "Favorite", "Camera", "Setting", "Video",
        "Sync", "Download", "Mail", "Find", "Help", "Upload", "Emoji",
        "TwoPage", "LeaveChat", "MailForward", "Clock", "Send", "Crop",
        "RotateCamera", "People", "OpenPane", "ClosePane", "World", "Flag",
        "PreviewLink", "Globe", "Trim", "AttachCamera", "ZoomIn", "Bookmarks",
        "Document", "ProtectedDocument", "Page", "Bullets", "Comment",
        "MailFilled", "ContactInfo", "HangUp", "ViewAll", "MapPin", "Phone",
        "VideoChat", "Switch", "Contact", "Rename", "Pin", "MusicInfo", "Go",
        "Keyboard", "DockLeft", "DockRight", "DockBottom", "Remote", "Refresh",
        "Rotate", "Shuffle", "List", "Shop", "SelectAll", "Orientation",
        "Import", "ImportAll", "BrowsePhotos", "WebCam", "Pictures",
        "SaveLocal", "Caption", "Stop", "ShowResults", "Volume", "Repair",
        "Message", "Page2", "CalendarDay", "CalendarWeek", "Calendar",
        "Character", "MailReplyAll", "Read", "Link", "Account", "ShowBcc",
        "HideBcc", "Cut", "Attach", "Paste", "Filter", "Copy", "Emoji2",
        "Important", "MailReply", "SlideShow", "Sort", "Manage", "AllApps",
        "DisconnectDrive", "MapDrive", "NewWindow", "OpenWith",
        "ContactPresence", "Priority", "GoToToday", "Font", "FontColor",
        "Contact2", "Folder", "Audio", "Placeholder", "View", "SetLockScreen",
        "SetTile", "ClosedCaption", "StopSlideShow", "Permissions",
        "Highlight", "DisableUpdates", "UnFavorite", "UnPin", "OpenLocal",
        "Mute", "Italic", "Underline", "Bold", "MoveToFolder", "LikeDislike",
        "Dislike", "Like", "AlignRight", "AlignCenter", "AlignLeft", "Zoom",
        "ZoomOut", "OpenFile", "OtherUser", "Admin", "Street", "Map",
        "ClearSelection", "FontDecrease", "FontIncrease", "FontSize",
        "CellPhone", "ReShare", "Tag", "RepeatOne", "RepeatAll", "OutlineStar",
        "SolidStar", "Calculator", "Directions", "Target", "Library",
        "PhoneBook", "Memo", "Microphone", "PostUpdate", "BackToWindow",
        "FullScreen", "NewFolder", "CalendarReply", "UnSyncFolder",
        "ReportHacked", "SyncFolder", "BlockContact", "SwitchApps",
        "AddFriend", "TouchPointer", "GoToStart", "ZeroBars", "OneBar",
        "TwoBars", "ThreeBars", "FourBars", "Scan", "Preview",
        "GlobalNavigationButton", "Share", "Print", "XboxOneConsole",
    };

    private static readonly Regex IconAttribute = new(
        "(?:Icon|Symbol)=\"(?<name>[^\"]+)\"",
        RegexOptions.Compiled);

    [Fact]
    public void AppXaml_EveryIconName_IsAKnownSymbolMember()
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
        var referenceCount = 0;
        foreach (var file in xamlFiles)
        {
            foreach (Match match in IconAttribute.Matches(File.ReadAllText(file)))
            {
                referenceCount++;
                var name = match.Groups["name"].Value;
                if (!ValidSymbols.Contains(name))
                    offenders.Add($"{Path.GetFileName(file)}: \"{name}\"");
            }
        }

        Assert.True(referenceCount > 0, "No Icon/Symbol attributes were found in the App XAML; the scan is not working.");
        Assert.True(
            offenders.Count == 0,
            "Icon names that are not Microsoft.UI.Xaml.Controls.Symbol members (these throw at window creation): "
            + string.Join(", ", offenders));
    }

    private static string FindAppProjectDirectory()
    {
        // Start from the test assembly's own folder (bin/<config>/net10.0) and
        // walk up until the directory that contains src/Meridian.App is found.
        // (AppContext.BaseDirectory is the test runner's folder, not this one.)
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(XamlSymbolIconTests).Assembly.Location)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Meridian.App");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/Meridian.App above the test assembly directory; the XAML icon guard cannot run.");
    }
}
