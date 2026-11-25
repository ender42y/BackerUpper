using BackerUpper.Core;

namespace BackerUpper.Tests;

public sealed class BackupEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BackerUpperTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly string _target;

    public BackupEngineTests()
    {
        _source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        _target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private static string Write(string folder, string relativePath, string content, DateTime? modifiedUtc = null)
    {
        string path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modifiedUtc is DateTime time)
            File.SetLastWriteTimeUtc(path, time);
        return path;
    }

    private static string Read(string folder, string relativePath) => File.ReadAllText(Path.Combine(folder, relativePath));

    private BackupResult Run(BackupMode mode, long? freeSpace = long.MaxValue) =>
        new BackupEngine(_source, _target, mode) { FreeSpaceProvider = _ => freeSpace }.Run();

    private static readonly DateTime Old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CopiesNestedFilesAndEmptyFolders()
    {
        Write(_source, "a.txt", "a");
        Write(_source, Path.Combine("docs", "deep", "b.txt"), "b");
        Directory.CreateDirectory(Path.Combine(_source, "empty"));

        BackupResult result = Run(BackupMode.UpdateChanged);

        Assert.Equal(BackupStatus.Completed, result.Status);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.FilesCopied);
        Assert.Equal("a", Read(_target, "a.txt"));
        Assert.Equal("b", Read(_target, Path.Combine("docs", "deep", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(_target, "empty")));
    }

    [Fact]
    public void UpdateChanged_ReplacesFilesThatAreNewerInSource()
    {
        // This was broken in 1.0: File.Copy without overwrite failed for every changed file.
        Write(_target, "report.txt", "old", Old);
        Write(_source, "report.txt", "new", New);

        BackupResult result = Run(BackupMode.UpdateChanged);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal("new", Read(_target, "report.txt"));
    }

    [Fact]
    public void UpdateChanged_SkipsUnchangedFiles_IncludingSmallTimestampDrift()
    {
        Write(_source, "same.txt", "source", New);
        Write(_target, "same.txt", "backup", New);
        Write(_source, "fat.txt", "source", New.AddSeconds(1)); // FAT rounds times to 2 seconds
        Write(_target, "fat.txt", "backup", New);
        Write(_source, "older.txt", "source", Old);
        Write(_target, "older.txt", "backup", New);

        BackupResult result = Run(BackupMode.UpdateChanged);

        Assert.Equal(0, result.FilesCopied);
        Assert.Equal(3, result.FilesUpToDate);
        Assert.Equal("backup", Read(_target, "same.txt"));
        Assert.Equal("backup", Read(_target, "fat.txt"));
        Assert.Equal("backup", Read(_target, "older.txt"));
    }

    [Fact]
    public void ReplaceAll_OverwritesEverything_IncludingReadOnlyFiles()
    {
        Write(_source, "a.txt", "source", Old);
        string existing = Write(_target, "a.txt", "backup", New);
        File.SetAttributes(existing, FileAttributes.ReadOnly);

        BackupResult result = Run(BackupMode.ReplaceAll);

        Assert.Empty(result.Errors);
        Assert.Equal("source", Read(_target, "a.txt"));
    }

    [Fact]
    public void NewOnly_NeverOverwrites()
    {
        Write(_source, "a.txt", "source", New);
        Write(_target, "a.txt", "backup", Old);
        Write(_source, "b.txt", "fresh");

        BackupResult result = Run(BackupMode.NewOnly);

        Assert.Equal(1, result.FilesCopied);
        Assert.Equal("backup", Read(_target, "a.txt"));
        Assert.Equal("fresh", Read(_target, "b.txt"));
    }

    [Fact]
    public void MapsPathsFromTheRoot_WhenFolderNamesRepeat()
    {
        // 1.0 rebuilt target paths with string.Replace, which mangled paths that repeat the root's name.
        string source = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        Write(source, Path.Combine("data", "data", "x.txt"), "x");

        var result = new BackupEngine(source, _target, BackupMode.UpdateChanged) { FreeSpaceProvider = _ => null }.Run();

        Assert.Empty(result.Errors);
        Assert.Equal("x", Read(_target, Path.Combine("data", "data", "x.txt")));
    }

    [Fact]
    public void StopsBeforeWritingAnything_WhenTargetLacksSpace()
    {
        Write(_source, Path.Combine("sub", "big.txt"), new string('x', 1000));

        BackupResult result = Run(BackupMode.UpdateChanged, freeSpace: 500);

        Assert.Equal(BackupStatus.InsufficientSpace, result.Status);
        Assert.Equal(1000, result.BytesRequired);
        Assert.Equal(500, result.BytesAvailable);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_target));
    }

    [Fact]
    public void SpaceCheck_CountsOnlyTheGrowthOfReplacedFiles()
    {
        Write(_target, "a.txt", new string('x', 900), Old);
        Write(_source, "a.txt", new string('y', 1000), New);

        BackupResult result = Run(BackupMode.UpdateChanged, freeSpace: 200);

        Assert.Equal(BackupStatus.Completed, result.Status);
        Assert.Equal(100, result.BytesRequired);
    }

    [Fact]
    public void ProceedsWhenFreeSpaceIsUnknown()
    {
        Write(_source, "a.txt", "a");
        Assert.Equal(BackupStatus.Completed, Run(BackupMode.UpdateChanged, freeSpace: null).Status);
    }

    [Fact]
    public void ReturnsCancelled_WithoutCopying()
    {
        Write(_source, "a.txt", "a");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        BackupResult result = new BackupEngine(_source, _target, BackupMode.UpdateChanged).Run(null, cts.Token);

        Assert.Equal(BackupStatus.Cancelled, result.Status);
        Assert.False(File.Exists(Path.Combine(_target, "a.txt")));
    }

    [Fact]
    public void SkipsSymlinkedFolders_AndRecycleBin()
    {
        Write(_source, Path.Combine("real", "a.txt"), "a");
        Write(_source, Path.Combine("$RECYCLE.BIN", "junk.txt"), "junk");
        Directory.CreateSymbolicLink(Path.Combine(_source, "loop"), _source);

        BackupResult result = Run(BackupMode.UpdateChanged);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.FilesCopied);
        Assert.False(Directory.Exists(Path.Combine(_target, "loop")));
        Assert.False(Directory.Exists(Path.Combine(_target, "$RECYCLE.BIN")));
    }

    [Fact]
    public void RecordsErrorsWithReasons_AndKeepsGoing()
    {
        Write(_source, "a.txt", "a");
        Write(_source, "b.txt", "b");
        // A folder where a file needs to go makes that one copy fail.
        Directory.CreateDirectory(Path.Combine(_target, "a.txt"));

        BackupResult result = Run(BackupMode.UpdateChanged);

        Assert.Equal(BackupStatus.Completed, result.Status);
        BackupError error = Assert.Single(result.Errors);
        Assert.EndsWith("a.txt", error.Path);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Equal("b", Read(_target, "b.txt"));
    }

    [Fact]
    public void ReportsProgressEndingInFinished()
    {
        for (int i = 0; i < 5; i++)
            Write(_source, $"f{i}.txt", "12345");
        var reports = new List<BackupProgress>();

        new BackupEngine(_source, _target, BackupMode.UpdateChanged) { FreeSpaceProvider = _ => null }
            .Run(new SyncProgress(reports.Add));

        BackupProgress last = reports[^1];
        Assert.Equal(BackupPhase.Finished, last.Phase);
        Assert.Equal(5, last.FilesToCopy);
        Assert.Equal(5, last.FilesDone);
        Assert.Equal(25, last.BytesDone);
        Assert.Equal(1, last.Fraction);
        Assert.Contains(reports, r => r.Phase == BackupPhase.Copying);
    }

    [Fact]
    public void Validate_RejectsBadFolderPairs()
    {
        Assert.Null(BackupEngine.Validate(_source, _target));
        Assert.NotNull(BackupEngine.Validate("", _target));
        Assert.NotNull(BackupEngine.Validate(_source, " "));
        Assert.NotNull(BackupEngine.Validate(Path.Combine(_root, "missing"), _target));
        Assert.NotNull(BackupEngine.Validate(_source, Path.Combine(_root, "missing")));
        Assert.NotNull(BackupEngine.Validate(_source, _source + Path.DirectorySeparatorChar));
        string inside = Directory.CreateDirectory(Path.Combine(_source, "backup")).FullName;
        Assert.NotNull(BackupEngine.Validate(_source, inside));
        // A sibling whose name merely starts with the source's name is fine.
        string sibling = Directory.CreateDirectory(_source + "-backup").FullName;
        Assert.Null(BackupEngine.Validate(_source, sibling));
    }

    private sealed class SyncProgress(Action<BackupProgress> onReport) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) => onReport(value);
    }
}
