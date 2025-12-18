# Repository Guidelines

## Project Structure & Module Organization
- `TextFileWatch.csproj` is a single WinForms app targeting `net6.0-windows`.
- Source lives in the repo root (`*.cs`): UI (`MainForm.cs`, `FileTabView.cs`, `DirectoryTabView.cs`) plus small helpers (`FileDocument.cs`, `LineDiff.cs`, `AppStateStore.cs`).
- Build outputs (`bin/`, `obj/`, `bin2/`) and local data (`logs/`, `*.log`) are ignored via `.gitignore`.
- App state is persisted to `%APPDATA%/TextFileWatch/state.json` (see `AppStateStore.cs`).

## Build, Test, and Development Commands
- `dotnet run` — build and run the app locally (Windows recommended).
- `dotnet build -c Release` — build a Release binary.
- `build.bat [Debug|Release]` — Windows CMD wrapper around `dotnet build`.
- Optional: `dotnet publish -c Release -r win-x64 --self-contained false` — produce publish output for distribution.

## Coding Style & Naming Conventions
- C# conventions used throughout:
  - Indentation: 4 spaces.
  - Types/methods: `PascalCase`; locals/parameters: `camelCase`; private fields: `_camelCase`.
  - Nullable reference types are enabled; prefer fixing warnings over suppressing them.
- Keep UI layout code readable: group control construction → property initialization → event wiring.

## Testing Guidelines
- No automated test project is currently included.
- Validate changes with a quick manual smoke test: open a file, toggle **Watch**, adjust interval, and verify highlight/scroll behaviors.
- If adding tests, prefer xUnit in `TextFileWatch.Tests/` and run with `dotnet test`.

## Commit & Pull Request Guidelines
- Commit messages in history are short, imperative, and capitalized (e.g., “Add directory watcher tab”).
- PRs should include: a concise description, manual test steps, and screenshots/GIFs for UI changes; note Windows version and .NET SDK version if relevant.

## Agent-Specific Instructions (Codex CLI Wrapper)
- Agents live in `codex/agents/` (or `~/.codex/agents/`) and run via `scripts/codex-agent.sh NAME [-- FLAGS]`.
- `$ARGUMENTS` / `$1..$9` placeholders are expanded before `--`; leading `!` lines execute locally—review them before running.
