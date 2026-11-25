using System.Globalization;
using BackerUpper.Core;

namespace BackerUpper.Tests;

public class HelperTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(2_415_919_104L, "2.25 GB")]
    [InlineData(3L * 1024 * 1024 * 1024 * 1024, "3 TB")]
    public void ByteSize_Formats(long bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Format(bytes, CultureInfo.InvariantCulture));

    private static BackupProgress Copying(long done, long total, double seconds) =>
        new(BackupPhase.Copying, 10, 10, total, 0, done, 0, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds));

    [Fact]
    public void EstimatedRemaining_ExtrapolatesFromCopyRate() =>
        Assert.Equal(TimeSpan.FromSeconds(30), Copying(done: 250, total: 1000, seconds: 10).EstimatedRemaining);

    [Fact]
    public void EstimatedRemaining_WaitsForEnoughData()
    {
        Assert.Null(Copying(done: 250, total: 1000, seconds: 1).EstimatedRemaining);
        Assert.Null(Copying(done: 0, total: 1000, seconds: 10).EstimatedRemaining);
    }

    [Fact]
    public void ErrorReport_ListsEachErrorWithItsReason()
    {
        string folder = Path.Combine(Path.GetTempPath(), "BackerUpperReport_" + Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new BackupEngine("/src", "/dst", BackupMode.NewOnly);
            var result = new BackupResult(
                BackupStatus.Completed, 3, 300, 0, [new BackupError("/src/locked.docx", "The file is in use.")], TimeSpan.Zero, 0, null);

            string path = ErrorReport.Write(folder, engine, result, new DateTime(2026, 11, 25, 14, 30, 5));

            Assert.EndsWith("ErrorReport_2026-11-25_14-30-05.txt", path);
            string text = File.ReadAllText(path);
            Assert.Contains("/src/locked.docx", text);
            Assert.Contains("The file is in use.", text);
            Assert.Contains("NewOnly", text);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void DiskSpace_ReturnsAValueForTheTempFolder() =>
        Assert.True(DiskSpace.GetAvailableBytes(Path.GetTempPath()) > 0);
}
