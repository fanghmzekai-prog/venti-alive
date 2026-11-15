using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Aeris.WindowProbe;

internal sealed record DesktopPreferences
{
    public bool Topmost { get; init; } = true;
    public bool PassThrough { get; init; }
    public bool Visible { get; init; } = true;
    public bool Playing { get; init; } = true;
    public bool Autonomous { get; init; } = true;
    public bool ProactiveSpeech { get; init; } = true;
    public bool AutonomousFlight { get; init; } = true;
    public int BubbleSeconds { get; init; } = 7;
    public int? Left { get; init; }
    public int? Top { get; init; }
    public string? DialogueModelDirectory { get; init; }
    public bool DialogueUseCpu { get; init; }
}

internal sealed class PreferencesStore(string path)
{
    internal string DataDirectory => Path.GetDirectoryName(path)!;
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AerisAlive", "desktop-settings.json");

    internal DesktopPreferences Load()
    {
        try
        {
            using FileStream file = File.OpenRead(path);
            return JsonSerializer.Deserialize<DesktopPreferences>(file) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine(ex);
            return new();
        }
    }

    internal void Save(DesktopPreferences settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(file, settings);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
