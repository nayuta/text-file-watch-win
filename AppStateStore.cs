using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TextFileWatch;

internal static class AppStateStore
{
    private const string AppName = "TextFileWatch";
    private const string StateFileName = "state.json";

    public static (IReadOnlyList<string> OpenFiles, string? WatchedDirectory, bool WatchDirectoryEnabled) Load()
    {
        var path = GetStatePath();
        if (!File.Exists(path))
            return (Array.Empty<string>(), null, false);

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<AppState>(json);
            var openFiles = (IReadOnlyList<string>?)state?.OpenFiles ?? Array.Empty<string>();
            return (openFiles, state?.WatchedDirectory, state?.WatchDirectoryEnabled ?? false);
        }
        catch
        {
            return (Array.Empty<string>(), null, false);
        }
    }

    public static void Save(IEnumerable<string> openFiles, string? watchedDirectory, bool watchDirectoryEnabled)
    {
        var state = new AppState
        {
            OpenFiles = new List<string>(openFiles),
            WatchedDirectory = watchedDirectory,
            WatchDirectoryEnabled = watchDirectoryEnabled
        };
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
        public string? WatchedDirectory { get; set; }
        public bool WatchDirectoryEnabled { get; set; }
    }
}
