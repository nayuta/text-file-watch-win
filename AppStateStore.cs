using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TextFileWatch;

internal static class AppStateStore
{
    private const string AppName = "TextFileWatch";
    private const string StateFileName = "state.json";

    public static (IReadOnlyList<string> OpenFiles, IReadOnlyList<string> OpenDirectories) Load()
    {
        var path = GetStatePath();
        if (!File.Exists(path))
            return (Array.Empty<string>(), Array.Empty<string>());

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<AppState>(json);
            var openFiles = (IReadOnlyList<string>?)state?.OpenFiles ?? Array.Empty<string>();
            var openDirectories = (IReadOnlyList<string>?)state?.OpenDirectories ?? Array.Empty<string>();

            if (openDirectories.Count == 0 && state?.WatchDirectoryEnabled == true && !string.IsNullOrWhiteSpace(state.WatchedDirectory))
                openDirectories = new[] { state.WatchedDirectory };

            return (openFiles, openDirectories);
        }
        catch
        {
            return (Array.Empty<string>(), Array.Empty<string>());
        }
    }

    public static void Save(IEnumerable<string> openFiles, IEnumerable<string> openDirectories)
    {
        var openDirectoryList = new List<string>(openDirectories);
        var state = new AppState
        {
            OpenFiles = new List<string>(openFiles),
            OpenDirectories = openDirectoryList,
            WatchedDirectory = openDirectoryList.Count > 0 ? openDirectoryList[0] : null,
            WatchDirectoryEnabled = openDirectoryList.Count > 0
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
        public List<string> OpenDirectories { get; set; } = new();
        public string? WatchedDirectory { get; set; }
        public bool WatchDirectoryEnabled { get; set; }
    }
}
