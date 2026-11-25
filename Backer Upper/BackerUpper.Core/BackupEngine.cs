using System.Diagnostics;

namespace BackerUpper.Core;

/// <summary>
/// Mirrors a source folder into a target folder in two phases: first it scans the source to work out
/// what needs copying (and checks there is room for it), then it copies those files.
/// </summary>
public sealed class BackupEngine
{
    private static readonly string[] ExcludedDirectoryNames = ["$RECYCLE.BIN", "System Volume Information"];

    /// <summary>FAT/exFAT drives store timestamps coarsely, so treat close-enough times as equal.</summary>
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public BackupEngine(string sourceRoot, string targetRoot, BackupMode mode)
    {
        SourceRoot = Path.GetFullPath(sourceRoot);
        TargetRoot = Path.GetFullPath(targetRoot);
        Mode = mode;
    }

    public string SourceRoot { get; }
    public string TargetRoot { get; }
    public BackupMode Mode { get; }

    /// <summary>Returns the free bytes for a path, or null if unknown. Replaceable for testing.</summary>
    public Func<string, long?> FreeSpaceProvider { get; init; } = DiskSpace.GetAvailableBytes;

    /// <summary>Checks a source/target pair before starting. Returns a user-facing message, or null if they are usable.</summary>
    public static string? Validate(string source, string target)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "Choose the folder you want to back up.";
        if (string.IsNullOrWhiteSpace(target))
            return "Choose where you want the backup to go.";

        string sourceFull, targetFull;
        try
        {
            sourceFull = WithTrailingSeparator(Path.GetFullPath(source));
            targetFull = WithTrailingSeparator(Path.GetFullPath(target));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "One of the folder paths isn't valid: " + ex.Message;
        }

        if (!Directory.Exists(sourceFull))
            return "Couldn't find the folder to back up. Check the path and try again.";
        if (!Directory.Exists(targetFull))
            return "Couldn't find the backup folder. Check the path and try again.";
        if (string.Equals(sourceFull, targetFull, PathComparison))
            return "The folder to back up and the backup folder are the same.";
        if (targetFull.StartsWith(sourceFull, PathComparison))
            return "The backup folder can't be inside the folder being backed up.";
        return null;
    }

    public Task<BackupResult> RunAsync(IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Run(progress, cancellationToken));

    public BackupResult Run(IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var run = new RunState(progress);

        // Phase 1: work out what to copy. Nothing is written to the target yet.
        if (!Scan(run, cancellationToken))
            return run.Finish(BackupStatus.Cancelled);

        run.BytesAvailable = SafeFreeSpace(TargetRoot);
        if (run.BytesAvailable is long free && run.BytesRequired > free)
            return run.Finish(BackupStatus.InsufficientSpace);

        // Phase 2: create folders, then copy files.
        run.Phase = BackupPhase.Copying;
        run.CopyClock.Start();
        run.Report(force: true);

        foreach (string relativeDir in run.Directories)
        {
            if (cancellationToken.IsCancellationRequested)
                return run.Finish(BackupStatus.Cancelled);
            string targetDir = Path.Combine(TargetRoot, relativeDir);
            try
            {
                Directory.CreateDirectory(targetDir);
            }
            catch (Exception ex)
            {
                run.Errors.Add(new BackupError(targetDir, ex.Message));
            }
        }

        foreach (PlannedCopy item in run.Plan)
        {
            if (cancellationToken.IsCancellationRequested)
                return run.Finish(BackupStatus.Cancelled);

            string source = Path.Combine(SourceRoot, item.RelativePath);
            string target = Path.Combine(TargetRoot, item.RelativePath);
            try
            {
                CopyFile(source, target);
                run.FilesCopied++;
                run.BytesCopied += item.Size;
            }
            catch (Exception ex)
            {
                run.Errors.Add(new BackupError(source, ex.Message));
            }

            run.FilesDone++;
            run.BytesDone += item.Size;
            run.Report();
        }

        return run.Finish(BackupStatus.Completed);
    }

    /// <summary>Walks the source tree. Returns false if cancelled.</summary>
    private bool Scan(RunState run, CancellationToken cancellationToken)
    {
        // Iterative walk using paths relative to the source root, so deep trees can't overflow the stack
        // and target paths are always built from the root rather than by string replacement.
        var pending = new Stack<string>();
        pending.Push(string.Empty);

        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            string relativeDir = pending.Pop();
            string sourceDir = Path.Combine(SourceRoot, relativeDir);
            string targetDir = Path.Combine(TargetRoot, relativeDir);

            DirectoryInfo[] subdirectories;
            FileInfo[] files;
            try
            {
                var info = new DirectoryInfo(sourceDir);
                subdirectories = info.GetDirectories();
                files = info.GetFiles();
            }
            catch (Exception ex)
            {
                run.Errors.Add(new BackupError(sourceDir, ex.Message));
                continue;
            }

            if (relativeDir.Length > 0)
                run.Directories.Add(relativeDir);

            // Push in reverse so folders are processed in the order the file system lists them.
            for (int i = subdirectories.Length - 1; i >= 0; i--)
            {
                DirectoryInfo dir = subdirectories[i];
                // Skip junctions and symlinks: they can point back up the tree and loop forever.
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint) || IsExcluded(dir.Name))
                    continue;
                pending.Push(Path.Combine(relativeDir, dir.Name));
            }

            foreach (FileInfo file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;
                run.FilesScanned++;
                try
                {
                    var existing = new FileInfo(Path.Combine(targetDir, file.Name));
                    if (ShouldCopy(file, existing))
                    {
                        long size = file.Length;
                        run.Plan.Add(new PlannedCopy(Path.Combine(relativeDir, file.Name), size));
                        run.BytesToCopy += size;
                        run.BytesRequired += size - (existing.Exists ? existing.Length : 0);
                    }
                    else
                    {
                        run.FilesUpToDate++;
                    }
                }
                catch (Exception ex)
                {
                    run.Errors.Add(new BackupError(file.FullName, ex.Message));
                }
                run.Report();
            }
        }
        return true;
    }

    private bool ShouldCopy(FileInfo source, FileInfo existing)
    {
        if (!existing.Exists)
            return true;
        return Mode switch
        {
            BackupMode.ReplaceAll => true,
            BackupMode.NewOnly => false,
            _ => source.LastWriteTimeUtc - existing.LastWriteTimeUtc > TimestampTolerance,
        };
    }

    private static void CopyFile(string source, string target)
    {
        try
        {
            File.Copy(source, target, overwrite: true);
        }
        catch (UnauthorizedAccessException) when (TryClearReadOnly(target))
        {
            // A read-only file in the backup blocks overwriting; it was cleared, so try once more.
            File.Copy(source, target, overwrite: true);
        }
    }

    private static bool TryClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.ReadOnly))
                return false;
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private long? SafeFreeSpace(string path)
    {
        try
        {
            return FreeSpaceProvider(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsExcluded(string directoryName) =>
        ExcludedDirectoryNames.Any(name => string.Equals(name, directoryName, StringComparison.OrdinalIgnoreCase));

    private static string WithTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    private readonly record struct PlannedCopy(string RelativePath, long Size);

    /// <summary>Mutable counters for a single run, plus throttled progress reporting.</summary>
    private sealed class RunState(IProgress<BackupProgress>? progress)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private TimeSpan? _lastReport;

        public BackupPhase Phase = BackupPhase.Scanning;
        public readonly Stopwatch CopyClock = new();
        public readonly List<string> Directories = [];
        public readonly List<PlannedCopy> Plan = [];
        public readonly List<BackupError> Errors = [];
        public int FilesScanned, FilesUpToDate, FilesDone, FilesCopied;
        public long BytesToCopy, BytesRequired, BytesDone, BytesCopied;
        public long? BytesAvailable;

        public void Report(bool force = false)
        {
            if (progress is null)
                return;
            TimeSpan now = _clock.Elapsed;
            if (!force && now - _lastReport < ReportInterval)
                return;
            _lastReport = now;
            progress.Report(new BackupProgress(
                Phase, FilesScanned, Plan.Count, BytesToCopy, FilesDone, BytesDone, Errors.Count, now, CopyClock.Elapsed));
        }

        public BackupResult Finish(BackupStatus status)
        {
            CopyClock.Stop();
            Phase = BackupPhase.Finished;
            Report(force: true);
            return new BackupResult(
                status, FilesCopied, BytesCopied, FilesUpToDate, Errors.ToArray(), _clock.Elapsed, BytesRequired, BytesAvailable);
        }
    }
}
