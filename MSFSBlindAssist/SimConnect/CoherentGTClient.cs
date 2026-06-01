using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// WebKit Inspector Protocol client for MSFS Coherent GT instrument debugger.
/// Requires MSFS Developer Mode ON (Options → General → Developers).
///
/// MSFS 2020: port 19999   MSFS 2024: port 19999 (same)
/// GET /pagelist.json → JSON array of instrument pages (title, id, inspectorUrl)
/// WS  /devtools/page/{id} → Runtime.evaluate to run JS in that instrument
///
/// Page IDs change every session — always resolve by title, never hardcode.
/// G1000 MFD title: "AS1000_MFD"  (confirmed 2026-05-31 live session)
/// </summary>
public sealed class CoherentGTClient : IDisposable
{
    private static readonly int[] DebuggerPorts = { 19999, 9999 };
    private static readonly string[] TargetEndpoints = { "/pagelist.json", "/json", "/json/list" };

    private ClientWebSocket? _ws;
    private CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string?>> _pending = new();
    private int _nextId;
    private Task? _receiveTask;
    private bool _disposed;

    // ClientWebSocket.SendAsync is NOT safe for concurrent calls. The 2-second
    // state poll (timer thread) and user actions (UI thread) both send evals on
    // this one socket — without serialization they corrupt frames / abort the
    // connection, which is why inserts failed under live polling but worked solo.
    // This semaphore makes every eval send-and-await atomic.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public string? ConnectedTargetTitle { get; private set; }
    public int ConnectedPort { get; private set; }

    // ── Connection ────────────────────────────────────────────────────────────

    public async Task<bool> TryConnectAsync(string targetFilter, CancellationToken ct = default)
    {
        foreach (int port in DebuggerPorts)
        {
            var (wsUrl, title) = await FindTargetAsync(port, targetFilter, ct);
            if (wsUrl == null) continue;
            try
            {
                _cts.Cancel(); _cts.Dispose(); _cts = new CancellationTokenSource();
                _ws?.Dispose(); _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(wsUrl), ct);
                ConnectedTargetTitle = title;
                ConnectedPort = port;
                _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
                System.Diagnostics.Debug.WriteLine($"[CoherentGT] connected to '{title}' port {port}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CoherentGT] connect failed port {port}: {ex.Message}");
                _ws?.Dispose(); _ws = null;
            }
        }
        ConnectedTargetTitle = null;
        return false;
    }

    public void Disconnect()
    {
        _cts.Cancel(); _ws?.Dispose(); _ws = null;
        ConnectedTargetTitle = null;
        foreach (var kv in _pending) kv.Value.TrySetResult(null);
        _pending.Clear();
    }

    // ── JS evaluation ─────────────────────────────────────────────────────────

    public async Task<string?> EvaluateAsync(string expression, int timeoutMs = 5000,
        CancellationToken ct = default)
        => await EvaluateInternalAsync(expression, awaitPromise: false, timeoutMs, ct);

    /// <summary>For async FMS calls (getFacility, insertApproach, etc.) that return Promises.</summary>
    public async Task<string?> EvaluatePromiseAsync(string expression, int timeoutMs = 12000,
        CancellationToken ct = default)
        => await EvaluateInternalAsync(expression, awaitPromise: true, timeoutMs, ct);

    private async Task<string?> EvaluateInternalAsync(string expression, bool awaitPromise,
        int timeoutMs, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return null;

        int id = System.Threading.Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        string escapedExpr = JsonSerializer.Serialize(expression);
        string awaitPart   = awaitPromise ? ",\"awaitPromise\":true" : "";
        string msg = $"{{\"id\":{id},\"method\":\"Runtime.evaluate\",\"params\":{{\"expression\":{escapedExpr},\"returnByValue\":true,\"generatePreview\":false{awaitPart}}}}}";

        // Serialize the SEND only — frames must not interleave on the socket.
        // The response is matched by id in the receive loop, so multiple evals
        // can still be awaited concurrently after their sends complete.
        await _sendLock.WaitAsync(ct);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(msg);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            System.Diagnostics.Debug.WriteLine($"[CoherentGT] send failed: {ex.Message}");
            return null;
        }
        finally
        {
            _sendLock.Release();
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

    // ── Receive loop ──────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buf = new byte[131072];
        var sb  = new StringBuilder();
        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (result.MessageType == WebSocketMessageType.Close) goto disconnected;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
                } while (!result.EndOfMessage);
                DispatchMessage(sb.ToString());
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CoherentGT] receive error: {ex.Message}"); break;
            }
        }
        disconnected:
        foreach (var kv in _pending) kv.Value.TrySetResult(null);
        _pending.Clear();
    }

    private void DispatchMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idProp)) return;
            if (!_pending.TryRemove(idProp.GetInt32(), out var tcs)) return;

            string? value = null;
            if (root.TryGetProperty("result", out var resultProp))
            {
                if (resultProp.TryGetProperty("wasThrown",       out var thrown) && thrown.GetBoolean())
                    { tcs.TrySetResult(null); return; }
                if (resultProp.TryGetProperty("exceptionDetails", out _))
                    { tcs.TrySetResult(null); return; }
                if (resultProp.TryGetProperty("result", out var inner))
                {
                    if (inner.TryGetProperty("value", out var v))
                        value = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
                    else if (inner.TryGetProperty("description", out var desc))
                        value = desc.GetString();
                }
            }
            tcs.TrySetResult(value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoherentGT] parse error: {ex.Message}");
        }
    }

    // ── Target discovery ──────────────────────────────────────────────────────

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
                         : root.TryGetProperty("pages",   out var p) ? p : root;
                if (arr.ValueKind != JsonValueKind.Array) continue;

                foreach (var target in arr.EnumerateArray())
                {
                    string url   = target.TryGetProperty("url",   out var u)  ? u.GetString()  ?? "" : "";
                    string title = target.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
                    if (!url.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !title.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

                    // Coherent GT: webSocketDebuggerUrl or build from inspectorUrl page id
                    if (target.TryGetProperty("webSocketDebuggerUrl", out var ws) && ws.GetString() != null)
                        return (ws.GetString(), title);
                    if (target.TryGetProperty("inspectorUrl", out var iu))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(iu.GetString() ?? "", @"page=(\d+)");
                        if (m.Success)
                            return ($"ws://127.0.0.1:{port}/devtools/page/{m.Groups[1].Value}", title);
                    }
                    if (target.TryGetProperty("id", out var id))
                        return ($"ws://127.0.0.1:{port}/devtools/page/{id.GetInt32()}", title);
                }
            }
            catch { }
        }
        return (null, null);
    }

    public static async Task<List<(string title, string url)>> ListTargetsAsync(int port = 19999)
    {
        var results = new List<(string, string)>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        foreach (string path in TargetEndpoints)
        {
            try
            {
                string body = await http.GetStringAsync($"http://127.0.0.1:{port}{path}");
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var arr  = root.ValueKind == JsonValueKind.Array ? root
                         : root.TryGetProperty("targets", out var t) ? t
                         : root.TryGetProperty("pages",   out var p) ? p : root;
                if (arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in arr.EnumerateArray())
                {
                    string title = item.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
                    string url   = item.TryGetProperty("url",   out var uu) ? uu.GetString() ?? "" : "";
                    results.Add((title, url));
                }
                if (results.Count > 0) return results;
            }
            catch { }
        }
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
