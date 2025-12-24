using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace TextFileWatch;

public sealed class DirectoryTabView : UserControl
{
    private readonly ListView _listView;
    private readonly CheckBox _autoOpenNewCheck;
    private readonly Label _statusLabel;
    private readonly WinFormsTimer _debounceTimer;
    private readonly WinFormsTimer _directoryHealthTimer;
    private readonly Dictionary<string, PendingInfo> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private FileSystemWatcher? _fileWatcher;
    private FileSystemWatcher? _parentWatcher;
    private string _directoryPath;
    private string? _parentDirectoryPath;
    private string _directoryName;
    private bool _directoryExists;
    private bool _hasDirectoryExistsState;

    public DirectoryTabView(string directoryPath)
    {
        _directoryPath = directoryPath;
        _directoryName = Path.GetFileName(_directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        DoubleBuffered = true;

        _autoOpenNewCheck = new CheckBox
        {
            Text = "Auto-open new files",
            Checked = true,
            AutoSize = true,
            Padding = new Padding(0, 2, 0, 0)
        };

        var openButton = new Button
        {
            Text = "Open selected",
            AutoSize = true
        };
        openButton.Click += (_, _) => OpenSelected();

        var closeOpenedButton = new Button
        {
            Text = "Close opened file tabs",
            AutoSize = true
        };
        closeOpenedButton.Click += (_, _) => CloseOpenedFileTabs();

        _statusLabel = new Label
        {
            AutoSize = true,
            Text = "",
            Padding = new Padding(0, 2, 0, 0)
        };

        var toolbarMargin = new Padding(6, 4, 6, 0);
        _autoOpenNewCheck.Margin = toolbarMargin;
        openButton.Margin = toolbarMargin;
        closeOpenedButton.Margin = toolbarMargin;
        _statusLabel.Margin = toolbarMargin;

        var pathLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = directoryPath,
            MaximumSize = new System.Drawing.Size(900, 0),
            Padding = new Padding(0, 4, 0, 0)
        };

        var pathPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(6)
        };
        pathPanel.Controls.Add(new Label { Text = "Dir:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 0, 0) });
        pathPanel.Controls.Add(pathLabel);

        var controlsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(6)
        };
        controlsPanel.Controls.Add(_autoOpenNewCheck);
        controlsPanel.Controls.Add(openButton);
        controlsPanel.Controls.Add(closeOpenedButton);
        controlsPanel.Controls.Add(_statusLabel);

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false
        };
        _listView.Columns.Add("File", 420);
        _listView.Columns.Add("Last write", 160);
        _listView.Columns.Add("Size", 100);
        _listView.DoubleClick += (_, _) => OpenSelected();

        // Dock layout is applied in reverse z-order; add Fill first so it lays out last (no overlap).
        Controls.Add(_listView);
        Controls.Add(controlsPanel);
        Controls.Add(pathPanel);

        _debounceTimer = new WinFormsTimer { Interval = 250 };
        _debounceTimer.Tick += (_, _) => ProcessPending();

        _directoryHealthTimer = new WinFormsTimer { Interval = 1000 };
        _directoryHealthTimer.Tick += (_, _) => CheckDirectoryHealth();

        StartWatching(directoryPath);
    }

    public string DirectoryPath => _directoryPath;

    public event Action<string>? OpenFileRequested;
    public event Func<string, int>? CloseOpenedFileTabsRequested;

    private void CloseOpenedFileTabs()
    {
        var handler = CloseOpenedFileTabsRequested;
        if (handler == null)
            return;

        try
        {
            var closed = handler.Invoke(_directoryPath);
            _statusLabel.Text = closed > 0 ? $"Closed {closed} file tab(s)." : "No opened file tabs to close.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Close tabs error: {ex.Message}";
        }
    }

    private void InitialScan(bool clearFirst)
    {
        try
        {
            if (!Directory.Exists(_directoryPath))
            {
                SetDirectoryExists(exists: false, reason: "Scan");
                return;
            }

            if (clearFirst)
                _listView.Items.Clear();

            foreach (var file in Directory.EnumerateFiles(_directoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsSupported(file))
                    continue;
                UpsertItem(file);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Scan error: {ex.Message}";
        }
    }

    private void StartWatching(string directoryPath)
    {
        StopWatching();

        _directoryPath = directoryPath;
        _directoryName = Path.GetFileName(_directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _parentDirectoryPath = Path.GetDirectoryName(_directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        StartParentWatcher();
        _directoryHealthTimer.Start();

        SetDirectoryExists(exists: Directory.Exists(_directoryPath), reason: "Start");
    }

    private void StartParentWatcher()
    {
        if (string.IsNullOrWhiteSpace(_parentDirectoryPath))
            return;

        if (!Directory.Exists(_parentDirectoryPath))
            return;

        _parentWatcher = new FileSystemWatcher(_parentDirectoryPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.DirectoryName
        };

        _parentWatcher.Created += (_, e) => OnParentDirectoryChanged(e.FullPath, oldFullPath: null);
        _parentWatcher.Deleted += (_, e) => OnParentDirectoryChanged(e.FullPath, oldFullPath: null);
        _parentWatcher.Renamed += (_, e) => OnParentDirectoryChanged(e.FullPath, e.OldFullPath);
        _parentWatcher.EnableRaisingEvents = true;
    }

    private void OnParentDirectoryChanged(string fullPath, string? oldFullPath)
    {
        if (!IsHandleCreated || IsDisposed)
            return;

        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var oldName = oldFullPath == null ? null : Path.GetFileName(oldFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var affectsTracked =
            string.Equals(name, _directoryName, StringComparison.OrdinalIgnoreCase) ||
            (oldName != null && string.Equals(oldName, _directoryName, StringComparison.OrdinalIgnoreCase));

        if (!affectsTracked)
            return;

        SafeBeginInvoke(() => SetDirectoryExists(exists: Directory.Exists(_directoryPath), reason: "Parent change"));
    }

    private void CheckDirectoryHealth()
    {
        SetDirectoryExists(exists: Directory.Exists(_directoryPath), reason: "Health check");
    }

    private void StartFileWatcher()
    {
        StopFileWatcher();

        if (!Directory.Exists(_directoryPath))
            return;

        _fileWatcher = new FileSystemWatcher(_directoryPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _fileWatcher.Created += (_, e) => Queue(e.FullPath, PendingAction.Upsert, isCreate: true);
        _fileWatcher.Changed += (_, e) => Queue(e.FullPath, PendingAction.Upsert, isCreate: false);
        _fileWatcher.Deleted += (_, e) => Queue(e.FullPath, PendingAction.Delete, isCreate: false);
        _fileWatcher.Renamed += (_, e) =>
        {
            if (IsSupported(e.OldFullPath))
                Queue(e.OldFullPath, PendingAction.Delete, isCreate: false);
            Queue(e.FullPath, PendingAction.Upsert, isCreate: true);
        };
        _fileWatcher.Error += (_, _) =>
        {
            SafeBeginInvoke(() => SetDirectoryExists(exists: Directory.Exists(_directoryPath), reason: "Watcher error"));
        };
        _fileWatcher.EnableRaisingEvents = true;
    }

    private void StopWatching()
    {
        _debounceTimer.Stop();
        _directoryHealthTimer.Stop();
        lock (_gate)
        {
            _pending.Clear();
        }

        StopFileWatcher();

        if (_parentWatcher != null)
        {
            _parentWatcher.EnableRaisingEvents = false;
            _parentWatcher.Dispose();
            _parentWatcher = null;
        }
    }

    private void StopFileWatcher()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }

    private void SetDirectoryExists(bool exists, string reason)
    {
        if (_hasDirectoryExistsState && _directoryExists == exists)
            return;

        _hasDirectoryExistsState = true;
        _directoryExists = exists;

        lock (_gate)
        {
            _pending.Clear();
        }
        _debounceTimer.Stop();

        if (!exists)
        {
            StopFileWatcher();
            _listView.Items.Clear();
            _listView.Enabled = false;
            _statusLabel.Text = $"Directory missing ({reason}) {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            return;
        }

        _listView.Enabled = true;
        _statusLabel.Text = $"Directory available ({reason}) {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        StartFileWatcher();
        InitialScan(clearFirst: true);
    }

    private void Queue(string path, PendingAction action, bool isCreate)
    {
        if (!IsSupported(path))
            return;

        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_pending.TryGetValue(path, out var info))
            {
                info.LastSeenUtc = now;
                info.Action = action;
                _pending[path] = info;
            }
            else
            {
                _pending[path] = new PendingInfo { FirstSeenUtc = now, LastSeenUtc = now, Action = action };
            }
        }

        if (IsHandleCreated && !_debounceTimer.Enabled)
            SafeBeginInvoke(() => _debounceTimer.Start());

        if (IsHandleCreated && isCreate && _autoOpenNewCheck.Checked)
            SafeBeginInvoke(() => OpenFileRequested?.Invoke(path));
    }

    private void ProcessPending()
    {
        List<ReadyItem>? ready = null;
        var now = DateTime.UtcNow;

        lock (_gate)
        {
            foreach (var kvp in _pending)
            {
                var info = kvp.Value;
                var quietForMs = (now - info.LastSeenUtc).TotalMilliseconds;
                var ageMs = (now - info.FirstSeenUtc).TotalMilliseconds;

                // If a file is being written continuously, we still want to refresh periodically.
                // Process either when it has been quiet briefly, or when it has been pending too long.
                if (quietForMs < 200 && ageMs < 1500)
                    continue;
                ready ??= new List<ReadyItem>();
                ready.Add(new ReadyItem(kvp.Key, info.Action));
            }

            if (ready != null)
            {
                for (var i = 0; i < ready.Count; i++)
                    _pending.Remove(ready[i].Path);
            }

            if (_pending.Count == 0)
                _debounceTimer.Stop();
        }

        if (ready == null)
            return;

        for (var i = 0; i < ready.Count; i++)
        {
            var item = ready[i];
            if (item.Action == PendingAction.Delete)
            {
                RemoveItem(item.Path);
                continue;
            }

            if (!File.Exists(item.Path))
            {
                RemoveItem(item.Path);
                continue;
            }

            UpsertItem(item.Path);
        }
    }

    private void RemoveItem(string path)
    {
        for (var i = 0; i < _listView.Items.Count; i++)
        {
            var it = _listView.Items[i];
            if (string.Equals((string?)it.Tag, path, StringComparison.OrdinalIgnoreCase))
            {
                _listView.Items.RemoveAt(i);
                _statusLabel.Text = $"Removed {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                return;
            }
        }
    }

    private void UpsertItem(string path)
    {
        var name = Path.GetFileName(path);
        var key = path;

        DateTime lastWriteUtc;
        long length;
        try
        {
            var info = new FileInfo(path);
            lastWriteUtc = info.LastWriteTimeUtc;
            length = info.Length;
        }
        catch
        {
            return;
        }

        ListViewItem? item = null;
        for (var i = 0; i < _listView.Items.Count; i++)
        {
            var it = _listView.Items[i];
            if (string.Equals((string?)it.Tag, key, StringComparison.OrdinalIgnoreCase))
            {
                item = it;
                break;
            }
        }

        if (item == null)
        {
            item = new ListViewItem(name) { Tag = key };
            item.SubItems.Add(lastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            item.SubItems.Add(length.ToString());
            _listView.Items.Add(item);
        }
        else
        {
            item.Text = name;
            item.SubItems[1].Text = lastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            item.SubItems[2].Text = length.ToString();
        }

        _statusLabel.Text = $"Updated {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    }

    private void SafeBeginInvoke(Action action)
    {
        if (!IsHandleCreated || IsDisposed)
            return;
        try
        {
            BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OpenSelected()
    {
        if (_listView.SelectedItems.Count == 0)
            return;

        var path = (string?)_listView.SelectedItems[0].Tag;
        if (string.IsNullOrWhiteSpace(path))
            return;

        OpenFileRequested?.Invoke(path);
    }

    private static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        for (var i = 0; i < MainForm.DefaultExtensions.Length; i++)
        {
            if (string.Equals(extension, MainForm.DefaultExtensions[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private struct PendingInfo
    {
        public DateTime FirstSeenUtc;
        public DateTime LastSeenUtc;
        public PendingAction Action;
    }

    private readonly struct ReadyItem
    {
        public ReadyItem(string path, PendingAction action)
        {
            Path = path;
            Action = action;
        }

        public string Path { get; }
        public PendingAction Action { get; }
    }

    private enum PendingAction
    {
        Upsert = 0,
        Delete = 1
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopWatching();
            _debounceTimer.Dispose();
            _directoryHealthTimer.Dispose();
            _listView.Dispose();
            _autoOpenNewCheck.Dispose();
            _statusLabel.Dispose();
        }
        base.Dispose(disposing);
    }
}
