using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.Citation680;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.Citation680;

/// <summary>
/// One G5000 touchscreen controller (GTC) as a list: the page title, its text, one row per
/// button and the knob labels, read through coherent-gtc-agent.js over the Coherent debugger.
/// Enter on a button row presses it; on a keyboard or keypad page typed keys press the
/// on-screen keys; Ctrl/Alt arrow chords turn the knobs; Ctrl+Home / Ctrl+Backspace / Ctrl+G
/// press Home / Back / MSG; F5 re-reads; Escape closes. The Side combo swaps the window to the
/// other seat's unit (PFD GTC 1 or 4, MFD GTC 2 or 3).
/// </summary>
public sealed class C680GtcForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SettleMs = 350;
    private readonly bool _isMfd;
    private C680Seat.Side _seat;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Action<C680Seat.Side> _seatChanged;
    private readonly IntPtr _previousWindow;
    private readonly DisplayListBox _list;
    private readonly ComboBox _side;
    private CoherentDisplayClient? _client;
    private IReadOnlyList<C680GtcRows.GtcRow> _rows = Array.Empty<C680GtcRows.GtcRow>();
    private string _lastTitle = "";
    private bool _everConnected;
    private string? _pendingPage;
    private bool _acting;

    public C680GtcForm(bool isMfd, C680Seat.Side seat, ScreenReaderAnnouncer announcer, Action<C680Seat.Side> seatChanged)
    {
        _isMfd = isMfd; _seat = seat; _announcer = announcer; _seatChanged = seatChanged;
        _previousWindow = GetForegroundWindow();
        Text = WindowTitle();
        Size = new Size(560, 620);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var sideLabel = new Label { Text = "Side", AutoSize = true, Location = new Point(12, 14) };
        _side = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(60, 10), Width = 140, AccessibleName = "Side", TabIndex = 1 };
        _side.Items.AddRange(new object[] { "Pilot", "Copilot" });
        _side.SelectedIndex = seat == C680Seat.Side.Copilot ? 1 : 0;
        _side.SelectedIndexChanged += (_, _) =>
        {
            var s = _side.SelectedIndex == 1 ? C680Seat.Side.Copilot : C680Seat.Side.Pilot;
            if (s == _seat) return;
            _seat = s; Text = WindowTitle(); _lastTitle = ""; _everConnected = false;
            Connect();
            _seatChanged(s);
        };

        _list = new DisplayListBox
        {
            Location = new Point(12, 44), Size = new Size(520, 520), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = "Touchscreen", SuppressTypeAhead = true, TabIndex = 0
        };
        _list.SetText("Connecting to the touchscreen. It is dark until the aircraft has power and both AVN buttons are on; Coherent allows one connection per screen.");
        Controls.Add(sideLabel); Controls.Add(_side); Controls.Add(_list);

        Shown += (_, _) => { _list.Focus(); Connect(); };
        FormClosing += (_, _) =>
        {
            _client?.Dispose(); _client = null;
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    private string WindowTitle() => $"{(_isMfd ? "MFD" : "PFD")} Touchscreen, {_seat} side";

    /// <summary>Press Home, then the named page button, once the screen has been read for the first time.</summary>
    public void OpenPageWhenReady(string pageButton) => _pendingPage = pageButton;

    private void Connect()
    {
        _client?.Dispose();
        int index = C680Seat.GtcIndexFor(_seat, _isMfd);
        var client = new CoherentDisplayClient($"WTG3000_GTC_{index}", 1000, "coherent-gtc-agent.js");
        client.RowsUpdated += rows =>
        {
            if (_acting) return;
            ApplyRows(rows);
            string title = C680GtcRows.TitleOf(_rows);
            if (_everConnected && title != _lastTitle && title.Length > 0) _announcer.Announce(title);
            _lastTitle = title;
            _everConnected = true;
            if (_pendingPage != null) { var page = _pendingPage; _pendingPage = null; _ = OpenPage(page); }
        };
        client.Error += msg => { if (!_everConnected) _list.SetText(msg); };
        _client = client;
        client.Start();
    }

    private void ApplyRows(IReadOnlyList<string> rows)
    {
        _rows = C680GtcRows.Parse(rows);
        int keep = _list.SelectedIndex;
        _list.SetLines(rows);
        if (keep >= 0 && keep < _list.Items.Count && _list.SelectedIndex < 0) _list.SelectedIndex = keep;
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private async Task OpenPage(string pageButton)
    {
        await Act("__MSFSBA_GTC.press('Home')", "Home", speak: false);
        await Act($"__MSFSBA_GTC.press({Js(pageButton)})", pageButton);
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        if (_client == null) { base.OnKeyDown(e); return; }
        if (e.KeyData == Keys.Escape) { e.Handled = true; Close(); return; }
        if (e.KeyData == Keys.F5) { e.Handled = true; ApplyRows(await _client.ScrapeNowAsync()); return; }
        if (_side.Focused && !e.Control && !e.Alt) { base.OnKeyDown(e); return; }   // let the combo take its own keys

        var knob = C680GtcRows.KeyToKnob(e.KeyData);
        if (knob != null) { e.Handled = true; e.SuppressKeyPress = true; await Act($"__MSFSBA_GTC.knob('{knob}')", C680GtcRows.DescribeKnob(knob)); return; }
        if (e.KeyData == (Keys.Control | Keys.Home)) { e.Handled = true; await Act("__MSFSBA_GTC.press('Home')", "Home"); return; }
        if (e.KeyData == (Keys.Control | Keys.Back)) { e.Handled = true; await Act("__MSFSBA_GTC.press('Back')", "Back"); return; }
        if (e.KeyData == (Keys.Control | Keys.G)) { e.Handled = true; await Act("__MSFSBA_GTC.press('MSG')", "MSG"); return; }

        bool keyboard = C680GtcRows.IsKeyboardPage(_rows);
        if (e.KeyData == Keys.Enter && !keyboard)
        {
            int i = _list.SelectedIndex;
            if (i >= 0 && i < _rows.Count && _rows[i].Kind == C680GtcRows.Kind.Button)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                var b = _rows[i];
                if (!b.Enabled) { _announcer.AnnounceImmediate(b.Label + " is disabled"); return; }
                await Act($"__MSFSBA_GTC.click({b.ButtonIndex})", b.Label);
                return;
            }
        }
        var label = C680GtcRows.KeyToButtonLabel(e.KeyData, keyboard);
        if (label != null)
        {
            e.Handled = true; e.SuppressKeyPress = true;
            await Act($"__MSFSBA_GTC.press({Js(label)})", label, speakTitleChange: false);
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Drive the page, wait for it to settle, re-read, and say what changed: the new page title, else what was pressed.</summary>
    private async Task Act(string expr, string spoken, bool speak = true, bool speakTitleChange = true)
    {
        if (_client == null) return;
        _acting = true;
        try
        {
            string r = await _client.InvokeAsync(expr);
            if (r == "none" || r == "stale") { _announcer.AnnounceImmediate(spoken + " is not on this page"); return; }
            if (r.Length == 0) { _announcer.AnnounceImmediate("The touchscreen did not answer"); return; }
            await Task.Delay(SettleMs);
            var rows = await _client.ScrapeNowAsync();
            ApplyRows(rows);
            string title = C680GtcRows.TitleOf(_rows);
            bool titleChanged = title != _lastTitle && title.Length > 0;
            _lastTitle = title;
            if (!speak) return;
            if (titleChanged && speakTitleChange) _announcer.AnnounceImmediate(title);
            else if (!speakTitleChange) { /* typed key: the screen reader already spoke the keystroke */ }
            else _announcer.AnnounceImmediate(spoken);
        }
        finally { _acting = false; }
    }

    private static string Js(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
}
