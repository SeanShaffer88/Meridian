using Meridian.Services;

namespace Meridian.Core.Transfers;

/// <summary>One progress snapshot from a running job: bytes and items done against the job's totals.</summary>
public sealed record TransferProgress(
    Guid JobId, long BytesCopied, long TotalBytes, int ItemsCompleted, int TotalItems, string? CurrentSource);

/// <summary>
/// Runs transfer jobs one item at a time over an <see cref="ITransferFileOperations"/>.
/// Copies are staged to a ".meridian-*.partial" file beside the destination and published
/// by rename only when complete (and, when the job asks, after a SHA-256 verify of the
/// staged bytes), so a destination never shows a half-written file. Pause is cooperative:
/// the copy loop checks a pause gate between chunks and between items, mid-file included.
/// Cancel removes the in-flight staging file and leaves completed items in place.
/// A move is always copy-then-delete per item — never a rename shortcut — so the same
/// verified path serves same-volume and cross-volume moves; a source is deleted only
/// after its copy is published (and verified, when requested). One item's failure is
/// recorded on the item and never aborts the job.
/// </summary>
public sealed class TransferExecutor
{
    private const int BufferSize = 128 * 1024;

    private readonly ITransferFileOperations _files;
    private readonly object _gateLock = new();
    private CancellationTokenSource? _runCancellation;
    private TaskCompletionSource? _resumeSignal;
    private bool _pauseRequested;

    public TransferExecutor(ITransferFileOperations? files = null) =>
        _files = files ?? new SystemTransferFileOperations();

    /// <summary>Asks the running job to pause at its next checkpoint. No-op when nothing is running.</summary>
    public void Pause()
    {
        lock (_gateLock)
        {
            if (_runCancellation is null) return;
            _pauseRequested = true;
            _resumeSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Releases a paused job so it continues from the checkpoint it stopped at.</summary>
    public void Resume()
    {
        TaskCompletionSource? signal;
        lock (_gateLock)
        {
            _pauseRequested = false;
            signal = _resumeSignal;
            _resumeSignal = null;
        }
        signal?.TrySetResult();
    }

    /// <summary>Cancels the running job, waking it first if it is paused. No-op when nothing is running.</summary>
    public void Cancel()
    {
        CancellationTokenSource? run;
        lock (_gateLock)
        {
            run = _runCancellation;
        }
        Resume();
        run?.Cancel();
    }

    /// <summary>
    /// Runs (or resumes) a job to its next stopping point: a terminal state, or
    /// WaitingOnConflict when Ask-policy items are parked for the user's decision.
    /// Resolve those items with <see cref="TransferItem.ResolveConflict"/> and call again
    /// to continue. A fresh Copy or Move job's destination folder must already exist;
    /// jobs carrying pre-built items create the folders their items need.
    /// </summary>
    public async Task<TransferJob> ExecuteAsync(
        TransferJob job, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.IsTerminal)
            throw new InvalidOperationException("This job has already finished and cannot run again.");
        if (job.State == TransferJobState.Running)
            throw new InvalidOperationException("This job is already running.");
        if (job.Items.Count == 0 && job.Sources.Count == 0)
            throw new InvalidOperationException("The job has nothing to transfer.");
        if (job.Items.Count == 0 && job.Kind != TransferJobKind.Delete)
        {
            // Fresh jobs fail fast on a bad destination. Jobs that already carry
            // their items (a resumed queue entry, an undo plan) are self-describing:
            // each item creates the folder it needs, so a destination folder that
            // vanished — or one the job itself recreates — is not a reason to refuse.
            if (string.IsNullOrWhiteSpace(job.DestinationFolder))
                throw new ArgumentException("Copy and Move jobs need a destination folder.", nameof(job));
            if (!_files.DirectoryExists(job.DestinationFolder))
                throw new DirectoryNotFoundException($"The destination folder does not exist: {job.DestinationFolder}");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gateLock)
        {
            _runCancellation = linked;
            _pauseRequested = false;
            _resumeSignal = null;
        }

        job.TransitionTo(TransferJobState.Running);
        try
        {
            if (job.Items.Count == 0)
            {
                if (job.Kind == TransferJobKind.Delete) BuildDeleteItems(job);
                else Expand(job);
            }
            ReportProgress(job, progress, null);

            foreach (var item in job.Items)
            {
                await ProcessItemAsync(job, item, progress, linked.Token);
                ReportProgress(job, progress, item.SourcePath);
            }

            if (job.Kind == TransferJobKind.Move) RemoveEmptiedSourceFolders(job);
            FinishJob(job);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            foreach (var item in job.Items)
            {
                if (item.State is TransferItemState.Pending or TransferItemState.InProgress
                    or TransferItemState.WaitingOnConflict)
                    item.State = TransferItemState.Cancelled;
            }
            if (job.State is TransferJobState.Running or TransferJobState.Paused)
                job.TransitionTo(TransferJobState.Cancelled);
        }
        finally
        {
            lock (_gateLock)
            {
                _runCancellation = null;
                _pauseRequested = false;
                _resumeSignal = null;
            }
        }
        return job;
    }

    // ----- expansion -------------------------------------------------------

    private void Expand(TransferJob job)
    {
        foreach (var rawSource in job.Sources)
        {
            string source;
            try
            {
                source = Path.GetFullPath(rawSource);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                job.Items.Add(new TransferItem
                {
                    SourcePath = rawSource,
                    State = TransferItemState.Failed,
                    Error = "The source path is not valid.",
                });
                continue;
            }

            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
            var plannedDestination = string.IsNullOrEmpty(name)
                ? job.DestinationFolder
                : Path.Combine(job.DestinationFolder, name);

            if (_files.DirectoryExists(source))
            {
                if (IsSameOrDescendant(source, job.DestinationFolder))
                {
                    job.Items.Add(new TransferItem
                    {
                        SourcePath = source,
                        DestinationPath = plannedDestination,
                        IsDirectory = true,
                        State = TransferItemState.Failed,
                        Error = "A folder cannot be copied into itself or one of its descendants.",
                    });
                    continue;
                }
                if (FileOperations.FindReparsePointAncestor(source) is { } linked)
                {
                    job.Items.Add(new TransferItem
                    {
                        SourcePath = source,
                        DestinationPath = plannedDestination,
                        IsDirectory = true,
                        State = TransferItemState.Failed,
                        Error = $"File operations through symbolic links and other reparse points are not supported: {linked}",
                    });
                    continue;
                }
                AddDirectoryTree(job, source, plannedDestination);
            }
            else if (_files.FileExists(source))
            {
                if (FileOperations.FindReparsePointAncestor(source) is { } linked)
                {
                    job.Items.Add(new TransferItem
                    {
                        SourcePath = source,
                        DestinationPath = plannedDestination,
                        State = TransferItemState.Failed,
                        Error = $"File operations through symbolic links and other reparse points are not supported: {linked}",
                    });
                    continue;
                }
                var facts = _files.GetFileFacts(source);
                job.Items.Add(new TransferItem
                {
                    SourcePath = source,
                    DestinationPath = plannedDestination,
                    Size = facts?.Size ?? 0,
                    SourceLastWriteUtc = facts?.LastWriteUtc,
                });
            }
            else
            {
                job.Items.Add(new TransferItem
                {
                    SourcePath = source,
                    DestinationPath = plannedDestination,
                    State = TransferItemState.Failed,
                    Error = "The source no longer exists.",
                });
            }
        }
    }

    private void AddDirectoryTree(TransferJob job, string sourceDirectory, string destinationDirectory)
    {
        var directoryItem = new TransferItem
        {
            SourcePath = sourceDirectory,
            DestinationPath = destinationDirectory,
            IsDirectory = true,
        };
        job.Items.Add(directoryItem);

        List<string> files;
        List<string> subdirectories;
        try
        {
            files = _files.EnumerateFiles(sourceDirectory).OrderBy(path => path, StringComparer.Ordinal).ToList();
            subdirectories = _files.EnumerateDirectories(sourceDirectory).OrderBy(path => path, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            directoryItem.State = TransferItemState.Failed;
            directoryItem.Error = $"The folder could not be read: {ex.Message}";
            return;
        }

        foreach (var file in files)
        {
            var destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
            if (IsReparsePoint(file))
            {
                job.Items.Add(new TransferItem
                {
                    SourcePath = file,
                    DestinationPath = destination,
                    State = TransferItemState.Failed,
                    Error = "Symbolic links are not followed by the transfer engine.",
                });
                continue;
            }
            var facts = _files.GetFileFacts(file);
            job.Items.Add(new TransferItem
            {
                SourcePath = file,
                DestinationPath = destination,
                Size = facts?.Size ?? 0,
                SourceLastWriteUtc = facts?.LastWriteUtc,
            });
        }

        foreach (var subdirectory in subdirectories)
        {
            var destination = Path.Combine(destinationDirectory, Path.GetFileName(subdirectory));
            if (IsReparsePoint(subdirectory))
            {
                job.Items.Add(new TransferItem
                {
                    SourcePath = subdirectory,
                    DestinationPath = destination,
                    IsDirectory = true,
                    State = TransferItemState.Failed,
                    Error = "Symbolic links are not followed by the transfer engine.",
                });
                continue;
            }
            AddDirectoryTree(job, subdirectory, destination);
        }
    }

    private void BuildDeleteItems(TransferJob job)
    {
        foreach (var rawSource in job.Sources)
        {
            string source;
            try
            {
                source = Path.GetFullPath(rawSource);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                job.Items.Add(new TransferItem
                {
                    SourcePath = rawSource,
                    State = TransferItemState.Failed,
                    Error = "The path is not valid.",
                });
                continue;
            }

            var isDirectory = _files.DirectoryExists(source);
            if (!isDirectory && !_files.FileExists(source))
            {
                job.Items.Add(new TransferItem
                {
                    SourcePath = source,
                    State = TransferItemState.Failed,
                    Error = "The item to delete no longer exists.",
                });
                continue;
            }
            job.Items.Add(new TransferItem { SourcePath = source, IsDirectory = isDirectory });
        }
    }

    // ----- per-item processing ----------------------------------------------

    private async Task ProcessItemAsync(
        TransferJob job, TransferItem item, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        if (item.State is not (TransferItemState.Pending or TransferItemState.InProgress))
            return;

        await CheckpointAsync(job, cancellationToken);

        if (job.Kind == TransferJobKind.Delete)
        {
            ProcessDeleteItem(item);
            return;
        }
        if (item.IsDirectory)
        {
            ProcessDirectoryItem(item);
            return;
        }
        await ProcessFileItemAsync(job, item, progress, cancellationToken);
    }

    private void ProcessDeleteItem(TransferItem item)
    {
        try
        {
            if (_files.DirectoryExists(item.SourcePath))
                _files.DeleteDirectory(item.SourcePath, recursive: true);
            else if (_files.FileExists(item.SourcePath))
                _files.DeleteFile(item.SourcePath);
            else
            {
                item.State = TransferItemState.Failed;
                item.Error = "The item to delete no longer exists.";
                return;
            }
            item.State = TransferItemState.Completed;
            item.Error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            item.State = TransferItemState.Failed;
            item.Error = ex.Message;
        }
    }

    private void ProcessDirectoryItem(TransferItem item)
    {
        // Folder collisions merge: an existing destination folder is the normal case.
        try
        {
            if (_files.FileExists(item.DestinationPath!))
            {
                item.State = TransferItemState.Failed;
                item.Error = "A file already exists where this folder needs to go.";
                return;
            }
            if (!_files.DirectoryExists(item.DestinationPath!))
                _files.CreateDirectory(item.DestinationPath!);
            item.State = TransferItemState.Completed;
            item.Error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            item.State = TransferItemState.Failed;
            item.Error = ex.Message;
        }
    }

    private async Task ProcessFileItemAsync(
        TransferJob job, TransferItem item, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        var sourceFacts = _files.GetFileFacts(item.SourcePath);
        if (sourceFacts is null)
        {
            item.State = TransferItemState.Failed;
            item.Error = _files.DirectoryExists(item.SourcePath)
                ? "The source is a folder, not a file."
                : "The source file no longer exists.";
            return;
        }
        if (string.Equals(item.SourcePath, item.DestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            item.State = TransferItemState.Failed;
            item.Error = "The source and destination are the same file.";
            return;
        }
        if (item.DestinationPath is null)
        {
            item.State = TransferItemState.Failed;
            item.Error = "The item has no destination.";
            return;
        }
        if (_files.DirectoryExists(item.DestinationPath))
        {
            item.State = TransferItemState.Failed;
            item.Error = "A folder with this name already exists in the destination.";
            return;
        }

        item.Size = sourceFacts.Size;
        item.SourceLastWriteUtc = sourceFacts.LastWriteUtc;

        var decision = ResolveCollision(job, item, sourceFacts);
        switch (decision.Kind)
        {
            case CollisionDecisionKind.NeedsUserChoice:
                item.State = TransferItemState.WaitingOnConflict;
                item.Error = null;
                return;
            case CollisionDecisionKind.Skip:
                item.State = TransferItemState.Skipped;
                item.Error = null;
                return;
            case CollisionDecisionKind.Rename:
                item.DestinationPath = decision.NewDestinationPath;
                break;
        }
        var overwrite = decision.Kind == CollisionDecisionKind.Replace;

        item.State = TransferItemState.InProgress;
        item.BytesCopied = 0;
        var destinationDirectory = Path.GetDirectoryName(item.DestinationPath!)!;
        var stagingPath = Path.Combine(destinationDirectory, $".meridian-{Guid.NewGuid():N}.partial");
        var staged = false;
        try
        {
            if (!_files.DirectoryExists(destinationDirectory))
                _files.CreateDirectory(destinationDirectory);

            staged = true; // from here on, any exit before publication must remove the staging file
            await CopyToStagingAsync(job, item, stagingPath, progress, cancellationToken);

            if (job.VerifyAfterCopy)
            {
                await CheckpointAsync(job, cancellationToken);
                string sourceHash, stagedHash;
                using (var sourceStream = _files.OpenRead(item.SourcePath))
                    sourceHash = await FileHash.Sha256HexAsync(sourceStream, cancellationToken);
                using (var stagedStream = _files.OpenRead(stagingPath))
                    stagedHash = await FileHash.Sha256HexAsync(stagedStream, cancellationToken);
                if (!string.Equals(sourceHash, stagedHash, StringComparison.Ordinal))
                {
                    TryDeleteFile(stagingPath);
                    item.State = TransferItemState.Failed;
                    item.Error = "The copy did not match the original when checked (SHA-256), so it was removed. The original is unchanged.";
                    return;
                }
            }

            _files.PublishFile(stagingPath, item.DestinationPath!, overwrite);
            staged = false;
            _files.SetLastWriteUtc(item.DestinationPath!, sourceFacts.LastWriteUtc);
            var written = _files.GetFileFacts(item.DestinationPath!);
            item.DestinationSize = written?.Size ?? sourceFacts.Size;
            item.DestinationLastWriteUtc = written?.LastWriteUtc ?? sourceFacts.LastWriteUtc;
            item.BytesCopied = item.Size;
            item.State = TransferItemState.Completed;
            item.Error = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (staged) TryDeleteFile(stagingPath);
            item.State = TransferItemState.Cancelled;
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            if (staged) TryDeleteFile(stagingPath);
            item.State = TransferItemState.Failed;
            item.Error = ex.Message;
            return;
        }

        if (job.Kind == TransferJobKind.Move)
        {
            try
            {
                _files.DeleteFile(item.SourcePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                item.State = TransferItemState.Failed;
                item.Error = $"The file was copied, but the original could not be removed: {ex.Message}";
            }
        }
    }

    private CollisionDecision ResolveCollision(TransferJob job, TransferItem item, FileFacts sourceFacts)
    {
        if (item.CollisionOverride is { } userDecision)
        {
            return userDecision switch
            {
                CollisionDecisionKind.Replace => new CollisionDecision(CollisionDecisionKind.Replace, null,
                    "You chose to replace the existing file."),
                CollisionDecisionKind.Skip => new CollisionDecision(CollisionDecisionKind.Skip, null,
                    "You chose to skip this file."),
                // A rename was already applied to DestinationPath when the conflict was resolved.
                _ => new CollisionDecision(CollisionDecisionKind.Proceed, null,
                    "You chose a new name for this file."),
            };
        }

        // The caller has already established the destination is not a folder.
        var destinationFacts = _files.GetFileFacts(item.DestinationPath!);
        return CollisionResolver.Resolve(
            job.CollisionPolicy,
            item.DestinationPath!,
            sourceFacts,
            destinationFacts,
            isDirectory: false,
            candidate => _files.FileExists(candidate) || _files.DirectoryExists(candidate));
    }

    private async Task CopyToStagingAsync(
        TransferJob job, TransferItem item, string stagingPath,
        IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        using var input = _files.OpenRead(item.SourcePath);
        using var output = _files.CreateFile(stagingPath);
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            item.BytesCopied += read;
            ReportProgress(job, progress, item.SourcePath);
            await CheckpointAsync(job, cancellationToken);
        }
        await output.FlushAsync(cancellationToken);
    }

    // ----- completion --------------------------------------------------------

    private void RemoveEmptiedSourceFolders(TransferJob job)
    {
        foreach (var directoryItem in job.Items.Where(item => item.IsDirectory).Reverse())
        {
            if (directoryItem.State != TransferItemState.Completed) continue;
            var prefix = Path.TrimEndingDirectorySeparator(directoryItem.SourcePath) + Path.DirectorySeparatorChar;
            var allFilesMoved = job.Items
                .Where(item => !item.IsDirectory && item.SourcePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .All(item => item.State == TransferItemState.Completed);
            if (!allFilesMoved) continue;
            try
            {
                if (_files.DirectoryExists(directoryItem.SourcePath))
                    _files.DeleteDirectory(directoryItem.SourcePath, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that will not go (still in use, or something new landed
                // in it) is simply left in place; the move itself already succeeded.
            }
        }
    }

    private static void FinishJob(TransferJob job)
    {
        if (job.Items.Any(item => item.State == TransferItemState.WaitingOnConflict))
        {
            job.TransitionTo(TransferJobState.WaitingOnConflict);
            return;
        }
        var failed = job.Items.Count(item => item.State == TransferItemState.Failed);
        var completed = job.Items.Count(item => item.State == TransferItemState.Completed);
        if (failed > 0 && completed == 0)
            job.TransitionTo(TransferJobState.Failed);
        else if (failed > 0)
            job.TransitionTo(TransferJobState.CompletedWithErrors);
        else
            job.TransitionTo(TransferJobState.Completed);
    }

    // ----- helpers -------------------------------------------------------------

    private async Task CheckpointAsync(TransferJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? waitForResume;
        lock (_gateLock)
        {
            waitForResume = _pauseRequested ? _resumeSignal?.Task : null;
        }
        if (waitForResume is null) return;

        if (job.State == TransferJobState.Running)
            job.TransitionTo(TransferJobState.Paused);
        await waitForResume.WaitAsync(cancellationToken);
        if (job.State == TransferJobState.Paused)
            job.TransitionTo(TransferJobState.Running);
    }

    private static bool IsSameOrDescendant(string sourceDirectory, string candidate)
    {
        var relative = Path.GetRelativePath(sourceDirectory, candidate);
        return !Path.IsPathRooted(relative) &&
            (relative == "." || (relative != ".." &&
             !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (_files.FileExists(path)) _files.DeleteFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preserve the original outcome; a leftover staging file is visible
            // and named .partial, never mistaken for a finished copy.
        }
    }

    private static void ReportProgress(TransferJob job, IProgress<TransferProgress>? progress, string? currentSource)
    {
        if (progress is null) return;
        try
        {
            progress.Report(new TransferProgress(
                job.Id, job.BytesCopied, job.TotalBytes, job.CompletedItems, job.TotalItems, currentSource));
        }
        catch
        {
            // Progress is observational; it must never change the outcome of a transfer.
        }
    }
}
