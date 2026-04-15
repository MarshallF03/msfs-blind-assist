using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    public class GNSStateUpdateEventArgs : EventArgs
    {
        public string Type { get; set; } = "";
        public JsonElement Data { get; set; }
    }

    public class GNSCommand
    {
        public string Command { get; set; } = "";
        public Dictionary<string, object>? Payload { get; set; }
    }

    /// <summary>
    /// HTTP bridge server for the GNS 530 accessibility bridge.
    /// Receives flight plan and navigation state from the injected JS bridge,
    /// sends commands back (direct-to, activate leg, etc.).
    /// Port 19778 (separate from PMDG EFB on 19777).
    /// </summary>
    public class GNSBridgeServer : IDisposable
    {
        private const int Port = 19778;
        private const string Prefix = "http://localhost:19778/";
        private const int HeartbeatTimeoutSeconds = 15;
        private const int MaxRequestBodyBytes = 256 * 1024; // 256 KB (flight plans can be large)

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentQueue<GNSCommand> _commandQueue = new();
        private readonly SynchronizationContext? _syncContext;
        private DateTime _lastHeartbeat = DateTime.MinValue;
        private bool _disposed;

        // Latest state from the bridge
        private JsonElement _lastFlightPlan;
        private JsonElement _lastNavState;
        private JsonElement _lastPageState;
        private bool _hasFms = false;
        private int _instrumentType = 0; // 1=GNS530, 2=GNS430, 3=G1000MFD

        public event EventHandler<GNSStateUpdateEventArgs>? StateUpdated;

        public bool IsRunning => _listener?.IsListening == true;
        public bool IsBridgeConnected => (DateTime.UtcNow - _lastHeartbeat).TotalSeconds < HeartbeatTimeoutSeconds;
        public bool HasFms => _hasFms;
        public int InstrumentType => _instrumentType;
        public JsonElement LastFlightPlan => _lastFlightPlan;
        public JsonElement LastNavState => _lastNavState;
        public JsonElement LastPageState => _lastPageState;
        public DateTime LastHeartbeat => _lastHeartbeat;

        public GNSBridgeServer()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);

            try
            {
                _listener.Start();
                Task.Run(() => ListenLoop(_cts.Token));
                System.Diagnostics.Debug.WriteLine($"[GNS Bridge Server] Started on {Prefix}");
            }
            catch (HttpListenerException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GNS Bridge Server] Failed to start: {ex.Message}");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        /// <summary>
        /// Queue a command to be sent to the GNS 530 bridge JS.
        /// </summary>
        public void SendCommand(string command, Dictionary<string, object>? payload = null)
        {
            _commandQueue.Enqueue(new GNSCommand { Command = command, Payload = payload });
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GNS Bridge Server] Listen error: {ex.Message}");
                    await Task.Delay(100, ct);
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // CORS headers
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            try
            {
                string path = request.Url?.AbsolutePath ?? "";

                switch (path)
                {
                    case "/ping":
                        await Respond(response, 200, "ok");
                        break;

                    case "/state":
                        if (request.HttpMethod == "POST")
                        {
                            await HandleStatePost(request, response);
                        }
                        else
                        {
                            await Respond(response, 405, "POST only");
                        }
                        break;

                    case "/commands":
                        await HandleCommandsGet(response);
                        break;

                    default:
                        await Respond(response, 404, "not found");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GNS Bridge Server] Request error: {ex.Message}");
                try { response.StatusCode = 500; response.Close(); } catch { }
            }
        }

        private async Task HandleStatePost(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.ContentLength64 > MaxRequestBodyBytes)
            {
                await Respond(response, 413, "too large");
                return;
            }

            using var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync();

            try
            {
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var type = root.GetProperty("type").GetString() ?? "";
                var data = root.TryGetProperty("data", out var d) ? d : default;

                _lastHeartbeat = DateTime.UtcNow;

                switch (type)
                {
                    case "connected":
                        System.Diagnostics.Debug.WriteLine("[GNS Bridge Server] Bridge connected");
                        _hasFms = false;
                        if (data.ValueKind == JsonValueKind.Object &&
                            data.TryGetProperty("instrumentType", out var typeProp) &&
                            typeProp.ValueKind == JsonValueKind.Number)
                        {
                            _instrumentType = typeProp.GetInt32();
                        }
                        RaiseStateUpdated(type, data.ValueKind != JsonValueKind.Undefined ? data.Clone() : default);
                        break;

                    case "heartbeat":
                        break;

                    case "flight_plan":
                        _lastFlightPlan = data.Clone();
                        _hasFms = true;
                        RaiseStateUpdated(type, data.Clone());
                        break;

                    case "page_state":
                        _lastPageState = data.Clone();
                        _hasFms = true;
                        RaiseStateUpdated(type, data.Clone());
                        break;

                    case "nav_state":
                        _lastNavState = data.Clone();
                        RaiseStateUpdated(type, data.Clone());
                        break;

                    case "no_fms":
                        _hasFms = false;
                        break;

                    case "command_ack":
                    case "command_error":
                        RaiseStateUpdated(type, data.Clone());
                        break;

                    case "error":
                        System.Diagnostics.Debug.WriteLine($"[GNS Bridge Server] Bridge error: {data}");
                        break;
                }
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GNS Bridge Server] JSON parse error: {ex.Message}");
            }

            await Respond(response, 200, "ok");
        }

        private async Task HandleCommandsGet(HttpListenerResponse response)
        {
            var commands = new List<GNSCommand>();
            while (_commandQueue.TryDequeue(out var cmd))
            {
                commands.Add(cmd);
            }

            var json = JsonSerializer.Serialize(commands);
            await Respond(response, 200, json, "application/json");
        }

        private void RaiseStateUpdated(string type, JsonElement data)
        {
            if (_syncContext != null)
            {
                _syncContext.Post(_ =>
                {
                    StateUpdated?.Invoke(this, new GNSStateUpdateEventArgs { Type = type, Data = data });
                }, null);
            }
            else
            {
                StateUpdated?.Invoke(this, new GNSStateUpdateEventArgs { Type = type, Data = data });
            }
        }

        private static async Task Respond(HttpListenerResponse response, int statusCode, string body, string contentType = "text/plain")
        {
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
