namespace BackerUpper.Core;

/// <summary>What to do with files that already exist in the backup folder.</summary>
public enum BackupMode
{
    /// <summary>Copy missing files and replace ones whose source copy is newer.</summary>
    UpdateChanged,

    /// <summary>Copy every file, overwriting anything already in the backup.</summary>
    ReplaceAll,

    /// <summary>Copy only files missing from the backup; never overwrite.</summary>
    NewOnly,
}

public enum BackupPhase
{
    Scanning,
    Copying,
    Finished,
}

public enum BackupStatus
{
    Completed,
    Cancelled,

    /// <summary>The scan found more data than the target has room for; nothing was written.</summary>
    InsufficientSpace,
}

/// <summary>A file or folder that could not be read or copied.</summary>
public sealed record BackupError(string Path, string Message);

/// <summary>A snapshot of a running backup, reported periodically.</summary>
public sealed record BackupProgress(
    BackupPhase Phase,
    int FilesScanned,
    int FilesToCopy,
    long BytesToCopy,
    int FilesDone,
    long BytesDone,
    int ErrorCount,
    TimeSpan Elapsed,
    TimeSpan CopyElapsed)
{
    /// <summary>How long to measure the copy rate before trusting an estimate.</summary>
    private static readonly TimeSpan MinimumSampleTime = TimeSpan.FromSeconds(2);

    /// <summary>Fraction of the copy phase finished, from 0 to 1.</summary>
    public double Fraction
    {
        get
        {
            if (Phase == BackupPhase.Finished)
                return 1;
            if (BytesToCopy > 0)
                return Math.Clamp((double)BytesDone / BytesToCopy, 0, 1);
            return FilesToCopy > 0 ? Math.Clamp((double)FilesDone / FilesToCopy, 0, 1) : 0;
        }
    }

    /// <summary>Estimated time left in the copy phase, or null while there isn't enough data to guess.</summary>
    public TimeSpan? EstimatedRemaining
    {
        get
        {
            if (Phase != BackupPhase.Copying || CopyElapsed < MinimumSampleTime)
                return null;
            double fraction = Fraction;
            if (fraction <= 0)
                return null;
            double remainingSeconds = CopyElapsed.TotalSeconds * (1 - fraction) / fraction;
            return TimeSpan.FromSeconds(Math.Round(remainingSeconds));
        }
    }
}

/// <summary>The outcome of a backup run.</summary>
public sealed record BackupResult(
    BackupStatus Status,
    int FilesCopied,
    long BytesCopied,
    int FilesUpToDate,
    IReadOnlyList<BackupError> Errors,
    TimeSpan Elapsed,
    long BytesRequired,
    long? BytesAvailable);
