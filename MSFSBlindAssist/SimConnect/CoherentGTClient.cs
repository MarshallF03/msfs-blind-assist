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
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            string json = await http.GetStringAsync($"http://127.0.0.1:{port}/json", ct);

            using var doc = JsonDocument.Parse(json);
            foreach (var target in doc.RootElement.EnumerateArray())
            {
                string url   = target.TryGetProperty("url",   out var u) ? u.GetString() ?? "" : "";
                string title = target.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";

                bool matches = url.Contains(filter, StringComparison.OrdinalIgnoreCase)
                            || title.Contains(filter, StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;

                if (target.TryGetProperty("webSocketDebuggerUrl", out var ws))
                    return (ws.GetString(), title);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CoherentGT] /json probe failed (port {port}): {ex.Message}");
        }
        return (null, null);
    }

    /// <summary>
    /// Returns all debuggable target titles/URLs on a given port, for diagnostics.
    /// </summary>
    public static async Task<List<(string title, string url)>> ListTargetsAsync(int port = 9999)
    {
        var results = new List<(string, string)>();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            string json = await http.GetStringAsync($"http://127.0.0.1:{port}/json");
            using var doc = JsonDocument.Parse(json);
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                string title = t.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
                string url   = t.TryGetProperty("url",   out var uu) ? uu.GetString() ?? "" : "";
                results.Add((title, url));
            }
        }
        catch { }
        return results;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        _cts.Dispose();
    }
}
