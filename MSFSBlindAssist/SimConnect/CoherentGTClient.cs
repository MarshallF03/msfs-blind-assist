using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// WebKit Inspector Protocol client for MSFS Coherent GT instrument debugger.
/// Requires MSFS Developer Mode to be enabled.
///
/// When dev mode is on, Coherent GT exposes a WebKit Inspector server:
///   MSFS 2020: http://127.0.0.1:9999
///   MSFS 2024: http://127.0.0.1:19999
///
/// GET /json        → JSON array of debuggable instrument pages (title, url, webSocketDebuggerUrl)
/// WebSocket /id    → WebKit Inspector protocol; use Runtime.evaluate to run JS in-instrument
///
/// Usage:
///   var client = new CoherentGTClient();
///   bool ok = await client.TryConnectAsync("WTG1000/MFD");
///   if (ok) {
///       string? json = await client.EvaluateAsync("JSON.stringify(fms.getPrimaryFlightPlan())");
///   }
/// </summary>
public sealed class CoherentGTClient : IDisposable
{
    // Port order: 2020 default first, 2024 default second
    private static readonly int[] DebuggerPorts = { 9999, 19999 };

    private ClientWebSocket? _ws;
    private CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string?>> _pending = new();
    private int _nextId;
    private Task? _receiveTask;
    private bool _disposed;

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public string? ConnectedTargetTitle { get; private set; }
    public int ConnectedPort { get; private set; }

    // ─────────────────────────────────────────────────────────────────────
    // Connection
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Try each known Coherent GT port. On each, list targets and look for one
    /// whose URL or title contains <paramref name="targetFilter"/>. Returns true
    /// if a WebSocket connection was established.
    /// </summary>
    public async Task<bool> TryConnectAsync(string targetFilter, CancellationToken ct = default)
    {
        foreach (int port in DebuggerPorts)
        {
            var (wsUrl, title) = await FindTargetAsync(port, targetFilter, ct);
            if (wsUrl == null) continue;

            try
            {
                // Tear down any existing connection
                _cts.Cancel();
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                _ws?.Dispose();
                _ws = new ClientWebSocket();

                await _ws.ConnectAsync(new Uri(wsUrl), ct);

                ConnectedTargetTitle = title;
                ConnectedPort = port;

                // Start background receive loop
                _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);

                // WebKit Inspector requires Runtime.enable before evaluate works
                _ = SendRawAsync("{\"id\":0,\"method\":\"Runtime.enable\"}", _cts.Token);

                System.Diagnostics.Debug.WriteLine(
                    $"[CoherentGT] Connected to '{title}' on port {port}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CoherentGT] Connect failed (port {port}): {ex.Message}");
                _ws?.Dispose();
                _ws = null;
            }
        }

        ConnectedTargetTitle = null;
        return false;
    }

    public void Disconnect()
    {
        _cts.Cancel();
        _ws?.Dispose();
        _ws = null;
        ConnectedTargetTitle = null;
        // Clear any pending waits
        foreach (var kv in _pending)
            kv.Value.TrySetResult(null);
        _pending.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────
    // JS evaluation
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate a JavaScript expression inside the connected instrument page.
    /// Returns the string result, or null on timeout / error / not connected.
    /// Wrap complex expressions in (function(){...})() so they can use return.
    /// </summary>
    public async Task<string?> EvaluateAsync(string expression, int timeoutMs = 5000,
        CancellationToken ct = default)
        => await EvaluateInternalAsync(expression, awaitPromise: false, timeoutMs, ct);

    /// <summary>
    /// Evaluate an expression that returns a Promise (e.g. async function calls).
    /// Passes awaitPromise:true so the debugger waits for the Promise to settle
    /// before returning the resolved value. Supported by Coherent GT 2.x+.
    /// Use for: fms.emptyPrimaryFlightPlan(), fms.facLoader.getFacility(), etc.
    /// </summary>
    public async Task<string?> EvaluatePromiseAsync(string expression, int timeoutMs = 10000,
        CancellationToken ct = default)
        => await EvaluateInternalAsync(expression, awaitPromise: true, timeoutMs, ct);

    private async Task<string?> EvaluateInternalAsync(string expression, bool awaitPromise,
        int timeoutMs, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return null;

        int id = System.Threading.Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        // Serialise safely — the expression may contain quotes and backslashes
        string escapedExpr = JsonSerializer.Serialize(expression); // includes surrounding quotes
        string awaitPart = awaitPromise ? ",\"awaitPromise\":true" : "";
        string msg = $"{{\"id\":{id},\"method\":\"Runtime.evaluate\",\"params\":{{\"expression\":{escapedExpr},\"returnByValue\":true,\"generatePreview\":false{awaitPart}}}}}";


        try
        {
            await SendRawAsync(msg, ct);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            System.Diagnostics.Debug.WriteLine($"[CoherentGT] Send failed: {ex.Message}");
            return null;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        try
        {
            await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, linked.Token));
            if (tcs.Task.IsCompleted) return await tcs.Task;
        }
        catch { }

        _pending.TryRemove(id, out _);
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Internal
    // ─────────────────────────────────────────────────────────────────────

    private async Task SendRawAsync(string json, CancellationToken ct)
    {
        if (_ws == null) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[131072]; // 128 KB — G1000 flight plans can be large
        var sb = new StringBuilder();

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        System.Diagnostics.Debug.WriteLine("[CoherentGT] Server closed connection");
                        goto disconnected;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                DispatchMessage(sb.ToString());
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CoherentGT] Receive error: {ex.Message}");
                break;
            }
        }

        disconnected:
        // Resolve all pending waits with null so callers don't hang
        foreach (var kv in _pending)
            kv.Value.TrySetResult(null);
        _pending.Clear();
    }

    private void DispatchMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Only process responses that have an id (ignoring events from the engine)
            if (!root.TryGetProperty("id", out var idProp)) return;
            if (!_pending.TryRemove(idProp.GetInt32(), out var tcs)) return;

            string? value = null;

            if (root.TryGetProperty("result", out var resultProp))
            {
                // WebKit: wasThrown = true means exception
                if (resultProp.TryGetProperty("wasThrown", out var thrown) && thrown.GetBoolean())
                {
                    tcs.TrySetResult(null);
                    return;
                }

                // CDP: exceptionDetails present means exception
                if (resultProp.TryGetProperty("exceptionDetails", out _))
                {
                    tcs.TrySetResult(null);
                    return;
                }

                // Normal result — get the value from result.result.value
                if (resultProp.TryGetProperty("result", out var inner))
                {
                    if (inner.TryGetProperty("value", out var v))
                    {
                        value = v.ValueKind == JsonValueKind.String
                            ? v.GetString()
                            : v.GetRawText();
                    }
                    else if (inner.TryGetProperty("description", out var desc))
                    {
                        // Fallback: use description for objects/undefined
                        value = desc.GetString();
                    }
                }
            }

            tcs.TrySetResult(value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoherentGT] Message parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Query the /json endpoint on the given port and find a target whose URL
    /// or title contains <paramref name="filter"/>. Returns (wsUrl, title) or (null, null).
    /// </summary>
    private static async Task<(string? wsUrl, string? title)> FindTargetAsync(
        int port, string filter, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        foreach (string path in TargetEndpoints)
        {
            try
            {
                string body = await http.GetStringAsync($"http://127.0.0.1:{port}{path}", ct);
                using var doc = JsonDocument.Parse(body);

                var root = doc.RootElement;
                var arr  = root.ValueKind == JsonValueKind.Array ? root
                         : root.TryGetProperty("targets", out var t) ? t
                         : root.TryGetProperty("pages",   out var p) ? p
                         : root;
                if (arr.ValueKind != JsonValueKind.Array) continue;

                foreach (var target in arr.EnumerateArray())
                {
                    string url   = target.TryGetProperty("url",   out var u)  ? u.GetString()  ?? ""
                                 : target.TryGetProperty("file",  out var uf) ? uf.GetString() ?? "" : "";
                    string title = target.TryGetProperty("title", out var tt) ? tt.GetString() ?? ""
                                 : target.TryGetProperty("name",  out var tn) ? tn.GetString() ?? "" : "";

                    bool matches = url.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                || title.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    if (!matches) continue;

                    // Coherent GT may use "webSocketDebuggerUrl" or "wsUrl" or build from id
                        // Chrome CDP style
                    if (target.TryGetProperty("webSocketDebuggerUrl", out var ws) && ws.GetString() != null)
                        return (ws.GetString(), title);
                    if (target.TryGetProperty("wsUrl", out var wu) && wu.GetString() != null)
                        return (wu.GetString(), title);

                    // Coherent GT style: inspectorUrl = "/inspector/Main.html?page=3"
                    // WebSocket is at ws://host:port/page/<id>
                    if (target.TryGetProperty("inspectorUrl", out var iu))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(
                            iu.GetString() ?? "", @"page=(\d+)");
                        if (m.Success)
                            return ($"ws://127.0.0.1:{port}/page/{m.Groups[1].Value}", title);
                    }
                    // Last resort: bare numeric id
                    if (target.TryGetProperty("id", out var id))
                        return ($"ws://127.0.0.1:{port}/page/{id.GetInt32()}", title);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CoherentGT] {path} probe failed (port {port}): {ex.Message}");
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Returns all debuggable target titles/URLs on a given port, for diagnostics.
    /// Tries /json and /json/list endpoints.
    /// </summary>
    // Coherent GT (MSFS) uses /pagelist.json — Chrome DevTools Protocol uses /json.
    // Both are tried; whichever returns a JSON array of targets wins.
    private static readonly string[] TargetEndpoints =
        { "/pagelist.json", "/json", "/json/list" };

    public static async Task<List<(string title, string url)>> ListTargetsAsync(int port = 9999)
    {
        var results = new List<(string, string)>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        foreach (string path in TargetEndpoints)
        {
            try
            {
                string body = await http.GetStringAsync($"http://127.0.0.1:{port}{path}");
                TryParseTargets(body, results);
                if (results.Count > 0) return results;
            }
            catch { }
        }
        return results;
    }

    private static void TryParseTargets(string body, List<(string title, string url)> results)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var arr = root.ValueKind == JsonValueKind.Array ? root
                    : root.TryGetProperty("targets", out var t) ? t
                    : root.TryGetProperty("pages",   out var p) ? p
                    : root;
            if (arr.ValueKind != JsonValueKind.Array) return;
            foreach (var item in arr.EnumerateArray())
            {
                // Coherent GT uses "title"/"url"; some versions may use "name"/"file"
                string title = item.TryGetProperty("title", out var tt) ? tt.GetString() ?? ""
                             : item.TryGetProperty("name",  out var tn) ? tn.GetString() ?? "" : "";
                string url   = item.TryGetProperty("url",   out var uu) ? uu.GetString() ?? ""
                             : item.TryGetProperty("file",  out var uf) ? uf.GetString() ?? "" : "";
                results.Add((title, url));
            }
        }
        catch { }
    }

    /// <summary>
    /// Full connection diagnostic: probes all known ports and endpoints and returns
    /// a human-readable report. Use the "Diagnose" button in G1000NavigatorForm.
    /// </summary>
    public static async Task<string> DiagnoseAsync()
    {
        var sb = new StringBuilder();
        int[] ports = { 9999, 19999, 9222, 19998 };
        string[] paths = { "/pagelist.json", "/json", "/json/list", "/", "/devtools/browser" };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        foreach (int port in ports)
        {
            bool anyHit = false;
            foreach (string path in paths)
            {
                try
                {
                    var resp = await http.GetAsync($"http://127.0.0.1:{port}{path}");
                    string body = await resp.Content.ReadAsStringAsync();
                    // For pagelist.json, show parsed titles so the log is readable
                    string preview;
                    if (path == "/pagelist.json" && body.TrimStart().StartsWith("["))
                    {
                        try
                        {
                            using var pd = JsonDocument.Parse(body);
                            var lines = pd.RootElement.EnumerateArray()
                                .Select(e => {
                                    string t = e.TryGetProperty("title", out var tv) ? tv.GetString() ?? "" : "";
                                    string u = e.TryGetProperty("url",   out var uv) ? uv.GetString() ?? "" : "";
                                    int    i = e.TryGetProperty("id",    out var iv) ? iv.GetInt32() : 0;
                                    return $"  [{i}] {(string.IsNullOrWhiteSpace(t) ? "(no title)" : t)} — {u}";
                                });
                            preview = string.Join("\n", lines);
                        }
                        catch { preview = body.Length > 2000 ? body[..2000] + "…" : body; }
                    }
                    else
                        preview = body.Length > 400 ? body[..400] + "…" : body;
                    sb.AppendLine($"Port {port}{path}  →  HTTP {(int)resp.StatusCode}");
                    sb.AppendLine($"  Body: {preview.Replace('\n', ' ').Replace('\r', ' ')}");
                    anyHit = true;
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Port {port}{path}  →  {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (anyHit) sb.AppendLine();
        }

        if (sb.Length == 0) sb.AppendLine("No ports responded.");
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        _cts.Dispose();
    }
}
