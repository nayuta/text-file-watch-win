using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace TextFileWatch;

public class MainForm : Form
{
    private sealed class TabIndicatorMetadata
    {
        public string BaseText { get; }
        public bool IsUnseen { get; set; }

        public TabIndicatorMetadata(string baseText, bool isUnseen)
        {
            BaseText = baseText;
            IsUnseen = isUnseen;
        }
    }

    private const string UnseenTabPrefix = "* ";
    private const int TabCloseButtonLogicalSize = 12;
    private const int TabCloseButtonLogicalPadding = 6;
    private const int TabTextLogicalLeftPadding = 8;
    private const int TabTextLogicalRightPadding = 2;
    private const int TabMinWidthLogicalFew = 260;
    private const int TabMinWidthLogicalMany = 140;
    private const int TabMaxWidthLogical = 600;
    private const int TabHeightLogical = 26;

    private readonly TabControl _tabControl;
    private readonly TextBox _emptyTextBox;
    private readonly Button _addButton;
    private readonly Button _closeButton;
    private readonly Button _addDirButton;
    private readonly Label _hintLabel;
    private readonly HashSet<string> _openFilePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TabPage> _openDirectoryTabs = new(StringComparer.OrdinalIgnoreCase);
    private string _lastDirectoryDialogPath = string.Empty;
    private int _hoverCloseTabIndex = -1;
    private TabPage? _dragCandidateTab;
    private System.Drawing.Point _dragStartPoint;

    internal static readonly string[] DefaultExtensions = new[]
    {
        ".txt",
        ".log",
        ".md",
        ".csv",
        ".json",
        ".xml",
        ".ini"
    };

    public MainForm()
    {
        Text = "Text File Watch";
        MinimumSize = new System.Drawing.Size(960, 640);
        KeyPreview = true;

        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            AllowDrop = true
        };
        _tabControl.DrawItem += TabControl_DrawItem;
        _tabControl.MouseDown += TabControl_MouseDown;
        _tabControl.MouseMove += TabControl_MouseMove;
        _tabControl.MouseLeave += TabControl_MouseLeave;
        _tabControl.MouseWheel += TabControl_MouseWheel;
        _tabControl.DragOver += TabControl_DragOver;
        _tabControl.DragDrop += TabControl_DragDrop;
        _tabControl.Resize += (_, _) => UpdateTabItemSize();
        _tabControl.SelectedIndexChanged += (_, _) =>
        {
            MarkSelectedTabAsSeen();
            UpdateCloseButtonState();
            UpdateTabItemSize();
        };
        _tabControl.MouseUp += TabControl_MouseUp;
        UpdateTabItemSize();

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
            Anchor = AnchorStyles.Left,
            Visible = false
        };

        _emptyTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font(FontFamily.GenericMonospace, 10),
            TabStop = false,
            Visible = false
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

        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill
        };
        contentPanel.Controls.Add(_tabControl);
        contentPanel.Controls.Add(_emptyTextBox);

        Controls.Add(contentPanel);
        Controls.Add(topPanel);

        Shown += (_, _) => RestoreTabsOnStartup();
        FormClosing += (_, _) => PersistTabsOnExit();
        KeyDown += MainForm_KeyDown;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    private void UpdateEmptyState()
    {
        var hasTabs = _tabControl.TabPages.Count > 0;
        _tabControl.Visible = hasTabs;
        _emptyTextBox.Visible = !hasTabs;
        _hintLabel.Visible = !hasTabs;
    }

    private void PromptAndAddTab()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Text files|*.txt;*.log;*.md;*.csv;*.json;*.xml;*.ini|All files|*.*",
            Multiselect = false,
            InitialDirectory = GetInitialDirectoryDialogPath()
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var path = dialog.FileName;
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "File does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
            _lastDirectoryDialogPath = parent;

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

        if (!restoredAny)
            LoadAllTextFilesAtStartup();

        if (_tabControl.TabPages.Count > 0)
            _tabControl.SelectedIndex = 0;
        UpdateCloseButtonState();
        UpdateEmptyState();
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
        UpdateCloseButtonState();
        UpdateEmptyState();
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
        var tab = new TabPage
        {
            ToolTipText = path
        };
        tab.Tag = new TabIndicatorMetadata(Path.GetFileName(path), isUnseen: !select);
        ApplyTabText(tab);
        viewer.Dock = DockStyle.Fill;
        tab.Controls.Add(viewer);
        _tabControl.TabPages.Add(tab);
        _openFilePaths.Add(path);
        UpdateTabItemSize();

        if (select)
            _tabControl.SelectedTab = tab;

        UpdateCloseButtonState();
        UpdateEmptyState();
        return true;
    }

    private void MarkSelectedTabAsSeen()
    {
        var tab = _tabControl.SelectedTab;
        if (tab == null)
            return;

        if (tab.Tag is not TabIndicatorMetadata metadata)
            return;

        if (!metadata.IsUnseen)
            return;

        metadata.IsUnseen = false;
        ApplyTabText(tab);
        UpdateTabItemSize();
    }

    private static void ApplyTabText(TabPage tab)
    {
        if (tab.Tag is not TabIndicatorMetadata metadata)
            return;

        tab.Text = metadata.IsUnseen ? $"{UnseenTabPrefix}{metadata.BaseText}" : metadata.BaseText;
    }

    private void CloseSelectedTab()
    {
        if (_tabControl.SelectedTab == null)
            return;
        CloseTab(_tabControl.SelectedTab);
    }

    private void CloseAllExcept(TabPage keep)
    {
        for (var i = _tabControl.TabPages.Count - 1; i >= 0; i--)
        {
            var tab = _tabControl.TabPages[i];
            if (!ReferenceEquals(tab, keep))
                CloseTab(tab);
        }

        if (_tabControl.TabPages.Contains(keep))
            _tabControl.SelectedTab = keep;
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
        UpdateTabItemSize();

        UpdateCloseButtonState();
        UpdateEmptyState();
    }

    private bool IsDirectoryTab(TabPage tab)
    {
        if (tab == null || tab.Controls.Count == 0)
            return false;
        
        return tab.Controls[0] is DirectoryTabView;
    }

    private void CloseAllButThisDirectory(TabPage directoryTab)
    {
        if (directoryTab == null || !IsDirectoryTab(directoryTab))
            return;

        CloseAllExcept(directoryTab);
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
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.F)
        {
            var view = GetSelectedFileTabView();
            if (view != null)
            {
                view.FocusSearch();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            return;
        }

        if (!e.Control && e.KeyCode == Keys.F3)
        {
            var view = GetSelectedFileTabView();
            if (view != null)
            {
                if (e.Shift)
                    view.FindPrevious();
                else
                    view.FindNext();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }
    }

    private FileTabView? GetSelectedFileTabView()
    {
        var tab = _tabControl.SelectedTab;
        if (tab == null || tab.Controls.Count == 0)
            return null;
        return tab.Controls[0] as FileTabView;
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
        menu.Items.Add("Close Others", null, (_, _) => CloseAllExcept(tab));
        menu.Items.Add("Close All", null, (_, _) =>
        {
            for (var i = _tabControl.TabPages.Count - 1; i >= 0; i--)
                CloseTab(_tabControl.TabPages[i]);
        });

        if (IsDirectoryTab(tab))
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Close All But This Directory", null, (_, _) =>
            {
                CloseAllButThisDirectory(tab);
            });
        }
        menu.Show(_tabControl, e.Location);
    }

    private void TabControl_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        var tab = GetTabAt(_tabControl, e.Location);
        if (tab == null)
            return;

        var tabIndex = _tabControl.TabPages.IndexOf(tab);
        var tabRect = _tabControl.GetTabRect(tabIndex);
        if (!GetTabCloseRect(tabRect).Contains(e.Location))
        {
            _dragCandidateTab = tab;
            _dragStartPoint = e.Location;
            return;
        }

        _hoverCloseTabIndex = -1;
        _tabControl.Cursor = Cursors.Default;
        CloseTab(tab);
    }

    private void TabControl_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragCandidateTab != null && Control.MouseButtons.HasFlag(MouseButtons.Left))
        {
            var dragRect = new Rectangle(
                _dragStartPoint.X - SystemInformation.DragSize.Width / 2,
                _dragStartPoint.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width,
                SystemInformation.DragSize.Height);

            if (!dragRect.Contains(e.Location))
            {
                var tabToDrag = _dragCandidateTab;
                _dragCandidateTab = null;
                _hoverCloseTabIndex = -1;
                _tabControl.Cursor = Cursors.Default;
                _tabControl.DoDragDrop(tabToDrag, DragDropEffects.Move);
                return;
            }
        }

        var newHoverIndex = -1;
        var tab = GetTabAt(_tabControl, e.Location);
        if (tab != null)
        {
            var tabIndex = _tabControl.TabPages.IndexOf(tab);
            var tabRect = _tabControl.GetTabRect(tabIndex);
            if (GetTabCloseRect(tabRect).Contains(e.Location))
                newHoverIndex = tabIndex;
        }

        if (newHoverIndex == _hoverCloseTabIndex)
            return;

        var previousHoverIndex = _hoverCloseTabIndex;
        _hoverCloseTabIndex = newHoverIndex;
        _tabControl.Cursor = _hoverCloseTabIndex >= 0 ? Cursors.Hand : Cursors.Default;
        InvalidateTab(previousHoverIndex);
        InvalidateTab(_hoverCloseTabIndex);
    }

    private void TabControl_MouseLeave(object? sender, EventArgs e)
    {
        _dragCandidateTab = null;
        if (_hoverCloseTabIndex < 0)
            return;

        var previousHoverIndex = _hoverCloseTabIndex;
        _hoverCloseTabIndex = -1;
        _tabControl.Cursor = Cursors.Default;
        InvalidateTab(previousHoverIndex);
    }

    private void TabControl_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (_tabControl.TabPages.Count <= 1)
            return;

        if (ModifierKeys != Keys.None)
            return;

        if (e.Delta == 0)
            return;

        var point = _tabControl.PointToClient(MousePosition);
        var tab = GetTabAt(_tabControl, point);
        if (tab == null)
            return;

        var direction = e.Delta < 0 ? 1 : -1;
        var index = _tabControl.SelectedIndex;
        if (index < 0)
            index = _tabControl.TabPages.IndexOf(tab);

        var next = index + direction;
        if (next < 0)
            next = 0;
        if (next >= _tabControl.TabPages.Count)
            next = _tabControl.TabPages.Count - 1;

        _tabControl.SelectedIndex = next;
    }

    private void TabControl_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.None;

        if (!e.Data.GetDataPresent(typeof(TabPage)))
            return;

        var clientPoint = _tabControl.PointToClient(new System.Drawing.Point(e.X, e.Y));
        var targetTab = GetTabAt(_tabControl, clientPoint);
        if (targetTab == null)
            return;

        e.Effect = DragDropEffects.Move;
    }

    private void TabControl_DragDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TabPage)))
            return;

        if (e.Data.GetData(typeof(TabPage)) is not TabPage draggedTab)
            return;

        var clientPoint = _tabControl.PointToClient(new System.Drawing.Point(e.X, e.Y));
        var targetTab = GetTabAt(_tabControl, clientPoint);
        if (targetTab == null)
            return;

        var fromIndex = _tabControl.TabPages.IndexOf(draggedTab);
        var toIndex = _tabControl.TabPages.IndexOf(targetTab);
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
            return;

        MoveTabPage(draggedTab, toIndex);
        _tabControl.SelectedTab = draggedTab;
        UpdateTabItemSize();
    }

    private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var tab = _tabControl.TabPages[e.Index];
        var tabRect = _tabControl.GetTabRect(e.Index);

        var isSelected = (e.State & DrawItemState.Selected) != 0;
        DrawTabBackground(e.Graphics, tabRect, isSelected, tab.BackColor);

        var closeRect = GetTabCloseRect(tabRect);
        if (closeRect.Left < tabRect.Left + 2)
            closeRect = Rectangle.FromLTRB(tabRect.Left + 2, closeRect.Top, tabRect.Left + 2 + closeRect.Width, closeRect.Bottom);
        if (closeRect.Right > tabRect.Right - 2)
            closeRect = Rectangle.FromLTRB(tabRect.Right - 2 - closeRect.Width, closeRect.Top, tabRect.Right - 2, closeRect.Bottom);

        var textRect = Rectangle.FromLTRB(
            tabRect.Left + 8,
            tabRect.Top + 2,
            closeRect.Left - 2,
            tabRect.Bottom - 2);
        if (textRect.Right < textRect.Left)
            textRect = Rectangle.FromLTRB(textRect.Left, textRect.Top, textRect.Left, textRect.Bottom);

        TextRenderer.DrawText(
            e.Graphics,
            tab.Text,
            tab.Font,
            textRect,
            SystemColors.ControlText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        DrawTabCloseButton(e.Graphics, closeRect, isSelected, isHot: e.Index == _hoverCloseTabIndex);

        e.DrawFocusRectangle();
    }

    private Rectangle GetTabCloseRect(Rectangle tabRect)
    {
        var scale = _tabControl.DeviceDpi / 96f;
        var size = (int)Math.Round(TabCloseButtonLogicalSize * scale);
        var padding = (int)Math.Round(TabCloseButtonLogicalPadding * scale);

        var x = tabRect.Right - padding - size;
        var y = tabRect.Top + (tabRect.Height - size) / 2;
        return new Rectangle(x, y, size, size);
    }

    private static void DrawTabBackground(Graphics graphics, Rectangle tabRect, bool isSelected, Color tabPageBackColor)
    {
        if (TabRenderer.IsSupported)
        {
            TabRenderer.DrawTabItem(graphics, tabRect, isSelected ? TabItemState.Selected : TabItemState.Normal);
            return;
        }

        var backColor = isSelected
            ? (tabPageBackColor.IsEmpty ? SystemColors.Control : tabPageBackColor)
            : SystemColors.ControlLight;

        using var brush = new SolidBrush(backColor);
        graphics.FillRectangle(brush, tabRect);
        ControlPaint.DrawBorder(graphics, tabRect, SystemColors.ControlDark, ButtonBorderStyle.Solid);
    }

    private static void DrawTabCloseButton(Graphics graphics, Rectangle bounds, bool isSelected, bool isHot)
    {
        if (isHot)
        {
            using var hotBrush = new SolidBrush(Color.FromArgb(28, Color.Firebrick));
            graphics.FillRectangle(hotBrush, bounds);
        }

        using var pen = new Pen(isHot ? Color.Firebrick : (isSelected ? SystemColors.ControlText : SystemColors.GrayText), 2);
        var originalSmoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var inset = Math.Max(2, bounds.Width / 4);
        var left = bounds.Left + inset;
        var right = bounds.Right - inset;
        var top = bounds.Top + inset;
        var bottom = bounds.Bottom - inset;

        graphics.DrawLine(pen, left, top, right, bottom);
        graphics.DrawLine(pen, left, bottom, right, top);
        graphics.SmoothingMode = originalSmoothing;
    }

    private void InvalidateTab(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= _tabControl.TabPages.Count)
            return;

        try
        {
            _tabControl.Invalidate(_tabControl.GetTabRect(tabIndex));
        }
        catch
        {
            _tabControl.Invalidate();
        }
    }

    private void MoveTabPage(TabPage tab, int newIndex)
    {
        var currentIndex = _tabControl.TabPages.IndexOf(tab);
        if (currentIndex < 0)
            return;

        if (newIndex < 0)
            newIndex = 0;
        if (newIndex >= _tabControl.TabPages.Count)
            newIndex = _tabControl.TabPages.Count - 1;

        if (currentIndex == newIndex)
            return;

        _tabControl.SuspendLayout();
        _tabControl.TabPages.Remove(tab);
        _tabControl.TabPages.Insert(newIndex, tab);
        _tabControl.ResumeLayout();
        _tabControl.Invalidate();
    }

    private void UpdateTabItemSize()
    {
        if (_tabControl.TabPages.Count == 0)
            return;

        var scale = _tabControl.DeviceDpi / 96f;
        var minWidthLogical = _tabControl.TabPages.Count <= 4 ? TabMinWidthLogicalFew : TabMinWidthLogicalMany;
        var minWidth = (int)Math.Round(minWidthLogical * scale);
        var maxWidth = (int)Math.Round(TabMaxWidthLogical * scale);
        var height = (int)Math.Round(TabHeightLogical * scale);

        var closeSize = (int)Math.Round(TabCloseButtonLogicalSize * scale);
        var closePadding = (int)Math.Round(TabCloseButtonLogicalPadding * scale);
        var leftPadding = (int)Math.Round(TabTextLogicalLeftPadding * scale);
        var rightPadding = (int)Math.Round(TabTextLogicalRightPadding * scale);

        var selectedTab = _tabControl.SelectedTab;
        var textWidth = 0;
        if (selectedTab != null)
        {
            var measured = TextRenderer.MeasureText(selectedTab.Text, selectedTab.Font, new Size(int.MaxValue, height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            textWidth = measured.Width;
        }

        var widthByText = leftPadding + textWidth + rightPadding + closePadding + closeSize;

        var available = Math.Max(0, _tabControl.ClientSize.Width - 6);
        var widthByCount = available > 0 ? available / _tabControl.TabPages.Count : maxWidth;

        var absoluteMinimum = leftPadding + rightPadding + closePadding + closeSize + (int)Math.Round(24 * scale);
        var width = Math.Min(widthByText, widthByCount);
        width = Math.Clamp(width, Math.Max(minWidth, absoluteMinimum), maxWidth);

        if (_tabControl.ItemSize.Width == width && _tabControl.ItemSize.Height == height)
            return;

        _tabControl.ItemSize = new Size(width, height);
        _tabControl.Invalidate();
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
        view.CloseOpenedFileTabsRequested += CloseFileTabsInDirectory;

        var tab = new TabPage($"Dir: {Path.GetFileName(normalizedDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}")
        {
            ToolTipText = normalizedDirectory
        };
        tab.Controls.Add(view);

        _tabControl.TabPages.Add(tab);
        _openDirectoryTabs[normalizedDirectory] = tab;
        UpdateDirectoryTabTitles();
        UpdateTabItemSize();

        if (select)
            _tabControl.SelectedTab = tab;

        UpdateCloseButtonState();
        UpdateEmptyState();
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
        UpdateTabItemSize();
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

    private int CloseFileTabsInDirectory(string directory)
    {
        var normalizedDirectory = NormalizeDirectoryPath(directory);
        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            return 0;

        var directoryPrefix = EnsureTrailingSeparator(normalizedDirectory);

        var tabsToClose = new List<TabPage>();
        foreach (TabPage tab in _tabControl.TabPages)
        {
            var path = tab.ToolTipText;
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (!_openFilePaths.Contains(path))
                continue;
            if (!IsPathUnderDirectory(path, directoryPrefix))
                continue;
            tabsToClose.Add(tab);
        }

        foreach (var tab in tabsToClose)
            CloseTab(tab);

        return tabsToClose.Count;
    }

    private static string EnsureTrailingSeparator(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return directory;

        return directory.EndsWith(Path.DirectorySeparatorChar) || directory.EndsWith(Path.AltDirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
    }

    private static bool IsPathUnderDirectory(string path, string directoryPrefixWithSeparator)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(directoryPrefixWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return path.StartsWith(directoryPrefixWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
    }
}
