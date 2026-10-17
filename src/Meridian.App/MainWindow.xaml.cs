using System.Collections.ObjectModel;
using Meridian.Models;
using Meridian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Meridian;

public sealed partial class MainWindow : Window
{
    private readonly PaneState _left;
    private readonly PaneState _right;
    private readonly WorkspaceStore _workspaceStore = new();
    private PaneState _activePane;
    private bool _restoringWorkspace;

    public MainWindow()
    {
        InitializeComponent();
        _left = new PaneState("Left", LeftPath, LeftItems, LeftStatus);
        _right = new PaneState("Right", RightPath, RightItems, RightStatus);
        _activePane = _left;
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
        var saved = await _workspaceStore.LoadAsync();
        var leftPath = saved?.LeftPath is { Length: > 0 } savedLeft && Directory.Exists(savedLeft) ? savedLeft : documents;
        var rightPath = saved?.RightPath is { Length: > 0 } savedRight && Directory.Exists(savedRight) ? savedRight : downloads;

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

    private async Task PersistWorkspaceAsync()
    {
        if (_restoringWorkspace) return;
        try
        {
            await _workspaceStore.SaveAsync(new WorkspaceSnapshot(_left.CurrentPath, _right.CurrentPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Workspace persistence is a convenience; file browsing must continue if it fails.
            GlobalStatus.Text = "Workspace could not be saved · " + ex.Message;
        }
    }

    private async Task NavigateAsync(PaneState pane, string requestedPath, bool addHistory = true)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath.Trim().Trim('"')));
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Folder not found: {fullPath}");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SetPaneStatus(pane, ex.Message); GlobalStatus.Text = "Navigation failed · " + ex.Message; pane.PathBox.Text = pane.CurrentPath; return;
        }
        if (addHistory && !string.IsNullOrWhiteSpace(pane.CurrentPath) && !string.Equals(pane.CurrentPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            pane.BackHistory.Push(pane.CurrentPath); pane.ForwardHistory.Clear();
        }
        pane.ReadCancellation?.Cancel(); pane.ReadCancellation?.Dispose(); pane.ReadCancellation = new CancellationTokenSource();
        var token = pane.ReadCancellation.Token;
        pane.CurrentPath = fullPath; pane.PathBox.Text = fullPath; SetPaneStatus(pane, "Loading folder…");
        try
        {
            var entries = await Task.Run(() => DirectoryReader.Read(fullPath, token), token);
            if (token.IsCancellationRequested) return;
            pane.Items.Clear(); foreach (var entry in entries) pane.Items.Add(entry);
            SetPaneStatus(pane, $"{entries.Count:N0} items"); GlobalStatus.Text = "Ready · Local-only · No telemetry";
            if (!_restoringWorkspace) await PersistWorkspaceAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { SetPaneStatus(pane, ex.Message); GlobalStatus.Text = "Could not read folder · " + ex.Message; }
    }

    private async Task NavigateUpAsync(PaneState pane)
    {
        if (string.IsNullOrWhiteSpace(pane.CurrentPath)) return;
        var parent = Directory.GetParent(pane.CurrentPath)?.FullName;
        if (parent is not null) await NavigateAsync(pane, parent);
    }
    private async Task NavigateBackAsync(PaneState pane)
    {
        if (pane.BackHistory.Count == 0) return;
        var target = pane.BackHistory.Pop();
        if (!string.IsNullOrWhiteSpace(pane.CurrentPath)) pane.ForwardHistory.Push(pane.CurrentPath);
        await NavigateAsync(pane, target, addHistory: false);
    }
    private async Task NavigateForwardAsync(PaneState pane)
    {
        if (pane.ForwardHistory.Count == 0) return;
        var target = pane.ForwardHistory.Pop();
        if (!string.IsNullOrWhiteSpace(pane.CurrentPath)) pane.BackHistory.Push(pane.CurrentPath);
        await NavigateAsync(pane, target, addHistory: false);
    }
    private async Task OpenSelectedAsync(PaneState pane)
    {
        if (pane.List.SelectedItem is not FileEntry selected) return;
        if (selected.IsDirectory) { await NavigateAsync(pane, selected.FullPath); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = selected.FullPath, UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { GlobalStatus.Text = "Could not open file · " + ex.Message; }
    }
    private async Task CopySelectedAsync(PaneState source, PaneState destination)
    {
        if (source.List.SelectedItem is not FileEntry selected) { GlobalStatus.Text = $"Select an item in the {source.Name.ToLowerInvariant()} pane first."; return; }
        if (string.IsNullOrWhiteSpace(destination.CurrentPath)) return;
        if (string.Equals(Path.GetFullPath(selected.FullPath), Path.GetFullPath(destination.CurrentPath), StringComparison.OrdinalIgnoreCase))
        { GlobalStatus.Text = "The destination is the selected folder. Choose another destination."; return; }
        var destinationPath = destination.CurrentPath;
        GlobalStatus.Text = $"Copying {selected.Name}…"; SetPaneStatus(source, "Copy in progress…");
        try
        {
            var copiedTo = await Task.Run(() => FileOperations.CopyItem(selected.FullPath, destinationPath));
            if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase)) await NavigateAsync(destination, destinationPath, addHistory: false);
            SetPaneStatus(source, "Ready"); GlobalStatus.Text = $"Copied successfully · {Path.GetFileName(copiedTo)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { SetPaneStatus(source, "Copy failed"); GlobalStatus.Text = "Copy failed · " + ex.Message; }
    }

    private static void SetPaneStatus(PaneState pane, string message) => pane.Status.Text = message;
    private void LeftItems_PointerPressed(object sender, PointerRoutedEventArgs e) => _activePane = _left;
    private void RightItems_PointerPressed(object sender, PointerRoutedEventArgs e) => _activePane = _right;
    private async void LeftUp_Click(object sender, RoutedEventArgs e) => await NavigateUpAsync(_left);
    private async void RightUp_Click(object sender, RoutedEventArgs e) => await NavigateUpAsync(_right);
    private async void LeftBack_Click(object sender, RoutedEventArgs e) => await NavigateBackAsync(_left);
    private async void RightBack_Click(object sender, RoutedEventArgs e) => await NavigateBackAsync(_right);
    private async void LeftForward_Click(object sender, RoutedEventArgs e) => await NavigateForwardAsync(_left);
    private async void RightForward_Click(object sender, RoutedEventArgs e) => await NavigateForwardAsync(_right);
    private async void CopyLeftToRight_Click(object sender, RoutedEventArgs e) => await CopySelectedAsync(_left, _right);
    private async void CopyRightToLeft_Click(object sender, RoutedEventArgs e) => await CopySelectedAsync(_right, _left);
    private async void RefreshBoth_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_left.CurrentPath)) await NavigateAsync(_left, _left.CurrentPath, addHistory: false);
        if (!string.IsNullOrWhiteSpace(_right.CurrentPath)) await NavigateAsync(_right, _right.CurrentPath, addHistory: false);
    }
    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{_activePane.CurrentPath}\"", UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { GlobalStatus.Text = "Could not open Explorer · " + ex.Message; }
    }
    private async void LeftItems_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenSelectedAsync(_left);
    private async void RightItems_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenSelectedAsync(_right);
    private async void LeftPath_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_left, LeftPath.Text); } }
    private async void RightPath_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_right, RightPath.Text); } }

    private sealed class PaneState(string name, TextBox pathBox, ListView list, TextBlock status)
    {
        public string Name { get; } = name;
        public TextBox PathBox { get; } = pathBox;
        public ListView List { get; } = list;
        public TextBlock Status { get; } = status;
        public ObservableCollection<FileEntry> Items { get; } = [];
        public Stack<string> BackHistory { get; } = new();
        public Stack<string> ForwardHistory { get; } = new();
        public string CurrentPath { get; set; } = string.Empty;
        public CancellationTokenSource? ReadCancellation { get; set; }
    }
}
