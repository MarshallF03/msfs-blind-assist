using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.Forms.GPS;

/// <summary>
/// Accessible GPS navigator window for aircraft using the Working Title G1000 NXi.
/// Connects to the G1000 MFD via the MSFS Coherent GT debugger (dev mode port 9999/19999)
/// and reads live FMS data: flight plan, active waypoint, and procedures.
///
/// Requires MSFS Developer Mode to be enabled (Options → General → Developers).
/// No mod package installation or sim restart needed.
///
/// Hotkey to open: Input mode → Shift+G
/// Inside the form: F5 refreshes all data.
/// </summary>
public sealed class G1000NavigatorForm : Form
{
    // G1000 NXi MFD page identifier — matched against URL and title
    private const string G1000MfdFilter = "WTG1000";

    private readonly CoherentGTClient _client;
    private readonly ScreenReaderAnnouncer _announcer;

    private TextBox _statusBox = null!;
    private TextBox _waypointBox = null!;
    private TextBox _flightPlanBox = null!;
    private TextBox _directToBox = null!;
    private Button _refreshBtn = null!;
    private Button _directToBtn = null!;
    private Label _directToLabel = null!;

    // ─────────────────────────────────────────────────────────────────────
    // JS expressions evaluated inside the G1000 MFD page
    // ─────────────────────────────────────────────────────────────────────

    // Probe: discover available FMS globals so we can report what the page exposes
    private const string JsProbe = @"
(function() {
  try {
    var keys = Object.keys(window)
      .filter(function(k) {
        var lk = k.toLowerCase();
        return lk === 'fms' || lk === 'flightmanager' || lk === 'g1000fms'
            || lk.includes('flightplan') || lk.includes('avionics');
      });
    return JSON.stringify({ ok: true, globals: keys });
  } catch(e) { return JSON.stringify({ ok: false, err: e.message }); }
})()";

    // Active leg: ident, name, active leg index, total legs
    private const string JsActiveLeg = @"
(function() {
  try {
    var fp = fms.getPrimaryFlightPlan();
    var ali = fp.activeLegIndex;
    if (ali < 0 || ali >= fp.length)
      return JSON.stringify({ ok: true, active: false });
    var leg = fp.getLeg(ali);
    var ident = '';
    if (leg && leg.leg) {
      ident = (leg.leg.fixIcao || '').trim().replace(/\x00/g, '').replace(/^\s+|\s+$/g, '');
      if (!ident) ident = leg.leg.type || 'unknown';
    }
    return JSON.stringify({ ok: true, active: true,
      ident: ident,
      name: (leg && leg.name) ? leg.name : ident,
      index: ali, total: fp.length });
  } catch(e) { return JSON.stringify({ ok: false, err: e.message }); }
})()";

    // Full flight plan: origin, destination, all legs, active index, active procedure names
    private const string JsFlightPlan = @"
(function() {
  try {
    var fp = fms.getPrimaryFlightPlan();
    var legs = [];
    for (var i = 0; i < fp.length; i++) {
      try {
        var l = fp.getLeg(i);
        var ident = '';
        if (l && l.leg) {
          ident = (l.leg.fixIcao || '').trim().replace(/\x00/g, '').replace(/^\s+|\s+$/g, '');
          if (!ident) ident = l.leg.type || '?';
        }
        legs.push({ ident: ident, name: (l && l.name) ? l.name : ident, active: i === fp.activeLegIndex });
      } catch(le) { legs.push({ ident: 'ERR', name: 'ERR', active: false }); }
    }
    var proc = {};
    try {
      var pd = fp.procedureDetails;
      if (pd) {
        proc.depIdx    = pd.departureIndex;
        proc.depRunway = pd.departureRunwayIndex;
        proc.arrIdx    = pd.arrivalIndex;
        proc.apprIdx   = pd.approachIndex;
        proc.apprType  = pd.approachType;
        proc.trans     = pd.approachTransitionIndex;
      }
    } catch(pe) {}
    return JSON.stringify({
      ok: true,
      origin: fp.originAirport || '',
      dest: fp.destinationAirport || '',
      activeLeg: fp.activeLegIndex,
      legs: legs,
      proc: proc
    });
  } catch(e) { return JSON.stringify({ ok: false, err: e.message }); }
})()";

    // Direct-to: set active leg to a waypoint by ident (best-effort via fms.createDirectTo)
    private static string JsDirectTo(string ident) => $@"
(function() {{
  try {{
    var ident = '{ident.ToUpperInvariant().Replace("'", "")}';
    if (typeof fms.createDirectTo === 'function') {{
      fms.createDirectTo(ident);
      return JSON.stringify({{ ok: true, method: 'createDirectTo' }});
    }} else if (typeof fms.directTo === 'function') {{
      fms.directTo(ident);
      return JSON.stringify({{ ok: true, method: 'directTo' }});
    }}
    return JSON.stringify({{ ok: false, err: 'no directTo API found' }});
  }} catch(e) {{ return JSON.stringify({{ ok: false, err: e.message }}); }}
}})()";

    // ─────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────

    public G1000NavigatorForm(CoherentGTClient client, ScreenReaderAnnouncer announcer)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));

        BuildUi();

        // Hide rather than close so the client stays connected
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            { e.Cancel = true; Hide(); }
        };

        KeyDown += HandleKeyDown;
    }

    // ─────────────────────────────────────────────────────────────────────
    // UI construction
    // ─────────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        Text = "G1000 Navigator";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 560);
        MinimumSize = new Size(480, 400);
        KeyPreview = true;
        ShowInTaskbar = true;

        // Status — single-line, always-focusable, tells you connection state
        _statusBox = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 26,
            ReadOnly = true,
            Text = "Not connected — open with Shift+G in Input mode",
            AccessibleName = "G1000 status"
        };

        // Active waypoint — compact readout
        var waypointLabel = new Label
        { Dock = DockStyle.Top, Height = 20, Text = "Active &waypoint:", Padding = new Padding(4, 4, 0, 0) };

        _waypointBox = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 52,
            Multiline = true,
            ReadOnly = true,
            AccessibleName = "Active waypoint"
        };

        // Flight plan — main display area
        var fplLabel = new Label
        { Dock = DockStyle.Top, Height = 20, Text = "&Flight plan:", Padding = new Padding(4, 4, 0, 0) };

        _flightPlanBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Flight plan"
        };

        // Direct-to row
        _directToLabel = new Label
        { Dock = DockStyle.Bottom, Height = 20, Text = "&Direct-to ICAO:", Padding = new Padding(4, 2, 0, 0) };

        _directToBox = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            MaxLength = 7,
            AccessibleName = "Direct-to waypoint ICAO",
            AccessibleDescription = "Type a waypoint ICAO and press Enter or click Direct-To"
        };
        _directToBox.KeyDown += DirectToBox_KeyDown;

        _directToBtn = new Button
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Text = "&Direct-To",
            AccessibleName = "Activate direct-to"
        };
        _directToBtn.Click += (_, _) => ExecuteDirectTo();

        // Refresh button at top
        _refreshBtn = new Button
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "&Refresh (F5)",
            AccessibleName = "Refresh G1000 data"
        };
        _refreshBtn.Click += async (_, _) => await RefreshAsync();

        // Assemble — order matters for DockStyle: add bottom controls first, then top, then fill
        Controls.Add(_flightPlanBox);
        Controls.Add(fplLabel);
        Controls.Add(_waypointBox);
        Controls.Add(waypointLabel);
        Controls.Add(_directToBtn);
        Controls.Add(_directToBox);
        Controls.Add(_directToLabel);
        Controls.Add(_refreshBtn);
        Controls.Add(_statusBox);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Connection
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Try to connect (or reconnect) to the G1000 MFD page.
    /// Call this when the form is first shown or when the user presses F5 while disconnected.
    /// </summary>
    public async Task ConnectAndRefreshAsync()
    {
        if (!_client.IsConnected)
        {
            SetStatus("Connecting to G1000 MFD via dev mode…");
            bool ok = await _client.TryConnectAsync(G1000MfdFilter);
            if (!ok)
            {
                // Try listing targets so we can tell the user what IS running
                var targets9999  = await CoherentGTClient.ListTargetsAsync(9999);
                var targets19999 = await CoherentGTClient.ListTargetsAsync(19999);
                var all = targets9999.Concat(targets19999).ToList();

                if (all.Count == 0)
                {
                    SetStatus("Dev mode not running — enable it in MSFS Options → General → Developers");
                    _announcer.AnnounceImmediate("G1000 bridge: dev mode not running");
                }
                else
                {
                    var names = string.Join(", ", all.Select(t => t.title).Where(t => !string.IsNullOrWhiteSpace(t)).Take(5));
                    SetStatus($"G1000 MFD not found. Running pages: {names}");
                    _announcer.AnnounceImmediate("G1000 MFD page not found. Is the G1000 aircraft loaded?");
                }
                return;
            }
            SetStatus($"Connected to {_client.ConnectedTargetTitle} (port {_client.ConnectedPort})");
        }

        await RefreshAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Data refresh
    // ─────────────────────────────────────────────────────────────────────

    public async Task RefreshAsync()
    {
        if (!_client.IsConnected)
        {
            await ConnectAndRefreshAsync();
            return;
        }

        SetStatus("Refreshing…");
        _refreshBtn.Enabled = false;

        try
        {
            // Run active leg and flight plan queries in parallel
            var legTask = _client.EvaluateAsync(JsActiveLeg);
            var fplTask = _client.EvaluateAsync(JsFlightPlan);
            await Task.WhenAll(legTask, fplTask);

            string legText  = ParseActiveLeg(legTask.Result);
            string fplText  = ParseFlightPlan(fplTask.Result);
            string announce = legText;

            InvokeOnUiThread(() =>
            {
                _waypointBox.Text = legText;
                _flightPlanBox.Text = fplText;
            });

            SetStatus($"Connected — {_client.ConnectedTargetTitle} (port {_client.ConnectedPort})  •  F5 to refresh");
            _announcer.AnnounceImmediate(announce);
        }
        catch (Exception ex)
        {
            SetStatus($"Refresh error: {ex.Message}");
        }
        finally
        {
            InvokeOnUiThread(() => _refreshBtn.Enabled = true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // JSON → readable text
    // ─────────────────────────────────────────────────────────────────────

    private static string ParseActiveLeg(string? json)
    {
        if (json == null) return "No data — check dev mode connection";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            if (!r.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                string err = r.TryGetProperty("err", out var e) ? e.GetString() ?? "" : "unknown";
                // If fms not found, the JS threw a ReferenceError
                if (err.Contains("fms") || err.Contains("undefined"))
                    return "FMS not available — is the G1000 powered on?";
                return $"FMS error: {err}";
            }

            if (r.TryGetProperty("active", out var act) && !act.GetBoolean())
                return "No active leg — flight plan empty or not activated";

            string ident = r.TryGetProperty("ident", out var id) ? id.GetString() ?? "?" : "?";
            string name  = r.TryGetProperty("name",  out var nm) ? nm.GetString() ?? ident : ident;
            int index    = r.TryGetProperty("index", out var ix) ? ix.GetInt32() : -1;
            int total    = r.TryGetProperty("total", out var tt) ? tt.GetInt32() : 0;

            string display = name != ident && !string.IsNullOrWhiteSpace(name) ? $"{ident} ({name})" : ident;
            return $"Active: {display}  [leg {index + 1} of {total}]";
        }
        catch { return $"Parse error: {json[..Math.Min(json.Length, 80)]}"; }
    }

    private static string ParseFlightPlan(string? json)
    {
        if (json == null) return "No data — check dev mode connection";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            if (!r.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                string err = r.TryGetProperty("err", out var e) ? e.GetString() ?? "" : "unknown";
                if (err.Contains("fms") || err.Contains("undefined"))
                    return "FMS not available — is the G1000 powered on?";
                return $"FMS error: {err}";
            }

            string origin = r.TryGetProperty("origin", out var og) ? og.GetString() ?? "" : "";
            string dest   = r.TryGetProperty("dest",   out var dt) ? dt.GetString() ?? "" : "";
            int active    = r.TryGetProperty("activeLeg", out var al) ? al.GetInt32() : -1;

            var sb = new StringBuilder();

            // Header
            if (!string.IsNullOrWhiteSpace(origin) || !string.IsNullOrWhiteSpace(dest))
            {
                string from = string.IsNullOrWhiteSpace(origin) ? "----" : origin;
                string to   = string.IsNullOrWhiteSpace(dest)   ? "----" : dest;
                sb.AppendLine($"{from} → {to}");
                sb.AppendLine(new string('─', 30));
            }

            // Legs
            if (r.TryGetProperty("legs", out var legs) && legs.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var leg in legs.EnumerateArray())
                {
                    string ident = leg.TryGetProperty("ident", out var id) ? id.GetString() ?? "?" : "?";
                    string name  = leg.TryGetProperty("name",  out var nm) ? nm.GetString() ?? ident : ident;
                    bool isAct   = i == active;

                    string prefix = isAct ? "▶ " : "  ";
                    string display = (name != ident && !string.IsNullOrWhiteSpace(name))
                        ? $"{ident}  {name}" : ident;
                    sb.AppendLine($"{prefix}{display}");
                    i++;
                }
            }

            if (sb.Length == 0) sb.AppendLine("Flight plan is empty");

            // Procedure summary
            if (r.TryGetProperty("proc", out var proc) && proc.ValueKind == JsonValueKind.Object)
            {
                var procLines = new List<string>();
                if (proc.TryGetProperty("depIdx",  out var dep) && dep.GetInt32() >= 0)
                    procLines.Add($"SID index {dep.GetInt32()}");
                if (proc.TryGetProperty("arrIdx",  out var arr) && arr.GetInt32() >= 0)
                    procLines.Add($"STAR index {arr.GetInt32()}");
                if (proc.TryGetProperty("apprIdx", out var apr) && apr.GetInt32() >= 0)
                {
                    string apprStr = $"Approach index {apr.GetInt32()}";
                    if (proc.TryGetProperty("apprType", out var at))
                        apprStr += $" (type {at.GetInt32()})";
                    procLines.Add(apprStr);
                }
                if (procLines.Count > 0)
                {
                    sb.AppendLine(new string('─', 30));
                    sb.AppendLine("Procedures: " + string.Join(", ", procLines));
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch { return $"Parse error: {json?[..Math.Min(json?.Length ?? 0, 80)] ?? "null"}"; }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Direct-To
    // ─────────────────────────────────────────────────────────────────────

    private void DirectToBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            ExecuteDirectTo();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private async void ExecuteDirectTo()
    {
        string ident = _directToBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ident))
        {
            _announcer.AnnounceImmediate("Enter a waypoint ICAO first");
            return;
        }

        if (!_client.IsConnected)
        {
            _announcer.AnnounceImmediate("Not connected to G1000");
            return;
        }

        _announcer.AnnounceImmediate($"Requesting direct to {ident}");
        string? result = await _client.EvaluateAsync(JsDirectTo(ident));

        if (result == null)
        {
            _announcer.AnnounceImmediate("Direct-to request failed — check connection");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(result);
            bool ok = doc.RootElement.TryGetProperty("ok", out var okP) && okP.GetBoolean();
            if (ok)
            {
                _announcer.AnnounceImmediate($"Direct to {ident} set");
                _directToBox.Clear();
                await Task.Delay(500);
                await RefreshAsync();
            }
            else
            {
                string err = doc.RootElement.TryGetProperty("err", out var e) ? e.GetString() ?? "" : "unknown";
                _announcer.AnnounceImmediate($"Direct-to failed: {err}");
            }
        }
        catch { _announcer.AnnounceImmediate("Direct-to response unclear"); }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Keyboard
    // ─────────────────────────────────────────────────────────────────────

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5)
        {
            _ = RefreshAsync();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private void SetStatus(string text)
    {
        InvokeOnUiThread(() => _statusBox.Text = text);
    }

    private void InvokeOnUiThread(Action action)
    {
        if (IsHandleCreated && InvokeRequired)
            BeginInvoke(action);
        else if (IsHandleCreated)
            action();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _client.Dispose();
        base.Dispose(disposing);
    }
}
