using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TextFileWatch;

public class MainForm : Form
{
    private readonly TabControl _tabControl;
    private readonly Button _addButton;
    private readonly Button _closeButton;
    private readonly Button _addDirButton;
    private readonly Label _hintLabel;
    private readonly HashSet<string> _openFilePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TabPage> _openDirectoryTabs = new(StringComparer.OrdinalIgnoreCase);
    private string _lastDirectoryDialogPath = string.Empty;

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
        var savedFiles = state.OpenFiles;
        var savedDirectories = state.OpenDirectories;
        _lastDirectoryDialogPath = Path.Combine(AppContext.BaseDirectory, "logs");

        for (var i = 0; i < savedFiles.Count; i++)
        {
            var path = savedFiles[i];
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (!File.Exists(path))
                continue;
            restoredAny |= AddFileTab(path, select: false);
        }

        for (var i = 0; i < savedDirectories.Count; i++)
        {
            var directory = savedDirectories[i];
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            AddDirectoryTab(directory, select: false);
        }

        if (_tabControl.TabPages.Count > 0)
            _hintLabel.Visible = false;

        if (!restoredAny)
            LoadAllTextFilesAtStartup();

        if (_tabControl.TabPages.Count > 0)
            _tabControl.SelectedIndex = 0;
        UpdateCloseButtonState();
    }

    private void PersistTabsOnExit()
    {
        try
        {
            var openFiles = _openFilePaths.ToList();
            openFiles.Sort(StringComparer.OrdinalIgnoreCase);
            var openDirectories = _openDirectoryTabs.Keys.ToList();
            openDirectories.Sort(StringComparer.OrdinalIgnoreCase);
            AppStateStore.Save(openFiles, openDirectories);
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
        if (_openFilePaths.Contains(path))
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
        _openFilePaths.Add(path);

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
        var path = tab.ToolTipText;
        if (!string.IsNullOrWhiteSpace(path))
        {
            _openFilePaths.Remove(path);
            if (_openDirectoryTabs.Remove(path))
                UpdateDirectoryTabTitles();
        }

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
            SelectedPath = GetInitialDirectoryDialogPath()
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _lastDirectoryDialogPath = dialog.SelectedPath;
        AddDirectoryTab(dialog.SelectedPath, select: true);
    }

    private bool AddDirectoryTab(string directory, bool select)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        var normalizedDirectory = NormalizeDirectoryPath(directory);
        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            return false;

        if (_openDirectoryTabs.TryGetValue(normalizedDirectory, out var existingTab))
        {
            if (select)
                _tabControl.SelectedTab = existingTab;
            return false;
        }

        DirectoryTabView view;
        try
        {
            view = new DirectoryTabView(normalizedDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to watch directory: {ex.Message}", "Directory Watch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        view.Dock = DockStyle.Fill;
        view.OpenFileRequested += path => AddFileTab(path, select: true);

        var tab = new TabPage($"Dir: {Path.GetFileName(normalizedDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}")
        {
            ToolTipText = normalizedDirectory
        };
        tab.Controls.Add(view);

        _tabControl.TabPages.Add(tab);
        _openDirectoryTabs[normalizedDirectory] = tab;
        UpdateDirectoryTabTitles();

        _hintLabel.Visible = false;
        if (select)
            _tabControl.SelectedTab = tab;

        UpdateCloseButtonState();
        return true;
    }

    private string GetInitialDirectoryDialogPath()
    {
        if (!string.IsNullOrWhiteSpace(_lastDirectoryDialogPath))
            return _lastDirectoryDialogPath;
        if (_openDirectoryTabs.Count > 0)
            return _openDirectoryTabs.Keys.First();
        return Path.Combine(AppContext.BaseDirectory, "logs");
    }

    private void UpdateDirectoryTabTitles()
    {
        if (_openDirectoryTabs.Count == 0)
            return;

        var directories = _openDirectoryTabs.Keys.ToList();
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            titles[directory] = $"Dir: {GetUniqueDirectorySuffix(directory, directories)}";
        }

        foreach (var (directory, tab) in _openDirectoryTabs)
        {
            if (titles.TryGetValue(directory, out var title))
                tab.Text = title;
        }
    }

    private static string NormalizeDirectoryPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return directory;

        var trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private static string GetUniqueDirectorySuffix(string directory, IReadOnlyList<string> allDirectories)
    {
        var segments = GetDirectorySegments(directory);
        if (segments.Count == 0)
            return directory;

        var minSegments = segments.Count >= 2 ? 2 : 1;
        for (var segmentCount = minSegments; segmentCount <= segments.Count; segmentCount++)
        {
            var suffix = JoinSuffix(segments, segmentCount);
            var isUnique = true;
            for (var i = 0; i < allDirectories.Count; i++)
            {
                var other = allDirectories[i];
                if (string.Equals(other, directory, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(JoinSuffix(GetDirectorySegments(other), segmentCount), suffix, StringComparison.OrdinalIgnoreCase))
                {
                    isUnique = false;
                    break;
                }
            }

            if (isUnique)
                return suffix;
        }

        return directory;
    }

    private static List<string> GetDirectorySegments(string directory)
    {
        var segments = new List<string>();
        var current = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (true)
        {
            var name = Path.GetFileName(current);
            if (string.IsNullOrWhiteSpace(name))
                break;

            segments.Add(name);

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent))
                break;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        return segments;
    }

    private static string JoinSuffix(List<string> leafToRootSegments, int segmentCount)
    {
        var take = Math.Min(segmentCount, leafToRootSegments.Count);
        if (take <= 0)
            return string.Empty;

        var parts = new string[take];
        for (var i = 0; i < take; i++)
        {
            parts[take - 1 - i] = leafToRootSegments[i];
        }

        return string.Join(Path.DirectorySeparatorChar, parts);
    }
}
