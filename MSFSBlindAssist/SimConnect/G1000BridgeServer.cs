using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// HTTP bridge server for the G1000 NXi accessibility bridge (port 19778).
/// The bridge JS (g1000-accessibility-bridge.js) polls /commands and POSTs
/// fpl_state, nav_state, facility_data and command results to /state.
/// </summary>
public sealed class G1000BridgeServer : IDisposable
{
    public const int Port = 19778;
    private string Prefix => $"http://localhost:{Port}/";

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<JsonObject> _commandQueue = new();
    private bool _disposed;

    public bool IsRunning { get; private set; }

    // Events fired on thread-pool — subscribers must InvokeUI themselves
    public event EventHandler<G1000FplStateArgs>?      FplStateReceived;
    public event EventHandler<G1000NavStateArgs>?      NavStateReceived;
    public event EventHandler<G1000FacilityDataArgs>?  FacilityDataReceived;
    public event EventHandler<string>?                 CommandAck;     // command name
    public event EventHandler<string>?                 CommandError;   // error message
    public event EventHandler?                         BridgeConnected;

    private bool _wasConnected;

    // ─────────────────────────────────────────────────────────────────────────
    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add(Prefix);
        try
        {
            _listener.Start();
            IsRunning = true;
            Task.Run(() => ListenAsync(_cts.Token));
            System.Diagnostics.Debug.WriteLine($"[G1000Bridge] listening on {Prefix}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[G1000Bridge] start failed: {ex.Message}");
        }
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Send a command to the JS bridge
    // ─────────────────────────────────────────────────────────────────────────

    public void SendCommand(string command, object? payload = null)
    {
        var obj = new JsonObject { ["command"] = command };
        if (payload != null)
        {
            var json = JsonSerializer.Serialize(payload);
            if (JsonNode.Parse(json) is JsonObject p) obj["payload"] = p;
        }
        _commandQueue.Enqueue(obj);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HTTP listener
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener!.GetContextAsync();
                _ = Task.Run(() => HandleAsync(ctx), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            string path   = ctx.Request.Url?.AbsolutePath ?? "/";
            string method = ctx.Request.HttpMethod.ToUpperInvariant();

            ctx.Response.Headers.Add("Access-Control-Allow-Origin",  "*");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (method == "OPTIONS") { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

            switch (path)
            {
                case "/ping":
                    if (!_wasConnected) { _wasConnected = true; BridgeConnected?.Invoke(this, EventArgs.Empty); }
                    await WriteJson(ctx, "{\"ok\":true,\"version\":\"3.0.0\"}");
                    break;

                case "/state" when method == "POST":
                    using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        string body = await sr.ReadToEndAsync();
                        DispatchStateMessage(body);
                    }
                    await WriteJson(ctx, "{\"ok\":true}");
                    break;

                case "/commands" when method == "GET":
                    var arr = new System.Text.Json.Nodes.JsonArray();
                    if (_commandQueue.TryDequeue(out var cmd)) arr.Add(cmd);
                    await WriteJson(ctx, arr.ToJsonString());
                    break;

                default:
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[G1000Bridge] handle error: {ex.Message}");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private void DispatchStateMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

            if (!root.TryGetProperty("data", out var data)) return;

            switch (type)
            {
                case "fpl_state":
                    FplStateReceived?.Invoke(this, G1000FplStateArgs.Parse(data));
                    break;

                case "nav_state":
                    NavStateReceived?.Invoke(this, G1000NavStateArgs.Parse(data));
                    break;

                case "facility_data":
                    FacilityDataReceived?.Invoke(this, G1000FacilityDataArgs.Parse(data));
                    break;

                case "command_ack":
                    CommandAck?.Invoke(this, data.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "");
                    break;

                case "command_error":
                {
                    string err = data.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "unknown";
                    string cmd2 = data.TryGetProperty("command", out var c2) ? c2.GetString() ?? "" : "";
                    CommandError?.Invoke(this, $"{cmd2}: {err}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[G1000Bridge] dispatch error: {ex.Message}");
        }
    }

    private static async Task WriteJson(HttpListenerContext ctx, string json)
    {
        byte[] buf = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType     = "application/json";
        ctx.Response.ContentLength64 = buf.Length;
        ctx.Response.StatusCode      = 200;
        await ctx.Response.OutputStream.WriteAsync(buf);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Data models
// ─────────────────────────────────────────────────────────────────────────────

public class G1000FplLeg
{
    public string Text     { get; init; } = "";
    public string Ident    { get; init; } = "";
    public double Distance { get; init; }
    public double Dtk      { get; init; }
    public int    Index    { get; init; }
    public bool   IsActive { get; init; }
}

public class G1000FplStateArgs : EventArgs
{
    public List<G1000FplLeg> Items    { get; init; } = new();
    public int               Selected { get; init; }
    public string            Origin   { get; init; } = "";
    public string            Dest     { get; init; } = "";

    public static G1000FplStateArgs Parse(JsonElement d)
    {
        var items = new List<G1000FplLeg>();
        if (d.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                items.Add(new G1000FplLeg
                {
                    Text     = e.TryGetProperty("text",     out var tx) ? tx.GetString() ?? "" : "",
                    Ident    = e.TryGetProperty("ident",    out var id) ? id.GetString() ?? "" : "",
                    Distance = e.TryGetProperty("distance", out var di) ? di.GetDouble()     : 0,
                    Dtk      = e.TryGetProperty("dtk",      out var dk) ? dk.GetDouble()     : 0,
                    Index    = e.TryGetProperty("index",    out var ix) ? ix.GetInt32()      : 0,
                    IsActive = e.TryGetProperty("isActive", out var ac) && ac.GetBoolean()
                });

        return new G1000FplStateArgs
        {
            Items    = items,
            Selected = d.TryGetProperty("selected", out var sel) ? sel.GetInt32() : -1,
            Origin   = d.TryGetProperty("origin",   out var og)  ? og.GetString()  ?? "" : "",
            Dest     = d.TryGetProperty("dest",     out var dt)  ? dt.GetString()  ?? "" : ""
        };
    }
}

public class G1000NavStateArgs : EventArgs
{
    public double NextWpDist        { get; init; }
    public double NextWpBearing     { get; init; }
    public double NextWpEte         { get; init; }
    public double Dtk               { get; init; }
    public double Xtk               { get; init; }
    public double GroundSpeed       { get; init; }
    public bool   IsDirectTo        { get; init; }
    public bool   IsApproachLoaded  { get; init; }
    public bool   IsApproachActive  { get; init; }
    public bool   GpsDrivesNav      { get; init; }
    public int    ApproachMode      { get; init; }  // 0=none 1=armed 2=active

    public static G1000NavStateArgs Parse(JsonElement d)
    {
        double G(string k) => d.TryGetProperty(k, out var v) ? v.GetDouble() : 0;
        bool   B(string k) => d.TryGetProperty(k, out var v) && v.GetBoolean();
        int    I(string k) => d.TryGetProperty(k, out var v) ? v.GetInt32() : 0;
        return new G1000NavStateArgs
        {
            NextWpDist       = G("nextWpDist"),    NextWpBearing    = G("nextWpBearing"),
            NextWpEte        = G("nextWpEte"),     Dtk              = G("dtk"),
            Xtk              = G("xtk"),           GroundSpeed      = G("groundSpeed"),
            IsDirectTo       = B("isDirectTo"),    IsApproachLoaded = B("isApproachLoaded"),
            IsApproachActive = B("isApproachActive"), GpsDrivesNav  = B("gpsDrivesNav"),
            ApproachMode     = I("approachMode")
        };
    }
}

public class G1000ProcedureItem
{
    public int              Index       { get; init; }
    public string           Name        { get; init; } = "";
    public string           Runway      { get; init; } = "";
    public int              Type        { get; init; }
    public List<(int i, string name)> Transitions { get; init; } = new();
    public List<(int i, string name)> Runways     { get; init; } = new();
}

public class G1000FacilityDataArgs : EventArgs
{
    public string                    Ident      { get; init; } = "";
    public string                    Name       { get; init; } = "";
    public string                    Slot       { get; init; } = "";
    public List<G1000ProcedureItem>  Departures { get; init; } = new();
    public List<G1000ProcedureItem>  Arrivals   { get; init; } = new();
    public List<G1000ProcedureItem>  Approaches { get; init; } = new();

    public static G1000FacilityDataArgs Parse(JsonElement d)
    {
        static List<G1000ProcedureItem> ParseList(JsonElement root, string key)
        {
            var list = new List<G1000ProcedureItem>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in arr.EnumerateArray())
            {
                var trans   = new List<(int, string)>();
                var runways = new List<(int, string)>();
                if (item.TryGetProperty("transitions", out var ta) && ta.ValueKind == JsonValueKind.Array)
                    foreach (var t in ta.EnumerateArray())
                        trans.Add((t.TryGetProperty("index", out var ti) ? ti.GetInt32() : 0,
                                   t.TryGetProperty("name",  out var tn) ? tn.GetString() ?? "" : ""));
                if (item.TryGetProperty("runways", out var ra) && ra.ValueKind == JsonValueKind.Array)
                    foreach (var r in ra.EnumerateArray())
                        runways.Add((r.TryGetProperty("index", out var ri) ? ri.GetInt32() : 0,
                                     r.TryGetProperty("name",  out var rn) ? rn.GetString() ?? "" : ""));
                list.Add(new G1000ProcedureItem
                {
                    Index   = item.TryGetProperty("index", out var ix) ? ix.GetInt32() : 0,
                    Name    = item.TryGetProperty("name",  out var nm) ? nm.GetString() ?? "" : "",
                    Runway  = item.TryGetProperty("runway",out var rw) ? rw.GetString() ?? "" : "",
                    Type    = item.TryGetProperty("type",  out var ty) ? ty.GetInt32() : 0,
                    Transitions = trans,
                    Runways     = runways
                });
            }
            return list;
        }

        return new G1000FacilityDataArgs
        {
            Ident      = d.TryGetProperty("ident", out var id) ? id.GetString() ?? "" : "",
            Name       = d.TryGetProperty("name",  out var nm) ? nm.GetString() ?? "" : "",
            Slot       = d.TryGetProperty("slot",  out var sl) ? sl.GetString() ?? "" : "",
            Departures = ParseList(d, "departures"),
            Arrivals   = ParseList(d, "arrivals"),
            Approaches = ParseList(d, "approaches")
        };
    }
}
