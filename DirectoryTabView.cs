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
    private readonly Dictionary<string, PendingInfo> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private FileSystemWatcher? _watcher;
    private string _directoryPath;

    public DirectoryTabView(string directoryPath)
    {
        _directoryPath = directoryPath;
        DoubleBuffered = true;

        _autoOpenNewCheck = new CheckBox
        {
            Text = "Auto-open new files",
            Checked = true,
            AutoSize = true
        };

        var openButton = new Button
        {
            Text = "Open selected",
            AutoSize = true
        };
        openButton.Click += (_, _) => OpenSelected();

        _statusLabel = new Label
        {
            AutoSize = true,
            Text = ""
        };

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
            WrapContents = true
        };
        top.Controls.Add(new Label { Text = "Dir:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        top.Controls.Add(new Label { Text = directoryPath, AutoSize = true, MaximumSize = new System.Drawing.Size(900, 0), Padding = new Padding(0, 6, 0, 0) });
        top.Controls.Add(_autoOpenNewCheck);
        top.Controls.Add(openButton);
        top.Controls.Add(_statusLabel);

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

        Controls.Add(_listView);
        Controls.Add(top);

        _debounceTimer = new WinFormsTimer { Interval = 250 };
        _debounceTimer.Tick += (_, _) => ProcessPending();

        StartWatching(directoryPath);
        InitialScan();
    }

    public string DirectoryPath => _directoryPath;

    public event Action<string>? OpenFileRequested;

    private void InitialScan()
    {
        try
        {
            if (!Directory.Exists(_directoryPath))
                return;

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
        Directory.CreateDirectory(_directoryPath);

        _watcher = new FileSystemWatcher(_directoryPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Created += (_, e) => Queue(e.FullPath, isCreate: true);
        _watcher.Changed += (_, e) => Queue(e.FullPath, isCreate: false);
        _watcher.Renamed += (_, e) => Queue(e.FullPath, isCreate: true);
        _watcher.EnableRaisingEvents = true;
    }

    private void StopWatching()
    {
        _debounceTimer.Stop();
        lock (_gate)
        {
            _pending.Clear();
        }

        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void Queue(string path, bool isCreate)
    {
        if (!IsSupported(path))
            return;

        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_pending.TryGetValue(path, out var info))
            {
                info.LastSeenUtc = now;
                _pending[path] = info;
            }
            else
            {
                _pending[path] = new PendingInfo { FirstSeenUtc = now, LastSeenUtc = now };
            }
        }

        if (IsHandleCreated && !_debounceTimer.Enabled)
            BeginInvoke(new Action(() => _debounceTimer.Start()));

        if (IsHandleCreated && isCreate && _autoOpenNewCheck.Checked)
            BeginInvoke(new Action(() => OpenFileRequested?.Invoke(path)));
    }

    private void ProcessPending()
    {
        List<string>? ready = null;
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
                ready ??= new List<string>();
                ready.Add(kvp.Key);
            }

            if (ready != null)
            {
                for (var i = 0; i < ready.Count; i++)
                    _pending.Remove(ready[i]);
            }

            if (_pending.Count == 0)
                _debounceTimer.Stop();
        }

        if (ready == null)
            return;

        for (var i = 0; i < ready.Count; i++)
        {
            var path = ready[i];
            if (!File.Exists(path))
                continue;
            UpsertItem(path);
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopWatching();
            _debounceTimer.Dispose();
            _listView.Dispose();
            _autoOpenNewCheck.Dispose();
            _statusLabel.Dispose();
        }
        base.Dispose(disposing);
    }
}
