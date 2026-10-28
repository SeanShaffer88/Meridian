using System.Collections.ObjectModel;
using Meridian.Models;
using Meridian.Services;
using Microsoft.VisualBasic.FileIO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Meridian;

public sealed partial class MainWindow : Window
{
    /// <summary>
    /// The sentinel a pane's CurrentPath holds while it shows the Home page.
    /// Not a filesystem path: navigation routes it to the Home view, the
    /// workspace store's directory checks reject it (so a pane left at Home
    /// reopens on the default folder), and it is never recorded as a recent.
    /// </summary>
    private const string HomePath = "meridian://home";

    private readonly PaneState _left;
    private readonly PaneState _right;
    private readonly WorkspaceStore _workspaceStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private ThemePreference _themePreference = ThemePreference.Dark;
    private DensityPreference _densityPreference = DensityPreference.Comfortable;
    private AccentMode _accentMode = AccentMode.Preset;
    private string? _accentPresetId = AccentPalette.DefaultPresetId;
    private string? _accentCustomColor;
    private double _paneSplitFraction = 0.5;
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private double _rasterizationScale = 1.0;
    private bool _enforcingMinimumWindowSize;
    private bool _splitDragging;
    private double _splitDragStartX;
    private double _splitDragStartFraction;
    private readonly DropShelf _dropShelf = new();
    private readonly RecentPathsStore _recentsStore = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian", "recents.json"));
    private bool _sidebarVisible = true;
    private readonly List<(Button Button, string TargetPath)> _sidebarNavButtons = [];
    private readonly List<ContextCapsule> _capsules = [];
    private readonly ContextCapsuleStore _capsuleStore = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian", "capsules.json"));
    private bool _capsuleStorageUnavailable;
    private bool _capsuleSaving;
    private readonly ObservableCollection<string> _shelfPaths = [];
    private readonly ObservableCollection<BookmarkSnapshot> _bookmarks = [];
    private readonly List<WorkspaceTab> _workspaceTabs = [];
    private int _activeWorkspaceTabIndex;
    private int _nextWorkspaceTabNumber = 1;
    private bool _switchingWorkspaceTab;
    private PaneState _activePane;
    private OrganizeSection _openOrganizeSection = OrganizeSection.None;
    private bool _restoringWorkspace;
    private CancellationTokenSource? _copyCancellation;
    private CancellationTokenSource? _batchCancellation;
    private bool _batchPreparing;
    private string? _pinnedError;
    private bool _detailsStripVisible;
    private FileEntry? _detailsCurrentEntry;
    private Dictionary<MetadataProperty, string> _detailsCurrentValues = new();
    private int _detailsStripGeneration;
    private CancellationTokenSource? _detailsStripCancellation;
    private PaneState? _quickPreviewPane;
    private FileEntry? _quickPreviewEntry;
    private PreviewMediaLease? _quickPreviewLease;
    private CancellationTokenSource? _quickPreviewCancellation;

    public MainWindow()
    {
        InitializeComponent();
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _appWindow.Changed += AppWindow_Changed;
        if (Content is FrameworkElement rootElement) rootElement.Loaded += RootElement_Loaded;
        _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
        Activated += MainWindow_Activated;
        BookmarksBox.ItemsSource = _bookmarks;
        ShelfItemsBox.ItemsSource = _shelfPaths;
        _left = new PaneState("Left", LeftPath, LeftFilter, LeftItems, LeftStatus, LeftFilterRow, LeftHomeView, LeftHomePanel);
        _right = new PaneState("Right", RightPath, RightFilter, RightItems, RightStatus, RightFilterRow, RightHomeView, RightHomePanel);
        _activePane = _left;
        UpdateActivePaneVisuals();
        LeftItems.ItemsSource = _left.Items;
        RightItems.ItemsSource = _right.Items;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents) || !Directory.Exists(documents)) documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloads)) downloads = documents;
        _ = InitializeAsync(documents, downloads);
    }

    private async Task InitializeAsync(string documents, string downloads)
    {
        var settings = await _settingsStore.LoadAsync();
        if (settings is not null)
        {
            _themePreference = ParseThemePreference(settings.Theme);
            _densityPreference = ParseDensityPreference(settings.Density);
            _accentMode = AccentPalette.ParseMode(settings.AccentMode, settings.FollowWindowsAccent);
            _accentPresetId = settings.AccentPreset ?? AccentPalette.DefaultPresetId;
            _accentCustomColor = settings.AccentCustomColor;
            _paneSplitFraction = Math.Clamp(settings.PaneSplitFraction, 0.25, 0.75);
            ApplyThemePreference();
            ApplyDensityPreference();
            ApplyPaneSplit();
            SetOrganizeSection(ParseOrganizeSection(settings.OrganizeSection));
            _detailsStripVisible = settings.DetailsStripVisible;
            ApplyDetailsStripVisibility();
            _sidebarVisible = settings.SidebarVisible;
            ApplySidebarVisibility();
        }
        try
        {
            _capsules.AddRange(await _capsuleStore.LoadAsync());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            // Never replace unreadable saved data with a new empty document.
            _capsuleStorageUnavailable = true;
            SetStatus("Saved Capsules could not be loaded; saving is disabled to preserve the original file · " + ex.Message, StatusSeverity.Error);
        }
        var saved = await _workspaceStore.LoadAsync();
        _bookmarks.Clear();
        foreach (var bookmark in saved?.Bookmarks ?? []) _bookmarks.Add(bookmark);

        var defaultLeft = saved?.LeftPath is { Length: > 0 } savedLeft && Directory.Exists(savedLeft) ? savedLeft : documents;
        var defaultRight = saved?.RightPath is { Length: > 0 } savedRight && Directory.Exists(savedRight) ? savedRight : downloads;
        if (saved?.Tabs is { Length: > 0 })
        {
            foreach (var tab in saved.Tabs)
            {
                if (tab is null) continue;
                var tabName = string.IsNullOrWhiteSpace(tab.Name) ? $"Workspace {_workspaceTabs.Count + 1}" : tab.Name;
                _workspaceTabs.Add(new WorkspaceTab(tabName, tab.LeftPath ?? defaultLeft, tab.RightPath ?? defaultRight));
            }
        }
        if (_workspaceTabs.Count == 0)
            _workspaceTabs.Add(new WorkspaceTab("Workspace 1", defaultLeft, defaultRight));
        _activeWorkspaceTabIndex = Math.Clamp(saved?.ActiveTabIndex ?? 0, 0, _workspaceTabs.Count - 1);
        _nextWorkspaceTabNumber = _workspaceTabs.Count + 1;
        RefreshWorkspaceTabs();
        await _recentsStore.LoadAsync();
        BuildSidebar();

        var activeTab = _workspaceTabs[_activeWorkspaceTabIndex];
        var leftPath = Directory.Exists(activeTab.LeftPath) ? activeTab.LeftPath : defaultLeft;
        var rightPath = Directory.Exists(activeTab.RightPath) ? activeTab.RightPath : defaultRight;
        activeTab.LeftPath = leftPath;
        activeTab.RightPath = rightPath;

        _restoringWorkspace = true;
        try
        {
            await NavigateAsync(_left, leftPath, addHistory: false);
            await NavigateAsync(_right, rightPath, addHistory: false);
        }
        finally
        {
            _restoringWorkspace = false;
        }

        await PersistWorkspaceAsync();
    }

    private async Task<bool> PersistWorkspaceAsync()
    {
        if (_restoringWorkspace) return false;
        try
        {
            StoreCurrentWorkspaceTab();
            var tabs = _workspaceTabs.Select(tab => new WorkspaceTabSnapshot(tab.Name, tab.LeftPath, tab.RightPath)).ToArray();
            await _workspaceStore.SaveAsync(new WorkspaceSnapshot(_left.CurrentPath, _right.CurrentPath, _bookmarks.ToArray(), tabs, _activeWorkspaceTabIndex));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Workspace persistence is a convenience; file browsing must continue if it fails.
            SetStatus("Workspace could not be saved · " + ex.Message, StatusSeverity.Error);
        }
        return false;
    }

    private async Task PersistSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(new SettingsSnapshot(
                _themePreference.ToString(),
                _densityPreference.ToString(),
                _accentMode == AccentMode.FollowWindows,
                _paneSplitFraction,
                _openOrganizeSection.ToString(),
                _accentMode.ToString(),
                _accentPresetId,
                _accentCustomColor,
                _detailsStripVisible,
                _sidebarVisible));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Settings persistence is a convenience; file browsing must continue if it fails.
            SetStatus("Settings could not be saved · " + ex.Message, StatusSeverity.Error);
        }
    }

    private static ThemePreference ParseThemePreference(string? value) => value switch
    {
        nameof(ThemePreference.Light) => ThemePreference.Light,
        nameof(ThemePreference.System) => ThemePreference.System,
        _ => ThemePreference.Dark
    };

    private static DensityPreference ParseDensityPreference(string? value) =>
        value == nameof(DensityPreference.Compact) ? DensityPreference.Compact : DensityPreference.Comfortable;

    private static OrganizeSection ParseOrganizeSection(string? value) => value switch
    {
        nameof(OrganizeSection.Bookmarks) => OrganizeSection.Bookmarks,
        nameof(OrganizeSection.Shelf) => OrganizeSection.Shelf,
        _ => OrganizeSection.None
    };

    private string NextWorkspaceTabName()
    {
        string name;
        do { name = $"Workspace {_nextWorkspaceTabNumber++}"; }
        while (_workspaceTabs.Any(tab => string.Equals(tab.Name, name, StringComparison.OrdinalIgnoreCase)));
        return name;
    }

    private void StoreCurrentWorkspaceTab()
    {
        if (_workspaceTabs.Count == 0 || _activeWorkspaceTabIndex < 0 || _activeWorkspaceTabIndex >= _workspaceTabs.Count) return;
        var active = _workspaceTabs[_activeWorkspaceTabIndex];
        if (!string.IsNullOrWhiteSpace(_left.CurrentPath)) active.LeftPath = _left.CurrentPath;
        if (!string.IsNullOrWhiteSpace(_right.CurrentPath)) active.RightPath = _right.CurrentPath;
    }

    private void RefreshWorkspaceTabs()
    {
        WorkspaceTabsPanel.Children.Clear();
        for (var i = 0; i < _workspaceTabs.Count; i++)
        {
            var tab = _workspaceTabs[i];
            var tabButton = new Button
            {
                Content = (i == _activeWorkspaceTabIndex ? "▸ " : "") + tab.Name,
                Tag = tab,
                MinWidth = 110,
                Padding = new Thickness(10, 4, 10, 4)
            };
            AutomationProperties.SetName(tabButton, (i == _activeWorkspaceTabIndex ? "Active workspace tab: " : "Workspace tab: ") + tab.Name);
            tabButton.Click += WorkspaceTab_Click;
            WorkspaceTabsPanel.Children.Add(tabButton);
            if (_workspaceTabs.Count > 1)
            {
                var closeButton = new Button
                {
                    Content = new SymbolIcon { Symbol = Symbol.Cancel },
                    Tag = tab,
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0)
                };
                AutomationProperties.SetName(closeButton, "Close workspace tab: " + tab.Name);
                ToolTipService.SetToolTip(closeButton, "Close " + tab.Name);
                closeButton.Click += CloseWorkspaceTab_Click;
                WorkspaceTabsPanel.Children.Add(closeButton);
            }
        }
    }

    private async void AddWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (_workspaceTabs.Count >= WorkspaceStore.MaximumTabs)
        {
            SetStatus($"Workspace tab limit reached · {WorkspaceStore.MaximumTabs}", StatusSeverity.Warning);
            return;
        }
        if (_switchingWorkspaceTab) return;
        StoreCurrentWorkspaceTab();
        var leftPath = string.IsNullOrWhiteSpace(_left.CurrentPath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : _left.CurrentPath;
        var rightPath = string.IsNullOrWhiteSpace(_right.CurrentPath) ? leftPath : _right.CurrentPath;
        var tab = new WorkspaceTab(NextWorkspaceTabName(), leftPath, rightPath);
        _workspaceTabs.Add(tab);
        _activeWorkspaceTabIndex = _workspaceTabs.Count - 1;
        RefreshWorkspaceTabs();
        await PersistWorkspaceAsync();
        SetStatus($"Opened {tab.Name}");
    }

    private async void WorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (_switchingWorkspaceTab || (sender as Button)?.Tag is not WorkspaceTab tab) return;
        var index = _workspaceTabs.IndexOf(tab);
        if (index < 0 || index == _activeWorkspaceTabIndex) return;
        StoreCurrentWorkspaceTab();
        _activeWorkspaceTabIndex = index;
        RefreshWorkspaceTabs();
        await LoadWorkspaceTabAsync(tab);
    }

    private async void CloseWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (_switchingWorkspaceTab || (sender as Button)?.Tag is not WorkspaceTab tab) return;
        var index = _workspaceTabs.IndexOf(tab);
        if (index < 0 || _workspaceTabs.Count <= 1) return;

        var wasActive = index == _activeWorkspaceTabIndex;
        if (wasActive) StoreCurrentWorkspaceTab();
        _workspaceTabs.RemoveAt(index);
        if (index < _activeWorkspaceTabIndex) _activeWorkspaceTabIndex--;
        else if (wasActive) _activeWorkspaceTabIndex = Math.Min(index, _workspaceTabs.Count - 1);
        RefreshWorkspaceTabs();

        if (wasActive)
            await LoadWorkspaceTabAsync(_workspaceTabs[_activeWorkspaceTabIndex]);
        else
            await PersistWorkspaceAsync();
    }

    private async Task LoadWorkspaceTabAsync(WorkspaceTab tab)
    {
        if (_switchingWorkspaceTab) return;
        _switchingWorkspaceTab = true;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(documents)) documents = profile;
        var downloads = Path.Combine(profile, "Downloads");
        if (!Directory.Exists(downloads)) downloads = documents;
        var leftPath = Directory.Exists(tab.LeftPath) ? tab.LeftPath : documents;
        var rightPath = Directory.Exists(tab.RightPath) ? tab.RightPath : downloads;
        _left.List.SelectedItem = null;
        _right.List.SelectedItem = null;
        _restoringWorkspace = true;
        try
        {
            await NavigateAsync(_left, leftPath, addHistory: false);
            await NavigateAsync(_right, rightPath, addHistory: false);
        }
        finally
        {
            _restoringWorkspace = false;
            _switchingWorkspaceTab = false;
        }
        tab.LeftPath = _left.CurrentPath;
        tab.RightPath = _right.CurrentPath;
        await PersistWorkspaceAsync();
        SetStatus($"Switched to {tab.Name}");
    }

    private async Task AddBookmarkAsync()
    {
        if (_bookmarks.Count >= WorkspaceStore.MaximumBookmarks)
        {
            SetStatus($"Bookmark limit reached · {WorkspaceStore.MaximumBookmarks}", StatusSeverity.Warning);
            return;
        }
        var path = _activePane.CurrentPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        if (_activePane.IsHome)
        {
            SetStatus("Home is already pinned at the top of the sidebar.", StatusSeverity.Warning);
            return;
        }
        if (_bookmarks.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("That location is already bookmarked.", StatusSeverity.Warning);
            return;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(name)) name = path;
        _bookmarks.Add(new BookmarkSnapshot(name, path));
        BuildSidebar();
        await PersistWorkspaceAsync();
        SetStatus($"Bookmarked · {name}", StatusSeverity.Success);
    }

    private async void AddBookmark_Click(object sender, RoutedEventArgs e) => await AddBookmarkAsync();

    private async void RemoveBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (BookmarksBox.SelectedItem is not BookmarkSnapshot selected)
        {
            SetStatus("Select a saved location to remove.", StatusSeverity.Warning);
            return;
        }

        _bookmarks.Remove(selected);
        BuildSidebar();
        BookmarksBox.SelectedItem = null;
        await PersistWorkspaceAsync();
        SetStatus($"Removed bookmark · {selected.Name}", StatusSeverity.Success);
    }

    private async void BookmarksBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BookmarksBox.SelectedItem is not BookmarkSnapshot selected) return;
        await NavigateAsync(_activePane, selected.Path);
        BookmarksBox.SelectedItem = null;
    }

    private async Task<bool> NavigateAsync(PaneState pane, string requestedPath, bool addHistory = true)
    {
        if (string.Equals(requestedPath.Trim(), HomePath, StringComparison.OrdinalIgnoreCase))
            return await NavigateHomeAsync(pane, addHistory);
        pane.ReadCancellation?.Cancel();
        pane.ReadCancellation?.Dispose();
        pane.ReadCancellation = new CancellationTokenSource();
        var token = pane.ReadCancellation.Token;
        SetPaneStatus(pane, "Checking folder…");
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath.Trim().Trim('"')));
            var exists = await Task.Run(() => Directory.Exists(fullPath), token);
            if (token.IsCancellationRequested) return false;
            if (!exists) throw new DirectoryNotFoundException($"Folder not found: {fullPath}");
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SetPaneStatus(pane, ex.Message); SetStatus("Navigation failed · " + ex.Message, StatusSeverity.Error); pane.PathBox.Text = pane.CurrentPath; return false;
        }
        pane.ReadCancellation?.Cancel(); pane.ReadCancellation?.Dispose(); pane.ReadCancellation = new CancellationTokenSource();
        token = pane.ReadCancellation.Token;
        SetPaneStatus(pane, "Loading folder…");
        try
        {
            var entries = await Task.Run(() => DirectoryReader.Read(fullPath, token), token);
            if (token.IsCancellationRequested) return false;
            if (addHistory && !string.IsNullOrWhiteSpace(pane.CurrentPath) && !string.Equals(pane.CurrentPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                pane.BackHistory.Push(pane.CurrentPath);
                pane.ForwardHistory.Clear();
            }
            pane.CurrentPath = fullPath;
            pane.PathBox.Text = fullPath;
            UpdatePaneHeader(pane);
            pane.AllItems = entries;
            ApplyFilter(pane);
            ApplyPaneMode(pane);
            RefreshSidebarSelection();
            _ = _recentsStore.RecordAsync(fullPath, isDirectory: true);
            RefreshDetailsStrip();
            SetStatus("Ready · Local-only · No telemetry");
            if (!_restoringWorkspace) await PersistWorkspaceAsync();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!token.IsCancellationRequested)
            {
                SetPaneStatus(pane, ex.Message);
                SetStatus("Could not read folder · " + ex.Message, StatusSeverity.Error);
                pane.PathBox.Text = pane.CurrentPath;
            }
            return false;
        }
    }

    /// <summary>
    /// Shows the Home page in a pane. Mirrors NavigateAsync's commit steps
    /// (history push, header, status, persistence) with the Home view in
    /// place of a directory read; the sentinel path is never recorded as a
    /// recent and never reaches the filesystem APIs.
    /// </summary>
    private async Task<bool> NavigateHomeAsync(PaneState pane, bool addHistory = true)
    {
        pane.ReadCancellation?.Cancel();
        pane.ReadCancellation?.Dispose();
        pane.ReadCancellation = new CancellationTokenSource();
        if (addHistory && !string.IsNullOrWhiteSpace(pane.CurrentPath) &&
            !string.Equals(pane.CurrentPath, HomePath, StringComparison.OrdinalIgnoreCase))
        {
            pane.BackHistory.Push(pane.CurrentPath);
            pane.ForwardHistory.Clear();
        }
        pane.CurrentPath = HomePath;
        pane.PathBox.Text = "Home";
        pane.AllItems = Array.Empty<FileEntry>();
        pane.Items.Clear();
        UpdatePaneHeader(pane);
        ApplyPaneMode(pane);
        RenderHomeView(pane);
        RefreshSidebarSelection();
        SetPaneStatus(pane, "Home · pinned folders, drives and recent items");
        RefreshDetailsStrip();
        SetStatus("Ready · Local-only · No telemetry");
        if (!_restoringWorkspace) await PersistWorkspaceAsync();
        return true;
    }

    // ----- Sidebar and Home -----

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) return;
        // Drives can appear or disappear while Meridian sits in the background
        // (USB storage, mapped drives). There is no device-change listener, so
        // the drive lists are rebuilt when the window comes back to the front,
        // and a visible Home page re-renders with fresh free-space figures.
        BuildSidebar();
        if (_left.IsHome) RenderHomeView(_left);
        if (_right.IsHome) RenderHomeView(_right);
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        _sidebarVisible = !_sidebarVisible;
        ApplySidebarVisibility();
        _ = PersistSettingsAsync();
    }

    private void ApplySidebarVisibility() =>
        SidebarPanel.Visibility = _sidebarVisible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Switches a pane between its file list and its Home view.</summary>
    private void ApplyPaneMode(PaneState pane)
    {
        var home = pane.IsHome;
        pane.HomeView.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        pane.List.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        pane.FilterRow.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BuildSidebar()
    {
        SidebarContent.Children.Clear();
        _sidebarNavButtons.Clear();
        AddSidebarSection("FAVORITES");
        AddSidebarItem(Symbol.Home, "Home", HomePath, "Show the Home page in the active pane");
        foreach (var bookmark in _bookmarks)
            AddSidebarItem(Symbol.Folder, bookmark.Name, bookmark.Path, $"Open {bookmark.Path} in the active pane");
        AddSidebarSection("THIS PC");
        AddSidebarItem(Symbol.Folder, "Desktop", KnownFolderPath(Environment.SpecialFolder.Desktop), "Open the Desktop folder in the active pane");
        AddSidebarItem(Symbol.Document, "Documents", KnownFolderPath(Environment.SpecialFolder.MyDocuments), "Open the Documents folder in the active pane");
        AddSidebarItem(Symbol.Download, "Downloads", DownloadsFolderPath(), "Open the Downloads folder in the active pane");
        AddSidebarItem(Symbol.Audio, "Music", KnownFolderPath(Environment.SpecialFolder.MyMusic), "Open the Music folder in the active pane");
        AddSidebarItem(Symbol.Pictures, "Pictures", KnownFolderPath(Environment.SpecialFolder.MyPictures), "Open the Pictures folder in the active pane");
        AddSidebarItem(Symbol.Video, "Videos", KnownFolderPath(Environment.SpecialFolder.MyVideos), "Open the Videos folder in the active pane");
        AddSidebarSection("DRIVES");
        foreach (var drive in EnumerateDrives())
            AddSidebarItem(Symbol.MapDrive, drive.Label, drive.RootPath, drive.ToolTip);
        RefreshSidebarSelection();
    }

    private static string KnownFolderPath(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(path) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : path;
    }

    private static string DownloadsFolderPath()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : KnownFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void AddSidebarSection(string title) => SidebarContent.Children.Add(new TextBlock
    {
        Text = title,
        FontSize = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = ThemeBrush("MeridianTextTertiaryBrush"),
        Margin = new Thickness(10, 10, 0, 4)
    });

    private void AddSidebarItem(Symbol glyph, string label, string targetPath, string toolTip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new SymbolIcon { Symbol = glyph, Width = 16, Height = 16 });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6, 10, 6),
            Tag = targetPath
        };
        ToolTipService.SetToolTip(button, toolTip);
        AutomationProperties.SetName(button, label);
        button.Click += SidebarItem_Click;
        SidebarContent.Children.Add(button);
        _sidebarNavButtons.Add((button, targetPath));
    }

    private async void SidebarItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target } || string.IsNullOrWhiteSpace(target)) return;
        await NavigateAsync(_activePane, target);
    }

    private void RefreshSidebarSelection()
    {
        var current = _activePane.CurrentPath;
        foreach (var (button, target) in _sidebarNavButtons)
        {
            var selected = string.Equals(target, current, StringComparison.OrdinalIgnoreCase);
            button.Background = selected
                ? ThemeBrush("MeridianSelectionBackgroundBrush")
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    private sealed record DriveEntry(string RootPath, string Label, string ToolTip, long TotalBytes, long FreeBytes, bool IsReady);

    /// <summary>
    /// One pass over the machine's drives for the sidebar and the Home page.
    /// A drive that will not answer (empty card reader, spun-down or
    /// disconnected storage) is listed as not ready with zero sizes instead
    /// of failing the whole list.
    /// </summary>
    private static List<DriveEntry> EnumerateDrives()
    {
        var drives = new List<DriveEntry>();
        DriveInfo[] all;
        try { all = DriveInfo.GetDrives(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return drives; }
        foreach (var drive in all)
        {
            var root = drive.Name;
            var shortName = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (shortName.Length == 0) shortName = root;
            try
            {
                if (!drive.IsReady)
                {
                    drives.Add(new DriveEntry(root, shortName, $"Open {root} in the active pane", 0, 0, false));
                    continue;
                }
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? DriveTypeLabel(drive.DriveType) : drive.VolumeLabel;
                drives.Add(new DriveEntry(root, $"{shortName} · {label}", $"Open {root} in the active pane", drive.TotalSize, drive.AvailableFreeSpace, true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                drives.Add(new DriveEntry(root, shortName, $"Open {root} in the active pane", 0, 0, false));
            }
        }
        return drives;
    }

    private static string DriveTypeLabel(DriveType type) => type switch
    {
        DriveType.Fixed => "Local disk",
        DriveType.Removable => "Removable",
        DriveType.Network => "Network drive",
        DriveType.CDRom => "Disc drive",
        DriveType.Ram => "RAM disk",
        _ => "Drive"
    };

    /// <summary>
    /// Rebuilds a pane's Home page: pinned drive cards with free-space bars,
    /// Desktop and Videos quick cards, and the recent list. Rebuilt on every
    /// show (and on window activation) so free space and recents are current.
    /// </summary>
    private void RenderHomeView(PaneState pane)
    {
        var panel = pane.HomePanel;
        panel.Children.Clear();
        panel.Children.Add(HomeSectionLabel("PINNED"));
        var cards = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 220, ItemHeight = 100 };
        foreach (var drive in EnumerateDrives())
            cards.Children.Add(DriveCard(pane, drive));
        cards.Children.Add(FolderCard(pane, "Desktop", KnownFolderPath(Environment.SpecialFolder.Desktop)));
        cards.Children.Add(FolderCard(pane, "Videos", KnownFolderPath(Environment.SpecialFolder.MyVideos)));
        panel.Children.Add(cards);
        panel.Children.Add(HomeSectionLabel("RECENT"));
        if (_recentsStore.Entries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Folders you open and files you launch will appear here.",
                FontSize = 12,
                Foreground = ThemeBrush("MeridianTextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        foreach (var entry in _recentsStore.Entries.Take(10))
            panel.Children.Add(RecentRow(pane, entry));
        var clear = new Button
        {
            Content = "Clear recent list",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        ToolTipService.SetToolTip(clear, "Forget the recent list. The files and folders themselves are not touched.");
        AutomationProperties.SetName(clear, "Clear recent list");
        clear.Click += async (s, e) =>
        {
            await _recentsStore.ClearAsync();
            if (_left.IsHome) RenderHomeView(_left);
            if (_right.IsHome) RenderHomeView(_right);
            SetStatus("Recent list cleared.");
        };
        panel.Children.Add(clear);
    }

    private TextBlock HomeSectionLabel(string title) => new()
    {
        Text = title,
        FontSize = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = ThemeBrush("MeridianTextTertiaryBrush"),
        Margin = new Thickness(0, 6, 0, 0)
    };

    private Button DriveCard(PaneState pane, DriveEntry drive)
    {
        var content = new StackPanel { Spacing = 4 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new SymbolIcon { Symbol = Symbol.MapDrive, Width = 16, Height = 16 });
        titleRow.Children.Add(new TextBlock { Text = drive.Label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(titleRow);
        content.Children.Add(new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = DriveSummary.UsedFraction(drive.TotalBytes, drive.FreeBytes) * 100,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        content.Children.Add(new TextBlock
        {
            Text = drive.IsReady ? DriveSummary.FreeSpaceLabel(drive.TotalBytes, drive.FreeBytes) : "Not ready",
            FontSize = 11,
            Foreground = ThemeBrush("MeridianTextSecondaryBrush")
        });
        var card = CardButton(pane, content, drive.RootPath, $"Open {drive.RootPath} in the {pane.Name.ToLowerInvariant()} pane");
        AutomationProperties.SetName(card, drive.IsReady
            ? $"{drive.Label}, {DriveSummary.FreeSpaceLabel(drive.TotalBytes, drive.FreeBytes)}"
            : $"{drive.Label}, not ready");
        return card;
    }

    private Button FolderCard(PaneState pane, string name, string path)
    {
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new FontIcon { Glyph = "\uE8B7", FontSize = 22, Foreground = ThemeBrush("MeridianTextSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center });
        return CardButton(pane, content, path, $"Open the {name} folder in the {pane.Name.ToLowerInvariant()} pane");
    }

    private Button CardButton(PaneState pane, UIElement content, string targetPath, string toolTip)
    {
        var card = new Button
        {
            Content = content,
            Width = 212,
            Height = 92,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = ThemeBrush("MeridianChromeBrush"),
            BorderBrush = ThemeBrush("MeridianBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = targetPath
        };
        ToolTipService.SetToolTip(card, toolTip);
        AutomationProperties.SetName(card, toolTip);
        card.Click += async (s, e) => await NavigateAsync(pane, targetPath);
        return card;
    }

    private Button RecentRow(PaneState pane, RecentPathEntry entry)
    {
        var name = Path.GetFileName(entry.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name)) name = entry.Path;
        var texts = new StackPanel { Spacing = 0 };
        texts.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = entry.Path, FontSize = 11, Foreground = ThemeBrush("MeridianTextSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new FontIcon { Glyph = entry.IsDirectory ? "\uE8B7" : "\uE7C3", FontSize = 16, Foreground = ThemeBrush("MeridianTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(texts);
        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4)
        };
        ToolTipService.SetToolTip(button, entry.IsDirectory ? $"Open {entry.Path}" : $"Open {entry.Path} with its default app");
        AutomationProperties.SetName(button, name);
        button.Click += async (s, e) => await OpenRecentAsync(pane, entry);
        return button;
    }

    private async Task OpenRecentAsync(PaneState pane, RecentPathEntry entry)
    {
        SetActivePane(pane);
        if (entry.IsDirectory)
        {
            await NavigateAsync(pane, entry.Path);
            return;
        }
        var folder = Path.GetDirectoryName(entry.Path);
        if (!string.IsNullOrWhiteSpace(folder)) await NavigateAsync(pane, folder);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = entry.Path, UseShellExecute = true });
            _ = _recentsStore.RecordAsync(entry.Path, isDirectory: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Could not open file · " + ex.Message, StatusSeverity.Error);
        }
    }

    private void ApplyFilter(PaneState pane)
    {
        if (pane.IsHome) return;
        var query = pane.FilterBox.Text.Trim();
        var matches = DirectoryReader.Filter(pane.AllItems, query, pane.SortField, pane.Descending, includeFullPath: false);

        pane.Items.Clear();
        foreach (var item in matches)
            pane.Items.Add(item);

        SetPaneStatus(pane, string.IsNullOrEmpty(query)
            ? $"{pane.AllItems.Count:N0} items"
            : $"{matches.Count:N0} of {pane.AllItems.Count:N0} items · filtered");
    }

    private void LeftSort_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSort(_left, LeftSort, LeftSortDescending);
    private void RightSort_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSort(_right, RightSort, RightSortDescending);
    private void LeftSortDirection_Changed(object sender, RoutedEventArgs e) => UpdateSort(_left, LeftSort, LeftSortDescending);
    private void RightSortDirection_Changed(object sender, RoutedEventArgs e) => UpdateSort(_right, RightSort, RightSortDescending);

    private void UpdateSort(PaneState? pane, ComboBox sort, ToggleButton direction)
    {
        if (pane is null || sort is null || direction is null) return;
        pane.SortField = (DirectorySortField)Math.Clamp(sort.SelectedIndex, 0, 3);
        pane.Descending = direction.IsChecked == true;
        ApplyFilter(pane);
    }

    private void LeftFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(_left);
    private void RightFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(_right);

    private async Task NavigateUpAsync(PaneState pane)
    {
        if (pane.IsHome || string.IsNullOrWhiteSpace(pane.CurrentPath)) return;
        var parent = Directory.GetParent(pane.CurrentPath)?.FullName;
        if (parent is not null) await NavigateAsync(pane, parent);
    }
    private async Task NavigateBackAsync(PaneState pane)
    {
        if (pane.BackHistory.Count == 0) return;
        var target = pane.BackHistory.Peek();
        var previous = pane.CurrentPath;
        if (await NavigateAsync(pane, target, addHistory: false))
        {
            pane.BackHistory.Pop();
            if (!string.IsNullOrWhiteSpace(previous)) pane.ForwardHistory.Push(previous);
        }
    }
    private async Task NavigateForwardAsync(PaneState pane)
    {
        if (pane.ForwardHistory.Count == 0) return;
        var target = pane.ForwardHistory.Peek();
        var previous = pane.CurrentPath;
        if (await NavigateAsync(pane, target, addHistory: false))
        {
            pane.ForwardHistory.Pop();
            if (!string.IsNullOrWhiteSpace(previous)) pane.BackHistory.Push(previous);
        }
    }

    private bool _zipInspectionOpen;

    private async void InspectZip_Click(object sender, RoutedEventArgs e)
    {
        if (_zipInspectionOpen || _zipCreationBusy || _zipExtractionBusy || _previewDialogOpen) return;
        if (_activePane.List.SelectedItem is not FileEntry selected || selected.IsDirectory ||
            Meridian.Core.Archives.ArchiveCapabilities.ForPath(selected.FullPath)?.CanList != true)
        {
            SetStatus("Select a supported archive to inspect.", StatusSeverity.Warning);
            return;
        }
        _zipInspectionOpen = true;
        try
        {
            var isZip = string.Equals(Path.GetExtension(selected.FullPath), ".zip", StringComparison.OrdinalIgnoreCase);
            var entries = await Task.Run(() => isZip
                ? Meridian.Core.Archives.ZipArchiveCatalog.List(selected.FullPath)
                    .Select(item => (item.Path, Bytes: item.UncompressedBytes, item.IsDirectory)).ToArray()
                : Meridian.Core.Archives.TarArchiveCatalog.List(selected.FullPath)
                    .Select(item => (item.Path, item.Bytes, item.IsDirectory)).ToArray());
            var totalBytes = entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Bytes);
            var fileCount = entries.Count(entry => !entry.IsDirectory);
            var folderCount = entries.Length - fileCount;
            var listing = string.Join(Environment.NewLine,
                entries.Select(entry => (entry.IsDirectory ? "[DIR] " : "[FILE] ") +
                    entry.Path + " (" + entry.Bytes.ToString("N0") + " bytes)"));
            await new ContentDialog
            {
                Title = "Archive contents · " + selected.Name + " · " + entries.Length.ToString("N0") +
                    " entries (" + fileCount.ToString("N0") + " files, " +
                    folderCount.ToString("N0") + " folders) · " + totalBytes.ToString("N0") + " bytes",
                Content = new TextBox { Text = listing, IsReadOnly = true,
                    AcceptsReturn = true, MinWidth = 540, Height = 380 },
                CloseButtonText = "Close",
                XamlRoot = LeftPath.XamlRoot
            }.ShowAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("Archive inspection failed · " + ex.Message, StatusSeverity.Error);
        }
        finally { _zipInspectionOpen = false; }
    }

    private bool _zipExtractionBusy;
    private CancellationTokenSource? _zipExtractionCancellation;

    private async void ExtractZip_Click(object sender, RoutedEventArgs e)
    {
        if (_zipExtractionBusy || _zipCreationBusy || _zipInspectionOpen || _previewDialogOpen) return;
        if (_activePane.List.SelectedItem is not FileEntry selected || selected.IsDirectory ||
            Meridian.Core.Archives.ArchiveCapabilities.ForPath(selected.FullPath)?.CanExtract != true)
        {
            SetStatus("Select a supported archive to extract.", StatusSeverity.Warning);
            return;
        }
        _zipExtractionBusy = true;
        var extractionPane = _activePane;
        try
        {
        var archivePath = selected.FullPath;
        var parent = extractionPane.CurrentPath;
        var folder = Path.GetFileNameWithoutExtension(archivePath);
        if (folder.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            folder = folder[..^4];
        var isZip = string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase);
        var entryCount = await Task.Run(() => isZip
            ? Meridian.Core.Archives.ZipArchiveCatalog.List(archivePath).Count
            : Meridian.Core.Archives.TarArchiveCatalog.List(archivePath).Count);
        var dialog = new ContentDialog
        {
            Title = "Extract archive?",
            Content = new TextBlock { Text = entryCount + " entries to " + Path.Combine(parent, folder) },
            PrimaryButtonText = "Extract",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = LeftPath.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _zipExtractionCancellation = new CancellationTokenSource();
        SetStatus("Extracting archive · " + selected.Name);
        var token = _zipExtractionCancellation.Token;
        var progress = new Progress<int>(percent =>
            SetStatus("Extracting archive · " + percent + "%"));
        var extractedFiles = await Task.Run(async () => isZip
            ? (await Meridian.Core.Archives.ZipTreeExtractor.ExtractAsync(
                archivePath, parent, folder, cancellationToken: token, progress: progress)).FileCount
            : (await Meridian.Core.Archives.TarTreeExtractor.ExtractAsync(
                archivePath, parent, folder, cancellationToken: token, progress: progress)).FileCount, token);
        SetStatus("Extracted " + extractedFiles + " files.", StatusSeverity.Success);
        if (string.Equals(extractionPane.CurrentPath, parent, StringComparison.OrdinalIgnoreCase))
            await NavigateAsync(extractionPane, parent, addHistory: false);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Archive extraction cancelled.", StatusSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus("Archive extraction failed · " + ex.Message, StatusSeverity.Error);
        }
        finally
        {
            _zipExtractionCancellation?.Dispose();
            _zipExtractionCancellation = null;
            _zipExtractionBusy = false;
        }
    }

    private void CancelZipExtraction_Click(object sender, RoutedEventArgs e)
    {
        _zipExtractionCancellation?.Cancel();
        if (_zipExtractionCancellation is not null)
            SetStatus("Cancelling ZIP extraction…");
    }

    private bool _zipCreationBusy;
    private CancellationTokenSource? _zipCreationCancellation;

    private async void CreateZip_Click(object sender, RoutedEventArgs e)
    {
        if (_zipCreationBusy || _zipExtractionBusy || _zipInspectionOpen || _previewDialogOpen) return;
        if (_activePane.List.SelectedItem is not FileEntry selected)
        {
            SetStatus("Select files or folders to archive.", StatusSeverity.Warning);
            return;
        }
        var sources = _activePane.List.SelectedItems.OfType<FileEntry>().ToArray();
        if (sources.Length == 0)
        {
            SetStatus("Select at least one file or folder to archive.", StatusSeverity.Warning);
            return;
        }
        var creationPane = _activePane;
        var creationParent = creationPane.CurrentPath;
        var destination = Path.Combine(creationParent,
            (sources.Length == 1 ? Path.GetFileNameWithoutExtension(selected.Name) : "Selection") + ".zip");
        var formatPicker = new ComboBox { SelectedIndex = 0, MinWidth = 260 };
        formatPicker.Items.Add("ZIP (.zip)");
        formatPicker.Items.Add("TAR (.tar)");
        formatPicker.Items.Add("TAR.GZ (.tar.gz)");
        var confirmation = new ContentDialog
        {
            Title = "Create archive?",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Archive format" },
                    formatPicker,
                    new TextBlock { Text = "A new archive will be created in the current folder. Existing files are never overwritten.", TextWrapping = TextWrapping.Wrap }
                }
            },
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = LeftPath.XamlRoot
        };
        _zipCreationBusy = true;
        ContentDialogResult confirmationResult;
        try
        {
            confirmationResult = await confirmation.ShowAsync();
        }
        catch (Exception ex)
        {
            _zipCreationBusy = false;
            SetStatus("Archive dialog failed · " + ex.Message, StatusSeverity.Error);
            return;
        }
        if (confirmationResult != ContentDialogResult.Primary)
        {
            _zipCreationBusy = false;
            return;
        }
        var extension = formatPicker.SelectedIndex switch
        {
            1 => ".tar",
            2 => ".tar.gz",
            _ => ".zip"
        };
        destination = Path.ChangeExtension(destination, extension);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            SetStatus("Archive destination already exists · " + destination, StatusSeverity.Warning);
            _zipCreationBusy = false;
            return;
        }
        _zipCreationCancellation = new CancellationTokenSource();
        try
        {
            SetStatus("Creating archive · " + selected.Name);
            var token = _zipCreationCancellation.Token;
            var planned = await Task.Run(() => Meridian.Core.Archives.ArchiveSourcePlanner.Plan(
                sources.Select(entry => entry.FullPath), token), token);
            if (extension == ".zip")
                await Task.Run(() => Meridian.Core.Archives.ZipArchiveCreator.CreateAsync(
                    destination, planned, cancellationToken: token), token);
            else
                await Task.Run(() => Meridian.Core.Archives.TarArchiveCreator.CreateAsync(
                    destination, planned, cancellationToken: token), token);
            if (string.Equals(creationPane.CurrentPath, creationParent, StringComparison.OrdinalIgnoreCase))
                await NavigateAsync(creationPane, creationParent, addHistory: false);
            SetStatus("Archive created · " + destination, StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Archive creation cancelled.", StatusSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("Archive creation failed · " + ex.Message, StatusSeverity.Error);
        }
        finally
        {
            _zipCreationCancellation?.Dispose();
            _zipCreationCancellation = null;
            _zipCreationBusy = false;
        }
    }

    private void CancelZipCreation_Click(object sender, RoutedEventArgs e)
    {
        _zipCreationCancellation?.Cancel();
        if (_zipCreationCancellation is not null)
            SetStatus("Cancelling ZIP creation…");
    }

    private bool _previewDialogOpen;

    /// <summary>
    /// Owns the media objects one preview surface created, so closing or
    /// replacing the preview always pauses and releases them.
    /// </summary>
    private sealed class PreviewMediaLease : IDisposable
    {
        public Microsoft.UI.Xaml.Controls.MediaPlayerElement? Player { get; set; }
        public Windows.Media.Playback.MediaPlayer? Session { get; set; }

        public void Dispose()
        {
            if (Player is not null)
            {
                Session?.Pause();
                Player.SetMediaPlayer(null);
                Player = null;
            }

            Session?.Dispose();
            Session = null;
        }
    }

    private async void PreviewSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_previewDialogOpen) return;
        if (_activePane.List.SelectedItem is not FileEntry selected || selected.IsDirectory)
        {
            SetStatus("Select a file to preview.", StatusSeverity.Warning);
            return;
        }

        var panel = new StackPanel { Spacing = 8, MinWidth = 540 };
        var lease = new PreviewMediaLease();
        _previewDialogOpen = true;
        try
        {
            await PopulatePreviewPanelAsync(selected, panel, lease);
            await new ContentDialog
            {
                Title = "Preview · " + selected.Name,
                Content = panel,
                CloseButtonText = "Close",
                XamlRoot = LeftPath.XamlRoot
            }.ShowAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or System.Security.SecurityException)
        {
            SetStatus("Preview unavailable · " + ex.Message, StatusSeverity.Error);
        }
        finally
        {
            _previewDialogOpen = false;
            lease.Dispose();
        }
    }

    /// <summary>
    /// Fills a panel with the read-only preview for one file. Shared by the
    /// Preview dialog and the Space quick preview so both render a file the
    /// same way; surfaces the same failures the dialog has always reported.
    /// </summary>
    private async Task PopulatePreviewPanelAsync(FileEntry selected, StackPanel panel, PreviewMediaLease lease)
    {
        var decision = Meridian.Core.Viewer.ViewerRouter.Classify(selected.FullPath);
        panel.Children.Add(new TextBlock
        {
            Text = decision.Reason,
            TextWrapping = TextWrapping.Wrap
        });
        var path = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(selected.FullPath);
        if (decision.Kind is Meridian.Core.Viewer.ViewerKind.Video or Meridian.Core.Viewer.ViewerKind.Audio)
        {
            var player = new Microsoft.UI.Xaml.Controls.MediaPlayerElement
            {
                AreTransportControlsEnabled = true,
                AutoPlay = false,
                Height = decision.Kind == Meridian.Core.Viewer.ViewerKind.Video ? 320 : 100
            };
            var mediaSession = new Windows.Media.Playback.MediaPlayer();
            player.SetMediaPlayer(mediaSession);
            lease.Player = player;
            lease.Session = mediaSession;
            var mediaFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            mediaSession.Source = Windows.Media.Core.MediaSource.CreateFromStorageFile(mediaFile);
            var playbackStatus = new TextBlock
            {
                Text = "Ready · press Play to start",
                TextWrapping = TextWrapping.Wrap
            };
            mediaSession.MediaFailed += (_, args) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                    playbackStatus.Text = "Playback failed · the format or codec may be unsupported. " +
                        args.ErrorMessage);
            };
            panel.Children.Add(player);
            panel.Children.Add(playbackStatus);
        }
        else if (decision.Kind == Meridian.Core.Viewer.ViewerKind.Image)
        {
            const long maximumImageFileBytes = 64L * 1024 * 1024;
            if (new FileInfo(path).Length > maximumImageFileBytes)
                throw new IOException("Image exceeds the 64 MiB preview file limit. Open it in a dedicated image editor.");
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bitmap.SetSourceAsync(stream);
            panel.Children.Add(new Microsoft.UI.Xaml.Controls.Image
            {
                Source = bitmap,
                MaxHeight = 440,
                MaxWidth = 780,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
            });
        }
        else if (decision.Kind is Meridian.Core.Viewer.ViewerKind.Text or Meridian.Core.Viewer.ViewerKind.Hex)
        {
            var bytes = await Meridian.Core.Viewer.SafeFilePreview.ReadPrefixAsync(path,
                maxBytes: decision.Kind == Meridian.Core.Viewer.ViewerKind.Hex ? 4096 : 65536);
            string display;
            if (decision.Kind == Meridian.Core.Viewer.ViewerKind.Hex)
            {
                display = Meridian.Core.Viewer.SafeFilePreview.HexDump(bytes);
            }
            else
            {
                if (Meridian.Core.Viewer.PreviewTextDecoder.TryDecode(bytes, out var decoded))
                    display = decoded;
                else
                    display = "Text encoding is unsupported or binary content detected. Showing the first 4 KiB as hexadecimal.\n\n" +
                        Meridian.Core.Viewer.SafeFilePreview.HexDump(bytes.AsSpan(0, Math.Min(bytes.Length, 4096)));
            }
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBox
                {
                    Text = display,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap,
                    MinHeight = 220
                }
            });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "This format does not yet have an integrated renderer. No external application will be launched.",
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private async Task OpenSelectedAsync(PaneState pane)
    {
        if (pane.List.SelectedItem is not FileEntry selected) return;
        if (selected.IsDirectory) { await NavigateAsync(pane, selected.FullPath); return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = selected.FullPath, UseShellExecute = true });
            _ = _recentsStore.RecordAsync(selected.FullPath, isDirectory: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { SetStatus("Could not open file · " + ex.Message, StatusSeverity.Error); }
    }
    private async Task MoveSelectedAsync(PaneState source, PaneState destination)
    {
        var selection = GetSelectedEntries(source);
        if (selection.Count == 0)
        {
            SetStatus($"Select an item in the {source.Name.ToLowerInvariant()} pane first.", StatusSeverity.Warning);
            return;
        }
        if (destination.IsHome)
        {
            SetStatus("Open a folder in the other pane first — Home is not a move destination.", StatusSeverity.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(destination.CurrentPath)) return;

        var confirmedPaths = selection.Select(item => item.FullPath).ToArray();
        var confirmedSource = source.CurrentPath;
        var confirmedDestination = destination.CurrentPath;
        ContentDialog dialog;
        if (selection.Count == 1)
        {
            var selected = selection[0];
            var kind = selected.IsDirectory ? "folder" : "file";
            dialog = new ContentDialog
            {
                Title = "Move item?",
                Content = $"Move this {kind} to the other pane's current folder?\n\nFrom: {selected.FullPath}\nTo: {destination.CurrentPath}\n\nExisting items will not be overwritten. Moves between volumes are refused; use Copy to keep the original.\n\nIf you change your mind, you can move it back from the destination folder.",
                PrimaryButtonText = "Move",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }
        else
        {
            dialog = new ContentDialog
            {
                Title = "Move items?",
                Content = $"Move {selection.Count:N0} items to the other pane's current folder?\n\nFrom: {source.CurrentPath}\nTo: {destination.CurrentPath}\n\nExisting items will not be overwritten. Moves between volumes are refused; use Copy to keep the original. If an item cannot be moved, the move stops and items already moved stay moved.\n\nYou can move items back from the destination folder afterwards.",
                PrimaryButtonText = "Move",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var currentPaths = GetSelectedEntries(source).Select(item => item.FullPath).ToArray();
        if (!currentPaths.SequenceEqual(confirmedPaths) ||
            !string.Equals(source.CurrentPath, confirmedSource, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(destination.CurrentPath, confirmedDestination, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Move cancelled · The selection or a pane location changed. Review the item and destination again.", StatusSeverity.Warning);
            return;
        }

        ClearPinnedError();
        var sourcePaths = selection.Select(item => item.FullPath).ToArray();
        var sourceFolder = source.CurrentPath;
        var destinationPath = destination.CurrentPath;
        var movedCount = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var sourcePath in sourcePaths)
                {
                    FileOperations.MoveItem(sourcePath, destinationPath);
                    movedCount++;
                }
            });
            if (string.Equals(source.CurrentPath, sourceFolder, StringComparison.OrdinalIgnoreCase))
                await NavigateAsync(source, sourceFolder, addHistory: false);
            if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase))
                await NavigateAsync(destination, destinationPath, addHistory: false);
            SetStatus(sourcePaths.Length == 1
                ? $"Moved {Path.GetFileName(sourcePaths[0])} to {destinationPath}"
                : $"Moved {movedCount:N0} items to {destinationPath}", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (movedCount > 0)
            {
                if (string.Equals(source.CurrentPath, sourceFolder, StringComparison.OrdinalIgnoreCase))
                    await NavigateAsync(source, sourceFolder, addHistory: false);
                if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase))
                    await NavigateAsync(destination, destinationPath, addHistory: false);
            }
            SetStatus(movedCount > 0
                ? $"Move stopped after {movedCount:N0} of {sourcePaths.Length:N0} items · {ex.Message}"
                : "Move failed · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async Task CopySelectedAsync(PaneState source, PaneState destination)
    {
        if (_copyCancellation is not null) { SetStatus("A copy is already in progress.", StatusSeverity.Warning); return; }
        var selection = GetSelectedEntries(source);
        if (selection.Count == 0) { SetStatus($"Select an item in the {source.Name.ToLowerInvariant()} pane first.", StatusSeverity.Warning); return; }
        if (destination.IsHome) { SetStatus("Open a folder in the other pane first — Home is not a copy destination.", StatusSeverity.Warning); return; }
        if (string.IsNullOrWhiteSpace(destination.CurrentPath)) return;

        var sourcePaths = selection.Select(item => item.FullPath).ToArray();
        var destinationPath = destination.CurrentPath;
        // A folder can never be copied into itself. Single copies check here;
        // batch copies are also refused by the BatchCopy preflight before anything is copied.
        foreach (var sourcePath in sourcePaths)
        {
            if (Directory.Exists(sourcePath) &&
                string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            { SetStatus("The destination is the selected folder. Choose another destination.", StatusSeverity.Warning); return; }
        }

        var cancellation = new CancellationTokenSource();
        _copyCancellation = cancellation;
        CancelCopyButton.IsEnabled = true;
        ClearPinnedError();
        SetStatus(sourcePaths.Length == 1 ? $"Copying {selection[0].Name}…" : $"Copying {sourcePaths.Length:N0} items…");
        if (sourcePaths.Length == 1) ShowOperationProgress($"Copying {selection[0].Name}", null);
        SetPaneStatus(source, "Copy in progress…");
        var acceptingProgress = true;
        try
        {
            if (sourcePaths.Length == 1)
            {
                var progress = new Progress<FileOperations.CopyProgress>(update =>
                {
                    if (acceptingProgress && ReferenceEquals(_copyCancellation, cancellation))
                        ShowOperationProgress($"Copying {selection[0].Name} · {FormatFileSize(update.BytesCopied)} written", null);
                });
                var copiedTo = await Task.Run(() => FileOperations.CopyItem(sourcePaths[0], destinationPath, cancellation.Token, progress), cancellation.Token);
                acceptingProgress = false;
                if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase)) await NavigateAsync(destination, destinationPath, addHistory: false);
                SetPaneStatus(source, "Ready"); SetStatus($"Copied {Path.GetFileName(copiedTo)} to {destinationPath}", StatusSeverity.Success);
            }
            else
            {
                var progress = new Progress<BatchCopy.Progress>(state =>
                {
                    if (_copyCancellation == cancellation)
                        ShowOperationProgress($"Copying {state.Completed:N0}/{state.Total:N0} items · {Path.GetFileName(state.CurrentSource)}",
                            state.Total > 0 ? state.Completed * 100.0 / state.Total : 0);
                });
                var result = await Task.Run(() => BatchCopy.Execute(sourcePaths, destinationPath, cancellation.Token, progress), cancellation.Token);
                if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase)) await NavigateAsync(destination, destinationPath, addHistory: false);
                if (result.Succeeded)
                {
                    SetPaneStatus(source, "Ready");
                    SetStatus($"Copied {result.CompletedTargets.Count:N0} items to {destinationPath}", StatusSeverity.Success);
                }
                else if (result.Cancelled)
                {
                    SetPaneStatus(source, "Copy cancelled");
                    SetStatus($"Copy cancelled · {result.CompletedTargets.Count:N0} of {sourcePaths.Length:N0} items copied. Items already copied stay copied.", StatusSeverity.Warning);
                }
                else
                {
                    SetPaneStatus(source, "Copy failed");
                    SetStatus($"Copy stopped after {result.CompletedTargets.Count:N0} of {sourcePaths.Length:N0} items · {result.Failure}", StatusSeverity.Error);
                }
            }
        }
        catch (OperationCanceledException)
        {
            SetPaneStatus(source, "Copy cancelled");
            SetStatus("Copy cancelled · Partial staging data was cleaned up where possible.", StatusSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException or InvalidOperationException)
        { SetPaneStatus(source, "Copy failed"); SetStatus("Copy failed · " + ex.Message, StatusSeverity.Error); }
        finally
        {
            acceptingProgress = false;
            if (ReferenceEquals(_copyCancellation, cancellation)) _copyCancellation = null;
            CancelCopyButton.IsEnabled = false;
            HideOperationProgress();
            cancellation.Dispose();
        }
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e) => await CreateNewFolderAsync();

    private async Task CreateNewFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath))
        {
            SetStatus("Choose a folder before creating a new folder.", StatusSeverity.Warning);
            return;
        }

        var pane = _activePane;
        var parentPath = pane.CurrentPath;
        var nameBox = new TextBox { PlaceholderText = "Folder name", MinWidth = 280 };
        AutomationProperties.SetName(nameBox, "Folder name");
        var newFolderContent = new StackPanel { Spacing = 8 };
        newFolderContent.Children.Add(new TextBlock
        {
            Text = $"The new folder is created inside:\n{_activePane.CurrentPath}\n\nNothing else changes. You can rename or delete the folder afterwards.",
            TextWrapping = TextWrapping.Wrap
        });
        newFolderContent.Children.Add(nameBox);
        var dialog = new ContentDialog
        {
            Title = "Create new folder",
            Content = newFolderContent,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar) ||
            Path.IsPathRooted(name))
        {
            SetStatus("Folder name is invalid. Enter a single folder name without path separators.", StatusSeverity.Warning);
            return;
        }

        var target = Path.Combine(_activePane.CurrentPath, name);
        if (File.Exists(target) || Directory.Exists(target))
        {
            SetStatus($"Cannot create folder · '{name}' already exists.", StatusSeverity.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(target);
            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            SetStatus($"Created folder · {name}", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Could not create folder · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void RenameSelected_Click(object sender, RoutedEventArgs e) => await RenameSelectedAsync(_activePane);

    private async Task RenameSelectedAsync(PaneState pane)
    {
        if (pane.List.SelectedItem is not FileEntry selected)
        {
            SetStatus("Select a file or folder to rename.", StatusSeverity.Warning);
            return;
        }

        var parentPath = pane.CurrentPath;
        var nameBox = new TextBox { Text = selected.Name, MinWidth = 280, SelectionStart = 0, SelectionLength = selected.Name.Length };
        AutomationProperties.SetName(nameBox, "New name");
        var renameContent = new StackPanel { Spacing = 8 };
        renameContent.Children.Add(new TextBlock
        {
            Text = "Only the name changes. The item stays in the same folder with its contents unchanged, and you can rename it again at any time.",
            TextWrapping = TextWrapping.Wrap
        });
        renameContent.Children.Add(nameBox);
        var dialog = new ContentDialog
        {
            Title = "Rename item",
            Content = renameContent,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var renamedTo = FileOperations.RenameItem(selected.FullPath, nameBox.Text);
            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            var unchanged = string.Equals(renamedTo, selected.FullPath, StringComparison.Ordinal);
            SetStatus(unchanged
                ? "Name unchanged."
                : $"Renamed · {Path.GetFileName(renamedTo)}",
                unchanged ? StatusSeverity.Info : StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Could not rename item · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void PropertiesSelected_Click(object sender, RoutedEventArgs e) => await PropertiesSelectedAsync(_activePane);

    private async Task PropertiesSelectedAsync(PaneState pane)
    {
        if (pane.List.SelectedItem is not FileEntry selected)
        {
            SetStatus("Select a file or folder to view its properties.", StatusSeverity.Warning);
            return;
        }

        try
        {
            FileSystemInfo info = selected.IsDirectory
                ? new DirectoryInfo(selected.FullPath)
                : new FileInfo(selected.FullPath);
            info.Refresh();
            if (!info.Exists)
                throw new FileNotFoundException("The item no longer exists.", selected.FullPath);

            var size = selected.IsDirectory
                ? "Not calculated for folders"
                : FormatFileSize(((FileInfo)info).Length);
            var details = new StackPanel { Spacing = 8, MaxWidth = 520 };
            AddPropertyRow(details, "Name", string.IsNullOrWhiteSpace(info.Name) ? info.FullName : info.Name);
            var extension = Path.GetExtension(info.Name);
            AddPropertyRow(details, "Type", selected.IsDirectory ? "Folder" : (string.IsNullOrWhiteSpace(extension) ? "File (no extension)" : extension + " file"));
            AddPropertyRow(details, "Location", info.FullName);
            AddPropertyRow(details, "Size", size);
            AddPropertyRow(details, "Created", info.CreationTime.ToString("g"));
            AddPropertyRow(details, "Modified", info.LastWriteTime.ToString("g"));
            AddPropertyRow(details, "Last accessed", info.LastAccessTime.ToString("g"));
            AddPropertyRow(details, "Attributes", info.Attributes.ToString());

            var dialog = new ContentDialog
            {
                Title = "Properties · " + info.Name,
                Content = new ScrollViewer { Content = details, MaxHeight = 440, MaxWidth = 560 },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Could not read properties · " + ex.Message, StatusSeverity.Error);
        }
    }

    private void AddPropertyRow(StackPanel panel, string label, string value)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = ThemeBrush("MeridianDialogLabelBrush") });
        row.Children.Add(new TextBlock { Text = value, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        panel.Children.Add(row);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]} ({bytes:N0} bytes)";
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_activePane);

    private async Task DeleteSelectedAsync(PaneState pane)
    {
        var selection = GetSelectedEntries(pane);
        if (selection.Count == 0)
        {
            SetStatus("Select a file or folder to delete.", StatusSeverity.Warning);
            return;
        }

        var confirmedPaths = selection.Select(item => item.FullPath).ToArray();
        var confirmedPath = pane.CurrentPath;
        ContentDialog dialog;
        if (selection.Count == 1)
        {
            var selected = selection[0];
            var kind = selected.IsDirectory ? "folder" : "file";
            dialog = new ContentDialog
            {
                Title = "Move to Recycle Bin?",
                Content = $"Move this {kind} to the Recycle Bin?\n\n{selected.FullPath}\n\nYou can restore it from the Recycle Bin unless Windows prevents doing so.",
                PrimaryButtonText = "Move to Recycle Bin",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }
        else
        {
            dialog = new ContentDialog
            {
                Title = "Move to Recycle Bin?",
                Content = $"Move {selection.Count:N0} items to the Recycle Bin?\n\nFrom: {pane.CurrentPath}\n\nYou can restore them from the Recycle Bin unless Windows prevents doing so. If an item cannot be moved, the delete stops and items already moved stay in the Recycle Bin.",
                PrimaryButtonText = "Move to Recycle Bin",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var currentPaths = GetSelectedEntries(pane).Select(item => item.FullPath).ToArray();
        if (!currentPaths.SequenceEqual(confirmedPaths) ||
            !string.Equals(pane.CurrentPath, confirmedPath, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Recycle Bin request cancelled · The selection or pane location changed. Review the item again.", StatusSeverity.Warning);
            return;
        }

        ClearPinnedError();
        var deletedCount = 0;
        try
        {
            foreach (var item in selection)
            {
                if (item.IsDirectory)
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                deletedCount++;
            }

            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            SetStatus(selection.Count == 1
                ? $"Moved to Recycle Bin · {selection[0].Name}"
                : $"Moved {deletedCount:N0} items to Recycle Bin", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            if (deletedCount > 0) await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            SetStatus(deletedCount > 0
                ? $"Delete stopped after {deletedCount:N0} of {selection.Count:N0} items · {ex.Message}"
                : "Could not move item to Recycle Bin · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void FolderDna_Click(object sender, RoutedEventArgs e)
    {
        var pane = _activePane;
        if (string.IsNullOrWhiteSpace(pane.CurrentPath))
        {
            SetStatus("Choose a folder to profile.", StatusSeverity.Warning);
            return;
        }

        // Analyze the complete cached listing, not the filtered visible subset.
        // No filesystem access, recursive traversal, or hashing is performed here.
        var snapshot = FolderDna.Analyze(pane.AllItems);
        var types = snapshot.Types.Take(8).Select(group =>
            $"{group.Label}: {group.Count:N0} files · {DirectoryReader.FormatSize(group.Bytes)}");
        var oldest = snapshot.OldestModifiedUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "Unknown";
        var newest = snapshot.NewestModifiedUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "Unknown";
        var details = $"Location: {pane.CurrentPath}\n\n" +
            $"Folders: {snapshot.Folders:N0}\n" +
            $"Files: {snapshot.Files:N0}\n" +
            $"Known file size: {DirectoryReader.FormatSize(snapshot.KnownFileBytes)}\n" +
            $"Oldest file modified: {oldest}\n" +
            $"Newest file modified: {newest}\n\n" +
            "Largest file-type groups:\n" +
            (snapshot.Types.Count == 0 ? "No files" : string.Join("\n", types)) +
            (snapshot.Types.Count > 8 ? $"\n…and {snapshot.Types.Count - 8:N0} more types" : "") +
            "\n\nOnly immediate files are included. Folder sizes and nested contents are not scanned.";
        var dialog = new ContentDialog
        {
            Title = "Folder DNA · Current folder",
            Content = new ScrollViewer
            {
                Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap },
                MaxHeight = 460
            },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void AddToShelf_Click(object sender, RoutedEventArgs e) => AddSelectedToShelf(_activePane);

    private void AddSelectedToShelf(PaneState pane)
    {
        var selection = GetSelectedEntries(pane);
        if (selection.Count == 0)
        {
            SetStatus("Select a file or folder to add to the Drop Shelf.", StatusSeverity.Warning);
            return;
        }
        var added = 0;
        foreach (var item in selection)
        {
            if (_dropShelf.Add(item.FullPath))
            {
                _shelfPaths.Add(Path.GetFullPath(item.FullPath));
                added++;
            }
        }
        var shelfAdded = selection.Count == 1 ? added == 1 : added > 0;
        SetStatus(selection.Count == 1
            ? (added == 1 ? $"Added to Drop Shelf · {_shelfPaths.Count:N0} collected" : "This item is already on the Drop Shelf.")
            : added > 0
                ? $"Added {added:N0} items to Drop Shelf · {_shelfPaths.Count:N0} collected"
                : "Those items are already on the Drop Shelf.",
            shelfAdded ? StatusSeverity.Success : StatusSeverity.Warning);
    }

    private void RemoveFromShelf_Click(object sender, RoutedEventArgs e)
    {
        if (ShelfItemsBox.SelectedItem is not string selected)
        {
            SetStatus("Select a Drop Shelf item to remove.", StatusSeverity.Warning);
            return;
        }
        _dropShelf.Remove(selected);
        _shelfPaths.Remove(selected);
        SetStatus($"Removed from Drop Shelf · {_shelfPaths.Count:N0} remaining", StatusSeverity.Success);
    }

    private async void PreviewShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_dropShelf.Paths.Count == 0)
        {
            SetStatus("Add items to the Drop Shelf before previewing a batch.", StatusSeverity.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath))
        {
            SetStatus("Open the intended destination folder in the active pane.", StatusSeverity.Warning);
            return;
        }

        try
        {
            var sources = _dropShelf.Paths.ToArray();
            var destination = _activePane.CurrentPath;
            var plan = await Task.Run(() => OperationPreview.PreviewCopy(sources, destination));
            var impact = ConsequenceMap.Explain(plan);
            var risks = impact.Risks.Count == 0
                ? "No known blockers identified at preview time."
                : string.Join("\n", impact.Risks.Select(risk => $"• {risk.Count:N0} {risk.Status}: {risk.Explanation}"));
            var lines = impact.DisplayedItems.Select(item =>
                $"[{item.Result}] {Path.GetFileName(item.Source)}\n  {item.Explanation}\n  → {item.Target ?? "(unavailable)"}");
            var summary = $"Destination: {plan.DestinationDirectory}\n" +
                $"Collected: {impact.TotalItems:N0} · Ready: {impact.ReadyItems:N0} · " +
                $"Blocked: {impact.BlockedItems:N0}\n\nRisk summary:\n{risks}\n\n" +
                string.Join("\n\n", lines) +
                (impact.OmittedItems > 0 ? $"\n\n…and {impact.OmittedItems:N0} more items not shown." : "") +
                "\n\nREAD-ONLY PREVIEW: No files were copied, moved, or deleted. " +
                "A preview cannot reserve paths or guarantee a later operation will succeed.";
            var dialog = new ContentDialog
            {
                Title = "Operation Simulator · Drop Shelf batch",
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap },
                    MaxHeight = 480
                },
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Could not preview shelf · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void CopyShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCancellation is not null || _batchPreparing) return;
        if (_dropShelf.Paths.Count == 0)
        {
            SetStatus("Add files to the Drop Shelf first.", StatusSeverity.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath))
        {
            SetStatus("Open the destination folder in the active pane.", StatusSeverity.Warning);
            return;
        }
        var sources = _dropShelf.Paths.ToArray();
        var destination = _activePane.CurrentPath;
        var destinationPane = _activePane;
        using var cancellation = new CancellationTokenSource();
        _batchCancellation = cancellation;
        CancelShelfButton.IsEnabled = true;
        _batchPreparing = true;
        try
        {
            var plan = await Task.Run(() => OperationPreview.PreviewCopy(sources, destination, cancellation.Token), cancellation.Token);
            if (plan.HasBlockers)
            {
                SetStatus($"Copy refused · {plan.Items.Count - plan.ReadyCount:N0} blocked. Use Preview shelf to inspect.", StatusSeverity.Error);
                return;
            }
            var confirmation = new ContentDialog
            {
                Title = "Copy Drop Shelf items?",
                Content = new TextBlock
                {
                    Text = $"Copy {plan.Items.Count:N0} collected items to:\n{plan.DestinationDirectory}\n\n" +
                           "Files will be copied, not moved. Existing destinations will never be overwritten. " +
                           "A later error or cancellation can leave earlier items copied; there is no automatic rollback. " +
                           "The destination is rechecked during execution.",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Copy items",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            cancellation.Token.ThrowIfCancellationRequested();
            ClearPinnedError();
            SetStatus("Copying Drop Shelf items…");
                var progress = new Progress<BatchCopy.Progress>(state =>
                {
                    if (_batchCancellation == cancellation)
                        ShowOperationProgress($"Batch copy · {state.Completed:N0}/{state.Total:N0} items · {Path.GetFileName(state.CurrentSource)}",
                            state.Total > 0 ? state.Completed * 100.0 / state.Total : 0);
                });
                var result = await Task.Run(() => BatchCopy.Execute(sources, destination, cancellation.Token, progress));
                if (string.Equals(destinationPane.CurrentPath, destination, StringComparison.OrdinalIgnoreCase))
                    await NavigateAsync(destinationPane, destination, addHistory: false);
                if (result.Succeeded)
                    SetStatus($"Copied {result.CompletedTargets.Count:N0} items to {destination}", StatusSeverity.Success);
                else if (result.Cancelled)
                    SetStatus($"Batch copy cancelled · {result.CompletedTargets.Count:N0} of {sources.Length:N0} items copied. Items already copied stay copied.", StatusSeverity.Warning);
                else
                    SetStatus($"Batch stopped after {result.CompletedTargets.Count:N0} of {sources.Length:N0} items · {result.Failure}", StatusSeverity.Error);
                if (!result.Succeeded)
                {
                    var details = new ContentDialog
                    {
                        Title = result.Cancelled ? "Batch copy cancelled" : "Batch copy stopped",
                        Content = new ScrollViewer
                        {
                            MaxHeight = 360,
                            Content = new TextBlock
                            {
                                Text = $"Completed: {result.CompletedTargets.Count:N0} of {sources.Length:N0} items\n" +
                                       $"Destination: {destination}\n" +
                                       $"Stopped at: {result.FailedSource ?? "(unknown)"}\n" +
                                       $"Reason: {(result.Cancelled ? "Cancelled by request" : result.Failure ?? "Unknown failure")}\n\n" +
                                       "Earlier successful copies remain in the destination. Original files were not moved. " +
                                       "There is no automatic rollback.\n\nCompleted targets:\n" +
                                       string.Join("\n", result.CompletedTargets.Take(100)) +
                                       (result.CompletedTargets.Count > 100 ? $"\n…and {result.CompletedTargets.Count - 100:N0} more" : ""),
                                TextWrapping = TextWrapping.Wrap,
                                IsTextSelectionEnabled = true
                            }
                        },
                        CloseButtonText = "Close",
                        XamlRoot = Content.XamlRoot
                    };
                    await details.ShowAsync();
                }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Batch preparation cancelled · No copy started.", StatusSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Batch copy did not start · " + ex.Message, StatusSeverity.Error);
        }
        finally
        {
            _batchCancellation = null;
            CancelShelfButton.IsEnabled = false;
            HideOperationProgress();
            _batchPreparing = false;
        }
    }

    private async void SaveCapsule_Click(object sender, RoutedEventArgs e)
    {
        if (_capsuleSaving) return;
        _capsuleSaving = true;
        try { await SaveCapsuleAsync(); }
        catch (InvalidOperationException ex)
        {
            SetStatus("Could not open Capsule save · " + ex.Message, StatusSeverity.Error);
        }
        finally
        {
            _capsuleSaving = false;
        }
    }

    private async Task SaveCapsuleAsync()
    {
        if (_capsuleStorageUnavailable)
        {
            SetStatus("Capsule saving disabled: the existing saved file could not be read. Back it up and repair it before saving.", StatusSeverity.Warning);
            return;
        }
        if (_capsules.Count >= ContextCapsuleFormat.MaximumCapsules)
        {
            SetStatus("Capsule limit reached. No new Capsule was saved; existing Capsules are unchanged.", StatusSeverity.Warning);
            return;
        }
        if (_dropShelf.Paths.Count == 0)
        {
            SetStatus("Add files or folders to the Drop Shelf before saving a Capsule.", StatusSeverity.Warning);
            return;
        }

        var nameBox = new TextBox
        {
            Header = "Capsule name",
            Text = $"Task {_capsules.Count + 1}",
            MaxLength = ContextCapsule.MaximumNameLength
        };
        var capsuleContent = new StackPanel { Spacing = 8 };
        capsuleContent.Children.Add(new TextBlock
        {
            Text = "A Capsule saves the current Drop Shelf paths as references only — file contents are not copied. You can review saved Capsules at any time with View Capsules.",
            TextWrapping = TextWrapping.Wrap
        });
        capsuleContent.Children.Add(nameBox);
        var dialog = new ContentDialog
        {
            Title = "Save a Context Capsule",
            Content = capsuleContent,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var capsule = ContextCapsule.Capture(nameBox.Text, _dropShelf.Paths);
            var updated = _capsules.Concat(new[] { capsule }).ToArray();
            await _capsuleStore.SaveAsync(updated);
            _capsules.Add(capsule);
            var available = capsule.Inspect().Count(item => item.State == ContextCapsule.Presence.Present);
            SetStatus($"Capsule '{capsule.Name}' saved locally · {available}/{capsule.Paths.Count} paths currently available. Paths are references only.", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SetStatus("Could not save Capsule · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void ViewCapsules_Click(object sender, RoutedEventArgs e)
    {
        var summary = _capsules.Count == 0
            ? "No Capsules saved. Add items to the Drop Shelf and choose Save Capsule."
            : string.Join("\n\n", _capsules.Select(capsule =>
            {
                var states = capsule.Inspect();
                var available = states.Count(item => item.State == ContextCapsule.Presence.Present);
                var preview = string.Join("\n", states.Take(12).Select(item =>
                    (item.State == ContextCapsule.Presence.Present ? "Available: " : "Unavailable: ") + item.Path));
                var omitted = states.Count > 12 ? $"\n… and {states.Count - 12} more references" : "";
                return $"{capsule.Name} · {available}/{states.Count} available\n{preview}{omitted}";
            }));
        var dialog = new ContentDialog
        {
            Title = "Context Capsules · read-only",
            Content = new ScrollViewer
            {
                MaxHeight = 440,
                MaxWidth = 720,
                Content = new TextBlock
                {
                    Text = summary,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void CancelShelf_Click(object sender, RoutedEventArgs e)
    {
        _batchCancellation?.Cancel();
        SetStatus("Cancelling batch copy…");
    }

    private void PruneShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCancellation is not null || _batchPreparing)
        {
            SetStatus("Wait until the batch copy is finished before pruning the shelf.", StatusSeverity.Warning);
            return;
        }
        var removed = _dropShelf.RemoveMissing();
        _shelfPaths.Clear();
        foreach (var path in _dropShelf.Paths) _shelfPaths.Add(path);
        SetStatus(removed == 0
            ? "Drop Shelf · No missing references found."
            : $"Drop Shelf · Removed {removed:N0} missing references; no files were deleted.",
            removed == 0 ? StatusSeverity.Info : StatusSeverity.Success);
    }

    private void ClearShelf_Click(object sender, RoutedEventArgs e)
    {
        _dropShelf.Clear();
        _shelfPaths.Clear();
        SetStatus("Drop Shelf cleared · No files were changed.", StatusSeverity.Success);
    }

    private async void ShowShelfLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ShelfItemsBox.SelectedItem is not string selected)
        {
            SetStatus("Select a Drop Shelf item first.", StatusSeverity.Warning);
            return;
        }
        if (!File.Exists(selected) && !Directory.Exists(selected))
        {
            SetStatus("Shelf item is missing · Nothing was changed.", StatusSeverity.Warning);
            return;
        }
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(selected));
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            SetStatus("The selected item's parent folder is unavailable.", StatusSeverity.Warning);
            return;
        }
        await NavigateAsync(_activePane, parent);
        var match = _activePane.Items.FirstOrDefault(item =>
            string.Equals(item.FullPath, selected, StringComparison.OrdinalIgnoreCase));
        if (match is not null) _activePane.List.SelectedItem = match;
        SetStatus($"Showing Drop Shelf item · {Path.GetFileName(selected)}");
    }

    private void CancelCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_copyCancellation is null) return;
        _copyCancellation.Cancel();
        SetStatus("Cancelling copy…");
    }

    private async void RefreshShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!string.IsNullOrWhiteSpace(_left.CurrentPath)) await NavigateAsync(_left, _left.CurrentPath, addHistory: false);
        if (!string.IsNullOrWhiteSpace(_right.CurrentPath)) await NavigateAsync(_right, _right.CurrentPath, addHistory: false);
    }

    private void SwitchPaneShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SetActivePane(ReferenceEquals(_activePane, _left) ? _right : _left);
        _activePane.List.Focus(FocusState.Programmatic);
        SetStatus($"Active pane · {_activePane.Name}");
    }

    private void FocusActivePathShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _activePane.PathBox.Focus(FocusState.Programmatic);
        _activePane.PathBox.SelectAll();
    }

    private void FocusActiveFilterShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _activePane.FilterBox.Focus(FocusState.Programmatic);
        _activePane.FilterBox.SelectAll();
    }

    private async void BackShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await NavigateBackAsync(_activePane);
    }

    private async void ForwardShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await NavigateForwardAsync(_activePane);
    }

    private async void UpShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await NavigateUpAsync(_activePane);
    }

    private async void NewFolderShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await CreateNewFolderAsync();
    }

    private async void CopyShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ReferenceEquals(_activePane, _left)) await CopySelectedAsync(_left, _right);
        else await CopySelectedAsync(_right, _left);
    }

    private async void MoveShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ReferenceEquals(_activePane, _left)) await MoveSelectedAsync(_left, _right);
        else await MoveSelectedAsync(_right, _left);
    }

    private void OpenInExplorerShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenActivePaneInExplorer();
    }

    private async void PropertiesShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await PropertiesSelectedAsync(_activePane);
    }

    private async void RenameShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // When a file list has focus, its own F2 handling (Slice 4) renames and
        // this accelerator must not claim the key, or rename would run twice.
        if (IsFocusInPaneList()) return;
        args.Handled = true;
        await RenameSelectedAsync(_activePane);
    }

    private void CancelCopyShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Esc only means "cancel copy" while a copy is actually running; every
        // other Esc press keeps its normal behavior (closing flyouts, etc.).
        if (_copyCancellation is null) return;
        args.Handled = true;
        CancelCopy_Click(this, new RoutedEventArgs());
    }

    private bool IsFocusInPaneList()
    {
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        for (DependencyObject? current = focused as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, LeftItems) || ReferenceEquals(current, RightItems)) return true;
        }
        return false;
    }

    // Theme resources live in the application theme dictionaries; the window's
    // current theme (not the application's startup theme) decides which
    // dictionary a code-side lookup must read.
    private Brush ThemeBrush(string key)
    {
        var themeKey = RootGrid.ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var themeDictionary) &&
            themeDictionary is ResourceDictionary dictionary &&
            dictionary.TryGetValue(key, out var value) &&
            value is Brush brush)
            return brush;
        return (Brush)Application.Current.Resources[key];
    }

    private void ApplyThemePreference()
    {
        RootGrid.RequestedTheme = _themePreference switch
        {
            ThemePreference.Dark => ElementTheme.Dark,
            ThemePreference.Light => ElementTheme.Light,
            // System: follow the Windows app theme, detected from the system
            // background color and refreshed when Windows changes it.
            _ => IsSystemDarkTheme() ? ElementTheme.Dark : ElementTheme.Light
        };
        ApplyAccentBrushes();
        UpdateActivePaneVisuals();
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            var background = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return 0.299 * background.R + 0.587 * background.G + 0.114 * background.B < 128;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return true;
        }
    }

    private void UiSettings_ColorValuesChanged(Windows.UI.ViewManagement.UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_themePreference == ThemePreference.System) ApplyThemePreference();
            else if (_accentMode == AccentMode.FollowWindows) ApplyAccentBrushes();
        });
    }

    private void ApplyAccentBrushes()
    {
        AccentColor? windowsAccent = null;
        if (_accentMode == AccentMode.FollowWindows)
        {
            try
            {
                var live = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                windowsAccent = new AccentColor(live.R, live.G, live.B);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { windowsAccent = null; }
        }
        // Resolve per theme dictionary (the light theme uses the derived
        // variant of preset and custom colours), then mutate the shared brush
        // objects so every ThemeResource consumer and code-side lookup sees
        // the same accent, applied live with no restart.
        foreach (var entry in Application.Current.Resources.ThemeDictionaries)
        {
            if (entry.Value is not ResourceDictionary dictionary) continue;
            var lightTheme = string.Equals(entry.Key?.ToString(), "Light", StringComparison.OrdinalIgnoreCase);
            var resolved = AccentPalette.Resolve(_accentMode, _accentPresetId, _accentCustomColor, lightTheme, windowsAccent);
            var accent = Windows.UI.Color.FromArgb(0xFF, resolved.R, resolved.G, resolved.B);
            SetBrushColor(dictionary, "MeridianAccentBrush", accent);
            SetBrushColor(dictionary, "MeridianActivePaneBorderBrush", accent);
            SetBrushColor(dictionary, "MeridianSelectionBackgroundBrush", Windows.UI.Color.FromArgb(0x33, accent.R, accent.G, accent.B));
        }
    }

    private static void SetBrushColor(ResourceDictionary dictionary, string key, Windows.UI.Color color)
    {
        if (dictionary.TryGetValue(key, out var value) && value is SolidColorBrush brush)
            brush.Color = color;
    }

    private void ApplyDensityPreference()
    {
        var vertical = _densityPreference == DensityPreference.Compact ? 2 : 4;
        Application.Current.Resources["MeridianListItemMargin"] = new Thickness(8, vertical, 8, vertical);
        // Reassigning the item sources forces both lists to rebuild their item
        // containers, so the template picks up the new margin immediately.
        if (LeftItems.ItemsSource is not null) { LeftItems.ItemsSource = null; LeftItems.ItemsSource = _left.Items; }
        if (RightItems.ItemsSource is not null) { RightItems.ItemsSource = null; RightItems.ItemsSource = _right.Items; }
    }

    private void ApplyPaneSplit()
    {
        LeftPaneColumn.Width = new GridLength(_paneSplitFraction, GridUnitType.Star);
        RightPaneColumn.Width = new GridLength(1 - _paneSplitFraction, GridUnitType.Star);
    }

    private void SplitGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _splitDragging = true;
        _splitDragStartFraction = _paneSplitFraction;
        _splitDragStartX = e.GetCurrentPoint(WorkAreaGrid).Position.X;
        (sender as UIElement)?.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void SplitGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_splitDragging) return;
        var width = WorkAreaGrid.ActualWidth;
        if (width <= 0) return;
        var x = e.GetCurrentPoint(WorkAreaGrid).Position.X;
        _paneSplitFraction = Math.Clamp(_splitDragStartFraction + (x - _splitDragStartX) / width, 0.25, 0.75);
        ApplyPaneSplit();
    }

    private void SplitGrip_PointerReleased(object sender, PointerRoutedEventArgs e) => EndSplitDrag();

    private void SplitGrip_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndSplitDrag();

    private void EndSplitDrag()
    {
        if (!_splitDragging) return;
        _splitDragging = false;
        _ = PersistSettingsAsync();
    }

    private void RootElement_Loaded(object sender, RoutedEventArgs e) => EnforceMinimumWindowSize();

    private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange) EnforceMinimumWindowSize();
    }

    private void EnforceMinimumWindowSize()
    {
        // The WinUI presenter exposes no minimum size, so the ≈900×600 floor
        // (in device-independent units) is enforced by clamping: once at
        // startup and again whenever the window is resized below it.
        if (_appWindow is null || _enforcingMinimumWindowSize) return;
        if (Content.XamlRoot is not null) _rasterizationScale = Content.XamlRoot.RasterizationScale;
        var minimumWidth = (int)Math.Round(900 * _rasterizationScale);
        var minimumHeight = (int)Math.Round(600 * _rasterizationScale);
        var size = _appWindow.Size;
        if (size.Width >= minimumWidth && size.Height >= minimumHeight) return;
        _enforcingMinimumWindowSize = true;
        try
        {
            _appWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(size.Width, minimumWidth), Math.Max(size.Height, minimumHeight)));
        }
        finally
        {
            _enforcingMinimumWindowSize = false;
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var themeButtons = new RadioButtons { Header = "Theme", MaxColumns = 3 };
        themeButtons.Items.Add("Dark");
        themeButtons.Items.Add("Light");
        themeButtons.Items.Add("System");
        themeButtons.SelectedIndex = _themePreference switch
        {
            ThemePreference.Dark => 0,
            ThemePreference.Light => 1,
            _ => 2
        };
        AutomationProperties.SetName(themeButtons, "Theme");
        var densityButtons = new RadioButtons { Header = "Density", MaxColumns = 2 };
        densityButtons.Items.Add("Comfortable");
        densityButtons.Items.Add("Compact");
        densityButtons.SelectedIndex = _densityPreference == DensityPreference.Compact ? 1 : 0;
        AutomationProperties.SetName(densityButtons, "Density");
        var accentToggle = new ToggleSwitch
        {
            Header = "Follow Windows accent color",
            IsOn = _accentMode == AccentMode.FollowWindows
        };
        AutomationProperties.SetName(accentToggle, "Follow Windows accent color");
        var accentHeader = new TextBlock { Text = "Accent color" };
        var swatchButtons = new List<(AccentPreset Preset, Button Button)>();
        var accentSwatches = new Grid { RowSpacing = 6, ColumnSpacing = 6 };
        for (var column = 0; column < 4; column++)
            accentSwatches.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // One row per four presets, however many presets the palette ships.
        var swatchRows = (AccentPalette.Presets.Count + 3) / 4;
        for (var row = 0; row < swatchRows; row++)
            accentSwatches.RowDefinitions.Add(new RowDefinition());
        // The custom color controls are created before the swatch loop: the
        // swatch handlers call RefreshAccentControls, which reads them, and
        // every variable it reads must be assigned before those handlers exist.
        var customPreview = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            BorderBrush = ThemeBrush("MeridianBorderBrush"),
            BorderThickness = new Thickness(1)
        };
        var customHexBox = new TextBox
        {
            PlaceholderText = "#RRGGBB",
            MinWidth = 120,
            Text = _accentCustomColor ?? string.Empty
        };
        AutomationProperties.SetName(customHexBox, "Custom accent color, hex value");
        ToolTipService.SetToolTip(customHexBox, "Type a hex color such as #4CC2FF");
        var applyCustomButton = new Button { Content = "Use custom color" };
        ToolTipService.SetToolTip(applyCustomButton, "Apply the hex color as the accent");
        AutomationProperties.SetName(applyCustomButton, "Use custom color");
        var customError = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MeridianErrorBrush"),
            Visibility = Visibility.Collapsed
        };
        var customPanel = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        customPanel.Children.Add(new TextBlock
        {
            Text = "Type a hex color such as #4CC2FF. It applies everywhere immediately. In the light theme it is darkened automatically so text stays readable.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MeridianTextSecondaryBrush")
        });
        var customRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        customRow.Children.Add(customPreview);
        customRow.Children.Add(customHexBox);
        customRow.Children.Add(applyCustomButton);
        customPanel.Children.Add(customRow);
        customPanel.Children.Add(customError);
        var customRevealButton = new Button { Content = "Custom color…" };
        ToolTipService.SetToolTip(customRevealButton, "Enter your own accent color as a hex value");
        AutomationProperties.SetName(customRevealButton, "Custom color");
        customRevealButton.Click += (s, args) =>
            customPanel.Visibility = customPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var presetIndex = 0;
        foreach (var preset in AccentPalette.Presets)
        {
            var swatch = new Button
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, preset.Color.R, preset.Color.G, preset.Color.B))
            };
            ToolTipService.SetToolTip(swatch, $"Accent color: {preset.Name}");
            AutomationProperties.SetName(swatch, $"Accent color: {preset.Name}");
            Grid.SetRow(swatch, presetIndex / 4);
            Grid.SetColumn(swatch, presetIndex % 4);
            presetIndex++;
            swatch.Click += (s, args) =>
            {
                if (_accentMode == AccentMode.Preset &&
                    string.Equals(_accentPresetId, preset.Id, StringComparison.OrdinalIgnoreCase)) return;
                _accentMode = AccentMode.Preset;
                _accentPresetId = preset.Id;
                ApplyAccentBrushes();
                RefreshAccentControls();
                _ = PersistSettingsAsync();
            };
            swatchButtons.Add((preset, swatch));
            accentSwatches.Children.Add(swatch);
        }
        void RefreshAccentControls()
        {
            foreach (var (preset, button) in swatchButtons)
            {
                var selected = _accentMode == AccentMode.Preset &&
                    string.Equals(_accentPresetId, preset.Id, StringComparison.OrdinalIgnoreCase);
                button.BorderBrush = selected ? ThemeBrush("MeridianTextPrimaryBrush") : ThemeBrush("MeridianBorderBrush");
                button.BorderThickness = new Thickness(selected ? 3 : 1);
            }
            if (accentToggle.IsOn != (_accentMode == AccentMode.FollowWindows))
                accentToggle.IsOn = _accentMode == AccentMode.FollowWindows;
            customPreview.Background = AccentColor.TryParse(_accentCustomColor, out var custom)
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, custom.R, custom.G, custom.B))
                : ThemeBrush("MeridianBorderBrush");
        }

        applyCustomButton.Click += (s, args) =>
        {
            if (!AccentColor.TryParse(customHexBox.Text, out var custom))
            {
                customError.Text = "That is not a valid hex color. Use six hex digits, like #4CC2FF.";
                customError.Visibility = Visibility.Visible;
                return;
            }
            customError.Visibility = Visibility.Collapsed;
            _accentMode = AccentMode.Custom;
            _accentCustomColor = custom.ToHex();
            ApplyAccentBrushes();
            RefreshAccentControls();
            _ = PersistSettingsAsync();
        };

        var settingsContent = new StackPanel { Spacing = 16, MinWidth = 340 };
        settingsContent.Children.Add(new TextBlock
        {
            Text = "Changes apply immediately and are saved on this PC. System theme follows the Windows app theme and updates when Windows changes it.",
            TextWrapping = TextWrapping.Wrap
        });
        settingsContent.Children.Add(themeButtons);
        settingsContent.Children.Add(densityButtons);
        settingsContent.Children.Add(accentHeader);
        settingsContent.Children.Add(accentSwatches);
        settingsContent.Children.Add(customRevealButton);
        settingsContent.Children.Add(customPanel);
        settingsContent.Children.Add(accentToggle);
        RefreshAccentControls();

        themeButtons.SelectionChanged += (s, args) =>
        {
            var preference = themeButtons.SelectedIndex switch
            {
                0 => ThemePreference.Dark,
                1 => ThemePreference.Light,
                _ => ThemePreference.System
            };
            if (preference == _themePreference) return;
            _themePreference = preference;
            ApplyThemePreference();
            _ = PersistSettingsAsync();
        };
        densityButtons.SelectionChanged += (s, args) =>
        {
            var preference = densityButtons.SelectedIndex == 1 ? DensityPreference.Compact : DensityPreference.Comfortable;
            if (preference == _densityPreference) return;
            _densityPreference = preference;
            ApplyDensityPreference();
            _ = PersistSettingsAsync();
        };
        accentToggle.Toggled += (s, args) =>
        {
            if (accentToggle.IsOn == (_accentMode == AccentMode.FollowWindows)) return;
            _accentMode = accentToggle.IsOn ? AccentMode.FollowWindows : AccentMode.Preset;
            ApplyAccentBrushes();
            RefreshAccentControls();
            _ = PersistSettingsAsync();
        };

        var dialog = new ContentDialog
        {
            Title = "Settings",
            Content = settingsContent,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = RootGrid.RequestedTheme
        };
        await dialog.ShowAsync();
    }

    private static void SetPaneStatus(PaneState pane, string message) => pane.Status.Text = message;

    // All global status messages go through SetStatus so severity styling and error
    // pinning behave identically everywhere. An error stays visible until the user
    // dismisses it or a new operation starts; routine messages never overwrite it.
    private void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        if (_pinnedError is not null && severity != StatusSeverity.Error) return;
        ApplyStatus(message, severity);
        _pinnedError = severity == StatusSeverity.Error ? message : null;
        DismissStatusButton.Visibility = _pinnedError is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyStatus(string message, StatusSeverity severity)
    {
        var (glyph, brushKey) = severity switch
        {
            StatusSeverity.Success => ("\uE73E", "MeridianSuccessBrush"),
            StatusSeverity.Warning => ("\uE7BA", "MeridianWarningBrush"),
            StatusSeverity.Error => ("\uE783", "MeridianErrorBrush"),
            _ => ("\uE946", "MeridianTextSecondaryBrush"),
        };
        StatusIcon.Glyph = glyph;
        StatusIcon.Foreground = ThemeBrush(brushKey);
        AutomationProperties.SetName(StatusIcon, severity switch
        {
            StatusSeverity.Success => "Status: success",
            StatusSeverity.Warning => "Status: warning",
            StatusSeverity.Error => "Status: error",
            _ => "Status: information"
        });
        GlobalStatus.Text = message;
        GlobalStatus.Foreground = ThemeBrush(
            severity is StatusSeverity.Warning or StatusSeverity.Error ? brushKey : "MeridianTextSecondaryBrush");
    }

    private void ClearPinnedError()
    {
        _pinnedError = null;
        DismissStatusButton.Visibility = Visibility.Collapsed;
    }

    private void DismissStatus_Click(object sender, RoutedEventArgs e)
    {
        ClearPinnedError();
        ApplyStatus("Ready · Local-only · No telemetry", StatusSeverity.Info);
    }

    private void ShowOperationProgress(string label, double? percent)
    {
        OperationProgressPanel.Visibility = Visibility.Visible;
        OperationProgressLabel.Text = label;
        if (percent is { } value)
        {
            OperationProgressBar.IsIndeterminate = false;
            OperationProgressBar.Value = Math.Clamp(value, 0, 100);
        }
        else
        {
            OperationProgressBar.IsIndeterminate = true;
        }
    }

    private void HideOperationProgress()
    {
        OperationProgressPanel.Visibility = Visibility.Collapsed;
        OperationProgressBar.IsIndeterminate = false;
    }

    private static List<FileEntry> GetSelectedEntries(PaneState pane) =>
        pane.List.SelectedItems.OfType<FileEntry>().ToList();

    private void LeftItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionStatus(_left);
        if (ReferenceEquals(_activePane, _left)) RefreshDetailsStrip();
    }

    private void RightItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionStatus(_right);
        if (ReferenceEquals(_activePane, _right)) RefreshDetailsStrip();
    }

    // Mirrors the counts ApplyFilter writes, without rebuilding the item list.
    private void UpdateSelectionStatus(PaneState pane)
    {
        var selected = GetSelectedEntries(pane);
        if (selected.Count > 0)
        {
            // FileEntry.SizeBytes is the file length for files and 0 for folders,
            // so folders count as selected items without adding to the size total.
            long totalBytes = 0;
            var hasFile = false;
            foreach (var entry in selected)
            {
                if (entry.IsDirectory) continue;
                hasFile = true;
                totalBytes += entry.SizeBytes;
            }
            SetPaneStatus(pane, hasFile
                ? $"{selected.Count:N0} selected · {DirectoryReader.FormatSize(totalBytes)}"
                : $"{selected.Count:N0} selected");
            return;
        }
        var query = pane.FilterBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            SetPaneStatus(pane, $"{pane.AllItems.Count:N0} items");
            return;
        }
        var matches = DirectoryReader.Filter(pane.AllItems, query, pane.SortField, pane.Descending, includeFullPath: false);
        SetPaneStatus(pane, $"{matches.Count:N0} of {pane.AllItems.Count:N0} items · filtered");
    }

    private async void LeftItems_KeyDown(object sender, KeyRoutedEventArgs e) => await HandleListKeyDownAsync(_left, e);
    private async void RightItems_KeyDown(object sender, KeyRoutedEventArgs e) => await HandleListKeyDownAsync(_right, e);

    private async Task HandleListKeyDownAsync(PaneState pane, KeyRoutedEventArgs e)
    {
        var ctrlDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (e.Key == VirtualKey.Space)
        {
            var shiftDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            var altDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if (!ctrlDown && !shiftDown && !altDown)
            {
                // Plain Space had no job in the file lists: selection
                // toggling is Ctrl+Space, which is left untouched and still
                // reaches the list. Plain Space opens the quick preview.
                e.Handled = true;
                await OpenQuickPreviewAsync(pane);
                return;
            }
        }

        if (ctrlDown && e.Key == VirtualKey.A)
        {
            e.Handled = true;
            pane.List.SelectAll();
            return;
        }
        switch (e.Key)
        {
            case VirtualKey.Delete:
                e.Handled = true;
                await DeleteSelectedAsync(pane);
                break;
            case VirtualKey.F2:
                e.Handled = true;
                if (pane.List.SelectedItems.Count == 1) await RenameSelectedAsync(pane);
                else if (pane.List.SelectedItems.Count > 1) SetStatus("Select a single item to rename.", StatusSeverity.Warning);
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                await OpenSelectedAsync(pane);
                break;
        }
    }

    private void LeftContextMenu_Opening(object sender, object e) => UpdateContextMenu(_left, isLeftPane: true);
    private void RightContextMenu_Opening(object sender, object e) => UpdateContextMenu(_right, isLeftPane: false);

    private void UpdateContextMenu(PaneState pane, bool isLeftPane)
    {
        var copyItem = isLeftPane ? LeftCopyMenuItem : RightCopyMenuItem;
        var moveItem = isLeftPane ? LeftMoveMenuItem : RightMoveMenuItem;
        var renameItem = isLeftPane ? LeftRenameMenuItem : RightRenameMenuItem;
        var propertiesItem = isLeftPane ? LeftPropertiesMenuItem : RightPropertiesMenuItem;
        var shelfItem = isLeftPane ? LeftShelfMenuItem : RightShelfMenuItem;
        var deleteItem = isLeftPane ? LeftDeleteMenuItem : RightDeleteMenuItem;
        var other = isLeftPane ? _right : _left;
        var count = pane.List.SelectedItems.Count;
        var hasDestination = !string.IsNullOrWhiteSpace(other.CurrentPath) && !other.IsHome;
        var direction = isLeftPane ? "right" : "left";
        copyItem.IsEnabled = count > 0 && hasDestination && _copyCancellation is null;
        moveItem.IsEnabled = count > 0 && hasDestination;
        renameItem.IsEnabled = count == 1;
        propertiesItem.IsEnabled = count == 1;
        shelfItem.IsEnabled = count > 0;
        deleteItem.IsEnabled = count > 0;
        copyItem.Text = count > 1 ? $"Copy {count:N0} items to {direction} pane" : $"Copy to {direction} pane";
        moveItem.Text = count > 1 ? $"Move {count:N0} items to {direction} pane" : $"Move to {direction} pane";
        shelfItem.Text = count > 1 ? $"Add {count:N0} items to Drop Shelf" : "Add to Drop Shelf";
        deleteItem.Text = count > 1 ? $"Delete {count:N0} items (Recycle Bin)" : "Delete (Recycle Bin)";
    }

    private async void RenameFromLeft_Click(object sender, RoutedEventArgs e) => await RenameSelectedAsync(_left);
    private async void RenameFromRight_Click(object sender, RoutedEventArgs e) => await RenameSelectedAsync(_right);
    private async void PropertiesFromLeft_Click(object sender, RoutedEventArgs e) => await PropertiesSelectedAsync(_left);
    private async void PropertiesFromRight_Click(object sender, RoutedEventArgs e) => await PropertiesSelectedAsync(_right);
    private async void DeleteFromLeft_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_left);
    private async void DeleteFromRight_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_right);
    private void AddToShelfFromLeft_Click(object sender, RoutedEventArgs e) => AddSelectedToShelf(_left);
    private void AddToShelfFromRight_Click(object sender, RoutedEventArgs e) => AddSelectedToShelf(_right);
    private void LeftPane_PointerPressed(object sender, PointerRoutedEventArgs e) => SetActivePane(_left);
    private void RightPane_PointerPressed(object sender, PointerRoutedEventArgs e) => SetActivePane(_right);
    private void LeftItems_PointerPressed(object sender, PointerRoutedEventArgs e) => SetActivePane(_left);
    private void RightItems_PointerPressed(object sender, PointerRoutedEventArgs e) => SetActivePane(_right);

    private void SetActivePane(PaneState pane)
    {
        _activePane = pane;
        UpdateActivePaneVisuals();
        RefreshSidebarSelection();
        RefreshDetailsStrip();
    }

    private void UpdateActivePaneVisuals()
    {
        SetPaneActiveVisual(LeftPaneRoot, LeftPaneHeader, ReferenceEquals(_activePane, _left));
        SetPaneActiveVisual(RightPaneRoot, RightPaneHeader, ReferenceEquals(_activePane, _right));
        UpdatePaneHeaderAutomation();
    }

    private void SetPaneActiveVisual(Grid paneRoot, Grid paneHeader, bool isActive)
    {
        paneRoot.BorderBrush = ThemeBrush(isActive ? "MeridianActivePaneBorderBrush" : "MeridianBorderBrush");
        paneHeader.Background = isActive
            ? ThemeBrush("MeridianActivePaneBackgroundBrush")
            : null;
    }

    private void UpdatePaneHeaderAutomation()
    {
        AutomationProperties.SetName(LeftPaneHeader, PaneHeaderAutomationName(_left, LeftFolderName.Text));
        AutomationProperties.SetName(RightPaneHeader, PaneHeaderAutomationName(_right, RightFolderName.Text));
    }

    private string PaneHeaderAutomationName(PaneState pane, string folderName) =>
        $"{pane.Name} pane: {folderName}" + (ReferenceEquals(_activePane, pane) ? " (active pane)" : string.Empty);

    private void UpdatePaneHeader(PaneState pane)
    {
        var nameBlock = ReferenceEquals(pane, _left) ? LeftFolderName : RightFolderName;
        var pathBlock = ReferenceEquals(pane, _left) ? LeftFolderPath : RightFolderPath;
        if (pane.IsHome)
        {
            nameBlock.Text = "Home";
            pathBlock.Text = "Pinned folders, drives and recent items";
            ToolTipService.SetToolTip(pathBlock, null);
            UpdatePaneHeaderAutomation();
            return;
        }
        var path = pane.CurrentPath;
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name)) name = string.IsNullOrWhiteSpace(path) ? pane.Name + " pane" : path;
        nameBlock.Text = name;
        pathBlock.Text = path;
        if (!string.IsNullOrWhiteSpace(path)) ToolTipService.SetToolTip(pathBlock, path);
        UpdatePaneHeaderAutomation();
    }

    private async void CopyActivePane_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        if (ReferenceEquals(_activePane, _left)) await CopySelectedAsync(_left, _right);
        else await CopySelectedAsync(_right, _left);
    }

    private async void MoveActivePane_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        if (ReferenceEquals(_activePane, _left)) await MoveSelectedAsync(_left, _right);
        else await MoveSelectedAsync(_right, _left);
    }

    private void OrganizeSection_Click(object sender, RoutedEventArgs e)
    {
        var target = ReferenceEquals(sender, BookmarksSectionToggle) ? OrganizeSection.Bookmarks : OrganizeSection.Shelf;
        SetOrganizeSection(_openOrganizeSection == target ? OrganizeSection.None : target);
        _ = PersistSettingsAsync();
    }

    private void SetOrganizeSection(OrganizeSection section)
    {
        _openOrganizeSection = section;
        BookmarksSection.Visibility = section == OrganizeSection.Bookmarks ? Visibility.Visible : Visibility.Collapsed;
        ShelfSection.Visibility = section == OrganizeSection.Shelf ? Visibility.Visible : Visibility.Collapsed;
        BookmarksSectionToggle.IsChecked = section == OrganizeSection.Bookmarks;
        ShelfSectionToggle.IsChecked = section == OrganizeSection.Shelf;
    }
    private async void LeftUp_Click(object sender, RoutedEventArgs e) => await NavigateUpAsync(_left);
    private async void RightUp_Click(object sender, RoutedEventArgs e) => await NavigateUpAsync(_right);
    private async void LeftBack_Click(object sender, RoutedEventArgs e) => await NavigateBackAsync(_left);
    private async void RightBack_Click(object sender, RoutedEventArgs e) => await NavigateBackAsync(_right);
    private async void LeftForward_Click(object sender, RoutedEventArgs e) => await NavigateForwardAsync(_left);
    private async void RightForward_Click(object sender, RoutedEventArgs e) => await NavigateForwardAsync(_right);
    private async void CopyLeftToRight_Click(object sender, RoutedEventArgs e) => await CopySelectedAsync(_left, _right);
    private async void MoveLeftToRight_Click(object sender, RoutedEventArgs e) => await MoveSelectedAsync(_left, _right);
    private async void MoveRightToLeft_Click(object sender, RoutedEventArgs e) => await MoveSelectedAsync(_right, _left);
    private async void CopyRightToLeft_Click(object sender, RoutedEventArgs e) => await CopySelectedAsync(_right, _left);
    private async void RefreshBoth_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_left.CurrentPath)) await NavigateAsync(_left, _left.CurrentPath, addHistory: false);
        if (!string.IsNullOrWhiteSpace(_right.CurrentPath)) await NavigateAsync(_right, _right.CurrentPath, addHistory: false);
    }
    private void OpenInExplorer_Click(object sender, RoutedEventArgs e) => OpenActivePaneInExplorer();

    private void OpenActivePaneInExplorer()
    {
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{_activePane.CurrentPath}\"", UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { SetStatus("Could not open Explorer · " + ex.Message, StatusSeverity.Error); }
    }
    private async void LeftItems_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenSelectedAsync(_left);
    private async void RightItems_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenSelectedAsync(_right);
    private async void LeftPath_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_left, LeftPath.Text); } }
    private async void RightPath_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_right, RightPath.Text); } }

    // ----- Details strip -----

    private void DetailsStripMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _detailsStripVisible = DetailsStripMenuItem.IsChecked;
        ApplyDetailsStripVisibility();
        _ = PersistSettingsAsync();
    }

    private void DetailsStripShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _detailsStripVisible = !_detailsStripVisible;
        ApplyDetailsStripVisibility();
        _ = PersistSettingsAsync();
    }

    private void ApplyDetailsStripVisibility()
    {
        DetailsStrip.Visibility = _detailsStripVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailsStripMenuItem.IsChecked = _detailsStripVisible;
        if (_detailsStripVisible)
        {
            RefreshDetailsStrip();
        }
        else
        {
            // Stop any in-flight detail work; a hidden strip shows nothing.
            _detailsStripGeneration++;
            _detailsStripCancellation?.Cancel();
        }
    }

    private void RefreshDetailsStrip()
    {
        if (!_detailsStripVisible) return;
        _ = RefreshDetailsStripAsync();
    }

    private async Task RefreshDetailsStripAsync()
    {
        var generation = ++_detailsStripGeneration;
        _detailsStripCancellation?.Cancel();
        _detailsStripCancellation?.Dispose();
        _detailsStripCancellation = new CancellationTokenSource();
        var token = _detailsStripCancellation.Token;
        try
        {
            var selected = GetSelectedEntries(_activePane);
            if (selected.Count != 1)
            {
                _detailsCurrentEntry = null;
                ShowDetailsStripHint(selected.Count);
                return;
            }

            var entry = selected[0];
            _detailsCurrentEntry = entry;
            FileSystemInfo info = entry.IsDirectory ? new DirectoryInfo(entry.FullPath) : new FileInfo(entry.FullPath);
            info.Refresh();
            if (!info.Exists)
            {
                ShowDetailsStripHint(0, "That item is no longer available.");
                return;
            }

            DetailsName.Text = entry.Name;
            ToolTipService.SetToolTip(DetailsName, entry.FullPath);
            DetailsHintText.Text = string.Empty;
            DetailsHintText.Visibility = Visibility.Collapsed;
            DetailsTypeText.Text = entry.TypeLabel;
            DetailsLocationText.Text = info.FullName;
            DetailsOpensInText.Text = entry.IsDirectory
                ? "—"
                : DefaultAppReader.TryGetFriendlyAppName(entry.FullPath) ?? "No default app found";
            DetailsCreatedText.Text = info.CreationTime.ToString("g");
            DetailsModifiedText.Text = info.LastWriteTime.ToString("g");
            DetailsAccessedText.Text = info.LastAccessTime.ToString("g");
            DetailsDimensionsText.Text = string.Empty;
            DetailsSizeText.Text = entry.IsDirectory ? "Calculating…" : FormatFileSize(((FileInfo)info).Length);
            DetailsExifText.Text = string.Empty;
            DetailsExifText.Visibility = Visibility.Collapsed;
            DetailsThumbnail.Source = null;
            DetailsThumbnail.Visibility = Visibility.Collapsed;
            DetailsThumbnailGlyph.Glyph = entry.Glyph;
            DetailsThumbnailGlyph.Visibility = Visibility.Visible;
            DetailsAccessText.Text = "Reading who can open this…";
            DetailsSecurityButton.IsEnabled = false;

            _ = LoadDetailsThumbnailAsync(entry, generation);
            if (entry.IsDirectory)
                _ = LoadDetailsFolderSizeAsync(entry, generation, token);
            else
                _ = LoadDetailsExifAsync(entry, generation);
            _ = LoadDetailsMetadataAsync(entry, generation);
            _ = LoadDetailsAccessAsync(entry, generation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (generation == _detailsStripGeneration)
                ShowDetailsStripHint(0, "Details could not be read · " + ex.Message);
        }
    }

    private void ShowDetailsStripHint(int selectionCount, string? message = null)
    {
        DetailsName.Text = "Details";
        ToolTipService.SetToolTip(DetailsName, null);
        DetailsHintText.Text = message ?? (selectionCount == 0
            ? "Select an item to see its details here."
            : $"{selectionCount:N0} items are selected. Details are shown for one item at a time.");
        DetailsHintText.Visibility = Visibility.Visible;
        DetailsTypeText.Text = string.Empty;
        DetailsOpensInText.Text = string.Empty;
        DetailsLocationText.Text = string.Empty;
        DetailsSizeText.Text = string.Empty;
        DetailsDimensionsText.Text = string.Empty;
        DetailsCreatedText.Text = string.Empty;
        DetailsModifiedText.Text = string.Empty;
        DetailsAccessedText.Text = string.Empty;
        DetailsExifText.Text = string.Empty;
        DetailsExifText.Visibility = Visibility.Collapsed;
        DetailsThumbnail.Source = null;
        DetailsThumbnail.Visibility = Visibility.Collapsed;
        DetailsThumbnailGlyph.Glyph = "\uE946";
        DetailsThumbnailGlyph.Visibility = Visibility.Visible;
        DetailsAccessText.Text = string.Empty;
        DetailsSecurityButton.IsEnabled = false;
        SetDetailsEditorEnabled(false, "Select one file to see the details you can edit.");
    }

    private async Task LoadDetailsThumbnailAsync(FileEntry entry, int generation)
    {
        Microsoft.UI.Xaml.Media.Imaging.BitmapImage? bitmap = null;
        try
        {
            if (entry.IsDirectory)
            {
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(entry.FullPath);
                using var thumbnail = await folder.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 120);
                if (thumbnail is not null)
                {
                    bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                }
            }
            else
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(entry.FullPath);
                using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 120);
                if (thumbnail is not null)
                {
                    bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            bitmap = null;
        }

        if (generation != _detailsStripGeneration || bitmap is null) return;
        DetailsThumbnail.Source = bitmap;
        DetailsThumbnail.Visibility = Visibility.Visible;
        DetailsThumbnailGlyph.Visibility = Visibility.Collapsed;
    }

    private async Task LoadDetailsExifAsync(FileEntry entry, int generation)
    {
        var extension = Path.GetExtension(entry.FullPath);
        if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase)) return;

        Meridian.Core.Viewer.ExifSummary? summary = null;
        try
        {
            var bytes = await Meridian.Core.Viewer.SafeFilePreview.ReadPrefixAsync(entry.FullPath, maxBytes: 262144);
            if (Meridian.Core.Viewer.ExifReader.TryRead(bytes, out var parsed)) summary = parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            summary = null;
        }

        if (generation != _detailsStripGeneration || summary is null) return;
        var lines = new List<string>();
        if (summary.PixelWidth is { } width && summary.PixelHeight is { } height)
            DetailsDimensionsText.Text = $"{width:N0} × {height:N0} pixels";
        if (FormatCameraName(summary.CameraMake, summary.CameraModel) is { } camera)
            lines.Add("Camera: " + camera);
        if (summary.DateTaken is { } taken)
            lines.Add("Taken: " + taken.ToString("g"));
        if (summary.FormatCoordinates() is { } coordinates)
            lines.Add("Location recorded in the photo: " + coordinates);
        if (lines.Count == 0) return;
        DetailsExifText.Text = string.Join("\n", lines);
        DetailsExifText.Visibility = Visibility.Visible;
    }

    private static string? FormatCameraName(string? make, string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return string.IsNullOrWhiteSpace(make) ? null : make;
        if (string.IsNullOrWhiteSpace(make)) return model;
        return model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model : make + " " + model;
    }

    private async Task LoadDetailsFolderSizeAsync(FileEntry entry, int generation, CancellationToken token)
    {
        try
        {
            var result = await FolderSizeCalculator.CalculateAsync(entry.FullPath, token, snapshot =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation != _detailsStripGeneration || token.IsCancellationRequested) return;
                    DetailsSizeText.Text = $"{DirectoryReader.FormatSize(snapshot.TotalBytes)} so far · counting…";
                });
            });
            if (generation != _detailsStripGeneration || token.IsCancellationRequested) return;
            DetailsSizeText.Text = $"{DirectoryReader.FormatSize(result.TotalBytes)} · {result.FileCount:N0} files in {result.DirectoryCount:N0} folders" +
                (result.SkippedCount > 0 ? $" · {result.SkippedCount:N0} items could not be counted" : string.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (generation == _detailsStripGeneration)
                DetailsSizeText.Text = "Could not be calculated · " + ex.Message;
        }
    }

    private async Task LoadDetailsMetadataAsync(FileEntry entry, int generation)
    {
        var values = new Dictionary<MetadataProperty, string>
        {
            [MetadataProperty.Title] = string.Empty,
            [MetadataProperty.Comment] = string.Empty,
            [MetadataProperty.Rating] = string.Empty,
            [MetadataProperty.Keywords] = string.Empty
        };
        bool markedReadOnly;
        try
        {
            markedReadOnly = !entry.IsDirectory && new FileInfo(entry.FullPath).IsReadOnly;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            markedReadOnly = false;
        }

        if (!EditableMetadataPolicy.TryGetEditableProperties(entry.FullPath, entry.IsDirectory, markedReadOnly, out _, out var reason))
        {
            if (generation != _detailsStripGeneration) return;
            _detailsCurrentValues = values;
            ApplyDetailsValuesToEditor(values);
            SetDetailsEditorEnabled(false, reason);
            return;
        }

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(entry.FullPath);
            var stored = await file.Properties.RetrievePropertiesAsync(
                EditableMetadataPolicy.All.Select(descriptor => descriptor.ShellName));
            values[MetadataProperty.Title] = StoredText(stored, "System.Title");
            values[MetadataProperty.Comment] = StoredText(stored, "System.Comment");
            values[MetadataProperty.Keywords] = MetadataEditPlanner.NormalizeKeywords(StoredKeywords(stored));
            values[MetadataProperty.Rating] = RatingScale.ToDisplayText(StoredRating(stored));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or NotSupportedException or InvalidOperationException)
        {
            if (generation != _detailsStripGeneration) return;
            _detailsCurrentValues = values;
            ApplyDetailsValuesToEditor(values);
            SetDetailsEditorEnabled(false, "Windows could not read this file's details here · " + ex.Message);
            return;
        }

        if (generation != _detailsStripGeneration) return;
        _detailsCurrentValues = values;
        ApplyDetailsValuesToEditor(values);
        SetDetailsEditorEnabled(true, null);
    }

    private static string StoredText(IDictionary<string, object> stored, string shellName) =>
        stored.TryGetValue(shellName, out var value) && value is string text ? text : string.Empty;

    private static string StoredKeywords(IDictionary<string, object> stored)
    {
        if (!stored.TryGetValue("System.Keywords", out var value) || value is null) return string.Empty;
        return value switch
        {
            string single => single,
            IEnumerable<string> many => string.Join("; ", many),
            System.Collections.IEnumerable items => string.Join("; ", items.Cast<object>().Select(item => item?.ToString() ?? string.Empty)),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static uint StoredRating(IDictionary<string, object> stored)
    {
        if (!stored.TryGetValue("System.Rating", out var value) || value is null) return 0;
        return value switch
        {
            uint unsigned => unsigned,
            int signed => signed <= 0 ? 0u : (uint)signed,
            long wide => wide <= 0 ? 0u : (uint)Math.Min(wide, uint.MaxValue),
            _ => 0
        };
    }

    private void ApplyDetailsValuesToEditor(IReadOnlyDictionary<MetadataProperty, string> values)
    {
        DetailsTitleBox.Text = values[MetadataProperty.Title];
        DetailsCommentBox.Text = values[MetadataProperty.Comment];
        DetailsKeywordsBox.Text = values[MetadataProperty.Keywords];
        DetailsRating.Value = int.TryParse(values[MetadataProperty.Rating], out var stars) ? stars : 0;
    }

    private void SetDetailsEditorEnabled(bool enabled, string? reason)
    {
        DetailsTitleBox.IsEnabled = enabled;
        DetailsCommentBox.IsEnabled = enabled;
        DetailsKeywordsBox.IsEnabled = enabled;
        DetailsRating.IsEnabled = enabled;
        DetailsReviewButton.IsEnabled = enabled;
        DetailsEditReason.Text = reason ?? string.Empty;
        DetailsEditReason.Visibility = string.IsNullOrEmpty(reason) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void DetailsReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detailsCurrentEntry is not { } entry) return;
        var stars = (int)Math.Round(DetailsRating.Value);
        var requested = new Dictionary<MetadataProperty, string>
        {
            [MetadataProperty.Title] = DetailsTitleBox.Text,
            [MetadataProperty.Comment] = DetailsCommentBox.Text,
            [MetadataProperty.Rating] = stars == 0 ? string.Empty : stars.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [MetadataProperty.Keywords] = DetailsKeywordsBox.Text
        };
        var plan = MetadataEditPlanner.BuildPlan(entry.FullPath, _detailsCurrentValues, requested);
        if (!plan.HasChanges)
        {
            SetStatus("No detail changes to save.", StatusSeverity.Warning);
            return;
        }

        var lines = string.Join("\n", plan.Changes.Select(change => $"{change.Property.Label}: \"{change.OldDisplay}\" → \"{change.NewDisplay}\""));
        var dialog = new ContentDialog
        {
            Title = "Save detail changes?",
            Content = $"Change the details stored inside this file?\n\n{entry.FullPath}\n\n{lines}\n\nOnly this file is changed. The old values are replaced; you can edit them again here at any time. If Windows cannot save a detail, nothing is written and the problem is reported.",
            PrimaryButtonText = "Save changes",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        // Revalidate, like every other confirmed action: if the selection
        // moved on while the dialog was open, do not write to a file the
        // user is no longer looking at.
        var current = GetSelectedEntries(_activePane);
        if (current.Count != 1 || !string.Equals(current[0].FullPath, plan.Path, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Save cancelled · The selection changed. Review the item and its details again.", StatusSeverity.Warning);
            return;
        }

        await ApplyMetadataPlanAsync(entry, plan, requested);
    }

    private async Task ApplyMetadataPlanAsync(FileEntry entry, MetadataEditPlan plan, IReadOnlyDictionary<MetadataProperty, string> requested)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(entry.FullPath);
            var values = new List<KeyValuePair<string, object>>();
            foreach (var change in plan.Changes)
            {
                var raw = requested.TryGetValue(change.Property.Property, out var text) ? text : string.Empty;
                object? value = change.Property.Property switch
                {
                    MetadataProperty.Title => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim(),
                    MetadataProperty.Comment => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim(),
                    MetadataProperty.Rating => string.IsNullOrWhiteSpace(raw)
                        ? null
                        : RatingScale.ToShellValue(int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)),
                    MetadataProperty.Keywords => MetadataEditPlanner.SplitKeywords(raw) is { Count: > 0 } tags
                        ? tags.ToArray()
                        : null,
                    _ => null
                };
                values.Add(new KeyValuePair<string, object>(change.Property.ShellName, value!));
            }

            await file.Properties.SavePropertiesAsync(values);
            SetStatus($"Details saved · {entry.Name}", StatusSeverity.Success);
            RefreshDetailsStrip();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or
            System.Runtime.InteropServices.COMException or NotSupportedException or InvalidOperationException)
        {
            SetStatus("Details could not be saved · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async Task LoadDetailsAccessAsync(FileEntry entry, int generation)
    {
        AccessControlSummary summary;
        try
        {
            summary = await Task.Run(() => AccessControlReader.Read(entry.FullPath, entry.IsDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            summary = new AccessControlSummary(null, [], "The access list could not be read.");
        }

        if (generation != _detailsStripGeneration) return;
        DetailsAccessText.Text = string.Join("\n", AccessControlSummaryFormatter.DescribeLines(summary));
        DetailsSecurityButton.IsEnabled = true;
    }

    private void DetailsSecurityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detailsCurrentEntry is not { } entry) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = entry.FullPath,
                UseShellExecute = true,
                Verb = "properties"
            });
            SetStatus("Opened Windows Properties · permissions are changed on its Security tab. Meridian only shows them.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            SetStatus("Could not open Windows Properties · " + ex.Message, StatusSeverity.Error);
        }
    }

    // ----- Quick preview (Space) -----

    private async Task OpenQuickPreviewAsync(PaneState pane)
    {
        if (_quickPreviewPane is not null) return;
        if (pane.List.SelectedItem is not FileEntry entry)
        {
            SetStatus("Select an item to preview.", StatusSeverity.Warning);
            return;
        }

        SetActivePane(pane);
        _quickPreviewPane = pane;
        QuickPreviewOverlay.Visibility = Visibility.Visible;
        await ShowQuickPreviewItemAsync(pane, entry);
        QuickPreviewOverlay.Focus(FocusState.Programmatic);
    }

    private async Task ShowQuickPreviewItemAsync(PaneState pane, FileEntry entry)
    {
        _quickPreviewCancellation?.Cancel();
        _quickPreviewCancellation?.Dispose();
        _quickPreviewCancellation = new CancellationTokenSource();
        _quickPreviewLease?.Dispose();
        _quickPreviewLease = new PreviewMediaLease();
        _quickPreviewEntry = entry;
        QuickPreviewPanel.Children.Clear();
        QuickPreviewTitle.Text = entry.Name;
        ToolTipService.SetToolTip(QuickPreviewTitle, entry.FullPath);
        var index = pane.Items.IndexOf(entry);
        QuickPreviewPosition.Text = pane.Items.Count > 0 && index >= 0
            ? $"Item {index + 1:N0} of {pane.Items.Count:N0} · {pane.CurrentPath}"
            : pane.CurrentPath;
        try
        {
            if (entry.IsDirectory)
            {
                QuickPreviewPanel.Children.Add(new TextBlock
                {
                    Text = "Folder · " + entry.FullPath,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeBrush("MeridianTextSecondaryBrush")
                });
                var sizeLine = new TextBlock { Text = "Size: calculating…", TextWrapping = TextWrapping.Wrap };
                QuickPreviewPanel.Children.Add(sizeLine);
                _ = LoadQuickPreviewFolderSizeAsync(entry, sizeLine, _quickPreviewCancellation.Token);
            }
            else
            {
                await PopulatePreviewPanelAsync(entry, QuickPreviewPanel, _quickPreviewLease);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or System.Security.SecurityException)
        {
            QuickPreviewPanel.Children.Add(new TextBlock
            {
                Text = "Preview unavailable · " + ex.Message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeBrush("MeridianErrorBrush")
            });
            SetStatus("Preview unavailable · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async Task LoadQuickPreviewFolderSizeAsync(FileEntry entry, TextBlock sizeLine, CancellationToken token)
    {
        try
        {
            var result = await FolderSizeCalculator.CalculateAsync(entry.FullPath, token);
            if (token.IsCancellationRequested || !ReferenceEquals(_quickPreviewEntry, entry)) return;
            sizeLine.Text = $"Size: {DirectoryReader.FormatSize(result.TotalBytes)} · {result.FileCount:N0} files in {result.DirectoryCount:N0} folders" +
                (result.SkippedCount > 0 ? $" · {result.SkippedCount:N0} items could not be counted" : string.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!token.IsCancellationRequested && ReferenceEquals(_quickPreviewEntry, entry))
                sizeLine.Text = "Size could not be calculated · " + ex.Message;
        }
    }

    private async Task StepQuickPreviewAsync(int delta)
    {
        if (_quickPreviewPane is not { } pane) return;
        if (pane.Items.Count == 0) return;
        var current = pane.List.SelectedItem as FileEntry;
        var index = current is null ? -1 : pane.Items.IndexOf(current);
        var next = Math.Clamp(index + delta, 0, pane.Items.Count - 1);
        if (next == index) return;
        var entry = pane.Items[next];
        pane.List.SelectedIndex = next;
        pane.List.ScrollIntoView(entry);
        await ShowQuickPreviewItemAsync(pane, entry);
    }

    private void CloseQuickPreview()
    {
        if (_quickPreviewPane is not { } pane) return;
        _quickPreviewCancellation?.Cancel();
        _quickPreviewLease?.Dispose();
        _quickPreviewLease = null;
        _quickPreviewPane = null;
        _quickPreviewEntry = null;
        QuickPreviewOverlay.Visibility = Visibility.Collapsed;
        pane.List.Focus(FocusState.Programmatic);
    }

    private async void QuickPreviewOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
            case VirtualKey.Space:
                e.Handled = true;
                CloseQuickPreview();
                break;
            case VirtualKey.Up:
            case VirtualKey.Left:
                e.Handled = true;
                await StepQuickPreviewAsync(-1);
                break;
            case VirtualKey.Down:
            case VirtualKey.Right:
                e.Handled = true;
                await StepQuickPreviewAsync(1);
                break;
        }
    }

    private void QuickPreviewCloseButton_Click(object sender, RoutedEventArgs e) => CloseQuickPreview();
    private enum OrganizeSection { None, Bookmarks, Shelf }

    private enum StatusSeverity { Info, Success, Warning, Error }

    private bool _timeVaultBusy;

    private async void TimeVault_Click(object sender, RoutedEventArgs e)
    {
        if (_timeVaultBusy) return;
        _timeVaultBusy = true;
        SetStatus("TimeVault · discovering existing snapshots…");
        try
        {
            Meridian.Core.TimeVault.IShadowCopyCatalog catalog = new Meridian.Core.TimeVault.WindowsShadowCopyCatalog();
            var snapshots = await catalog.ListAsync();
            var selectedPath = _activePane.CurrentPath;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = "Existing snapshots only · read-only · no recovery operations",
                TextWrapping = TextWrapping.Wrap
            });
            var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a snapshot" };
            foreach (var snapshot in snapshots.OrderByDescending(item => item.CreatedAt))
            {
                selector.Items.Add(new ComboBoxItem
                {
                    Content = $"{(snapshot.CreatedAt == DateTimeOffset.MinValue ? "Unknown date" : snapshot.CreatedAt.ToLocalTime().ToString("g"))} · {snapshot.OriginalVolume}",
                    Tag = snapshot
                });
            }
            panel.Children.Add(selector);
            var listing = new TextBlock
            {
                Text = snapshots.Count == 0
                    ? "No accessible shadow copies reported by Windows."
                    : "Select a snapshot, then inspect the active pane's folder.",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };
            var inspect = new Button { Content = "Inspect current folder (read-only)", IsEnabled = snapshots.Count > 0 };
            inspect.Click += async (_, _) =>
            {
                if (selector.SelectedItem is not ComboBoxItem { Tag: Meridian.Core.TimeVault.ShadowCopy selected }) return;
                inspect.IsEnabled = false;
                listing.Text = "Reading snapshot folder…";
                try
                {
                    var entries = await new Meridian.Core.TimeVault.SnapshotDirectoryReader().ListAsync(selected, selectedPath);
                    listing.Text = entries.Count == 0
                        ? "Snapshot folder is empty."
                        : string.Join("\n", entries.Take(250).Select(item => (item.IsDirectory ? "[Folder] " : "[File] ") + item.Name))
                            + (entries.Count > 250 ? $"\n… and {entries.Count - 250} more entries (display limited)." : "");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                    PlatformNotSupportedException or System.ComponentModel.Win32Exception)
                {
                    listing.Text = "Cannot inspect this snapshot folder: " + ex.Message;
                }
                finally { inspect.IsEnabled = true; }
            };
            panel.Children.Add(inspect);
            panel.Children.Add(new ScrollViewer { MaxHeight = 340, Content = listing });
            await new ContentDialog
            {
                Title = $"TimeVault · {snapshots.Count} snapshot(s)",
                Content = panel,
                CloseButtonText = "Close",
                XamlRoot = LeftPath.XamlRoot
            }.ShowAsync();
            SetStatus($"TimeVault · {snapshots.Count} snapshot(s) discovered", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or
            System.Runtime.InteropServices.COMException or UnauthorizedAccessException or
            IOException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            SetStatus("TimeVault discovery unavailable · " + ex.Message, StatusSeverity.Error);
            await new ContentDialog
            {
                Title = "TimeVault discovery unavailable",
                Content = "Windows could not enumerate shadow copies. This may be caused by permissions, WMI availability, or VSS configuration.\n\n" + ex.Message,
                CloseButtonText = "Close",
                XamlRoot = LeftPath.XamlRoot
            }.ShowAsync();
        }
        finally { _timeVaultBusy = false; }
    }

    private sealed class WorkspaceTab(string name, string leftPath, string rightPath)
    {
        public string Name { get; } = name;
        public string LeftPath { get; set; } = leftPath;
        public string RightPath { get; set; } = rightPath;
    }

    private sealed class PaneState(string name, TextBox pathBox, TextBox filterBox, ListView list, TextBlock status, Grid filterRow, ScrollViewer homeView, StackPanel homePanel)
    {
        public string Name { get; } = name;
        public TextBox PathBox { get; } = pathBox;
        public TextBox FilterBox { get; } = filterBox;
        public ListView List { get; } = list;
        public TextBlock Status { get; } = status;
        public Grid FilterRow { get; } = filterRow;
        public ScrollViewer HomeView { get; } = homeView;
        public StackPanel HomePanel { get; } = homePanel;
        public ObservableCollection<FileEntry> Items { get; } = [];
        public IReadOnlyList<FileEntry> AllItems { get; set; } = Array.Empty<FileEntry>();
        public DirectorySortField SortField { get; set; } = DirectorySortField.Name;
        public bool Descending { get; set; }
        public Stack<string> BackHistory { get; } = new();
        public Stack<string> ForwardHistory { get; } = new();
        public string CurrentPath { get; set; } = string.Empty;
        public CancellationTokenSource? ReadCancellation { get; set; }

        /// <summary>True while the pane shows the Home page instead of a folder.</summary>
        public bool IsHome => string.Equals(CurrentPath, HomePath, StringComparison.Ordinal);
    }
}
