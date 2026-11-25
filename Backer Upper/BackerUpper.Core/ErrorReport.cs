using System.Globalization;
using System.Text;

namespace BackerUpper.Core;

public static class ErrorReport
{
    /// <summary>The folder reports are written to by default: Documents\Backer_Upper_Reports.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Backer_Upper_Reports");

    /// <summary>Writes a timestamped report listing every error and why it happened. Returns the file path.</summary>
    public static string Write(string folder, BackupEngine engine, BackupResult result, DateTime? timestamp = null)
    {
        DateTime when = timestamp ?? DateTime.Now;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"ErrorReport_{when.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.txt");

        var text = new StringBuilder()
            .AppendLine("Backer Upper error report")
            .AppendLine(CultureInfo.InvariantCulture, $"Date:     {when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}")
            .AppendLine(CultureInfo.InvariantCulture, $"Source:   {engine.SourceRoot}")
            .AppendLine(CultureInfo.InvariantCulture, $"Target:   {engine.TargetRoot}")
            .AppendLine(CultureInfo.InvariantCulture, $"Mode:     {engine.Mode}")
            .AppendLine(CultureInfo.InvariantCulture, $"Result:   {result.Status}, {result.FilesCopied} files copied, {result.Errors.Count} errors")
            .AppendLine();

        foreach (BackupError error in result.Errors)
        {
            text.AppendLine(error.Path)
                .Append("    ").AppendLine(error.Message);
        }

        File.WriteAllText(path, text.ToString());
        return path;
    }
}
