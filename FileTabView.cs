using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace TextFileWatch;

public sealed class FileTabView : UserControl
{
    private readonly string _path;
    private readonly FileDocument _document;
    private readonly WinFormsTimer _timer;
    private readonly RichTextBox _viewer;
    private readonly Color _normalViewerForeColor;
    private readonly Label _statusLabel;
    private readonly NumericUpDown _intervalUpDown;
    private readonly CheckBox _watchCheck;
    private readonly CheckBox _highlightCheck;
    private readonly CheckBox _scrollToChangesCheck;
    private bool _isMissing;
    private bool _hasRenderedSnapshot;

    public FileTabView(string path)
    {
        _path = path;
        _document = new FileDocument(path);
        DoubleBuffered = true;

        _viewer = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font(FontFamily.GenericMonospace, 10),
            WordWrap = false,
            HideSelection = false
        };
        _normalViewerForeColor = _viewer.ForeColor;

        var refreshButton = new Button
        {
            Text = "Refresh now",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        refreshButton.Click += (_, _) => RefreshFromFile(force: true);

        _watchCheck = new CheckBox
        {
            Text = "Watch",
            Checked = true,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _watchCheck.CheckedChanged += (_, _) => UpdateTimerState();

        _highlightCheck = new CheckBox
        {
            Text = "Highlight changes",
            Checked = true,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _scrollToChangesCheck = new CheckBox
        {
            Text = "Scroll to changes",
            Checked = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _intervalUpDown = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 3600,
            Value = 1,
            Increment = 1,
            Width = 80,
            Anchor = AnchorStyles.Left
        };
        _intervalUpDown.ValueChanged += (_, _) => UpdateTimerInterval();

        _statusLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = "Waiting for first refresh…"
        };

        var pathLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = _path,
            MaximumSize = new Size(900, 0)
        };

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(6)
        };
        controls.Controls.Add(refreshButton);
        controls.Controls.Add(_watchCheck);
        controls.Controls.Add(new Label { Text = "Interval (s):", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(6, 6, 2, 2) });
        controls.Controls.Add(_intervalUpDown);
        controls.Controls.Add(_highlightCheck);
        controls.Controls.Add(_scrollToChangesCheck);
        controls.Controls.Add(_statusLabel);

        var pathPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(6)
        };
        pathPanel.Controls.Add(new Label { Text = "File:", AutoSize = true, Anchor = AnchorStyles.Left });
        pathPanel.Controls.Add(pathLabel);

        // Dock layout is applied in reverse z-order; add Fill first so it lays out last (no overlap).
        Controls.Add(_viewer);
        Controls.Add(controls);
        Controls.Add(pathPanel);

        _timer = new WinFormsTimer();
        _timer.Tick += (_, _) => RefreshFromFile(force: false);
        UpdateTimerInterval();
        UpdateTimerState();
        RefreshFromFile(force: true);
    }

    private void UpdateTimerInterval()
    {
        _timer.Interval = (int)_intervalUpDown.Value * 1000;
    }

    private void UpdateTimerState()
    {
        _timer.Enabled = _watchCheck.Checked;
    }

    private void RefreshFromFile(bool force)
    {
        if (!force && !_watchCheck.Checked)
            return;

        if (!File.Exists(_path))
        {
            ShowMissing($"File missing: {_path}");
            _statusLabel.Text = $"File missing {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            return;
        }

        var wasMissing = _isMissing;
        if (wasMissing)
        {
            _isMissing = false;
            _viewer.ForeColor = _normalViewerForeColor;
        }

        var result = _document.TryRefresh(force: force || wasMissing);
        if (!result.Success)
        {
            _statusLabel.Text = $"Error reading file: {result.ErrorMessage}";
            return;
        }

        var now = DateTime.Now;
        if (wasMissing)
        {
            ApplyTextPlain(_document.Text);
            _hasRenderedSnapshot = true;
            _statusLabel.Text = $"Recovered {now:yyyy-MM-dd HH:mm:ss}";
            return;
        }

        if (!_hasRenderedSnapshot)
        {
            ApplyTextPlain(_document.Text);
            _hasRenderedSnapshot = true;
            _statusLabel.Text = $"Loaded {now:yyyy-MM-dd HH:mm:ss}";
            return;
        }

        if (result.Changed)
        {
            ApplyTextWithHighlight(result.OldText, result.NewText);
            _statusLabel.Text = $"Updated {now:yyyy-MM-dd HH:mm:ss}";
            return;
        }

        _statusLabel.Text = $"Checked {now:yyyy-MM-dd HH:mm:ss}";
    }

    private void ShowMissing(string message)
    {
        if (_isMissing)
            return;
        _isMissing = true;

        _viewer.SuspendLayout();
        SetRedraw(_viewer, enabled: false);
        _viewer.Clear();
        _viewer.ForeColor = Color.DarkRed;
        _viewer.AppendText(message);
        SetRedraw(_viewer, enabled: true);
        _viewer.Invalidate();
        _viewer.ResumeLayout();
    }

    private void ApplyTextWithHighlight(string oldText, string newText)
    {
        var wasAtBottom = IsAtBottom(_viewer);
        var firstVisibleLine = GetFirstVisibleLineManaged(_viewer);
        var hasScrollPos = TryGetScrollPos(_viewer, out var scrollPos);
        var selectionStart = _viewer.SelectionStart;
        var selectionLength = _viewer.SelectionLength;

        var highlight = _highlightCheck.Checked;
        var scrollToChanges = _scrollToChangesCheck.Checked;
        string[] newLines = newText.Replace("\r\n", "\n").Split('\n');
        string[] oldLines = oldText.Replace("\r\n", "\n").Split('\n');

        var needDiff = highlight || scrollToChanges;
        bool[]? changed = null;
        var firstChangedLine = -1;
        if (needDiff)
        {
            changed = LineDiff.ComputeChangedNewLines(oldLines, newLines);
            for (var i = 0; i < changed.Length; i++)
            {
                if (!changed[i])
                    continue;
                firstChangedLine = i;
                break;
            }
        }

        _viewer.SuspendLayout();
        SetRedraw(_viewer, enabled: false);
        _viewer.Clear();

        for (int i = 0; i < newLines.Length; i++)
        {
            int start = _viewer.TextLength;
            _viewer.AppendText(newLines[i]);

            if (highlight && changed != null && i < changed.Length && changed[i])
            {
                _viewer.Select(start, newLines[i].Length);
                _viewer.SelectionBackColor = Color.LightGoldenrodYellow;
                _viewer.Select(start + newLines[i].Length, 0);
                _viewer.SelectionBackColor = _viewer.BackColor;
            }

            if (i < newLines.Length - 1)
                _viewer.AppendText(Environment.NewLine);
        }

        var maxIndex = _viewer.TextLength;
        if (selectionStart > maxIndex)
            selectionStart = maxIndex;
        if (selectionStart + selectionLength > maxIndex)
            selectionLength = Math.Max(0, maxIndex - selectionStart);

        _viewer.Select(selectionStart, selectionLength);

        if (wasAtBottom)
        {
            _viewer.Select(_viewer.TextLength, 0);
            _viewer.ScrollToCaret();
        }
        else
        {
            if (scrollToChanges && firstChangedLine >= 0)
            {
                ScrollToLineManaged(_viewer, Math.Min(firstChangedLine, GetLastLineIndex(_viewer)));
            }
            else if (hasScrollPos && TrySetScrollPos(_viewer, scrollPos))
            {
            }
            else
            {
                ScrollToLineManaged(_viewer, Math.Min(firstVisibleLine, GetLastLineIndex(_viewer)));
            }
        }

        SetRedraw(_viewer, enabled: true);
        _viewer.Invalidate();
        _viewer.ResumeLayout();
    }

    private void ApplyTextPlain(string text)
    {
        _viewer.SuspendLayout();
        SetRedraw(_viewer, enabled: false);
        _viewer.Clear();
        _viewer.AppendText(text);
        SetRedraw(_viewer, enabled: true);
        _viewer.Invalidate();
        _viewer.ResumeLayout();
    }

    private static bool IsAtBottom(RichTextBox viewer)
    {
        if (viewer.TextLength == 0)
            return true;

        var y = Math.Max(0, viewer.ClientSize.Height - 1);
        var lastVisibleChar = viewer.GetCharIndexFromPosition(new Point(1, y));
        var lastVisibleLine = viewer.GetLineFromCharIndex(lastVisibleChar);
        var lastLine = GetLastLineIndex(viewer);
        return lastVisibleLine >= lastLine - 1;
    }

    private static int GetLastLineIndex(RichTextBox viewer)
    {
        if (viewer.TextLength == 0)
            return 0;
        return viewer.GetLineFromCharIndex(Math.Max(0, viewer.TextLength - 1));
    }

    private static int GetFirstVisibleLineManaged(RichTextBox viewer)
    {
        var charIndex = viewer.GetCharIndexFromPosition(new Point(1, 1));
        return viewer.GetLineFromCharIndex(Math.Max(0, charIndex));
    }

    private static void ScrollToLineManaged(RichTextBox viewer, int firstVisibleLine)
    {
        if (firstVisibleLine <= 0)
            return;
        var charIndex = viewer.GetFirstCharIndexFromLine(firstVisibleLine);
        if (charIndex < 0)
            return;
        viewer.Select(charIndex, 0);
        viewer.ScrollToCaret();
    }

    private static void SetRedraw(Control control, bool enabled)
    {
        if (!control.IsHandleCreated)
            return;
        SendMessage(control.Handle, WM_SETREDRAW, enabled ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static bool TryGetScrollPos(RichTextBox viewer, out POINT scrollPos)
    {
        scrollPos = default;
        if (!viewer.IsHandleCreated)
            return false;
        return SendMessage(viewer.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref scrollPos) != IntPtr.Zero;
    }

    private static bool TrySetScrollPos(RichTextBox viewer, POINT scrollPos)
    {
        if (!viewer.IsHandleCreated)
            return false;
        return SendMessage(viewer.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref scrollPos) != IntPtr.Zero;
    }

    private const int WM_SETREDRAW = 0x000B;
    private const int EM_GETSCROLLPOS = 0x04DD;
    private const int EM_SETSCROLLPOS = 0x04DE;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref POINT lParam);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _viewer.Dispose();
            _statusLabel.Dispose();
            _intervalUpDown.Dispose();
            _watchCheck.Dispose();
            _highlightCheck.Dispose();
            _scrollToChangesCheck.Dispose();
        }

        base.Dispose(disposing);
    }
}
