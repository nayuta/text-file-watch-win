using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace TextFileWatch;

public class MainForm : Form
{
    private readonly TabControl _tabControl;
    private readonly Button _addButton;
    private readonly Button _closeButton;
    private readonly Button _addDirButton;
    private readonly Label _hintLabel;
    private readonly HashSet<string> _openPaths = new(StringComparer.OrdinalIgnoreCase);

    private string _watchedDirectory = string.Empty;
    private TabPage? _directoryTab;
    private DirectoryTabView? _directoryView;

    internal static readonly string[] DefaultExtensions = new[]
    {
        ".txt",
        ".log",
        ".md",
        ".csv",
        ".json",
        ".xml"
    };

    public MainForm()
    {
        Text = "Text File Watch";
        MinimumSize = new System.Drawing.Size(960, 640);
        KeyPreview = true;

        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill
        };
        _tabControl.SelectedIndexChanged += (_, _) => UpdateCloseButtonState();
        _tabControl.MouseUp += TabControl_MouseUp;

        _addButton = new Button
        {
            Text = "Add File…",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _addButton.Click += (_, _) => PromptAndAddTab();

        _closeButton = new Button
        {
            Text = "Close Tab",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Enabled = false
        };
        _closeButton.Click += (_, _) => CloseSelectedTab();

        _addDirButton = new Button
        {
            Text = "Add Dir…",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _addDirButton.Click += (_, _) => PromptAndAddDirectoryTab();

        _hintLabel = new Label
        {
            Text = "Add a file to start watching.",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        topPanel.Controls.Add(_addButton);
        topPanel.Controls.Add(_closeButton);
        topPanel.Controls.Add(_addDirButton);
        topPanel.Controls.Add(_hintLabel);

        Controls.Add(_tabControl);
        Controls.Add(topPanel);

        Shown += (_, _) => RestoreTabsOnStartup();
        FormClosing += (_, _) => PersistTabsOnExit();
        KeyDown += MainForm_KeyDown;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_directoryView != null)
            {
                _directoryView.Dispose();
                _directoryView = null;
            }
        }
        base.Dispose(disposing);
    }

    private void PromptAndAddTab()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Text files|*.txt;*.log;*.md;*.csv;*.json;*.xml|All files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var path = dialog.FileName;
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "File does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        AddFileTab(path);
    }

    private void RestoreTabsOnStartup()
    {
        var restoredAny = false;
        var state = AppStateStore.Load();
        var saved = state.OpenFiles;
        _watchedDirectory = state.WatchedDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");

        for (var i = 0; i < saved.Count; i++)
        {
            var path = saved[i];
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (!File.Exists(path))
                continue;
            restoredAny |= AddFileTab(path, select: false);
        }

        if (!restoredAny)
            LoadAllTextFilesAtStartup();

        if (state.WatchDirectoryEnabled && Directory.Exists(_watchedDirectory))
            AddDirectoryTab(_watchedDirectory, select: false);

        if (_tabControl.TabPages.Count > 0)
            _tabControl.SelectedIndex = 0;
        UpdateCloseButtonState();
    }

    private void PersistTabsOnExit()
    {
        try
        {
            AppStateStore.Save(_openPaths, _directoryView != null ? _watchedDirectory : null, _directoryView != null);
        }
        catch
        {
            // best-effort only
        }
    }

    private void LoadAllTextFilesAtStartup()
    {
        var directory = AppContext.BaseDirectory;
        List<string> paths;
        try
        {
            paths = new List<string>(Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly));
        }
        catch
        {
            return;
        }

        paths.Sort(StringComparer.OrdinalIgnoreCase);

        var anyAdded = false;
        foreach (var path in paths)
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(extension))
                continue;

            var matches = false;
            for (var i = 0; i < DefaultExtensions.Length; i++)
            {
                if (string.Equals(extension, DefaultExtensions[i], StringComparison.OrdinalIgnoreCase))
                {
                    matches = true;
                    break;
                }
            }
            if (!matches)
                continue;

            anyAdded |= AddFileTab(path, select: false);
        }

        if (anyAdded)
            _hintLabel.Visible = false;
        UpdateCloseButtonState();
    }

    private bool AddFileTab(string path, bool select = true)
    {
        if (_openPaths.Contains(path))
        {
            for (var i = 0; i < _tabControl.TabPages.Count; i++)
            {
                var tabPage = _tabControl.TabPages[i];
                if (string.Equals(tabPage.ToolTipText, path, StringComparison.OrdinalIgnoreCase))
                {
                    _tabControl.SelectedTab = tabPage;
                    break;
                }
            }
            return false;
        }

        var viewer = new FileTabView(path);
        var tab = new TabPage(Path.GetFileName(path))
        {
            ToolTipText = path
        };
        viewer.Dock = DockStyle.Fill;
        tab.Controls.Add(viewer);
        _tabControl.TabPages.Add(tab);
        _openPaths.Add(path);

        _hintLabel.Visible = false;
        if (select)
            _tabControl.SelectedTab = tab;

        UpdateCloseButtonState();
        return true;
    }

    private void CloseSelectedTab()
    {
        if (_tabControl.SelectedTab == null)
            return;
        CloseTab(_tabControl.SelectedTab);
    }

    private void CloseTab(TabPage tab)
    {
        if (_directoryTab != null && ReferenceEquals(tab, _directoryTab))
        {
            if (_directoryView != null)
            {
                _directoryView.Dispose();
                _directoryView = null;
            }
            _directoryTab = null;
            _watchedDirectory = string.Empty;
        }

        var path = tab.ToolTipText;
        if (!string.IsNullOrWhiteSpace(path))
            _openPaths.Remove(path);

        _tabControl.TabPages.Remove(tab);
        tab.Dispose();

        if (_tabControl.TabPages.Count == 0)
            _hintLabel.Visible = true;

        UpdateCloseButtonState();
    }

    private void UpdateCloseButtonState()
    {
        _closeButton.Enabled = _tabControl.TabPages.Count > 0;
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.W)
        {
            CloseSelectedTab();
            e.Handled = true;
        }
    }

    private void TabControl_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;

        var tab = GetTabAt(_tabControl, e.Location);
        if (tab == null)
            return;

        _tabControl.SelectedTab = tab;

        using var menu = new ContextMenuStrip();
        menu.Items.Add("Close", null, (_, _) => CloseTab(tab));
        menu.Items.Add("Close Others", null, (_, _) =>
        {
            var keep = tab;
            for (var i = _tabControl.TabPages.Count - 1; i >= 0; i--)
            {
                var t = _tabControl.TabPages[i];
                if (!ReferenceEquals(t, keep))
                    CloseTab(t);
            }
        });
        menu.Items.Add("Close All", null, (_, _) =>
        {
            for (var i = _tabControl.TabPages.Count - 1; i >= 0; i--)
                CloseTab(_tabControl.TabPages[i]);
        });

        menu.Show(_tabControl, e.Location);
    }

    private static TabPage? GetTabAt(TabControl tabControl, System.Drawing.Point point)
    {
        for (var i = 0; i < tabControl.TabPages.Count; i++)
        {
            if (tabControl.GetTabRect(i).Contains(point))
                return tabControl.TabPages[i];
        }
        return null;
    }

    private void PromptAndAddDirectoryTab()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select directory to watch (shows as a tab).",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(_watchedDirectory) ? Path.Combine(AppContext.BaseDirectory, "logs") : _watchedDirectory
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _watchedDirectory = dialog.SelectedPath;
        AddDirectoryTab(_watchedDirectory, select: true);
    }

    private void AddDirectoryTab(string directory, bool select)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to watch directory: {ex.Message}", "Directory Watch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_directoryView != null)
        {
            _directoryView.Dispose();
            _directoryView = null;
        }

        _watchedDirectory = directory;
        _directoryView = new DirectoryTabView(directory);
        _directoryView.Dock = DockStyle.Fill;
        _directoryView.OpenFileRequested += path => AddFileTab(path, select: true);

        var title = $"Dir: {Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
        var tab = new TabPage(title) { ToolTipText = directory };
        tab.Controls.Add(_directoryView);

        if (_directoryTab != null)
        {
            var index = _tabControl.TabPages.IndexOf(_directoryTab);
            _tabControl.TabPages.Remove(_directoryTab);
            _directoryTab.Dispose();
            _directoryTab = tab;
            _tabControl.TabPages.Insert(Math.Max(0, index), tab);
        }
        else
        {
            _directoryTab = tab;
            _tabControl.TabPages.Add(tab);
        }

        if (select)
            _tabControl.SelectedTab = tab;

        UpdateCloseButtonState();
    }
}
