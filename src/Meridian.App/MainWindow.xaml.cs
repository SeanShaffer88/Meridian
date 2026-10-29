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
using Meridian.Core.Transfers;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Storage;
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
    private readonly FolderViewStore _folderViewStore = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian", "folder-views.json"));
    private readonly FolderSizeCache _folderSizeCache = new();
    private readonly ThumbnailCache _thumbnailCache = new();
    private bool _folderViewsLoaded;
    private bool _applyingViewState;
    private bool _sidebarVisible = true;
    private readonly List<(Button Button, string TargetPath)> _sidebarNavButtons = [];
    private readonly List<ContextCapsule> _capsules = [];
    private readonly ContextCapsuleStore _capsuleStore = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian", "capsules.json"));
    private bool _capsuleStorageUnavailable;
    private bool _capsuleSaving;
    private readonly ObservableCollection<string> _shelfPaths = [];
    private readonly ObservableCollection<BookmarkSnapshot> _bookmarks = [];

    /// <summary>The favorites-group organization over <see cref="_bookmarks"/>:
    /// which bookmark paths are filed under which named group. Rebuilt from
    /// the saved group data at startup (files written before groups existed
    /// reconcile to no groups — every bookmark ungrouped) and written back on
    /// every session save.</summary>
    private BookmarkGroupSet _bookmarkGroups = BookmarkGroupSet.Reconcile([], null);
    private readonly IDriveEjectService _driveEject = new WindowsDriveEjectService();
    private PaneState _activePane;
    private OrganizeSection _openOrganizeSection = OrganizeSection.None;
    private bool _restoringSession;
    private CancellationTokenSource? _batchCancellation;
    private bool _batchPreparing;
    private string? _pinnedError;
    /// <summary>The DetailsStripVisible value the settings file carried at
    /// load, kept only so saves write it back unchanged: the strip it named
    /// is gone, and the per-pane preview blocks it migrated into keep their
    /// own saved values (see PreviewBlockMigration).</summary>
    private bool _legacyDetailsStripVisible;
    private PaneState? _quickPreviewPane;
    private FileEntry? _quickPreviewEntry;
    private PreviewMediaLease? _quickPreviewLease;
    private CancellationTokenSource? _quickPreviewCancellation;
    private const double DefaultColumnWidth = 280;
    private const double MinimumColumnWidth = 180;
    private const double MaximumColumnWidth = 480;
    private const double PreviewColumnWidth = 320;
    private double _columnWidth = DefaultColumnWidth;
    private bool _columnSplitterDragging;
    private double _columnDragStartX;
    private double _columnDragStartWidth;

    public MainWindow()
    {
        InitializeComponent();
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _appWindow.Changed += AppWindow_Changed;
        SetupCustomTitleBar();
        if (Content is FrameworkElement rootElement) rootElement.Loaded += RootElement_Loaded;
        _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        BookmarksBox.ItemsSource = _bookmarks;
        ShelfItemsBox.ItemsSource = _shelfPaths;
        _left = new PaneState("Left", LeftPath, LeftFilter, LeftItems, LeftStatus, LeftFilterRow, LeftHomeView, LeftHomePanel, LeftColumnsView, LeftColumnsPanel, LeftStandardLayoutToggle, LeftColumnsLayoutToggle, LeftDetailsViewToggle, LeftGridViewToggle, LeftTilesViewToggle, LeftGroup, LeftFolderTree, LeftFolderTreeToggle);
        _right = new PaneState("Right", RightPath, RightFilter, RightItems, RightStatus, RightFilterRow, RightHomeView, RightHomePanel, RightColumnsView, RightColumnsPanel, RightStandardLayoutToggle, RightColumnsLayoutToggle, RightDetailsViewToggle, RightGridViewToggle, RightTilesViewToggle, RightGroup, RightFolderTree, RightFolderTreeToggle);
        _left.BreadcrumbPanel = LeftBreadcrumbSegments;
        _right.BreadcrumbPanel = RightBreadcrumbSegments;
        _left.Block = new PreviewBlockControls(LeftPreviewContent, LeftPreviewTotals, LeftPreviewToggle, LeftPreviewToggleGlyph, LeftPreviewGlyph, LeftPreviewImage, LeftPreviewText, LeftPreviewName, LeftPreviewMeta, LeftPreviewExif);
        _right.Block = new PreviewBlockControls(RightPreviewContent, RightPreviewTotals, RightPreviewToggle, RightPreviewToggleGlyph, RightPreviewGlyph, RightPreviewImage, RightPreviewText, RightPreviewName, RightPreviewMeta, RightPreviewExif);
        WireBreadcrumbEditArea(_left, LeftBreadcrumbEditArea);
        WireBreadcrumbEditArea(_right, RightBreadcrumbEditArea);
        _activePane = _left;
        InitializeTransfers();
        UpdateActivePaneVisuals();
        LeftItems.ItemsSource = _left.Items;
        RightItems.ItemsSource = _right.Items;
        LeftItems.SizeChanged += (_, _) => ApplyAdaptiveColumns(_left);
        RightItems.SizeChanged += (_, _) => ApplyAdaptiveColumns(_right);
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
            _columnWidth = settings.ColumnWidth is > 0
                ? Math.Clamp(settings.ColumnWidth, MinimumColumnWidth, MaximumColumnWidth)
                : DefaultColumnWidth;
            ApplyThemePreference();
            ApplyDensityPreference();
            ApplyPaneSplit();
            SetOrganizeSection(ParseOrganizeSection(settings.OrganizeSection));
            _legacyDetailsStripVisible = settings.DetailsStripVisible;
            var (leftBlockExpanded, rightBlockExpanded) = PreviewBlockMigration.Resolve(
                settings.LeftPreviewBlockExpanded, settings.RightPreviewBlockExpanded, settings.DetailsStripVisible);
            _left.PreviewBlockExpanded = leftBlockExpanded;
            _right.PreviewBlockExpanded = rightBlockExpanded;
            ApplyPreviewBlockVisibility(_left);
            ApplyPreviewBlockVisibility(_right);
            _sidebarVisible = settings.SidebarVisible;
            ApplySidebarVisibility();
            _verifyCopiesWithChecksum = settings.VerifyCopiesWithChecksum;
            _nameCollisionPolicy = ParseCollisionPolicy(settings.NameCollisionPolicy);
            _transfersStripCollapsed = settings.TransfersStripCollapsed;
        }
        await InitializeTransfersAsync();
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
        // Favorites groups ride along in the same file. A file written before
        // groups existed has none: Reconcile yields no groups and every
        // bookmark reads as ungrouped — nothing is lost or reshuffled.
        _bookmarkGroups = BookmarkGroupSet.Reconcile(
            _bookmarks.Select(bookmark => bookmark.Path),
            saved?.BookmarkGroups?.Select(group => new BookmarkGroup(group.Name, group.Collapsed, group.Paths ?? [])));

        // One session only. A file written while the app had workspace tabs
        // resolves to the tab that was active; its other tabs are ignored.
        var session = saved is null ? null : WorkspaceStore.ResolveSession(saved);
        var leftPath = session?.LeftPath is { Length: > 0 } savedLeft && Directory.Exists(savedLeft) ? savedLeft : documents;
        var rightPath = session?.RightPath is { Length: > 0 } savedRight && Directory.Exists(savedRight) ? savedRight : downloads;
        _left.Layout = ParsePaneLayout(session?.LeftLayout);
        _right.Layout = ParsePaneLayout(session?.RightLayout);
        _left.FolderTreeVisible = session?.LeftFolderTreeVisible ?? false;
        _right.FolderTreeVisible = session?.RightFolderTreeVisible ?? false;
        await _folderViewStore.LoadAsync();
        _folderViewsLoaded = true;
        await _recentsStore.LoadAsync();
        BuildSidebar();

        _restoringSession = true;
        try
        {
            await NavigateAsync(_left, leftPath, addHistory: false);
            await NavigateAsync(_right, rightPath, addHistory: false);
        }
        finally
        {
            _restoringSession = false;
        }

        await PersistSessionAsync();
    }

    private async Task<bool> PersistSessionAsync()
    {
        if (_restoringSession) return false;
        try
        {
            await _workspaceStore.SaveAsync(new WorkspaceSnapshot(
                _left.CurrentPath, _right.CurrentPath, _bookmarks.ToArray(),
                Tabs: null, ActiveTabIndex: 0,
                _left.Layout.ToString(), _right.Layout.ToString(),
                _bookmarkGroups.Groups.Select(group => new BookmarkGroupSnapshot(group.Name, group.BookmarkPaths.ToArray(), group.Collapsed)).ToArray(),
                _left.FolderTreeVisible, _right.FolderTreeVisible));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Session persistence is a convenience; file browsing must continue if it fails.
            SetStatus("Session could not be saved · " + ex.Message, StatusSeverity.Error);
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
                _legacyDetailsStripVisible,
                _sidebarVisible,
                _columnWidth,
                // LoadAsync has already applied the one-time accent-default
                // migration to the values above; every save records that, so
                // an accent picked deliberately later is never re-migrated.
                AccentDefaultMigrated: true,
                VerifyCopiesWithChecksum: _verifyCopiesWithChecksum,
                NameCollisionPolicy: _nameCollisionPolicy.ToString(),
                TransfersStripCollapsed: _transfersStripCollapsed,
                LeftPreviewBlockExpanded: _left.PreviewBlockExpanded,
                RightPreviewBlockExpanded: _right.PreviewBlockExpanded));
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
        _bookmarkGroups.AddBookmark(path);
        BuildSidebar();
        await PersistSessionAsync();
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
        _bookmarkGroups.RemoveBookmark(selected.Path);
        BuildSidebar();
        BookmarksBox.SelectedItem = null;
        await PersistSessionAsync();
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
        CancelFolderSizeMeasurement(pane);
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
            SetPaneStatus(pane, ex.Message); SetStatus("Navigation failed · " + ex.Message, StatusSeverity.Error); RebuildBreadcrumbs(pane); return false;
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
            RebuildBreadcrumbs(pane);
            UpdatePaneHeader(pane);
            ApplyFolderViewState(pane);
            pane.AllItems = entries;
            ApplyFilter(pane);
            ApplyPaneMode(pane);
            if (pane.Layout == PaneLayout.Columns) SyncColumnsAfterNavigation(pane, fullPath);
            else ClearColumns(pane);
            RefreshSidebarSelection();
            _ = _recentsStore.RecordAsync(fullPath, isDirectory: true);
            RefreshPreviewBlock(pane);
            SetStatus("Ready · Local-only · No telemetry");
            if (!_restoringSession) await PersistSessionAsync();
            UpdatePaneWatch(pane);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!token.IsCancellationRequested)
            {
                SetPaneStatus(pane, ex.Message);
                SetStatus("Could not read folder · " + ex.Message, StatusSeverity.Error);
                RebuildBreadcrumbs(pane);
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
        CancelFolderSizeMeasurement(pane);
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
        RebuildBreadcrumbs(pane);
        pane.AllItems = Array.Empty<FileEntry>();
        pane.Items.Clear();
        UpdatePaneHeader(pane);
        ApplyPaneMode(pane);
        ClearColumns(pane);
        RenderHomeView(pane);
        RefreshSidebarSelection();
        SetPaneStatus(pane, "Home · pinned folders, drives and recent items");
        RefreshPreviewBlock(pane);
        SetStatus("Ready · Local-only · No telemetry");
        if (!_restoringSession) await PersistSessionAsync();
        UpdatePaneWatch(pane);
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

    /// <summary>Switches a pane between its file list, its columns view and its Home view,
    /// and keeps the layout switcher in step (disabled at Home, which has no folder root).</summary>
    private void ApplyPaneMode(PaneState pane)
    {
        var home = pane.IsHome;
        var columns = !home && pane.Layout == PaneLayout.Columns;
        pane.HomeView.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        pane.List.Visibility = !home && !columns ? Visibility.Visible : Visibility.Collapsed;
        pane.ColumnsView.Visibility = columns ? Visibility.Visible : Visibility.Collapsed;
        pane.FilterRow.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        pane.StandardLayoutToggle.IsChecked = pane.Layout == PaneLayout.Standard;
        pane.ColumnsLayoutToggle.IsChecked = pane.Layout == PaneLayout.Columns;
        pane.StandardLayoutToggle.IsEnabled = !home;
        pane.ColumnsLayoutToggle.IsEnabled = !home;
        var viewControlsEnabled = !home && !columns;
        pane.DetailsViewToggle.IsEnabled = viewControlsEnabled;
        pane.GridViewToggle.IsEnabled = viewControlsEnabled;
        pane.TilesViewToggle.IsEnabled = viewControlsEnabled;
        pane.GroupBox.IsEnabled = viewControlsEnabled;
        pane.FolderTreeToggle.IsEnabled = !home;
        ApplyViewMode(pane);
        ApplyFolderTreeVisibility(pane);
    }

    /// <summary>Shows or hides a pane's folder tree to match its saved on/off
    /// state. The tree also hides at Home, which has no drive root to show;
    /// when it appears over a real folder it is synced to that folder. Runs
    /// from ApplyPaneMode, so every navigation (path box, sidebar,
    /// back/forward, tree pick) keeps the tree following the pane.</summary>
    private void ApplyFolderTreeVisibility(PaneState pane)
    {
        var show = pane.FolderTreeVisible && !pane.IsHome;
        pane.FolderTree.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        pane.FolderTreeToggle.IsChecked = pane.FolderTreeVisible;
        if (show && !string.IsNullOrWhiteSpace(pane.CurrentPath)) _ = SyncFolderTreeAsync(pane);
    }

    private async void FolderTreeToggle_Click(object sender, RoutedEventArgs e)
    {
        var pane = sender is ToggleButton toggle && ReferenceEquals(toggle, _right.FolderTreeToggle) ? _right : _left;
        pane.FolderTreeVisible = pane.FolderTreeToggle.IsChecked == true;
        ApplyFolderTreeVisibility(pane);
        await PersistSessionAsync();
    }

    // ----- Per-pane folder tree -----

    /// <summary>Which pane owns a tree control — or null when the event fired
    /// before the pane states exist (the handlers are wired in XAML and can
    /// be raised during construction) or from an unknown tree.</summary>
    private PaneState? PaneForTree(object? sender)
    {
        if (sender is not TreeView tree) return null;
        if (_left is not null && ReferenceEquals(tree, _left.FolderTree)) return _left;
        if (_right is not null && ReferenceEquals(tree, _right.FolderTree)) return _right;
        return null;
    }

    /// <summary>
    /// Points a pane's folder tree at the pane's current folder: the tree is
    /// rooted at the folder's drive root, then the ancestor chain is
    /// expanded level by level (loading each level lazily) and the folder's
    /// own node is selected. Safe to call redundantly — child loads are
    /// cached per node and a generation counter drops stale walks.
    /// </summary>
    private async Task SyncFolderTreeAsync(PaneState pane)
    {
        if (!pane.FolderTreeVisible || pane.IsHome) return;
        var current = pane.CurrentPath;
        var rootPath = FolderTree.RootOf(current);
        if (rootPath is null) return;
        var generation = ++pane.FolderTreeGeneration;
        if (!string.Equals(pane.FolderTreeRootPath, rootPath, StringComparison.OrdinalIgnoreCase) ||
            pane.FolderTreeRootItem is null)
        {
            pane.FolderTree.RootNodes.Clear();
            pane.FolderTreeLoadedItems.Clear();
            var rootNode = new TreeViewNode
            {
                Content = new FolderTreeItem { Name = FolderTreeDisplayName(rootPath), FullPath = rootPath },
                HasUnrealizedChildren = true
            };
            pane.FolderTree.RootNodes.Add(rootNode);
            pane.FolderTreeRootPath = rootPath;
            pane.FolderTreeRootItem = rootNode;
        }

        var chain = FolderTree.AncestorChain(current);
        TreeViewNode? node = pane.FolderTreeRootItem;
        pane.SyncingFolderTree = true;
        try
        {
            // chain[0] is the root itself; load and expand each level on the
            // way down so the next level's node exists to descend into.
            for (var depth = 1; depth < chain.Count && node is not null; depth++)
            {
                await LoadFolderTreeChildrenAsync(pane, node);
                if (generation != pane.FolderTreeGeneration) return;
                node.IsExpanded = true;
                node = FindFolderTreeChild(node, chain[depth]);
            }
            if (node is not null && generation == pane.FolderTreeGeneration)
            {
                pane.FolderTree.SelectedNode = node;
                if (pane.FolderTree.ContainerFromNode(node) is TreeViewItem container)
                    container.StartBringIntoView();
            }
        }
        finally
        {
            if (generation == pane.FolderTreeGeneration) pane.SyncingFolderTree = false;
        }
    }

    private static string FolderTreeDisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static TreeViewNode? FindFolderTreeChild(TreeViewNode parent, string path)
    {
        foreach (var child in parent.Children)
            if (child.Content is FolderTreeItem item &&
                string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))
                return child;
        return null;
    }

    /// <summary>
    /// Loads one node's folder children on first use. The children are
    /// computed off the UI thread (the folder read plus the has-children
    /// probes that decide which nodes show an expand arrow); a node whose
    /// folder cannot be read simply ends up childless. The node is marked
    /// loaded only once its children are actually in place — marking it
    /// before the await would let a superseded load (a newer navigation
    /// bumped the generation) leave the node permanently empty. Overlapping
    /// loads of the same node are harmless instead: each population runs
    /// whole on the UI thread and replaces the children outright.
    /// </summary>
    private async Task LoadFolderTreeChildrenAsync(PaneState pane, TreeViewNode node)
    {
        if (pane.FolderTreeLoadedItems.Contains(node)) return;
        if (node.Content is not FolderTreeItem item) return;
        var generation = pane.FolderTreeGeneration;
        IReadOnlyList<(string Name, string FullPath, bool HasChildren)> children;
        try
        {
            children = await Task.Run(() =>
            {
                var result = new List<(string Name, string FullPath, bool HasChildren)>();
                foreach (var entry in FolderTree.ChildFolders(item.FullPath))
                    result.Add((entry.Name, entry.FullPath, FolderTree.HasChildFolders(entry.FullPath)));
                return result;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            children = [];
        }
        if (generation != pane.FolderTreeGeneration) return;
        node.Children.Clear();
        foreach (var (name, fullPath, hasChildren) in children)
            node.Children.Add(new TreeViewNode
            {
                Content = new FolderTreeItem { Name = name, FullPath = fullPath },
                HasUnrealizedChildren = hasChildren
            });
        node.HasUnrealizedChildren = false;
        pane.FolderTreeLoadedItems.Add(node);
    }

    private async void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (PaneForTree(sender) is not { } pane) return;
        await LoadFolderTreeChildrenAsync(pane, args.Node);
    }

    private async void FolderTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        // Wired in XAML, so this can fire during construction before the
        // pane states exist — PaneForTree returns null then and the event
        // is ignored. Programmatic follow-sync selections are ignored via
        // SyncingFolderTree; a pick of the folder already shown is a no-op.
        if (PaneForTree(sender) is not { } pane) return;
        if (pane.SyncingFolderTree) return;
        if (sender.SelectedNode?.Content is not FolderTreeItem item) return;
        if (string.Equals(item.FullPath, pane.CurrentPath, StringComparison.OrdinalIgnoreCase)) return;
        await NavigateAsync(pane, item.FullPath);
    }

    private void FolderTree_DragOver(object sender, DragEventArgs e)
    {
        if (PaneForTree(sender) is not { } pane)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        if (TryHitFolderTreeNode(pane, e) is { } destination)
            HandleDropTargetDragOver(e, destination, list: null);
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.DragUIOverride.Caption = "Drop on a folder in the tree";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private void FolderTree_Drop(object sender, DragEventArgs e)
    {
        if (PaneForTree(sender) is not { } pane) return;
        if (TryHitFolderTreeNode(pane, e) is { } destination)
            _ = HandleDropAsync(e, destination, list: null);
    }

    /// <summary>
    /// The folder path of the tree node under the pointer, found by walking
    /// the realized containers of the expanded branches (the same hit-test
    /// shape as the pane lists' row test). Nodes on collapsed branches have
    /// no container and cannot be hit — correct, since they are not visible.
    /// </summary>
    private string? TryHitFolderTreeNode(PaneState pane, DragEventArgs e)
    {
        if (pane.FolderTreeRootItem is null) return null;
        return HitFolderTreeNode(pane, pane.FolderTreeRootItem, e.GetPosition(pane.FolderTree));
    }

    private string? HitFolderTreeNode(PaneState pane, TreeViewNode node, Windows.Foundation.Point position)
    {
        if (pane.FolderTree.ContainerFromNode(node) is TreeViewItem container)
        {
            var bounds = container.TransformToVisual(pane.FolderTree)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight));
            if (bounds.Contains(position) && node.Content is FolderTreeItem item) return item.FullPath;
        }
        if (!node.IsExpanded) return null;
        foreach (var child in node.Children)
            if (HitFolderTreeNode(pane, child, position) is { } hit) return hit;
        return null;
    }

    // ----- Miller columns -----

    private async void LeftStandardLayout_Click(object sender, RoutedEventArgs e) => await SetPaneLayoutAsync(_left, PaneLayout.Standard);
    private async void LeftColumnsLayout_Click(object sender, RoutedEventArgs e) => await SetPaneLayoutAsync(_left, PaneLayout.Columns);
    private async void RightStandardLayout_Click(object sender, RoutedEventArgs e) => await SetPaneLayoutAsync(_right, PaneLayout.Standard);
    private async void RightColumnsLayout_Click(object sender, RoutedEventArgs e) => await SetPaneLayoutAsync(_right, PaneLayout.Columns);

    /// <summary>Ctrl+Shift+V toggles the active pane between the standard list and columns.</summary>
    private async void LayoutShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SetPaneLayoutAsync(_activePane, _activePane.Layout == PaneLayout.Columns ? PaneLayout.Standard : PaneLayout.Columns);
    }

    private async Task SetPaneLayoutAsync(PaneState pane, PaneLayout layout)
    {
        // Columns need a folder root; at Home the switcher is disabled and a
        // toggle attempt simply leaves the pane exactly as it is.
        if (pane.IsHome) { ApplyPaneMode(pane); return; }
        if (pane.Layout == layout) { ApplyPaneMode(pane); return; }
        pane.Layout = layout;
        if (layout == PaneLayout.Columns) CancelFolderSizeMeasurement(pane);
        if (layout == PaneLayout.Columns)
        {
            pane.Chain = new ColumnChain(pane.CurrentPath);
            ApplyPaneMode(pane);
            RebuildColumns(pane);
        }
        else
        {
            ClearColumns(pane);
            ApplyPaneMode(pane);
            // The standard list still shows the folder the cascade started
            // from; reload it at the folder the cascade reached, without a
            // history entry for what is only a layout change.
            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
        }
        RecordFolderView(pane);
        await PersistSessionAsync();
    }

    // ----- View modes (Details / Grid / Tiles), grouping, per-folder view memory -----

    /// <summary>Applies the pane's view mode to its list: row template, item panel,
    /// and the view switcher's checked states. Only the standard layout has view
    /// modes; the columns layout is untouched by them.</summary>
    private void ApplyViewMode(PaneState pane)
    {
        pane.DetailsViewToggle.IsChecked = pane.ViewMode == PaneViewMode.Details;
        pane.GridViewToggle.IsChecked = pane.ViewMode == PaneViewMode.Grid;
        pane.TilesViewToggle.IsChecked = pane.ViewMode == PaneViewMode.Tiles;
        if (pane.Layout != PaneLayout.Standard || pane.IsHome) return;
        switch (pane.ViewMode)
        {
            case PaneViewMode.Grid:
                pane.List.ItemTemplate = (DataTemplate)RootGrid.Resources["GridItemTemplate"];
                pane.List.ItemsPanel = (ItemsPanelTemplate)RootGrid.Resources["WrapItemsPanel"];
                break;
            case PaneViewMode.Tiles:
                pane.List.ItemTemplate = (DataTemplate)RootGrid.Resources["TileItemTemplate"];
                pane.List.ItemsPanel = (ItemsPanelTemplate)RootGrid.Resources["VerticalItemsPanel"];
                break;
            default:
                pane.List.ItemTemplate = (DataTemplate)RootGrid.Resources[DetailsTemplateKey(pane)];
                pane.List.ItemsPanel = (ItemsPanelTemplate)RootGrid.Resources["VerticalItemsPanel"];
                break;
        }
    }

    /// <summary>The Details row template matching the pane's visible column set —
    /// the adaptive-column chain sets map one-to-one to templates.</summary>
    private static string DetailsTemplateKey(PaneState pane)
    {
        var columns = pane.VisibleColumns;
        return (columns.Contains(DetailsColumn.Size), columns.Contains(DetailsColumn.Age), columns.Contains(DetailsColumn.Modified)) switch
        {
            (true, true, true) => "DetailsAllItemTemplate",
            (true, true, false) => "DetailsNoModifiedItemTemplate",
            (true, false, false) => "DetailsSizeItemTemplate",
            _ => "DetailsNameItemTemplate",
        };
    }

    /// <summary>Binds the pane's list to its items — through the grouped view while
    /// grouping is on, directly to the item collection otherwise. The list control
    /// itself never changes, so selection, context menus, and every operation keep
    /// working identically across view modes and grouping.</summary>
    private void BindPaneList(PaneState pane)
    {
        if (pane.Layout == PaneLayout.Standard && !pane.IsHome && pane.Grouping != GroupMode.None)
        {
            if (!ReferenceEquals(pane.GroupSource.Source, pane.Groups))
            {
                pane.GroupSource.Source = pane.Groups;
                pane.GroupSource.IsSourceGrouped = true;
            }

            if (!ReferenceEquals(pane.List.ItemsSource, pane.GroupSource.View))
                pane.List.ItemsSource = pane.GroupSource.View;
        }
        else if (!ReferenceEquals(pane.List.ItemsSource, pane.Items))
        {
            pane.List.ItemsSource = pane.Items;
        }
    }

    /// <summary>Forces the pane's list to rebuild its containers (density/theme refresh).</summary>
    private void RefreshPaneList(PaneState pane)
    {
        if (pane.List.ItemsSource is null) return;
        pane.List.ItemsSource = null;
        BindPaneList(pane);
    }

    private void RebuildGroups(PaneState pane, IReadOnlyList<FileEntry> matches)
    {
        pane.Groups.Clear();
        if (pane.Grouping == GroupMode.None) return;
        foreach (var group in ItemGrouping.Group(matches, pane.Grouping, DateTime.UtcNow))
            pane.Groups.Add(new PaneItemGroup(group.Title, group.Items));
    }

    /// <summary>Applies the adaptive Details columns for the pane's current width:
    /// as the pane narrows, lower-priority columns hide (Name never does); widening
    /// restores them. Only a template swap — the item model is untouched.</summary>
    private void ApplyAdaptiveColumns(PaneState pane)
    {
        if (pane.List.ActualWidth <= 0) return;
        var visible = AdaptiveColumns.VisibleForWidth(pane.List.ActualWidth);
        if (visible.SequenceEqual(pane.VisibleColumns)) return;
        pane.VisibleColumns = visible;
        if (pane.Layout == PaneLayout.Standard && pane.ViewMode == PaneViewMode.Details) ApplyViewMode(pane);
        RecordFolderView(pane);
    }

    private void CancelFolderSizeMeasurement(PaneState pane)
    {
        pane.FolderSizeCancellation?.Cancel();
        pane.FolderSizeCancellation?.Dispose();
        pane.FolderSizeCancellation = null;
        pane.FolderSizeRunKey = null;
    }

    /// <summary>Starts (or keeps) the background folder-size measurement for the
    /// pane's visible folders. Details and Tiles show folder sizes; Grid does not,
    /// so it never pays for measuring. Cached results apply instantly; navigating
    /// away cancels the run.</summary>
    private void StartFolderSizeMeasurement(PaneState pane)
    {
        if (pane.Layout != PaneLayout.Standard || pane.IsHome || pane.ViewMode == PaneViewMode.Grid)
        {
            CancelFolderSizeMeasurement(pane);
            return;
        }

        var folders = pane.Items.Where(item => item.IsDirectory).ToList();
        if (folders.Count == 0)
        {
            CancelFolderSizeMeasurement(pane);
            return;
        }

        var runKey = string.Join("\n", folders.Select(folder => $"{folder.FullPath}|{folder.ModifiedUtc.Ticks}"));
        if (pane.FolderSizeCancellation is not null && string.Equals(pane.FolderSizeRunKey, runKey, StringComparison.Ordinal))
            return; // the same folder set is already measured or being measured
        CancelFolderSizeMeasurement(pane);
        pane.FolderSizeCancellation = new CancellationTokenSource();
        pane.FolderSizeRunKey = runKey;
        _ = MeasureFolderSizesAsync(folders, pane.FolderSizeCancellation.Token);
    }

    private async Task MeasureFolderSizesAsync(List<FileEntry> folders, CancellationToken token)
    {
        foreach (var entry in folders)
        {
            if (token.IsCancellationRequested) return;
            if (_folderSizeCache.TryGet(entry.FullPath, entry.ModifiedUtc, out var cached))
            {
                entry.FolderSizeLabel = DirectoryReader.FormatSize(cached.TotalBytes);
                continue;
            }

            entry.FolderSizeLabel = "Measuring…";
            try
            {
                var result = await FolderSizeCalculator.CalculateAsync(entry.FullPath, token);
                _folderSizeCache.Store(entry.FullPath, entry.ModifiedUtc, result);
                if (!token.IsCancellationRequested)
                    entry.FolderSizeLabel = DirectoryReader.FormatSize(result.TotalBytes);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (!token.IsCancellationRequested)
                    entry.FolderSizeLabel = null; // fall back to the plain dash
            }
        }

        // Sizes this run landed in the cache now count toward the preview
        // blocks' totals; refresh both panes' header lines.
        if (!token.IsCancellationRequested)
        {
            UpdatePreviewBlockTotals(_left);
            UpdatePreviewBlockTotals(_right);
        }
    }

    /// <summary>
    /// Applies the folder's remembered view state, if any, before the listing is
    /// filtered, so layout, view mode, sort, grouping, and visible columns are all
    /// in place for the first render. Precedence: the folder's remembered state
    /// wins while the pane is in that folder; the workspace-tab snapshot keeps
    /// recording whatever the panes currently show and still restores paths and
    /// layouts when a tab is reopened — the folder state then refines the view
    /// the moment the pane lands in the folder.
    /// </summary>
    private void ApplyFolderViewState(PaneState pane)
    {
        var state = _folderViewStore.Find(pane.CurrentPath);
        if (state is null) return;
        _applyingViewState = true;
        try
        {
            pane.Layout = ParsePaneLayout(state.Layout);
            if (Enum.TryParse<DirectorySortField>(state.SortField, out var sortField)) pane.SortField = sortField;
            pane.Descending = state.Descending;
            pane.ViewMode = ParseViewMode(state.ViewMode);
            pane.Grouping = ParseGroupMode(state.Grouping);
            if (state.VisibleColumns is { Length: > 0 })
            {
                var parsed = state.VisibleColumns
                    .Select(name => Enum.TryParse<DetailsColumn>(name, out var column) ? column : (DetailsColumn?)null)
                    .Where(column => column is not null)
                    .Select(column => column!.Value)
                    .Distinct()
                    .ToArray();
                if (parsed.Length > 0) pane.VisibleColumns = AdaptiveColumns.SnapToReachableSet(parsed);
            }

            var (sortBox, directionToggle) = SortControls(pane);
            sortBox.SelectedIndex = (int)pane.SortField;
            directionToggle.IsChecked = pane.Descending;
            pane.GroupBox.SelectedIndex = (int)pane.Grouping;
        }
        finally
        {
            _applyingViewState = false;
        }

        ApplyViewMode(pane);
    }

    private (ComboBox Sort, ToggleButton Direction) SortControls(PaneState pane) =>
        ReferenceEquals(pane, _left) ? (LeftSort, LeftSortDescending) : (RightSort, RightSortDescending);

    private static PaneViewMode ParseViewMode(string? value) => value switch
    {
        nameof(PaneViewMode.Grid) => PaneViewMode.Grid,
        nameof(PaneViewMode.Tiles) => PaneViewMode.Tiles,
        _ => PaneViewMode.Details,
    };

    private static GroupMode ParseGroupMode(string? value) =>
        Enum.TryParse<GroupMode>(value, out var mode) ? mode : GroupMode.None;

    /// <summary>Remembers the pane's current view state for the folder it is in.
    /// Skipped while the session is being restored, while a remembered state is
    /// being applied, at Home, and before the store has loaded.</summary>
    private void RecordFolderView(PaneState pane)
    {
        if (!_folderViewsLoaded || _restoringSession || _applyingViewState) return;
        if (pane.IsHome || string.IsNullOrWhiteSpace(pane.CurrentPath)) return;
        var state = new FolderViewState(
            pane.Layout.ToString(),
            pane.ViewMode.ToString(),
            pane.SortField.ToString(),
            pane.Descending,
            pane.Grouping.ToString(),
            pane.VisibleColumns.Select(column => column.ToString()).ToArray());
        _ = _folderViewStore.RecordAsync(pane.CurrentPath, state);
    }

    private void LeftItems_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) => HandleContainerContentChanging(_left, args);
    private void RightItems_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) => HandleContainerContentChanging(_right, args);

    /// <summary>
    /// Per-realized-row finishing for the view modes: tint the Age column by bucket
    /// in Details (recent items in the secondary text tier, older in the tertiary),
    /// and fill thumbnails asynchronously in Grid/Tiles. Runs again on container
    /// recycle, so it always starts from the entry the container now shows; a
    /// pending thumbnail is applied only if the image still belongs to that entry
    /// (its Tag carries the path).
    /// </summary>
    private void HandleContainerContentChanging(PaneState pane, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not FileEntry entry) return;
        if (args.ItemContainer is not ListViewItem container) return;
        if (container.ContentTemplateRoot is not FrameworkElement root) return;
        if (pane.Layout != PaneLayout.Standard) return;
        if (pane.ViewMode == PaneViewMode.Details)
        {
            if (FindTaggedElement(root, "AgeColumn") is TextBlock ageBlock)
                ageBlock.Foreground = ThemeBrush(RelativeAge.IsRecent(entry.AgeBucket) ? "MeridianTextSecondaryBrush" : "MeridianTextTertiaryBrush");
            return;
        }

        if (FindFirstImage(root) is not { } image) return;
        image.Tag = entry.FullPath;
        // WinUI 3 has no InRecyclingQueue on the event args; clearing here and
        // letting the Tag check in LoadThumbnailIntoAsync discard late loads
        // for reused containers covers the same ground.
        image.Source = null;

        var sizeClass = pane.ViewMode == PaneViewMode.Grid ? ThumbnailSizeClass.Grid : ThumbnailSizeClass.Tile;
        if (_thumbnailCache.TryGet(entry.FullPath, sizeClass, out var cached))
        {
            image.Source = cached;
            return;
        }

        image.Source = null;
        _ = LoadThumbnailIntoAsync(image, entry, sizeClass);
    }

    private async Task LoadThumbnailIntoAsync(Image image, FileEntry entry, ThumbnailSizeClass sizeClass)
    {
        var bitmap = await _thumbnailCache.LoadAsync(entry, sizeClass);
        if (bitmap is not null && string.Equals(image.Tag as string, entry.FullPath, StringComparison.Ordinal))
            image.Source = bitmap;
    }

    private static FrameworkElement? FindTaggedElement(FrameworkElement root, string tag)
    {
        if (string.Equals(root.Tag as string, tag, StringComparison.Ordinal)) return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(root, i) is FrameworkElement child && FindTaggedElement(child, tag) is { } found)
                return found;
        }

        return null;
    }

    private static Image? FindFirstImage(DependencyObject root)
    {
        if (root is Image image) return image;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindFirstImage(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }

        return null;
    }

    /// <summary>Keeps the columns view in step with a committed navigation: a
    /// refresh of the folder the cascade already ends in re-reads the chain in
    /// place (selections that survive are kept); any other navigation
    /// re-roots the cascade at the new folder.</summary>
    private void SyncColumnsAfterNavigation(PaneState pane, string fullPath)
    {
        if (pane.Chain is { } chain && string.Equals(chain.CurrentFolderPath, fullPath, StringComparison.OrdinalIgnoreCase))
            RebuildColumns(pane);
        else
        {
            pane.Chain = new ColumnChain(fullPath);
            RebuildColumns(pane);
        }
    }

    private void RebuildColumns(PaneState pane) => _ = RebuildColumnsAsync(pane);

    private async Task RebuildColumnsAsync(PaneState pane)
    {
        if (pane.Chain is not { } chain) return;
        pane.ColumnsCancellation?.Cancel();
        pane.ColumnsCancellation?.Dispose();
        pane.ColumnsCancellation = new CancellationTokenSource();
        var token = pane.ColumnsCancellation.Token;
        var generation = ++pane.ColumnsGeneration;
        ClearColumnsVisual(pane);
        pane.RebuildingColumns = true;
        FileEntry? previewEntry = null;
        try
        {
            for (var depth = 0; depth < chain.FolderColumnCount; depth++)
            {
                if (chain.FolderPathAtDepth(depth) is not { } folderPath) break;
                var column = CreateColumn(pane, depth, folderPath);
                pane.Columns.Add(column);
                RefreshColumnsPanel(pane);
                if (!await LoadColumnAsync(pane, column, token)) break;
                if (token.IsCancellationRequested || generation != pane.ColumnsGeneration) return;
                var selection = chain.SelectionAtDepth(depth);
                if (selection is null) continue;
                if (!column.AllItems.Any(item => string.Equals(item.FullPath, selection.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    // The item the chain points at was deleted or renamed
                    // since the chain was built: drop it and everything
                    // deeper, and let the pane follow the shorter cascade.
                    chain.ClearFromDepth(depth);
                    break;
                }
                if (!selection.IsDirectory)
                    previewEntry = column.AllItems.FirstOrDefault(item => string.Equals(item.FullPath, selection.Path, StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            pane.RebuildingColumns = false;
        }
        if (token.IsCancellationRequested || generation != pane.ColumnsGeneration) return;
        if (chain.ShowsPreviewColumn && previewEntry is not null)
            await ShowPreviewColumnAsync(pane, previewEntry);
        if (!string.Equals(pane.CurrentPath, chain.CurrentFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            pane.CurrentPath = chain.CurrentFolderPath;
            RebuildBreadcrumbs(pane);
            UpdatePaneHeader(pane);
        }
        pane.PendingRevealPath = null;
        UpdateSelectionStatus(pane);
    }

    /// <summary>Reads one column's folder through the same directory-read path
    /// the standard list uses. A read failure is shown inside the column (and
    /// in the pane/global status) exactly the way the standard list surfaces
    /// it — never a crash and never a silent blank.</summary>
    private async Task<bool> LoadColumnAsync(PaneState pane, ColumnViewState column, CancellationToken token)
    {
        try
        {
            var entries = await Task.Run(() => DirectoryReader.Read(column.FolderPath, token), token);
            if (token.IsCancellationRequested) return false;
            column.AllItems = entries;
            ApplyColumnFilter(pane, column);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!token.IsCancellationRequested)
            {
                column.ErrorBlock.Text = "Could not read this folder · " + ex.Message;
                column.ErrorBlock.Visibility = Visibility.Visible;
                column.List.Visibility = Visibility.Collapsed;
                SetPaneStatus(pane, ex.Message);
                SetStatus("Could not read folder · " + ex.Message, StatusSeverity.Error);
            }
            return false;
        }
    }

    /// <summary>Re-filters one column from its full listing with the pane's
    /// query and sort, then restores the chain's selection (or a pending
    /// "show location" reveal) in it. Runs under the rebuilding guard so the
    /// selection changes it makes are not mistaken for user cascades.</summary>
    private void ApplyColumnFilter(PaneState pane, ColumnViewState column)
    {
        var wasRebuilding = pane.RebuildingColumns;
        pane.RebuildingColumns = true;
        try
        {
            var query = pane.FilterBox.Text.Trim();
            var matches = DirectoryReader.Filter(column.AllItems, query, pane.SortField, pane.Descending, includeFullPath: false);
            column.Items.Clear();
            foreach (var item in matches) column.Items.Add(item);
            var selection = pane.Chain?.SelectionAtDepth(column.Depth);
            if (selection is not null)
            {
                var match = column.Items.FirstOrDefault(item => string.Equals(item.FullPath, selection.Path, StringComparison.OrdinalIgnoreCase));
                if (match is not null && !ReferenceEquals(column.List.SelectedItem, match))
                    column.List.SelectedItem = match;
            }
            else if (pane.PendingRevealPath is { } reveal)
            {
                var revealMatch = column.Items.FirstOrDefault(item => string.Equals(item.FullPath, reveal, StringComparison.OrdinalIgnoreCase));
                if (revealMatch is not null)
                {
                    column.List.SelectedItem = revealMatch;
                    column.List.ScrollIntoView(revealMatch);
                    pane.PendingRevealPath = null;
                }
            }
        }
        finally
        {
            pane.RebuildingColumns = wasRebuilding;
        }
    }

    private ColumnViewState CreateColumn(PaneState pane, int depth, string folderPath)
    {
        var errorBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(10),
            Visibility = Visibility.Collapsed,
            Foreground = ThemeBrush("MeridianErrorBrush")
        };
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(4, 0, 4, 0),
            ItemTemplate = (DataTemplate)RootGrid.Resources["ColumnItemTemplate"]
        };
        var column = new ColumnViewState(folderPath, list, errorBlock) { Depth = depth };
        list.ItemsSource = column.Items;
        AutomationProperties.SetName(list, $"Column {depth + 1}, {pane.Name.ToLowerInvariant()} pane: {folderPath}");
        ToolTipService.SetToolTip(list, folderPath);
        list.SelectionChanged += (s, e) => _ = HandleColumnSelectionAsync(pane, column);
        list.KeyDown += async (s, e) => await HandleColumnKeyDownAsync(pane, column, e);
        list.DoubleTapped += (s, e) => _ = OpenSelectedAsync(pane);
        list.PointerPressed += (s, e) => SetActivePane(pane);
        list.ContextFlyout = BuildColumnContextFlyout(pane);
        list.CanDragItems = true;
        list.AllowDrop = true;
        list.DragItemsStarting += PaneList_DragItemsStarting;
        list.DragItemsCompleted += PaneList_DragItemsCompleted;
        list.DragOver += (s, e) => HandlePaneDragOver(pane, list, column.FolderPath, e);
        list.Drop += async (s, e) => await HandlePaneDropAsync(pane, list, column.FolderPath, e);
        var grid = new Grid();
        grid.Children.Add(list);
        grid.Children.Add(errorBlock);
        column.Root = new Border
        {
            Width = _columnWidth,
            BorderBrush = ThemeBrush("MeridianBorderBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = grid
        };
        return column;
    }

    /// <summary>Builds a column's context menu from the same item handlers the
    /// standard lists' XAML menus use, with the same Opening-time enable/label
    /// pass — one operations implementation for both layouts.</summary>
    private MenuFlyout BuildColumnContextFlyout(PaneState pane)
    {
        var isLeftPane = ReferenceEquals(pane, _left);
        var direction = isLeftPane ? "right" : "left";
        var copyItem = new MenuFlyoutItem { Text = $"Copy to {direction} pane" };
        var moveItem = new MenuFlyoutItem { Text = $"Move to {direction} pane" };
        var renameItem = new MenuFlyoutItem { Text = "Rename" };
        var propertiesItem = new MenuFlyoutItem { Text = "Properties" };
        var shelfItem = new MenuFlyoutItem { Text = "Add to Drop Shelf" };
        var deleteItem = new MenuFlyoutItem { Text = "Delete (Recycle Bin)" };
        if (isLeftPane)
        {
            copyItem.Click += CopyLeftToRight_Click;
            moveItem.Click += MoveLeftToRight_Click;
            renameItem.Click += RenameFromLeft_Click;
            propertiesItem.Click += PropertiesFromLeft_Click;
            shelfItem.Click += AddToShelfFromLeft_Click;
            deleteItem.Click += DeleteFromLeft_Click;
        }
        else
        {
            copyItem.Click += CopyRightToLeft_Click;
            moveItem.Click += MoveRightToLeft_Click;
            renameItem.Click += RenameFromRight_Click;
            propertiesItem.Click += PropertiesFromRight_Click;
            shelfItem.Click += AddToShelfFromRight_Click;
            deleteItem.Click += DeleteFromRight_Click;
        }
        ToolTipService.SetToolTip(copyItem, $"Copy the selected item to the {direction} pane. The copy runs in the Transfers queue");
        ToolTipService.SetToolTip(moveItem, $"Move the selected item to the {direction} pane. The original is removed only after its copy is safely in place");
        ToolTipService.SetToolTip(renameItem, "Rename the selected item (F2)");
        ToolTipService.SetToolTip(propertiesItem, "See the selected item's size, dates, and details");
        ToolTipService.SetToolTip(shelfItem, "Collect the selected item on the Drop Shelf without moving it");
        ToolTipService.SetToolTip(deleteItem, "Move the selected item to the Recycle Bin. It can be restored from there until the bin is emptied");
        var flyout = new MenuFlyout();
        flyout.Items.Add(copyItem);
        flyout.Items.Add(moveItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(renameItem);
        flyout.Items.Add(propertiesItem);
        flyout.Items.Add(shelfItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(deleteItem);
        flyout.Opening += (s, e) => UpdateContextMenuItems(pane, isLeftPane, copyItem, moveItem, renameItem, propertiesItem, shelfItem, deleteItem);
        return flyout;
    }

    /// <summary>The cascade: a column selection rewrites the chain at that
    /// depth (deeper columns and the preview are discarded), then either loads
    /// the selected folder as the next column or, for a file, shows the
    /// preview column. The pane's current path always follows the deepest
    /// selected folder; no per-step history is pushed.</summary>
    private async Task HandleColumnSelectionAsync(PaneState pane, ColumnViewState column)
    {
        if (pane.RebuildingColumns || pane.Chain is not { } chain) return;
        SetActivePane(pane);
        pane.PendingRevealPath = null;
        var generation = ++pane.ColumnsGeneration;
        var selected = column.List.SelectedItem as FileEntry;
        if (selected is null)
        {
            chain.ClearFromDepth(column.Depth);
            TruncateColumns(pane, column.Depth);
        }
        else
        {
            chain.Select(column.Depth, selected.FullPath, selected.IsDirectory);
            TruncateColumns(pane, column.Depth);
            if (selected.IsDirectory)
            {
                var next = CreateColumn(pane, column.Depth + 1, selected.FullPath);
                pane.Columns.Add(next);
                RefreshColumnsPanel(pane);
                ScrollColumnsToEnd(pane);
                next.ReadCancellation = new CancellationTokenSource();
                await LoadColumnAsync(pane, next, next.ReadCancellation.Token);
                if (generation != pane.ColumnsGeneration) return;
            }
            else
            {
                await ShowPreviewColumnAsync(pane, selected);
                if (generation != pane.ColumnsGeneration) return;
            }
        }
        pane.CurrentPath = chain.CurrentFolderPath;
        RebuildBreadcrumbs(pane);
        UpdatePaneHeader(pane);
        RefreshSidebarSelection();
        UpdateSelectionStatus(pane);
        RefreshPreviewBlock(pane);
        _ = _recentsStore.RecordAsync(chain.CurrentFolderPath, isDirectory: true);
        if (!_restoringSession) await PersistSessionAsync();
    }

    /// <summary>Column keyboard model: Up/Down stay native to the list;
    /// Left/Right move focus across columns (Right enters the selected
    /// folder's column, whose contents the cascade already loaded); Enter,
    /// Delete, F2 and Space run the same operations as the standard list.</summary>
    private async Task HandleColumnKeyDownAsync(PaneState pane, ColumnViewState column, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left:
                if (column.Depth > 0 && column.Depth <= pane.Columns.Count)
                {
                    e.Handled = true;
                    var previous = pane.Columns[column.Depth - 1];
                    previous.List.Focus(FocusState.Keyboard);
                    if (previous.List.SelectedItem is FileEntry entry) previous.List.ScrollIntoView(entry);
                }
                break;
            case VirtualKey.Right:
                if (column.List.SelectedItem is FileEntry { IsDirectory: true } && column.Depth + 1 < pane.Columns.Count)
                {
                    e.Handled = true;
                    pane.Columns[column.Depth + 1].List.Focus(FocusState.Keyboard);
                }
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                await OpenSelectedAsync(pane);
                break;
            case VirtualKey.Delete:
                e.Handled = true;
                await DeleteSelectedAsync(pane);
                break;
            case VirtualKey.F2:
                e.Handled = true;
                if (pane.SelectionList.SelectedItems.Count == 1) await RenameSelectedAsync(pane);
                else if (pane.SelectionList.SelectedItems.Count > 1) SetStatus("Select a single item to rename.", StatusSeverity.Warning);
                break;
            case VirtualKey.Space:
            {
                // Same rule as the standard list: plain Space (no modifiers)
                // opens the quick preview; Ctrl+Space selection toggling is
                // left to the list.
                var ctrlDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
                var shiftDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
                var altDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
                if (!ctrlDown && !shiftDown && !altDown)
                {
                    e.Handled = true;
                    await OpenQuickPreviewAsync(pane);
                }
                break;
            }
        }
    }

    /// <summary>Shows the preview column for a selected file, rendered by the
    /// same preview engine the Preview dialog and quick preview use, with the
    /// details data the inspector shows one pane over. One previewer, three
    /// surfaces.</summary>
    private async Task ShowPreviewColumnAsync(PaneState pane, FileEntry entry)
    {
        HidePreviewColumn(pane, refreshPanel: false);
        pane.PreviewCancellation = new CancellationTokenSource();
        pane.PreviewLease = new PreviewMediaLease();
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(10) };
        var heading = new TextBlock
        {
            Text = entry.Name,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MeridianTextPrimaryBrush")
        };
        ToolTipService.SetToolTip(heading, entry.FullPath);
        panel.Children.Add(heading);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel
        };
        pane.PreviewRoot = new Border { Width = PreviewColumnWidth, Child = scroll };
        RefreshColumnsPanel(pane);
        ScrollColumnsToEnd(pane);
        try
        {
            await PopulatePreviewPanelAsync(entry, panel, pane.PreviewLease);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or System.Security.SecurityException)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Preview unavailable · " + ex.Message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeBrush("MeridianErrorBrush")
            });
            SetStatus("Preview unavailable · " + ex.Message, StatusSeverity.Error);
        }
    }

    private void HidePreviewColumn(PaneState pane, bool refreshPanel = true)
    {
        pane.PreviewCancellation?.Cancel();
        pane.PreviewCancellation?.Dispose();
        pane.PreviewCancellation = null;
        pane.PreviewLease?.Dispose();
        pane.PreviewLease = null;
        if (pane.PreviewRoot is not null)
        {
            pane.PreviewRoot = null;
            if (refreshPanel) RefreshColumnsPanel(pane);
        }
    }

    /// <summary>Drops every column deeper than <paramref name="depth"/> plus
    /// the preview column, cancelling any read still in flight for them.</summary>
    private void TruncateColumns(PaneState pane, int depth)
    {
        HidePreviewColumn(pane, refreshPanel: false);
        for (var i = pane.Columns.Count - 1; i >= 0; i--)
        {
            if (pane.Columns[i].Depth <= depth) continue;
            pane.Columns[i].ReadCancellation?.Cancel();
            pane.Columns[i].ReadCancellation?.Dispose();
            pane.Columns.RemoveAt(i);
        }
        RefreshColumnsPanel(pane);
    }

    private void ClearColumnsVisual(PaneState pane)
    {
        foreach (var column in pane.Columns)
        {
            column.ReadCancellation?.Cancel();
            column.ReadCancellation?.Dispose();
        }
        pane.Columns.Clear();
        HidePreviewColumn(pane, refreshPanel: false);
        pane.ColumnsPanel.Children.Clear();
    }

    private void ClearColumns(PaneState pane)
    {
        pane.ColumnsCancellation?.Cancel();
        pane.ColumnsCancellation?.Dispose();
        pane.ColumnsCancellation = null;
        pane.Chain = null;
        pane.PendingRevealPath = null;
        ClearColumnsVisual(pane);
    }

    /// <summary>Rebuilds the columns panel's children from the column states:
    /// a resize splitter between each pair of folder columns, then the
    /// preview column (unsplittered, fixed width) when one is showing.</summary>
    private void RefreshColumnsPanel(PaneState pane)
    {
        pane.ColumnsPanel.Children.Clear();
        for (var i = 0; i < pane.Columns.Count; i++)
        {
            if (i > 0) pane.ColumnsPanel.Children.Add(CreateColumnSplitter());
            if (pane.Columns[i].Root is { } root) pane.ColumnsPanel.Children.Add(root);
        }
        if (pane.PreviewRoot is { } preview) pane.ColumnsPanel.Children.Add(preview);
    }

    private void ScrollColumnsToEnd(PaneState pane)
    {
        pane.ColumnsPanel.UpdateLayout();
        pane.ColumnsView.ChangeView(pane.ColumnsView.ScrollableWidth, null, null);
    }

    private Border CreateColumnSplitter()
    {
        var splitter = new Border
        {
            Width = 6,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        ToolTipService.SetToolTip(splitter, "Drag to resize the columns");
        AutomationProperties.SetName(splitter, "Resize columns");
        splitter.PointerPressed += ColumnSplitter_PointerPressed;
        splitter.PointerMoved += ColumnSplitter_PointerMoved;
        splitter.PointerReleased += ColumnSplitter_PointerReleased;
        splitter.PointerCaptureLost += ColumnSplitter_PointerCaptureLost;
        return splitter;
    }

    private void ColumnSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _columnSplitterDragging = true;
        _columnDragStartWidth = _columnWidth;
        _columnDragStartX = e.GetCurrentPoint(RootGrid).Position.X;
        (sender as UIElement)?.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ColumnSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_columnSplitterDragging) return;
        var x = e.GetCurrentPoint(RootGrid).Position.X;
        SetColumnWidth(_columnDragStartWidth + (x - _columnDragStartX));
    }

    private void ColumnSplitter_PointerReleased(object sender, PointerRoutedEventArgs e) => EndColumnSplitterDrag();

    private void ColumnSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndColumnSplitterDrag();

    private void EndColumnSplitterDrag()
    {
        if (!_columnSplitterDragging) return;
        _columnSplitterDragging = false;
        _ = PersistSettingsAsync();
    }

    /// <summary>Applies the shared column width to every live column in both
    /// panes; the value is clamped here and persisted when the drag ends.</summary>
    private void SetColumnWidth(double width)
    {
        _columnWidth = Math.Clamp(width, MinimumColumnWidth, MaximumColumnWidth);
        foreach (var pane in new[] { _left, _right })
            foreach (var column in pane.Columns)
                if (column.Root is { } root) root.Width = _columnWidth;
    }

    private void BuildSidebar()
    {
        SidebarContent.Children.Clear();
        _sidebarNavButtons.Clear();
        AddSidebarSection("Drives");
        foreach (var drive in EnumerateDrives())
            AddSidebarDriveItem(drive);
        AddSidebarSection("Places");
        AddSidebarItem(Symbol.Home, "Home", HomePath, "Show the Home page in the active pane");
        AddSidebarItem(Symbol.Folder, "Desktop", KnownFolderPath(Environment.SpecialFolder.Desktop), "Open the Desktop folder in the active pane");
        AddSidebarItem(Symbol.Document, "Documents", KnownFolderPath(Environment.SpecialFolder.MyDocuments), "Open the Documents folder in the active pane");
        AddSidebarItem(Symbol.Download, "Downloads", DownloadsFolderPath(), "Open the Downloads folder in the active pane");
        AddSidebarItem(Symbol.Audio, "Music", KnownFolderPath(Environment.SpecialFolder.MyMusic), "Open the Music folder in the active pane");
        AddSidebarItem(Symbol.Pictures, "Pictures", KnownFolderPath(Environment.SpecialFolder.MyPictures), "Open the Pictures folder in the active pane");
        AddSidebarItem(Symbol.Video, "Videos", KnownFolderPath(Environment.SpecialFolder.MyVideos), "Open the Videos folder in the active pane");
        AddSidebarSection("Favorites");
        // Ungrouped favorites first (in saved order, exactly as the flat
        // list always showed them), then each group under its header.
        foreach (var path in _bookmarkGroups.UngroupedPaths)
            if (BookmarkByPath(path) is { } ungrouped)
                AddSidebarItem(Symbol.Folder, ungrouped.Name, ungrouped.Path, $"Open {ungrouped.Path} in the active pane", BuildBookmarkContextFlyout(ungrouped, groupName: null));
        foreach (var group in _bookmarkGroups.Groups)
        {
            AddSidebarGroupHeader(group);
            if (group.Collapsed) continue;
            foreach (var path in group.BookmarkPaths)
                if (BookmarkByPath(path) is { } bookmark)
                    AddSidebarItem(Symbol.Folder, bookmark.Name, bookmark.Path, $"Open {bookmark.Path} in the active pane", BuildBookmarkContextFlyout(bookmark, group.Name), indent: true);
        }
        RefreshSidebarSelection();
    }

    /// <summary>
    /// A drive's sidebar row: name, then a slim usage bar with the
    /// percent-used and total size beside it. The bar's fill is the accent
    /// brush; a drive that will not answer shows "Not ready" and no bar
    /// instead of invented numbers. Everything else about the row — the
    /// Inspect/Eject menu, drop targeting, selection highlight — matches
    /// the other sidebar entries.
    /// </summary>
    private void AddSidebarDriveItem(DriveEntry drive)
    {
        var content = new StackPanel { Spacing = 3 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        titleRow.Children.Add(new SymbolIcon { Symbol = Symbol.MapDrive, Width = 16, Height = 16 });
        titleRow.Children.Add(new TextBlock { Text = drive.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(titleRow);
        if (drive.IsReady && drive.TotalBytes > 0)
        {
            var usageRow = new Grid { ColumnSpacing = 8 };
            usageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            usageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = DriveSummary.UsedFraction(drive.TotalBytes, drive.FreeBytes) * 100,
                Foreground = ThemeBrush("MeridianAccentBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(bar, DriveSummary.FreeSpaceLabel(drive.TotalBytes, drive.FreeBytes));
            usageRow.Children.Add(bar);
            var usageText = new TextBlock
            {
                Text = $"{DriveSummary.UsedPercent(drive.TotalBytes, drive.FreeBytes)}% · {DirectoryReader.FormatSize(drive.TotalBytes)}",
                FontSize = 11,
                Foreground = ThemeBrush("MeridianTextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(usageText, 1);
            usageRow.Children.Add(usageText);
            content.Children.Add(usageRow);
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = "Not ready",
                FontSize = 11,
                Foreground = ThemeBrush("MeridianTextSecondaryBrush")
            });
        }
        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6, 10, 6),
            Tag = drive.RootPath,
            ContextFlyout = BuildDriveContextFlyout(drive)
        };
        ToolTipService.SetToolTip(button, drive.ToolTip);
        AutomationProperties.SetName(button, drive.IsReady
            ? $"{drive.Label}, {DriveSummary.UsedPercent(drive.TotalBytes, drive.FreeBytes)} percent used, {DriveSummary.FreeSpaceLabel(drive.TotalBytes, drive.FreeBytes)}"
            : $"{drive.Label}, not ready");
        button.Click += SidebarItem_Click;
        button.AllowDrop = true;
        button.DragOver += SidebarItem_DragOver;
        button.Drop += SidebarItem_Drop;
        SidebarContent.Children.Add(button);
        _sidebarNavButtons.Add((button, drive.RootPath));
    }

    private BookmarkSnapshot? BookmarkByPath(string path) =>
        _bookmarks.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>A favorites group header: name + collapse chevron. Clicking
    /// toggles the group open or closed (persisted with the session); the
    /// right-click menu carries the group's create/rename/delete actions.</summary>
    private void AddSidebarGroupHeader(BookmarkGroup group)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new FontIcon { Glyph = group.Collapsed ? "\uE76C" : "\uE70D", FontSize = 12, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = group.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4, 10, 4),
            Tag = group.Name
        };
        ToolTipService.SetToolTip(button, group.Collapsed ? $"Expand the {group.Name} group" : $"Collapse the {group.Name} group");
        AutomationProperties.SetName(button, $"Favorites group {group.Name}");
        button.Click += SidebarGroupHeader_Click;
        button.ContextFlyout = BuildGroupContextFlyout(group.Name);
        SidebarContent.Children.Add(button);
    }

    private async void SidebarGroupHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        var group = _bookmarkGroups.Groups.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (group is null) return;
        _bookmarkGroups.SetGroupCollapsed(name, !group.Collapsed);
        BuildSidebar();
        await PersistSessionAsync();
    }

    private MenuFlyout BuildGroupContextFlyout(string groupName)
    {
        var flyout = new MenuFlyout();
        var newItem = new MenuFlyoutItem { Text = "New group" };
        ToolTipService.SetToolTip(newItem, "Create a new favorites group");
        newItem.Click += async (s, e) => await NewBookmarkGroupAsync();
        flyout.Items.Add(newItem);
        var renameItem = new MenuFlyoutItem { Text = "Rename group…" };
        ToolTipService.SetToolTip(renameItem, $"Rename the {groupName} group. Its favorites stay in the group");
        renameItem.Click += async (s, e) => await RenameBookmarkGroupAsync(groupName);
        flyout.Items.Add(renameItem);
        var deleteItem = new MenuFlyoutItem { Text = "Delete group" };
        ToolTipService.SetToolTip(deleteItem, $"Delete the {groupName} group. Its favorites are not deleted — they move back to the ungrouped favorites");
        deleteItem.Click += async (s, e) => await DeleteBookmarkGroupAsync(groupName);
        flyout.Items.Add(deleteItem);
        return flyout;
    }

    /// <summary>A bookmark's right-click menu: "Move to group" with one entry
    /// per group (the current one disabled), "New group…", and "No group"
    /// when the bookmark is currently filed under one.</summary>
    private MenuFlyout BuildBookmarkContextFlyout(BookmarkSnapshot bookmark, string? groupName)
    {
        var flyout = new MenuFlyout();
        var moveSub = new MenuFlyoutSubItem { Text = "Move to group" };
        ToolTipService.SetToolTip(moveSub, "File this favorite under a group, or move it back to the ungrouped favorites");
        foreach (var group in _bookmarkGroups.Groups)
        {
            var target = group.Name;
            var item = new MenuFlyoutItem
            {
                Text = target,
                IsEnabled = !string.Equals(target, groupName, StringComparison.OrdinalIgnoreCase)
            };
            ToolTipService.SetToolTip(item, $"Move {bookmark.Name} into the {target} group");
            item.Click += async (s, e) => await MoveBookmarkToGroupAsync(bookmark.Path, target);
            moveSub.Items.Add(item);
        }
        if (_bookmarkGroups.Groups.Count > 0) moveSub.Items.Add(new MenuFlyoutSeparator());
        var newGroupItem = new MenuFlyoutItem { Text = "New group…" };
        ToolTipService.SetToolTip(newGroupItem, "Create a new favorites group and move this favorite into it");
        newGroupItem.Click += async (s, e) => await NewBookmarkGroupAsync(bookmark.Path);
        moveSub.Items.Add(newGroupItem);
        if (groupName is not null)
        {
            moveSub.Items.Add(new MenuFlyoutSeparator());
            var noGroupItem = new MenuFlyoutItem { Text = "No group" };
            ToolTipService.SetToolTip(noGroupItem, $"Move {bookmark.Name} back to the ungrouped favorites");
            noGroupItem.Click += async (s, e) => await MoveBookmarkToGroupAsync(bookmark.Path, null);
            moveSub.Items.Add(noGroupItem);
        }
        flyout.Items.Add(moveSub);
        return flyout;
    }

    private async Task MoveBookmarkToGroupAsync(string path, string? groupName)
    {
        if (!_bookmarkGroups.MoveBookmark(path, groupName)) return;
        BuildSidebar();
        await PersistSessionAsync();
    }

    private async Task<string?> PromptGroupNameAsync(string title, string? initial, string explanation)
    {
        var nameBox = new TextBox { Text = initial ?? string.Empty, MinWidth = 280, PlaceholderText = "Group name" };
        AutomationProperties.SetName(nameBox, "Group name");
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(nameBox);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return nameBox.Text;
    }

    private async Task NewBookmarkGroupAsync(string? bookmarkPathToFile = null)
    {
        var name = await PromptGroupNameAsync("New favorites group", null,
            "Groups collect related favorites under a heading in the sidebar. Move favorites into a group from a favorite's right-click menu.");
        if (name is null) return;
        if (!_bookmarkGroups.TryCreateGroup(name, out var error))
        {
            SetStatus(error ?? "The group could not be created.", StatusSeverity.Warning);
            return;
        }
        var created = name.Trim();
        if (bookmarkPathToFile is not null) _bookmarkGroups.MoveBookmark(bookmarkPathToFile, created);
        BuildSidebar();
        await PersistSessionAsync();
        SetStatus($"Created group · {created}", StatusSeverity.Success);
    }

    private async Task RenameBookmarkGroupAsync(string groupName)
    {
        var name = await PromptGroupNameAsync("Rename favorites group", groupName,
            $"Rename the {groupName} group. Its favorites stay in the group.");
        if (name is null) return;
        if (!_bookmarkGroups.TryRenameGroup(groupName, name, out var error))
        {
            SetStatus(error ?? "The group could not be renamed.", StatusSeverity.Warning);
            return;
        }
        BuildSidebar();
        await PersistSessionAsync();
        SetStatus($"Renamed group · {name.Trim()}", StatusSeverity.Success);
    }

    private async Task DeleteBookmarkGroupAsync(string groupName)
    {
        var group = _bookmarkGroups.Groups.FirstOrDefault(item => string.Equals(item.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (group is null) return;
        var dialog = new ContentDialog
        {
            Title = "Delete favorites group",
            Content = new TextBlock
            {
                Text = group.BookmarkPaths.Count == 0
                    ? $"Delete the {groupName} group? The group is empty, so nothing else changes."
                    : $"Delete the {groupName} group? Its {group.BookmarkPaths.Count} favorite(s) are not deleted — they move back to the ungrouped favorites at the top of the list.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Delete group",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!_bookmarkGroups.DeleteGroup(groupName)) return;
        BuildSidebar();
        await PersistSessionAsync();
        SetStatus($"Deleted group · {groupName}. Its favorites are now ungrouped.", StatusSeverity.Success);
    }

    /// <summary>A drive's right-click menu: inspection always; eject only for
    /// removable drives (the eject service refuses anything else as well).</summary>
    private MenuFlyout BuildDriveContextFlyout(DriveEntry drive)
    {
        var flyout = new MenuFlyout();
        var inspectItem = new MenuFlyoutItem { Text = "Inspect drive…" };
        ToolTipService.SetToolTip(inspectItem, "See this drive's capacity, file system and free space");
        inspectItem.Click += async (s, e) => await InspectDriveAsync(drive.RootPath);
        flyout.Items.Add(inspectItem);
        if (drive.Type == DriveType.Removable)
        {
            var ejectItem = new MenuFlyoutItem { Text = "Eject" };
            ToolTipService.SetToolTip(ejectItem, "Safely remove this drive so it can be unplugged");
            ejectItem.Click += async (s, e) => await EjectDriveAsync(drive.RootPath);
            flyout.Items.Add(ejectItem);
        }
        return flyout;
    }

    private async Task InspectDriveAsync(string rootPath)
    {
        IDriveFactsSource source = new DriveInfoFactsSource();
        var facts = await Task.Run(() => source.Read(rootPath));
        var dialog = new ContentDialog
        {
            Title = $"Drive {rootPath}",
            Content = new TextBlock
            {
                Text = string.Join("\n", DriveInspector.Describe(facts)),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task EjectDriveAsync(string rootPath)
    {
        SetStatus($"Ejecting {rootPath}…", StatusSeverity.Info);
        var result = await _driveEject.EjectAsync(rootPath);
        switch (result.Outcome)
        {
            case DriveEjectOutcome.Ejected:
                SetStatus(result.Message, StatusSeverity.Success);
                BuildSidebar();
                if (_left.IsHome) RenderHomeView(_left);
                if (_right.IsHome) RenderHomeView(_right);
                break;
            case DriveEjectOutcome.Busy:
                SetStatus(result.Message, StatusSeverity.Warning);
                break;
            default:
                SetStatus(result.Message, StatusSeverity.Error);
                break;
        }
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

    private void AddSidebarItem(Symbol glyph, string label, string targetPath, string toolTip, MenuFlyout? contextFlyout = null, bool indent = false)
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
            Padding = new Thickness(indent ? 26 : 10, 6, 10, 6),
            Tag = targetPath
        };
        ToolTipService.SetToolTip(button, toolTip);
        AutomationProperties.SetName(button, label);
        if (contextFlyout is not null) button.ContextFlyout = contextFlyout;
        button.Click += SidebarItem_Click;
        if (!string.Equals(targetPath, HomePath, StringComparison.OrdinalIgnoreCase))
        {
            button.AllowDrop = true;
            button.DragOver += SidebarItem_DragOver;
            button.Drop += SidebarItem_Drop;
        }
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

    private sealed record DriveEntry(string RootPath, string Label, string ToolTip, long TotalBytes, long FreeBytes, bool IsReady, DriveType Type);

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
                    drives.Add(new DriveEntry(root, shortName, $"Open {root} in the active pane", 0, 0, false, drive.DriveType));
                    continue;
                }
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? DriveTypeLabel(drive.DriveType) : drive.VolumeLabel;
                drives.Add(new DriveEntry(root, $"{shortName} · {label}", $"Open {root} in the active pane", drive.TotalSize, drive.AvailableFreeSpace, true, drive.DriveType));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                drives.Add(new DriveEntry(root, shortName, $"Open {root} in the active pane", 0, 0, false, drive.DriveType));
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
        card.AllowDrop = true;
        card.DragOver += Card_DragOver;
        card.Drop += Card_Drop;
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

    private void ApplyFilter(PaneState? pane)
    {
        if (pane is null) return;
        if (pane.IsHome) return;
        var query = pane.FilterBox.Text.Trim();
        if (pane.Layout == PaneLayout.Columns)
        {
            // The standard items mirror the root folder's listing; every
            // visible column re-filters from its own full listing with the
            // same query and sort as the standard list.
            pane.Items.Clear();
            foreach (var item in DirectoryReader.Filter(pane.AllItems, query, pane.SortField, pane.Descending, includeFullPath: false))
                pane.Items.Add(item);
            foreach (var column in pane.Columns) ApplyColumnFilter(pane, column);
            UpdateSelectionStatus(pane);
            return;
        }
        var matches = DirectoryReader.Filter(pane.AllItems, query, pane.SortField, pane.Descending, includeFullPath: false);

        pane.Items.Clear();
        foreach (var item in matches)
            pane.Items.Add(item);

        RebuildGroups(pane, matches);
        BindPaneList(pane);
        StartFolderSizeMeasurement(pane);

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
        if (_applyingViewState) return; // control sync while a remembered view is being applied
        ApplyFilter(pane);
        RecordFolderView(pane);
    }

    private void LeftGroup_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateGrouping(_left, LeftGroup);
    private void RightGroup_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateGrouping(_right, RightGroup);

    private void UpdateGrouping(PaneState? pane, ComboBox? groupBox)
    {
        if (pane is null || groupBox is null) return;
        if (_applyingViewState) return; // control sync while a remembered view is being applied
        var mode = (GroupMode)Math.Clamp(groupBox.SelectedIndex, 0, 3);
        if (pane.Grouping == mode) return;
        pane.Grouping = mode;
        ApplyFilter(pane);
        RecordFolderView(pane);
    }

    private void LeftDetailsView_Click(object sender, RoutedEventArgs e) => SetViewMode(_left, PaneViewMode.Details);
    private void LeftGridView_Click(object sender, RoutedEventArgs e) => SetViewMode(_left, PaneViewMode.Grid);
    private void LeftTilesView_Click(object sender, RoutedEventArgs e) => SetViewMode(_left, PaneViewMode.Tiles);
    private void RightDetailsView_Click(object sender, RoutedEventArgs e) => SetViewMode(_right, PaneViewMode.Details);
    private void RightGridView_Click(object sender, RoutedEventArgs e) => SetViewMode(_right, PaneViewMode.Grid);
    private void RightTilesView_Click(object sender, RoutedEventArgs e) => SetViewMode(_right, PaneViewMode.Tiles);

    private void SetViewMode(PaneState pane, PaneViewMode mode)
    {
        if (pane.Layout != PaneLayout.Standard || pane.IsHome || pane.ViewMode == mode)
        {
            ApplyViewMode(pane); // keep the switcher honest when the click changed nothing
            return;
        }

        pane.ViewMode = mode;
        ApplyViewMode(pane);
        StartFolderSizeMeasurement(pane);
        RecordFolderView(pane);
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
        if (_activePane.SelectionList.SelectedItem is not FileEntry selected || selected.IsDirectory ||
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
        if (_activePane.SelectionList.SelectedItem is not FileEntry selected || selected.IsDirectory ||
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
        if (_activePane.SelectionList.SelectedItem is not FileEntry selected)
        {
            SetStatus("Select files or folders to archive.", StatusSeverity.Warning);
            return;
        }
        var sources = _activePane.SelectionList.SelectedItems.OfType<FileEntry>().ToArray();
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
        AutomationProperties.SetName(formatPicker, "Archive format");
        ToolTipService.SetToolTip(formatPicker, "The archive format to create. ZIP opens almost anywhere; TAR and TAR.GZ hold the same files in one uncompressed or compressed bundle.");
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
        if (_activePane.SelectionList.SelectedItem is not FileEntry selected || selected.IsDirectory)
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
        if (pane.SelectionList.SelectedItem is not FileEntry selected) return;
        if (selected.IsDirectory)
        {
            // In the columns layout a selected folder is already cascaded into
            // its own column; there is nothing further to open.
            if (pane.Layout == PaneLayout.Columns) return;
            await NavigateAsync(pane, selected.FullPath); return;
        }
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
                Content = $"Move this {kind} to the other pane's current folder?\n\nFrom: {selected.FullPath}\nTo: {destination.CurrentPath}\n\nThe move runs in the Transfers queue below the panes. Each item is copied first and its original is removed only after the copy is safely in place, so moves between volumes work too. If a name already exists, your Settings choice under \u201cWhen a name already exists\u201d applies. Finished moves appear in Transfer History, where many can be undone.",
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
                Content = $"Move {selection.Count:N0} items to the other pane's current folder?\n\nFrom: {source.CurrentPath}\nTo: {destination.CurrentPath}\n\nThe move runs in the Transfers queue below the panes. Each item is copied first and its original is removed only after the copy is safely in place, so moves between volumes work too. If a name already exists, your Settings choice under \u201cWhen a name already exists\u201d applies. If an item cannot be moved, the job finishes with errors and items already moved stay moved. Finished moves appear in Transfer History, where many can be undone.",
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
        var job = TransferJob.Create(
            TransferJobKind.Move, currentPaths, confirmedDestination,
            _nameCollisionPolicy, _verifyCopiesWithChecksum);
        await _transfers.EnqueueAsync(job);
        SetPaneStatus(source, "Move queued");
        SetStatus($"Queued move \u00b7 {currentPaths.Length:N0} item(s) to {confirmedDestination}. Progress is in the Transfers strip below.", StatusSeverity.Success);
    }

    private async Task CopySelectedAsync(PaneState source, PaneState destination)
    {
        var selection = GetSelectedEntries(source);
        if (selection.Count == 0) { SetStatus($"Select an item in the {source.Name.ToLowerInvariant()} pane first.", StatusSeverity.Warning); return; }
        if (destination.IsHome) { SetStatus("Open a folder in the other pane first \u2014 Home is not a copy destination.", StatusSeverity.Warning); return; }
        if (string.IsNullOrWhiteSpace(destination.CurrentPath)) return;

        var sourcePaths = selection.Select(item => item.FullPath).ToArray();
        var destinationPath = destination.CurrentPath;
        // A folder can never be copied into itself or one of its own folders;
        // the executor also refuses per item, but the whole request is refused
        // here so nothing is queued that can only fail.
        foreach (var sourcePath in sourcePaths)
        {
            if (Directory.Exists(sourcePath) && IsSameOrDescendantPath(sourcePath, destinationPath))
            { SetStatus("The destination is inside the selected folder. Choose another destination.", StatusSeverity.Warning); return; }
        }

        ClearPinnedError();
        var job = TransferJob.Create(
            TransferJobKind.Copy, sourcePaths, destinationPath,
            _nameCollisionPolicy, _verifyCopiesWithChecksum);
        await _transfers.EnqueueAsync(job);
        SetPaneStatus(source, "Copy queued");
        SetStatus($"Queued copy \u00b7 {sourcePaths.Length:N0} item(s) to {destinationPath}. Progress is in the Transfers strip below.", StatusSeverity.Success);
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
        if (pane.SelectionList.SelectedItem is not FileEntry selected)
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
        if (pane.SelectionList.SelectedItem is not FileEntry selected)
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
            var job = TransferJob.Create(
                TransferJobKind.Copy, sources, destination,
                _nameCollisionPolicy, _verifyCopiesWithChecksum);
            await _transfers.EnqueueAsync(job);
            _shelfJobId = job.Id;
            CancelShelfButton.IsEnabled = true;
            SetStatus($"Queued Drop Shelf copy \u00b7 {sources.Length:N0} item(s) to {destination}. Progress is in the Transfers strip below.");

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
            CancelShelfButton.IsEnabled = _shelfJobId is not null;
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
        if (_shelfJobId is { } jobId &&
            _transfers.Jobs.FirstOrDefault(job => job.Id == jobId) is { } job)
        {
            _ = _transfers.CancelJobAsync(job);
            SetStatus("Cancelling the Drop Shelf transfer… Items already copied stay copied; a half-copied item is removed.");
            return;
        }
        _batchCancellation?.Cancel();
        SetStatus("Cancelling batch preparation…");
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
        _activePane.PendingRevealPath = selected;
        await NavigateAsync(_activePane, parent);
        if (_activePane.Layout == PaneLayout.Standard)
        {
            var match = _activePane.Items.FirstOrDefault(item =>
                string.Equals(item.FullPath, selected, StringComparison.OrdinalIgnoreCase));
            if (match is not null) _activePane.List.SelectedItem = match;
            _activePane.PendingRevealPath = null;
        }
        SetStatus($"Showing Drop Shelf item · {Path.GetFileName(selected)}");
    }

    private void CancelCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_transfers.ActiveJob is not { } job) return;
        _ = _transfers.CancelJobAsync(job);
        SetStatus("Cancelling transfer… Files already finished stay in place; the half-copied file is removed.");
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
        EnterPathEditMode(_activePane);
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
        // Esc only means "cancel transfer" while a transfer is actually
        // running (or a shelf batch is being prepared); every other Esc press
        // keeps its normal behavior (closing flyouts, etc.).
        if (_transfers.ActiveJob is not null)
        {
            args.Handled = true;
            CancelCopy_Click(this, new RoutedEventArgs());
            return;
        }
        if (_batchCancellation is not null)
        {
            args.Handled = true;
            CancelShelf_Click(this, new RoutedEventArgs());
        }
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
        ApplyTitleBarButtonForeground();
        ApplyAccentBrushes();
        UpdateActivePaneVisuals();
        // Rebuild the item containers so per-row finishes that resolve theme
        // brushes in code (the Age column tint) pick up the new theme.
        RefreshPaneList(_left);
        RefreshPaneList(_right);
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
        // the same accent, applied live with no restart. The selection
        // brushes are deliberately NOT touched here: selection is a fixed
        // red-gray token in each theme dictionary and never follows the
        // accent colour.
        foreach (var entry in Application.Current.Resources.ThemeDictionaries)
        {
            if (entry.Value is not ResourceDictionary dictionary) continue;
            var lightTheme = string.Equals(entry.Key?.ToString(), "Light", StringComparison.OrdinalIgnoreCase);
            var resolved = AccentPalette.Resolve(_accentMode, _accentPresetId, _accentCustomColor, lightTheme, windowsAccent);
            var accent = Windows.UI.Color.FromArgb(0xFF, resolved.R, resolved.G, resolved.B);
            SetBrushColor(dictionary, "MeridianAccentBrush", accent);
            SetBrushColor(dictionary, "MeridianActivePaneBorderBrush", accent);
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
        RefreshPaneList(_left);
        RefreshPaneList(_right);
        foreach (var pane in new[] { _left, _right })
            foreach (var column in pane.Columns)
                if (column.List.ItemsSource is not null) { column.List.ItemsSource = null; column.List.ItemsSource = column.Items; }
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

    private void SetupCustomTitleBar()
    {
        // If titlebar customization is unavailable the window keeps the
        // system title bar and stays fully functional; never block startup.
        if (_appWindow is null || !Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        // The header row is the window's title bar: content extends into the
        // titlebar area, the header grid becomes the drag region (its
        // interactive children — the sidebar toggle and the CommandBar —
        // stay clickable), and Windows keeps drawing the real caption
        // buttons at the top-right corner, blended into the header through
        // transparent button backgrounds.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        _appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        _appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        ApplyTitleBarButtonForeground();
        UpdateTitleBarInsets();
        ApplyPresenterMinimumSize();
    }

    private void ApplyTitleBarButtonForeground()
    {
        if (_appWindow is null) return;
        // The caption glyphs must contrast with the header in both themes.
        var foreground = RootGrid.ActualTheme == ElementTheme.Dark
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black;
        var titleBar = _appWindow.TitleBar;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void UpdateTitleBarInsets()
    {
        // Reserve the system caption region inside the header so nothing is
        // hidden under the caption buttons. The insets are physical pixels;
        // XAML padding is in device-independent units.
        if (_appWindow is null) return;
        if (Content.XamlRoot is not null) _rasterizationScale = Content.XamlRoot.RasterizationScale;
        var scale = _rasterizationScale <= 0 ? 1.0 : _rasterizationScale;
        var titleBar = _appWindow.TitleBar;
        AppTitleBar.Padding = new Thickness(titleBar.LeftInset / scale, 0, titleBar.RightInset / scale, 0);
    }

    private void ApplyPresenterMinimumSize()
    {
        // The overlapped presenter enforces the ≈960×640 floor (in
        // device-independent units) natively; the clamp in
        // EnforceMinimumWindowSize remains as a fallback.
        if (_appWindow?.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter) return;
        if (Content.XamlRoot is not null) _rasterizationScale = Content.XamlRoot.RasterizationScale;
        var scale = _rasterizationScale <= 0 ? 1.0 : _rasterizationScale;
        presenter.PreferredMinimumWidth = (int)Math.Round(960 * scale);
        presenter.PreferredMinimumHeight = (int)Math.Round(640 * scale);
    }

    private void RootElement_Loaded(object sender, RoutedEventArgs e)
    {
        EnforceMinimumWindowSize();
        UpdateTitleBarInsets();
        ApplyPresenterMinimumSize();
    }

    private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange) EnforceMinimumWindowSize();
        UpdateTitleBarInsets();
        ApplyPresenterMinimumSize();
    }

    private void EnforceMinimumWindowSize()
    {
        // Fallback for the ≈960×640 floor (in device-independent units):
        // the overlapped presenter's preferred minimums are the primary
        // enforcement (ApplyPresenterMinimumSize); this clamp covers any
        // state where they are not yet in effect.
        if (_appWindow is null || _enforcingMinimumWindowSize) return;
        if (Content.XamlRoot is not null) _rasterizationScale = Content.XamlRoot.RasterizationScale;
        var minimumWidth = (int)Math.Round(960 * _rasterizationScale);
        var minimumHeight = (int)Math.Round(640 * _rasterizationScale);
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
        ToolTipService.SetToolTip(accentToggle, "Use the accent color Windows is set to. Turn it off to use one of the colors above, or your own hex color.");
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
        ToolTipService.SetToolTip(customHexBox, "Type a hex color such as #F04444");
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
            Text = "Type a hex color such as #F04444. It applies everywhere immediately. In the light theme it is darkened automatically so text stays readable.",
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
                customError.Text = "That is not a valid hex color. Use six hex digits, like #F04444.";
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

        var verifyCopiesToggle = new ToggleSwitch
        {
            Header = "Verify copies with a checksum (slower)",
            IsOn = _verifyCopiesWithChecksum
        };
        AutomationProperties.SetName(verifyCopiesToggle, "Verify copies with a checksum");
        ToolTipService.SetToolTip(verifyCopiesToggle, "After each file is copied, Meridian reads the copy back and compares its SHA-256 checksum with the original. If they differ, the copy is removed and the original is kept. Transfers take longer.");
        verifyCopiesToggle.Toggled += (s, args) =>
        {
            if (verifyCopiesToggle.IsOn == _verifyCopiesWithChecksum) return;
            _verifyCopiesWithChecksum = verifyCopiesToggle.IsOn;
            _ = PersistSettingsAsync();
        };
        var collisionHeader = new TextBlock { Text = "When a name already exists" };
        var collisionPicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        collisionPicker.Items.Add("Ask me each time");
        collisionPicker.Items.Add("Skip the item");
        collisionPicker.Items.Add("Keep both (add a number to the name)");
        collisionPicker.Items.Add("Replace the existing file");
        collisionPicker.Items.Add("Replace only if the incoming file is newer");
        collisionPicker.SelectedIndex = (int)_nameCollisionPolicy;
        AutomationProperties.SetName(collisionPicker, "When a name already exists");
        ToolTipService.SetToolTip(collisionPicker, "What Copy and Move do when the destination already has an item with the same name. Ask me each time pauses the transfer in the Transfers strip and lets you choose for each file.");
        collisionPicker.SelectionChanged += (s, args) =>
        {
            if (collisionPicker.SelectedIndex < 0) return;
            var policy = (CollisionPolicy)collisionPicker.SelectedIndex;
            if (policy == _nameCollisionPolicy) return;
            _nameCollisionPolicy = policy;
            _ = PersistSettingsAsync();
        };
        var collisionNote = new TextBlock
        {
            Text = "Applies to new Copy and Move transfers. Replace overwrites the existing file; Keep both saves the incoming file under a numbered name.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MeridianTextSecondaryBrush")
        };
        var settingsContent = new StackPanel { Spacing = 16, MinWidth = 340 };
        settingsContent.Children.Add(new TextBlock
        {
            Text = "Changes apply immediately and are saved on this PC. System theme follows the Windows app theme and updates when Windows changes it.",
            TextWrapping = TextWrapping.Wrap
        });
        settingsContent.Children.Add(themeButtons);
        settingsContent.Children.Add(densityButtons);
        settingsContent.Children.Add(verifyCopiesToggle);
        settingsContent.Children.Add(collisionHeader);
        settingsContent.Children.Add(collisionPicker);
        settingsContent.Children.Add(collisionNote);
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
        pane.SelectionList.SelectedItems.OfType<FileEntry>().ToList();

    private void LeftItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionStatus(_left);
        RefreshPreviewBlock(_left);
    }

    private void RightItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionStatus(_right);
        RefreshPreviewBlock(_right);
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
        // In the columns layout the counts describe the deepest column (the
        // folder the cascade currently ends in); elsewhere they describe the
        // pane's own listing.
        var allItems = pane.Layout == PaneLayout.Columns && pane.Columns.Count > 0
            ? pane.Columns[^1].AllItems
            : pane.AllItems;
        var query = pane.FilterBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            SetPaneStatus(pane, $"{allItems.Count:N0} items");
            return;
        }
        var matches = DirectoryReader.Filter(allItems, query, pane.SortField, pane.Descending, includeFullPath: false);
        SetPaneStatus(pane, $"{matches.Count:N0} of {allItems.Count:N0} items · filtered");
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
                if (pane.SelectionList.SelectedItems.Count == 1) await RenameSelectedAsync(pane);
                else if (pane.SelectionList.SelectedItems.Count > 1) SetStatus("Select a single item to rename.", StatusSeverity.Warning);
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
        if (isLeftPane)
            UpdateContextMenuItems(pane, isLeftPane, LeftCopyMenuItem, LeftMoveMenuItem, LeftRenameMenuItem, LeftPropertiesMenuItem, LeftShelfMenuItem, LeftDeleteMenuItem);
        else
            UpdateContextMenuItems(pane, isLeftPane, RightCopyMenuItem, RightMoveMenuItem, RightRenameMenuItem, RightPropertiesMenuItem, RightShelfMenuItem, RightDeleteMenuItem);
    }

    /// <summary>Shared by the standard lists' XAML menus and the code-built column
    /// menus, so both layouts enable and label the same operations identically.</summary>
    private void UpdateContextMenuItems(PaneState pane, bool isLeftPane, MenuFlyoutItem copyItem, MenuFlyoutItem moveItem, MenuFlyoutItem renameItem, MenuFlyoutItem propertiesItem, MenuFlyoutItem shelfItem, MenuFlyoutItem deleteItem)
    {
        var other = isLeftPane ? _right : _left;
        var count = pane.SelectionList.SelectedItems.Count;
        var hasDestination = !string.IsNullOrWhiteSpace(other.CurrentPath) && !other.IsHome;
        var direction = isLeftPane ? "right" : "left";
        copyItem.IsEnabled = count > 0 && hasDestination;
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
    private async void LeftPath_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_left, LeftPath.Text); }
        else if (e.Key == VirtualKey.Escape) { e.Handled = true; ExitPathEditMode(_left); }
    }

    private async void RightPath_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_right, RightPath.Text); }
        else if (e.Key == VirtualKey.Escape) { e.Handled = true; ExitPathEditMode(_right); }
    }

    private void LeftPath_LostFocus(object sender, RoutedEventArgs e) => ExitPathEditMode(_left);

    private void RightPath_LostFocus(object sender, RoutedEventArgs e) => ExitPathEditMode(_right);

    // ----- Breadcrumb path bar -----

    /// <summary>The empty stretch of a pane's breadcrumb bar: clicking it
    /// swaps the breadcrumbs for the path text box, and files dropped on it
    /// land in the pane's current folder, like drops on the old path box's
    /// pane targets.</summary>
    private void WireBreadcrumbEditArea(PaneState pane, Border editArea)
    {
        editArea.Tapped += (s, e) => { EnterPathEditMode(pane); e.Handled = true; };
        editArea.AllowDrop = true;
        editArea.DragOver += (s, e) => HandleDropTargetDragOver(e, pane.CurrentPath, list: null);
        editArea.Drop += (s, e) => _ = HandleDropAsync(e, pane.CurrentPath, list: null);
    }

    /// <summary>
    /// Rebuilds a pane's breadcrumb segments from its current path and
    /// returns the bar to breadcrumb mode: every navigation commit and
    /// every failed navigation lands here, so the bar always names the
    /// folder the pane is actually showing. The path text box keeps the
    /// current path as its text (empty at Home, where the placeholder
    /// invites a typed path instead).
    /// </summary>
    private void RebuildBreadcrumbs(PaneState pane)
    {
        pane.EditingPath = false;
        pane.PathBox.Visibility = Visibility.Collapsed;
        pane.PathBox.Text = pane.IsHome ? string.Empty : pane.CurrentPath;
        if (pane.BreadcrumbPanel is not { } panel) return;
        panel.Visibility = Visibility.Visible;
        panel.Children.Clear();
        var segments = BreadcrumbPath.SegmentsFor(pane.CurrentPath, HomePath);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (i > 0)
            {
                panel.Children.Add(new FontIcon
                {
                    Glyph = "\uE76C",
                    FontSize = 9,
                    Foreground = ThemeBrush("MeridianTextTertiaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            var label = new TextBlock
            {
                Text = segment.Label,
                MaxWidth = 110,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = i == segments.Count - 1 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
            };
            var button = new Button
            {
                Content = label,
                Padding = new Thickness(6, 3, 6, 3),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Tag = segment.Path
            };
            ToolTipService.SetToolTip(button, segment.Path);
            AutomationProperties.SetName(button, $"Open {segment.Label}");
            var target = segment.Path;
            button.Click += async (s, e) => await NavigateAsync(pane, target);
            if (!string.Equals(target, HomePath, StringComparison.OrdinalIgnoreCase))
            {
                button.AllowDrop = true;
                button.DragOver += (s, e) => HandleDropTargetDragOver(e, target, list: null);
                button.Drop += (s, e) => _ = HandleDropAsync(e, target, list: null);
            }
            panel.Children.Add(button);
        }
    }

    /// <summary>Swaps a pane's breadcrumbs for its path text box, holding
    /// the full current path (empty at Home). Enter navigates, Escape or
    /// simply moving focus away returns to the breadcrumbs.</summary>
    private void EnterPathEditMode(PaneState pane)
    {
        pane.EditingPath = true;
        if (pane.BreadcrumbPanel is { } panel) panel.Visibility = Visibility.Collapsed;
        pane.PathBox.Text = pane.IsHome ? string.Empty : pane.CurrentPath;
        pane.PathBox.Visibility = Visibility.Visible;
        pane.PathBox.Focus(FocusState.Programmatic);
        pane.PathBox.SelectAll();
    }

    /// <summary>Leaves path editing without navigating: the typed text is
    /// discarded and the breadcrumbs return. A no-op outside edit mode, so
    /// the focus loss that follows a committed navigation is harmless.</summary>
    private void ExitPathEditMode(PaneState pane)
    {
        if (!pane.EditingPath) return;
        pane.EditingPath = false;
        pane.PathBox.Text = pane.IsHome ? string.Empty : pane.CurrentPath;
        pane.PathBox.Visibility = Visibility.Collapsed;
        if (pane.BreadcrumbPanel is { } panel) panel.Visibility = Visibility.Visible;
    }

    // ----- Preview blocks (one per pane, below the file area) -----

    private void LeftPreviewToggle_Click(object sender, RoutedEventArgs e) => TogglePreviewBlock(_left);

    private void RightPreviewToggle_Click(object sender, RoutedEventArgs e) => TogglePreviewBlock(_right);

    private void TogglePreviewBlock(PaneState pane)
    {
        pane.PreviewBlockExpanded = !pane.PreviewBlockExpanded;
        RefreshPreviewBlock(pane);
        _ = PersistSettingsAsync();
    }

    /// <summary>Applies a pane's expanded/collapsed state to its block:
    /// content visibility, the chevron's direction, and the toggle's own
    /// explanation, which names the action the button will take.</summary>
    private void ApplyPreviewBlockVisibility(PaneState pane)
    {
        if (pane.Block is not { } block) return;
        var expanded = pane.PreviewBlockExpanded;
        block.Content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        block.ToggleGlyph.Glyph = expanded ? "\uE70D" : "\uE70E";
        ToolTipService.SetToolTip(block.Toggle, expanded
            ? "Hide this pane's preview and details"
            : "Show this pane's preview and details");
        AutomationProperties.SetName(block.Toggle, expanded
            ? $"Hide preview and details, {pane.Name.ToLowerInvariant()} pane"
            : $"Show preview and details, {pane.Name.ToLowerInvariant()} pane");
    }

    /// <summary>
    /// Refreshes a pane's block after anything it describes changes — the
    /// pane navigated, its selection changed, folder sizes landed in the
    /// cache. The totals header always updates; the preview and details
    /// load only while the block is expanded, and collapsing stops any
    /// load still in flight.
    /// </summary>
    private void RefreshPreviewBlock(PaneState pane)
    {
        UpdatePreviewBlockTotals(pane);
        ApplyPreviewBlockVisibility(pane);
        if (!pane.PreviewBlockExpanded)
        {
            pane.PreviewBlockGeneration++;
            pane.PreviewBlockCancellation?.Cancel();
            return;
        }
        _ = RefreshPreviewBlockAsync(pane);
    }

    /// <summary>The listing a pane's block totals describe: in the columns
    /// layout the deepest column's folder (the convention the pane status
    /// line uses), elsewhere the pane's own listing.</summary>
    private static IReadOnlyList<FileEntry> BlockItems(PaneState pane) =>
        pane.Layout == PaneLayout.Columns && pane.Columns.Count > 0
            ? pane.Columns[^1].AllItems
            : pane.AllItems;

    /// <summary>A folder's already-measured size, or null when it has not
    /// been measured (or has changed since): the totals rule that keeps the
    /// header from ever starting a folder-size walk of its own.</summary>
    private long? CachedFolderSize(FileEntry entry) =>
        _folderSizeCache.TryGet(entry.FullPath, entry.ModifiedUtc, out var cached) ? cached.TotalBytes : null;

    private void UpdatePreviewBlockTotals(PaneState pane)
    {
        if (pane.Block is not { } block) return;
        if (pane.IsHome)
        {
            block.Totals.Text = "Home";
            return;
        }
        var items = BlockItems(pane);
        var selected = GetSelectedEntries(pane);
        var (total, selectedTotal) = PaneTotals.SumSizes(items, selected, CachedFolderSize);
        block.Totals.Text = PaneTotals.FormatHeader(items.Count, total, selected.Count, selectedTotal);
    }

    private async Task RefreshPreviewBlockAsync(PaneState pane)
    {
        if (pane.Block is not { } block) return;
        var generation = ++pane.PreviewBlockGeneration;
        pane.PreviewBlockCancellation?.Cancel();
        pane.PreviewBlockCancellation?.Dispose();
        pane.PreviewBlockCancellation = new CancellationTokenSource();
        var token = pane.PreviewBlockCancellation.Token;

        block.Image.Source = null;
        block.Image.Visibility = Visibility.Collapsed;
        block.Text.Visibility = Visibility.Collapsed;
        block.Glyph.Visibility = Visibility.Visible;
        block.Glyph.Glyph = "\uE946";
        block.Exif.Text = string.Empty;
        block.Exif.Visibility = Visibility.Collapsed;
        ToolTipService.SetToolTip(block.Name, null);

        try
        {
            if (pane.IsHome)
            {
                block.Glyph.Glyph = "\uE80F";
                block.Name.Text = "Home";
                block.Meta.Text = "Pinned folders, drives and recent items";
                return;
            }

            var selected = GetSelectedEntries(pane);
            if (selected.Count > 1)
            {
                var (_, selectedBytes) = PaneTotals.SumSizes([], selected, CachedFolderSize);
                block.Name.Text = $"{selected.Count:N0} items selected";
                block.Meta.Text = $"Size: {DirectoryReader.FormatSize(selectedBytes)}";
                return;
            }

            if (selected.Count == 0)
            {
                ShowCurrentFolderDetails(pane, block);
                return;
            }

            var entry = selected[0];
            FileSystemInfo info = entry.IsDirectory ? new DirectoryInfo(entry.FullPath) : new FileInfo(entry.FullPath);
            info.Refresh();
            if (!info.Exists)
            {
                block.Name.Text = "Details";
                block.Meta.Text = "That item is no longer available.";
                return;
            }

            block.Glyph.Glyph = entry.Glyph;
            block.Name.Text = entry.Name;
            ToolTipService.SetToolTip(block.Name, entry.FullPath);
            var lines = new List<string>
            {
                $"Type: {entry.TypeLabel}",
                $"Created: {info.CreationTime.ToString("g")}",
                $"Modified: {info.LastWriteTime.ToString("g")}"
            };
            if (entry.IsDirectory)
            {
                // The folder treatment the Space quick preview gives a
                // folder: its glyph, then its measured size and counts
                // when the walk finishes.
                block.Meta.Text = string.Join("\n", lines.Append("Size: calculating…"));
                _ = LoadBlockFolderSizeAsync(pane, entry, generation, token, string.Join("\n", lines));
            }
            else
            {
                lines.Add($"Size: {FormatFileSize(((FileInfo)info).Length)}");
                lines.Add($"Opens in: {DefaultAppReader.TryGetFriendlyAppName(entry.FullPath) ?? "No default app found"}");
                block.Meta.Text = string.Join("\n", lines);
                if (Meridian.Core.Viewer.ViewerRouter.Classify(entry.FullPath).Kind == Meridian.Core.Viewer.ViewerKind.Text)
                    _ = LoadBlockTextPreviewAsync(pane, entry, generation);
                else
                    _ = LoadBlockThumbnailAsync(pane, entry, generation);
                _ = LoadBlockExifAsync(pane, entry, generation);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (generation == pane.PreviewBlockGeneration)
            {
                block.Name.Text = "Details";
                block.Meta.Text = "Details could not be read · " + ex.Message;
            }
        }
    }

    /// <summary>The no-selection state: the block describes the folder the
    /// pane is standing in — its name, how much the listing holds, and when
    /// it last changed — with the plain folder treatment as its preview.</summary>
    private void ShowCurrentFolderDetails(PaneState pane, PreviewBlockControls block)
    {
        var path = pane.CurrentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            block.Name.Text = "Details";
            block.Meta.Text = string.Empty;
            return;
        }
        try
        {
            var info = new DirectoryInfo(path);
            info.Refresh();
            block.Glyph.Glyph = "\uE8B7";
            block.Name.Text = string.IsNullOrWhiteSpace(info.Name) ? path : info.Name;
            ToolTipService.SetToolTip(block.Name, path);
            block.Meta.Text = string.Join("\n",
                "Type: Folder",
                $"Contains: {BlockItems(pane).Count:N0} items",
                $"Modified: {info.LastWriteTime.ToString("g")}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            block.Name.Text = "Details";
            block.Meta.Text = "Details could not be read · " + ex.Message;
        }
    }

    private async Task LoadBlockThumbnailAsync(PaneState pane, FileEntry entry, int generation)
    {
        Microsoft.UI.Xaml.Media.Imaging.BitmapImage? bitmap = null;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(entry.FullPath);
            using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 256);
            if (thumbnail is not null)
            {
                bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(thumbnail);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            bitmap = null;
        }

        if (generation != pane.PreviewBlockGeneration || bitmap is null || pane.Block is not { } block) return;
        block.Image.Source = bitmap;
        block.Image.Visibility = Visibility.Visible;
        block.Glyph.Visibility = Visibility.Collapsed;
    }

    /// <summary>A text file's preview: the opening of the file, decoded by
    /// the same bounded reader and decoder the Space quick preview uses. A
    /// file that does not decode as text keeps its glyph instead.</summary>
    private async Task LoadBlockTextPreviewAsync(PaneState pane, FileEntry entry, int generation)
    {
        string? sample = null;
        try
        {
            var bytes = await Meridian.Core.Viewer.SafeFilePreview.ReadPrefixAsync(entry.FullPath, maxBytes: 65536);
            if (Meridian.Core.Viewer.PreviewTextDecoder.TryDecode(bytes, out var decoded))
                sample = decoded.Length <= 800 ? decoded : decoded[..800];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            sample = null;
        }

        if (generation != pane.PreviewBlockGeneration || sample is null || pane.Block is not { } block) return;
        block.Text.Text = sample;
        block.Text.Visibility = Visibility.Visible;
        block.Glyph.Visibility = Visibility.Collapsed;
    }

    private async Task LoadBlockFolderSizeAsync(PaneState pane, FileEntry entry, int generation, CancellationToken token, string baseMeta)
    {
        try
        {
            var result = await FolderSizeCalculator.CalculateAsync(entry.FullPath, token);
            if (generation != pane.PreviewBlockGeneration || token.IsCancellationRequested || pane.Block is not { } block) return;
            block.Meta.Text = baseMeta + $"\nSize: {DirectoryReader.FormatSize(result.TotalBytes)} · {result.FileCount:N0} files in {result.DirectoryCount:N0} folders" +
                (result.SkippedCount > 0 ? $" · {result.SkippedCount:N0} items could not be counted" : string.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (generation == pane.PreviewBlockGeneration && pane.Block is { } block)
                block.Meta.Text = baseMeta + "\nSize: could not be calculated · " + ex.Message;
        }
    }

    private async Task LoadBlockExifAsync(PaneState pane, FileEntry entry, int generation)
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

        if (generation != pane.PreviewBlockGeneration || summary is null || pane.Block is not { } block) return;
        var lines = new List<string>();
        if (summary.PixelWidth is { } width && summary.PixelHeight is { } height)
            lines.Add($"Dimensions: {width:N0} × {height:N0} pixels");
        if (FormatCameraName(summary.CameraMake, summary.CameraModel) is { } camera)
            lines.Add("Camera: " + camera);
        if (summary.DateTaken is { } taken)
            lines.Add("Taken: " + taken.ToString("g"));
        if (summary.FormatCoordinates() is { } coordinates)
            lines.Add("Location recorded in the photo: " + coordinates);
        if (lines.Count == 0) return;
        block.Exif.Text = string.Join("\n", lines);
        block.Exif.Visibility = Visibility.Visible;
    }

    private static string? FormatCameraName(string? make, string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return string.IsNullOrWhiteSpace(make) ? null : make;
        if (string.IsNullOrWhiteSpace(make)) return model;
        return model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model : make + " " + model;
    }

    // ----- Quick preview (Space) -----

    private async Task OpenQuickPreviewAsync(PaneState pane)
    {
        if (_quickPreviewPane is not null) return;
        if (pane.SelectionList.SelectedItem is not FileEntry entry)
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
        var index = pane.SelectionItems.IndexOf(entry);
        QuickPreviewPosition.Text = pane.SelectionItems.Count > 0 && index >= 0
            ? $"Item {index + 1:N0} of {pane.SelectionItems.Count:N0} · {pane.CurrentPath}"
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
        if (pane.SelectionItems.Count == 0) return;
        var current = pane.SelectionList.SelectedItem as FileEntry;
        var index = current is null ? -1 : pane.SelectionItems.IndexOf(current);
        var next = Math.Clamp(index + delta, 0, pane.SelectionItems.Count - 1);
        if (next == index) return;
        var entry = pane.SelectionItems[next];
        pane.SelectionList.SelectedIndex = next;
        pane.SelectionList.ScrollIntoView(entry);
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
        pane.SelectionList.Focus(FocusState.Programmatic);
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

    private enum PaneLayout { Standard, Columns }

    private enum PaneViewMode { Details, Grid, Tiles }

    private static PaneLayout ParsePaneLayout(string? value) =>
        string.Equals(value, nameof(PaneLayout.Columns), StringComparison.OrdinalIgnoreCase)
            ? PaneLayout.Columns
            : PaneLayout.Standard;

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
            AutomationProperties.SetName(selector, "Snapshot");
            ToolTipService.SetToolTip(selector, "Choose which shadow copy snapshot to inspect. Snapshots are read-only.");
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
            AutomationProperties.SetName(inspect, "Inspect current folder in snapshot");
            ToolTipService.SetToolTip(inspect, "List the active pane's folder as it appears in the chosen snapshot. Nothing is changed or restored.");
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

    private sealed class PaneState(string name, TextBox pathBox, TextBox filterBox, ListView list, TextBlock status, Grid filterRow, ScrollViewer homeView, StackPanel homePanel, ScrollViewer columnsView, StackPanel columnsPanel, ToggleButton standardLayoutToggle, ToggleButton columnsLayoutToggle, ToggleButton detailsViewToggle, ToggleButton gridViewToggle, ToggleButton tilesViewToggle, ComboBox groupBox, TreeView folderTree, ToggleButton folderTreeToggle)
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

        /// <summary>The live WatchKit watch over the pane's current folder,
        /// or null when watching is off — at Home (never watched), before the
        /// first navigation, or when WatchKit is unavailable. Owned by the
        /// window (MainWindow.Watching.cs), which restarts it on navigation
        /// and disposes it at window close.</summary>
        public FolderWatchService? Watch { get; set; }

        /// <summary>True while the pane shows the Home page instead of a folder.</summary>
        public bool IsHome => string.Equals(CurrentPath, HomePath, StringComparison.Ordinal);

        // ----- Miller columns state (unused while Layout is Standard) -----

        public ScrollViewer ColumnsView { get; } = columnsView;
        public StackPanel ColumnsPanel { get; } = columnsPanel;
        public ToggleButton StandardLayoutToggle { get; } = standardLayoutToggle;
        public ToggleButton ColumnsLayoutToggle { get; } = columnsLayoutToggle;
        public ToggleButton DetailsViewToggle { get; } = detailsViewToggle;
        public ToggleButton GridViewToggle { get; } = gridViewToggle;
        public ToggleButton TilesViewToggle { get; } = tilesViewToggle;
        public ComboBox GroupBox { get; } = groupBox;

        // ----- Folder tree state -----

        public TreeView FolderTree { get; } = folderTree;
        public ToggleButton FolderTreeToggle { get; } = folderTreeToggle;

        /// <summary>Whether the user asked for this pane's folder tree. The
        /// tree itself also hides at Home, which has no drive root.</summary>
        public bool FolderTreeVisible { get; set; }

        /// <summary>The drive root the live tree is built from; null when no tree is built.</summary>
        public string? FolderTreeRootPath { get; set; }

        /// <summary>The root node of the live tree, or null when no tree is built.</summary>
        public TreeViewNode? FolderTreeRootItem { get; set; }

        /// <summary>Nodes whose children have been loaded (or probed empty), so
        /// expansion and follow-sync never read the same folder twice.</summary>
        public HashSet<TreeViewNode> FolderTreeLoadedItems { get; } = [];

        /// <summary>True while the tree is being expanded and selected to follow
        /// the pane, so those selection changes are not mistaken for user picks.</summary>
        public bool SyncingFolderTree { get; set; }

        /// <summary>Bumped whenever the tree is rebuilt, so a stale asynchronous
        /// child load can tell it has been superseded.</summary>
        public int FolderTreeGeneration { get; set; }

        // ----- Breadcrumb bar + preview block state -----

        /// <summary>The panel this pane's breadcrumb segment buttons are
        /// built into; assigned after construction from the named XAML.</summary>
        public StackPanel? BreadcrumbPanel { get; set; }

        /// <summary>True while the pane's path text box replaces its
        /// breadcrumbs for typed-path editing.</summary>
        public bool EditingPath { get; set; }

        /// <summary>This pane's preview block controls; assigned after
        /// construction from the named XAML.</summary>
        public PreviewBlockControls? Block { get; set; }

        /// <summary>Whether this pane's preview block is expanded. Resolved
        /// at startup by PreviewBlockMigration and saved per pane.</summary>
        public bool PreviewBlockExpanded { get; set; }

        /// <summary>Bumped whenever the block refreshes, so a stale
        /// asynchronous preview load can tell it has been superseded.</summary>
        public int PreviewBlockGeneration { get; set; }

        /// <summary>Cancels the block's in-flight folder-size walk.</summary>
        public CancellationTokenSource? PreviewBlockCancellation { get; set; }

        // ----- View mode state (standard layout only; the columns layout is unaffected) -----

        /// <summary>Which view mode the standard layout renders: Details, Grid, or Tiles.</summary>
        public PaneViewMode ViewMode { get; set; } = PaneViewMode.Details;

        /// <summary>How the standard list is grouped; off by default.</summary>
        public GroupMode Grouping { get; set; } = GroupMode.None;

        /// <summary>The Details columns currently shown, narrowed by the pane's width.</summary>
        public IReadOnlyList<DetailsColumn> VisibleColumns { get; set; } = AdaptiveColumns.VisibleForWidth(double.PositiveInfinity);

        /// <summary>The groups the list shows while grouping is on; rebuilt on every filter pass.</summary>
        public ObservableCollection<PaneItemGroup> Groups { get; } = [];

        /// <summary>Grouped view over <see cref="Groups"/>; the list binds to it while grouping is on.</summary>
        public Microsoft.UI.Xaml.Data.CollectionViewSource GroupSource { get; } = new();

        /// <summary>Cancels the in-flight background folder-size measurement.</summary>
        public CancellationTokenSource? FolderSizeCancellation { get; set; }

        /// <summary>Identifies the folder set the current measurement run covers, so
        /// a re-filter over the same folders does not restart it.</summary>
        public string? FolderSizeRunKey { get; set; }

        /// <summary>Which layout the pane renders: the standard list or Miller columns.</summary>
        public PaneLayout Layout { get; set; } = PaneLayout.Standard;

        /// <summary>The column chain the columns view renders; null outside the columns layout.</summary>
        public ColumnChain? Chain { get; set; }

        /// <summary>The live columns, in depth order (index == depth).</summary>
        public List<ColumnViewState> Columns { get; } = [];

        /// <summary>True while columns are being rebuilt or re-filtered, so the
        /// selection changes that work causes are not mistaken for user cascades.</summary>
        public bool RebuildingColumns { get; set; }

        /// <summary>Bumped whenever columns are rebuilt or a cascade starts, so a
        /// stale asynchronous column load can tell it has been superseded.</summary>
        public int ColumnsGeneration { get; set; }

        public CancellationTokenSource? ColumnsCancellation { get; set; }
        public Border? PreviewRoot { get; set; }
        public PreviewMediaLease? PreviewLease { get; set; }
        public CancellationTokenSource? PreviewCancellation { get; set; }

        /// <summary>An item path a "show location" reveal wants selected in
        /// whichever column lists it, once that column has loaded.</summary>
        public string? PendingRevealPath { get; set; }

        /// <summary>The column that owns the pane's selection in the columns
        /// layout: the deepest column with a selection, falling back to the
        /// deepest column (whose empty selection simply reads as no selection).</summary>
        public ColumnViewState? SelectionColumn
        {
            get
            {
                for (var i = Columns.Count - 1; i >= 0; i--)
                    if (Columns[i].List.SelectedItem is not null) return Columns[i];
                return Columns.Count > 0 ? Columns[^1] : null;
            }
        }

        /// <summary>The list operations should read the selection from: the
        /// standard list, or the selection-owning column in the columns layout.
        /// Every operation handler goes through here, so both layouts share one
        /// implementation of copy/move/delete/rename/properties/preview.</summary>
        public ListView SelectionList => Layout == PaneLayout.Columns && SelectionColumn is { } column ? column.List : List;

        /// <summary>The item collection matching <see cref="SelectionList"/>,
        /// used by the quick preview for position and stepping.</summary>
        public ObservableCollection<FileEntry> SelectionItems => Layout == PaneLayout.Columns && SelectionColumn is { } column ? column.Items : Items;
    }

    /// <summary>One live Miller column: the folder it lists, its list control,
    /// and its own read state, kept apart from the pure <see cref="ColumnChain"/>
    /// model so the model stays UI-free.</summary>
    private sealed class ColumnViewState(string folderPath, ListView list, TextBlock errorBlock)
    {
        public string FolderPath { get; } = folderPath;
        public ListView List { get; } = list;
        public TextBlock ErrorBlock { get; } = errorBlock;
        public int Depth { get; set; }
        public Border? Root { get; set; }
        public ObservableCollection<FileEntry> Items { get; } = [];
        public IReadOnlyList<FileEntry> AllItems { get; set; } = Array.Empty<FileEntry>();
        public CancellationTokenSource? ReadCancellation { get; set; }
    }
}

/// <summary>One folder shown in a pane's folder tree: its display name and
/// full path. Expansion state and children live on the TreeViewNode that
/// wraps it, so this stays a plain display item for the item template.</summary>
public sealed class FolderTreeItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
}

/// <summary>The named controls of one pane's preview block, gathered so the
/// block logic addresses a pane's set as one value instead of ten fields.
/// Content is the collapsible details area; the header row (totals +
/// toggle) stays visible either way.</summary>
public sealed record PreviewBlockControls(
    Grid Content,
    TextBlock Totals,
    Button Toggle,
    FontIcon ToggleGlyph,
    FontIcon Glyph,
    Image Image,
    TextBlock Text,
    TextBlock Name,
    TextBlock Meta,
    TextBlock Exif);
