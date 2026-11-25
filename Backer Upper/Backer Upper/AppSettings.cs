using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackerUpper.Core;

namespace Backer_Upper;

/// <summary>The last folders and mode used, saved to %AppData%\Backer Upper\settings.json.</summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Backer Upper", "settings.json");

    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public BackupMode Mode { get; set; } = BackupMode.UpdateChanged;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A missing or corrupt settings file just means starting with defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Remembering folders is a convenience; never block a backup over it.
        }
    }
}
