using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// HTTP bridge server for the G1000 NXi accessibility bridge.
/// The JS bridge (g1000-accessibility-bridge.js) polls /commands and POSTs to /state.
/// Port 19779 (different from 787 bridge at 19778).
/// </summary>
public sealed class G1000BridgeServer : IDisposable
{
    public const int Port = 19779;
    private string Prefix => $"http://localhost:{Port}/";

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<JsonObject> _commands = new();
    private bool _disposed;

    public bool IsRunning { get; private set; }

    // Fired on the thread-pool when the JS bridge POSTs state
    public event EventHandler<string>? StateReceived;  // arg = raw JSON string

    // ─────────────────────────────────────────────────────────────────────────
    // Start / Stop
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

    public void SendCommand(string command, JsonObject? payload = null)
    {
        var obj = new JsonObject { ["command"] = command };
        if (payload != null) obj["payload"] = payload;
        _commands.Enqueue(obj);
    }

    // Convenience overloads
    public void SendCommand(string command, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var node = JsonNode.Parse(json) as JsonObject;
        SendCommand(command, node);
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

            // CORS headers so JS can reach us
            ctx.Response.Headers.Add("Access-Control-Allow-Origin",  "*");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (method == "OPTIONS") { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

            switch (path)
            {
                case "/ping":
                    await WriteJson(ctx, "{\"ok\":true}");
                    break;

                case "/state" when method == "POST":
                    using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        string body = await sr.ReadToEndAsync();
                        StateReceived?.Invoke(this, body);
                    }
                    await WriteJson(ctx, "{\"ok\":true}");
                    break;

                case "/commands" when method == "GET":
                    // Return up to 1 pending command (JS processes one at a time)
                    var pending = new System.Text.Json.Nodes.JsonArray();
                    if (_commands.TryDequeue(out var cmd))
                        pending.Add(cmd);
                    await WriteJson(ctx, pending.ToJsonString());
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

    private static async Task WriteJson(HttpListenerContext ctx, string json)
    {
        byte[] buf = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType    = "application/json";
        ctx.Response.ContentLength64 = buf.Length;
        ctx.Response.StatusCode     = 200;
        await ctx.Response.OutputStream.WriteAsync(buf);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
