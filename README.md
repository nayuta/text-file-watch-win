# TextFileWatch

Small WinForms viewer that shows text files in tabs and optionally watches them for changes.

## Features
- Tab per file with read-only viewer.
- Toggle watch on/off per tab; configurable refresh interval (seconds).
- Optional line-level highlight of changes on each refresh.
- Manual refresh button per tab.

## Build / Run
1. Install .NET 6+ SDK on Windows.
2. From the `TextFileWatch` directory run:
   ```bash
   dotnet run
   ```
3. On startup, the app auto-loads text files in the app directory (`.txt`, `.log`, `.md`, `.csv`, `.json`, `.xml`).
4. Click **Add File…** to open another text file. Each tab manages its own watch and highlight settings.
5. Open tabs are remembered and restored on the next launch (missing files are skipped).
