using System;
using System.Collections.Generic;
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
    private readonly TextBox _findBox;
    private readonly Button _findPrevButton;
    private readonly Button _findNextButton;
    private readonly CheckBox _findMatchCaseCheck;
    private readonly Label _findStatusLabel;
    private string _lastFindText = string.Empty;
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

        _findBox = new TextBox
        {
            Width = 220,
            PlaceholderText = "Find…",
            Anchor = AnchorStyles.Left
        };
        _findBox.TextChanged += (_, _) => _findStatusLabel.Text = "";
        _findBox.KeyDown += FindBox_KeyDown;

        _findPrevButton = new Button
        {
            Text = "Prev",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _findPrevButton.Click += (_, _) => FindPrevious();

        _findNextButton = new Button
        {
            Text = "Next",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _findNextButton.Click += (_, _) => FindNext();

        _findMatchCaseCheck = new CheckBox
        {
            Text = "Match case",
            Checked = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _findStatusLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = ""
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

        var toolbarMargin = new Padding(6, 4, 6, 0);
        refreshButton.Margin = toolbarMargin;
        _watchCheck.Margin = toolbarMargin;
        _intervalUpDown.Margin = toolbarMargin;
        _highlightCheck.Margin = toolbarMargin;
        _scrollToChangesCheck.Margin = toolbarMargin;
        _statusLabel.Margin = toolbarMargin;
        _findBox.Margin = toolbarMargin;
        _findPrevButton.Margin = toolbarMargin;
        _findNextButton.Margin = toolbarMargin;
        _findMatchCaseCheck.Margin = toolbarMargin;
        _findStatusLabel.Margin = toolbarMargin;

        var pathLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = _path,
            MaximumSize = new Size(900, 0),
            Padding = new Padding(0, 4, 0, 0)
        };

        var intervalLabel = new Label
        {
            Text = "Interval (s):",
            AutoSize = false,
            Anchor = AnchorStyles.Left,
            Height = _intervalUpDown.Height,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(6, 4, 2, 0)
        };
        intervalLabel.Width = TextRenderer.MeasureText(intervalLabel.Text, intervalLabel.Font, new Size(int.MaxValue, intervalLabel.Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 2;

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(6)
        };
        controls.Controls.Add(_watchCheck);
        controls.Controls.Add(intervalLabel);
        controls.Controls.Add(_intervalUpDown);
        controls.Controls.Add(refreshButton);
        controls.Controls.Add(_highlightCheck);
        controls.Controls.Add(_scrollToChangesCheck);
        controls.Controls.Add(new Label { Text = "Find:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 0, 0), Margin = toolbarMargin });
        controls.Controls.Add(_findBox);
        controls.Controls.Add(_findPrevButton);
        controls.Controls.Add(_findNextButton);
        controls.Controls.Add(_findMatchCaseCheck);
        controls.Controls.Add(_findStatusLabel);
        controls.Controls.Add(_statusLabel);

        var pathPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(6)
        };
        pathPanel.Controls.Add(new Label { Text = "File:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 0, 0) });
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

    public void FocusSearch()
    {
        _findBox.Focus();
        _findBox.SelectAll();
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

    private void FindBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            if (e.Shift)
                FindPrevious();
            else
                FindNext();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            _viewer.Focus();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    public bool FindNext()
    {
        return FindCore(reverse: false);
    }

    public bool FindPrevious()
    {
        return FindCore(reverse: true);
    }

    private bool FindCore(bool reverse)
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            _findStatusLabel.Text = "Empty";
            return false;
        }

        var options = RichTextBoxFinds.None;
        if (_findMatchCaseCheck.Checked)
            options |= RichTextBoxFinds.MatchCase;
        if (reverse)
            options |= RichTextBoxFinds.Reverse;

        var startIndex = reverse ? Math.Max(0, _viewer.SelectionStart - 1) : _viewer.SelectionStart + _viewer.SelectionLength;
        if (!string.Equals(_lastFindText, query, StringComparison.Ordinal))
        {
            _lastFindText = query;
            startIndex = reverse ? _viewer.TextLength : 0;
        }

        // For reverse: Find searches backwards from end to start, so pass (0, startIndex)
        // For forward: Find searches from start to end, so pass (startIndex, TextLength)
        var foundIndex = reverse
            ? (startIndex > 0 ? _viewer.Find(query, 0, startIndex, options) : -1)
            : _viewer.Find(query, startIndex, _viewer.TextLength, options);

        var wrapped = false;
        if (foundIndex < 0)
        {
            wrapped = true;
            // Wrap: for reverse search from end back to startIndex, for forward from 0 to end
            foundIndex = reverse
                ? _viewer.Find(query, startIndex, _viewer.TextLength, options)
                : _viewer.Find(query, 0, _viewer.TextLength, options);
        }

        if (foundIndex < 0)
        {
            _findStatusLabel.Text = "Not found";
            return false;
        }

        // Select the found text and scroll to make it visible
        // First scroll to the start position, then apply the full selection
        _viewer.Select(foundIndex, 0);
        _viewer.ScrollToCaret();
        _viewer.Select(foundIndex, query.Length);

        var line = _viewer.GetLineFromCharIndex(foundIndex);
        var lineStart = _viewer.GetFirstCharIndexFromLine(line);
        var col = foundIndex - Math.Max(0, lineStart);
        var position = $"{line + 1}:{col + 1}";
        if (wrapped)
            _findStatusLabel.Text = reverse ? $"Passed start ({position})" : $"Passed end ({position})";
        else
            _findStatusLabel.Text = position;
        return true;
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
        var normalizedNew = NormalizeLineEndingsToLf(newText);
        var normalizedOld = NormalizeLineEndingsToLf(oldText);
        string[] newLines = normalizedNew.Split('\n');
        string[] oldLines = normalizedOld.Split('\n');

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
        _viewer.Text = string.Join(Environment.NewLine, newLines);
        if (_viewer.TextLength > 0)
        {
            _viewer.Select(0, _viewer.TextLength);
            _viewer.SelectionBackColor = _viewer.BackColor;
        }

        if (highlight && changed != null)
        {
            var lineStarts = ComputeLineStarts(newLines, Environment.NewLine);
            var maxHighlightLines = Math.Min(changed.Length, newLines.Length);
            for (var i = 0; i < maxHighlightLines; i++)
            {
                if (!changed[i])
                    continue;
                var start = lineStarts[i];
                var length = newLines[i].Length;
                if (length <= 0)
                    continue;
                _viewer.Select(start, length);
                _viewer.SelectionBackColor = Color.LightGoldenrodYellow;
            }
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
            else
            {
                ScrollToLineManaged(_viewer, Math.Min(firstVisibleLine, GetLastLineIndex(_viewer)));
            }

            if (hasScrollPos)
                RestoreHorizontalScroll(_viewer, scrollPos.X);
        }

        SetRedraw(_viewer, enabled: true);
        _viewer.Invalidate();
        _viewer.ResumeLayout();
    }

    private void ApplyTextPlain(string text)
    {
        _viewer.SuspendLayout();
        SetRedraw(_viewer, enabled: false);
        _viewer.Text = ToViewerLineEndings(text);
        if (_viewer.TextLength > 0)
        {
            _viewer.Select(0, _viewer.TextLength);
            _viewer.SelectionBackColor = _viewer.BackColor;
        }
        _viewer.Select(0, 0);
        SetRedraw(_viewer, enabled: true);
        _viewer.ScrollToCaret();
        _viewer.Invalidate();
        _viewer.ResumeLayout();
    }

    private static string NormalizeLineEndingsToLf(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string ToViewerLineEndings(string text)
    {
        var lf = NormalizeLineEndingsToLf(text);
        return Environment.NewLine == "\n" ? lf : lf.Replace("\n", Environment.NewLine);
    }

    private static int[] ComputeLineStarts(IReadOnlyList<string> lines, string newline)
    {
        var starts = new int[lines.Count];
        var index = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            starts[i] = index;
            index += lines[i].Length;
            if (i < lines.Count - 1)
                index += newline.Length;
        }
        return starts;
    }

    private static void RestoreHorizontalScroll(RichTextBox viewer, int desiredX)
    {
        if (!viewer.IsHandleCreated)
            return;
        if (!TryGetScrollPos(viewer, out var current))
            return;
        var target = new POINT { X = desiredX, Y = current.Y };
        TrySetScrollPos(viewer, target);
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
            _findBox.Dispose();
            _findPrevButton.Dispose();
            _findNextButton.Dispose();
            _findMatchCaseCheck.Dispose();
            _findStatusLabel.Dispose();
        }

        base.Dispose(disposing);
    }
}
