using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.Learjet35;

/// <summary>
/// Live read-out window for a Working Title GNS 530 or 430 in the Learjet 35A, with the bezel
/// on the keyboard.
///
/// READ AND WRITE OVER THE SAME COHERENT SOCKET. The GNS renders its pages as DOM text, so
/// the generic row-clustering agent (coherent-display-agent.js) reads it; the bezel keys are
/// the H: events the vendor's own bezel fires (`H:AS530_ENT_Push` and friends, the Working
/// Title InteractionEventMap names), sent through the page's SimVar.SetSimVarValue — measured
/// 2026-09-07 to turn the pages and advance the self-test screen.
///
/// The key map avoids everything the list itself uses (arrows, Home, End, Page keys), so the
/// bezel sits on Ctrl and Alt: Ctrl+arrows are the right (FMS) knob, Alt+arrows the left
/// (radio) knob, Ctrl+letter the named buttons.
/// </summary>
public sealed class Lj35GnsDisplayForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly CoherentDisplayClient _client;
    private readonly DisplayListBox _text;
    private readonly IntPtr _previousWindow;
    private readonly string _prefix;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly System.Windows.Forms.Timer _connectWatchdog;
    private bool _gotRows;
    private bool _disposed;

    /// <summary>Bezel keys → Working Title GNS interaction event suffix and what to say.</summary>
    private static readonly Dictionary<Keys, (string Event, string Spoken)> BezelKeys = new()
    {
        [Keys.Control | Keys.Right] = ("RightLargeKnob_Right", "next page group"),
        [Keys.Control | Keys.Left] = ("RightLargeKnob_Left", "previous page group"),
        [Keys.Control | Keys.Down] = ("RightSmallKnob_Right", "next page"),
        [Keys.Control | Keys.Up] = ("RightSmallKnob_Left", "previous page"),
        [Keys.Shift | Keys.Enter] = ("RightSmallKnob_Push", "cursor"),
        [Keys.Control | Keys.Enter] = ("ENT_Push", "enter"),
        [Keys.Control | Keys.D] = ("DirectTo_Push", "direct to"),
        [Keys.Control | Keys.F] = ("FPL_Push", "flight plan"),
        [Keys.Control | Keys.P] = ("PROC_Push", "procedures"),
        [Keys.Control | Keys.E] = ("MENU_Push", "menu"),
        [Keys.Control | Keys.L] = ("CLR_Push", "clear"),
        [Keys.Control | Keys.G] = ("MSG_Push", "message"),
        [Keys.Control | Keys.O] = ("OBS_Push", "OBS"),
        [Keys.Control | Keys.C] = ("CDI_Push", "CDI"),
        [Keys.Control | Keys.V] = ("VNAV_Push", "VNAV"),
        [Keys.Control | Keys.PageUp] = ("RNG_Dezoom", "range out"),
        [Keys.Control | Keys.PageDown] = ("RNG_Zoom", "range in"),
        [Keys.Alt | Keys.Up] = ("LeftLargeKnob_Right", "megahertz up"),
        [Keys.Alt | Keys.Down] = ("LeftLargeKnob_Left", "megahertz down"),
        [Keys.Alt | Keys.Right] = ("LeftSmallKnob_Right", "kilohertz up"),
        [Keys.Alt | Keys.Left] = ("LeftSmallKnob_Left", "kilohertz down"),
        [Keys.Alt | Keys.Enter] = ("LeftSmallKnob_Push", "COM NAV tuning toggle"),
        [Keys.Alt | Keys.Shift | Keys.Enter] = ("COMSWAP_Push", "COM swapped"),
        [Keys.Control | Keys.Alt | Keys.Shift | Keys.Enter] = ("NAVSWAP_Push", "NAV swapped"),
    };

    public Lj35GnsDisplayForm(string title, string coherentViewNeedle, string eventPrefix, ScreenReaderAnnouncer announcer)
    {
        _previousWindow = GetForegroundWindow();
        _prefix = eventPrefix;
        _announcer = announcer;

        Text = title;
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _text = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            TabIndex = 0,
            AccessibleName = title,
            AccessibleDescription = title + ". Read with the arrow keys. " +
                "Control with left and right turns the large right knob (page group), control with up and down the small right knob (page). " +
                "Shift with Enter pushes the cursor, control with Enter is ENT. " +
                "Control with D direct to, F flight plan, P procedures, E menu, L clear, G message, O OBS, C CDI, V VNAV. " +
                "Control with Page Up and Page Down is the map range. " +
                "Alt with up and down is the radio megahertz, Alt with left and right the kilohertz, Alt with Enter toggles COM and NAV tuning, " +
                "Alt with Shift and Enter swaps COM, Control Alt Shift Enter swaps NAV. F5 refreshes; Escape closes. Auto-updates."
        };
        _text.SetText("Connecting to the display...");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var refreshButton = new Button { Text = "&Refresh (F5)", Location = new Point(560, 8), Size = new Size(90, 30), TabIndex = 1, AccessibleName = "Refresh" };
        refreshButton.Click += (s, e) => _ = _client?.ScrapeNowAsync();
        var closeButton = new Button { Text = "&Close", Location = new Point(655, 8), Size = new Size(85, 30), TabIndex = 2, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();
        bottom.Controls.AddRange(new Control[] { refreshButton, closeButton });
        Controls.Add(_text);
        Controls.Add(bottom);
        CancelButton = closeButton;

        _client = new CoherentDisplayClient(coherentViewNeedle, pollIntervalMs: 1200);
        _client.RowsUpdated += OnRowsUpdated;
        _client.Error += OnClientError;

        _connectWatchdog = new System.Windows.Forms.Timer { Interval = 6000 };
        _connectWatchdog.Tick += (s, e) =>
        {
            _connectWatchdog!.Stop();
            if (_disposed || _gotRows) return;
            _text.SetLines(new List<string>
            {
                "Could not read the display.",
                "",
                "The GNS is dark until the aircraft has power and the avionics master is on,",
                "and Coherent allows only one debugger connection per screen.",
                "",
                "Check the sim is running with the aircraft loaded and powered, then press F5."
            });
        };

        Load += (s, e) =>
        {
            BringToFront();
            Activate();
            _text.Focus();
            _client.Start();
            _client.SetActive(true);
            _connectWatchdog.Start();
        };

        FormClosed += (s, e) =>
        {
            _connectWatchdog.Stop();
            _connectWatchdog.Dispose();
            _client.RowsUpdated -= OnRowsUpdated;
            _client.Error -= OnClientError;
            _client.Stop();
            _client.Dispose();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    private void OnClientError(string message)
    {
        if (_disposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                _text.SetLines(new List<string> { "Display error: " + message, "", "Press F5 to retry." });
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void OnRowsUpdated(List<string> rows)
    {
        if (_disposed || !IsHandleCreated) return;
        _gotRows = true;
        IReadOnlyList<string> lines = rows.Count > 0 ? rows : new[] { "No data from the display." };
        try
        {
            BeginInvoke(new Action(() => { if (!_disposed) _text.SetLines(lines); }));
        }
        catch (InvalidOperationException) { }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            _ = _client.ScrapeNowAsync();
            return true;
        }
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        if (BezelKeys.TryGetValue(keyData, out var key))
        {
            _ = PressAsync(key.Event, key.Spoken);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Fires one bezel event inside the page and re-reads the screen straight after, because
    /// the page under the pilot's fingers has just changed.
    /// </summary>
    private async Task PressAsync(string eventSuffix, string spoken)
    {
        string js = $"SimVar.SetSimVarValue('H:{_prefix}_{eventSuffix}','number',1); 'sent'";
        string result = await _client.InvokeAsync(js);
        if (!result.Contains("sent", StringComparison.Ordinal))
        {
            _announcer.AnnounceImmediate("Key did not reach the display");
            return;
        }
        _announcer.AnnounceImmediate(spoken);
        await Task.Delay(350);
        _ = _client.ScrapeNowAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _client.RowsUpdated -= OnRowsUpdated;
            _client.Dispose();
        }
        base.Dispose(disposing);
    }
}
