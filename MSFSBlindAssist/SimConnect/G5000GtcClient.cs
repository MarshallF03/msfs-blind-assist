using System.Text.Json;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Approach-A engine for the Citation Longitude: drives the REAL Working Title
/// G3000/G5000 touchscreen (GTC) over Coherent GT. Because it commands the live
/// avionics UI, the in-sim GTC pages actually switch and edit as the user works
/// in the accessible mirror.
///
/// Connects to a GTC page (default WTG3000_GTC_2, which runs in MFD control mode
/// where the full FMS page set lives). All JS runs against:
///     document.querySelector('wtg3000-gtc').fsInstrument.gtcService
///
/// Discovery (live 2026-06-01, see docs/longitude-g5000.md):
///  - Navigation: changePageTo(key) / goToHomePage() / goBack().
///  - Active page key: gtcService.currentPage.get().key  (e.g. "Perf").
///  - Active page DOM root: dig currentPage.get() for the first Element node
///    (all pages persist in the DOM at once; a non-focused GTC reports every view
///    as .hidden, so we locate the active page by its key/ref, never by visibility).
///  - Controls are .touch-button / .list-item; toggle state in the class list.
/// </summary>
public sealed class G5000GtcClient : IDisposable
{
    public const string DefaultGtcTitle = "WTG3000_GTC_2";

    private readonly CoherentGTClient _cgt = new();
    private readonly string _gtcTitle;
    private bool _disposed;

    private const string Gs = "document.querySelector('wtg3000-gtc').fsInstrument.gtcService";

    public bool IsConnected => _cgt.IsConnected;
    public string? PageTitle => _cgt.ConnectedTargetTitle;

    public G5000GtcClient(string? gtcTitle = null) => _gtcTitle = gtcTitle ?? DefaultGtcTitle;

    public async Task<bool> TryConnectAsync(CancellationToken ct = default)
    {
        if (_cgt.IsConnected) return true;
        return await _cgt.TryConnectAsync(_gtcTitle, ct);
    }

    /// <summary>Ensure this GTC is in MFD control mode (where the FMS pages live).</summary>
    public async Task EnsureMfdModeAsync()
    {
        const string js = $@"(function(){{try{{var gs={Gs};
  var m=gs.activeControlMode&&gs.activeControlMode.get?gs.activeControlMode.get():null;
  if(m!=='MFD' && gs.changeControlModeTo) gs.changeControlModeTo(1);
  return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        await _cgt.EvaluateAsync(js, 2500);
    }

    /// <summary>Navigate the GTC to a registered page key (e.g. "FlightPlan", "Perf").</summary>
    public async Task<bool> NavigateAsync(string viewKey)
    {
        if (!_cgt.IsConnected && !await TryConnectAsync()) return false;
        string js = $@"(function(){{try{{{Gs}.changePageTo('{viewKey.Replace("'", "")}');return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await _cgt.EvaluateAsync(js, 3000) == "ok";
    }

    public async Task<bool> GoHomeAsync()
        => await _cgt.EvaluateAsync($"(function(){{try{{{Gs}.goToHomePage();return 'ok';}}catch(e){{return 'ERR';}}}})()", 2500) == "ok";

    public async Task<bool> GoBackAsync()
        => await _cgt.EvaluateAsync($"(function(){{try{{{Gs}.goBack();return 'ok';}}catch(e){{return 'ERR';}}}})()", 2500) == "ok";

    /// <summary>Read the currently-active GTC page: its key and ordered controls.</summary>
    public async Task<G5000GtcPage?> ReadActivePageAsync()
    {
        if (!_cgt.IsConnected && !await TryConnectAsync()) return null;
        string? r = await _cgt.EvaluateAsync(ReadJs, 4000);
        if (r == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(r);
            return G5000GtcPage.Parse(doc.RootElement);
        }
        catch { return null; }
    }

    /// <summary>
    /// Press a control on the active page by its (trimmed) visible text. Dispatches a
    /// real mouse click on the matching .touch-button — this actuates the live GTC.
    /// </summary>
    public async Task<bool> PressByTextAsync(string text)
    {
        if (!_cgt.IsConnected && !await TryConnectAsync()) return false;
        string needle = text.Replace("'", "").Replace("\\", "");
        string js = $@"(function(){{try{{
  var cp={Gs}.currentPage.get();
  var dig=function(o,d){{if(!o||d>5)return null;for(var k in o){{try{{var v=o[k];
    if(v&&v.nodeType===1)return v; if(v&&typeof v==='object'){{var r=dig(v,d+1); if(r)return r;}}}}catch(e){{}}}}return null;}};
  var el=null; try{el=cp.ref.thisNode.children[0].instance;}catch(e){} if(!el||!el.nodeType) el=dig(cp,0);
  if(!el) return 'no_page';
  var btns=el.querySelectorAll('.touch-button, .list-item');
  var target=null;
  for(var i=0;i<btns.length;i++){{ var t=(btns[i].textContent||'').replace(/\s+/g,' ').trim();
    if(t.indexOf('{needle}')>=0){{ target=btns[i]; break; }} }}
  if(!target) return 'not_found';
  ['mousedown','mouseup','click'].forEach(function(ev){{
    target.dispatchEvent(new MouseEvent(ev,{{bubbles:true,cancelable:true,view:window}})); }});
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await _cgt.EvaluateAsync(js, 3000) == "ok";
    }

    public static Task<List<(string title, string url)>> ListPagesAsync()
        => CoherentGTClient.ListTargetsAsync(19999);

    // Active-page reader. Locates the active page's DOM root via currentPage ref
    // (NOT visibility — a non-focused GTC marks everything .hidden), then dumps
    // its controls in order with toggle state.
    private const string ReadJs = $@"
(function(){{try{{
  var gs={Gs}; var cp=gs.currentPage.get();
  var key=(cp&&cp.key)?cp.key:'?';
  var dig=function(o,d){{if(!o||d>5)return null;for(var k in o){{try{{var v=o[k];
    if(v&&v.nodeType===1)return v; if(v&&typeof v==='object'){{var r=dig(v,d+1); if(r)return r;}}}}catch(e){{}}}}return null;}};
  var el=null; try{{el=cp.ref.thisNode.children[0].instance;}}catch(e){{}} if(!el||!el.nodeType) el=dig(cp,0);
  if(!el) return JSON.stringify({{key:key,rows:[]}});
  var rows=[];
  el.querySelectorAll('.touch-button, .list-item, [class*=title]').forEach(function(b){{
    var t=(b.textContent||'').replace(/\s+/g,' ').trim();
    var cls=b.className.toString();
    var kind=/toggle/.test(cls)?'toggle':(/value/.test(cls)?'value':(/title/.test(cls)?'title':'button'));
    var on=/(^|[\s-])(active|selected|toggle-on|primed|cyan|checked)([\s-]|$)/.test(cls);
    if(t && t.length<80) rows.push({{k:kind,on:on,t:t}});
  }});
  var out=[]; for(var i=0;i<rows.length;i++){{ if(!out.length||out[out.length-1].t!==rows[i].t) out.push(rows[i]); }}
  return JSON.stringify({{key:key,rows:out}});
}}catch(e){{return JSON.stringify({{key:'ERR',rows:[],err:e.message}});}}}})()";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cgt.Dispose();
    }
}

public record G5000GtcControl(string Kind, bool On, string Text)
{
    public string DisplayText => (On ? "[ON] " : "") + Text + (Kind == "toggle" && !On ? " [off]" : "");
}

public sealed class G5000GtcPage
{
    public string Key { get; init; } = "";
    public List<G5000GtcControl> Controls { get; init; } = new();

    public static G5000GtcPage Parse(JsonElement r)
    {
        var controls = new List<G5000GtcControl>();
        if (r.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            foreach (var c in rows.EnumerateArray())
                controls.Add(new G5000GtcControl(
                    c.TryGetProperty("k", out var k) ? k.GetString() ?? "button" : "button",
                    c.TryGetProperty("on", out var on) && on.GetBoolean(),
                    c.TryGetProperty("t", out var t) ? t.GetString() ?? "" : ""));
        return new G5000GtcPage
        {
            Key = r.TryGetProperty("key", out var key) ? key.GetString() ?? "" : "",
            Controls = controls
        };
    }
}
