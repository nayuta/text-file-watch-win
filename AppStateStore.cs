using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TextFileWatch;

internal static class AppStateStore
{
    private const string AppName = "TextFileWatch";
    private const string StateFileName = "state.json";

    public static IReadOnlyList<string> LoadOpenFiles()
    {
        var path = GetStatePath();
        if (!File.Exists(path))
            return Array.Empty<string>();

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<AppState>(json);
            return (IReadOnlyList<string>?)state?.OpenFiles ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void SaveOpenFiles(IEnumerable<string> openFiles)
    {
        var state = new AppState { OpenFiles = new List<string>(openFiles) };
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });

        var path = GetStatePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, json);
    }

    private static string GetStatePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(baseDir, AppName, StateFileName);
    }

    private sealed class AppState
    {
        public List<string> OpenFiles { get; set; } = new();
    }
}
