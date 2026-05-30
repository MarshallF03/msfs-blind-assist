// G1000 NXi MFD Accessibility Bridge
// Injected into WTG1000_MFD.html via zzz-g1000-accessibility Community package.
// Polls FMS state and POSTs it to MSFS Blind Assist on localhost:19779.
// GETs commands from the server and executes them against the live FMS.
//
// Coherent GT constraints: no AbortSignal.timeout(), top-level try-catch required.
try {

if (window._g1000_bridge_loaded) {
    console.log('[G1000 Bridge] already loaded, skipping');
} else { window._g1000_bridge_loaded = true;

var _g1000 = {
    VERSION: '1',
    SERVER:  'http://localhost:19779',
    connected: false,
    statePollTimer: null,
    cmdPollTimer: null,
    heartbeatTimer: null,
    reconnectTimer: null,
    // Loaded airport facilities cached by slot name ('dep' or 'arr')
    facilities: {},
    // Queue for async operations so commands don't stack
    busy: false
};

// ─── HTTP helpers ────────────────────────────────────────────────────────────

_g1000.fetchTimeout = function(url, opts, ms) {
    return new Promise(function(resolve, reject) {
        var timer = setTimeout(function() { reject(new Error('timeout')); }, ms);
        fetch(url, opts).then(function(r) { clearTimeout(timer); resolve(r); },
                              function(e) { clearTimeout(timer); reject(e); });
    });
};

_g1000.post = function(type, data) {
    return _g1000.fetchTimeout(_g1000.SERVER + '/state', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ type: type, data: data })
    }, 3000);
};

_g1000.getCommands = function() {
    return _g1000.fetchTimeout(_g1000.SERVER + '/commands', {}, 3000);
};

// ─── FMS state builder ───────────────────────────────────────────────────────

_g1000.buildState = function() {
    try {
        var fp = fms.getPrimaryFlightPlan();
        var legs = [];
        for (var i = 0; i < fp.length; i++) {
            try {
                var l = fp.getLeg(i);
                var id = l && l.leg ? (l.leg.fixIcao || '').trim().replace(/\x00/g,'') : '';
                if (!id && l && l.leg) id = l.leg.type || '?';
                legs.push({ ident: id, name: (l && l.name) || id, active: i === fp.activeLegIndex });
            } catch(e) { legs.push({ ident: '?', name: '?', active: false }); }
        }
        var proc = {};
        try {
            var pd = fp.procedureDetails;
            if (pd) { proc.depIdx = pd.departureIndex; proc.depRwy = pd.departureRunwayIndex;
                      proc.arrIdx = pd.arrivalIndex;   proc.apprIdx = pd.approachIndex; }
        } catch(e) {}
        return { ok: true,
                 origin: fp.originAirport || '', dest: fp.destinationAirport || '',
                 activeLeg: fp.activeLegIndex, legs: legs, proc: proc };
    } catch(e) {
        return { ok: false, err: e.message };
    }
};

// ─── Command execution ───────────────────────────────────────────────────────

_g1000.execCommand = function(cmd) {
    if (_g1000.busy) return;
    _g1000.busy = true;

    var done = function(result) {
        _g1000.busy = false;
        _g1000.post('command_result', { command: cmd.command, result: result });
    };

    try {
        switch (cmd.command) {

        case 'load_airport': {
            var icao = (cmd.payload && cmd.payload.icao) || '';
            var slot = (cmd.payload && cmd.payload.slot) || 'dep';
            fms.facLoader.getFacility('A', icao).then(function(fac) {
                _g1000.facilities[slot] = fac;
                var deps  = _g1000.serializeProcs(fac.departures  || [], 'dep');
                var arrs  = _g1000.serializeProcs(fac.arrivals    || [], 'arr');
                var apprs = _g1000.serializeProcs(fac.approaches  || [], 'appr');
                done({ ok: true, icao: fac.icao, name: fac.name || icao,
                       departures: deps, arrivals: arrs, approaches: apprs });
            }, function(e) { done({ ok: false, err: e.message }); });
            return; // busy released in callbacks
        }

        case 'load_dep': {
            var fac = _g1000.facilities['dep'];
            if (!fac) { done({ ok: false, err: 'dep airport not loaded' }); break; }
            var p = cmd.payload || {};
            fms.loadDeparture(fac, p.depIdx || 0, p.rwyIdx || 0, p.transIdx !== undefined ? p.transIdx : -1);
            done({ ok: true });
            break;
        }

        case 'load_arr': {
            var fac = _g1000.facilities['arr'];
            if (!fac) { done({ ok: false, err: 'arr airport not loaded' }); break; }
            var p = cmd.payload || {};
            fms.loadArrival(fac, p.arrIdx || 0, p.transIdx !== undefined ? p.transIdx : -1, p.rwyIdx || 0);
            done({ ok: true });
            break;
        }

        case 'arm_approach': {
            var fac = _g1000.facilities['arr'];
            if (!fac) { done({ ok: false, err: 'arr airport not loaded' }); break; }
            var p = cmd.payload || {};
            fms.insertApproach({ facility: fac,
                                 approachIndex: p.apprIdx || 0,
                                 approachTransitionIndex: p.transIdx !== undefined ? p.transIdx : -1
            }).then(function(r) { done({ ok: true, result: r }); },
                    function(e) { done({ ok: false, err: e.message }); });
            return;
        }

        case 'activate_approach': {
            fms.activateApproach();
            done({ ok: true });
            break;
        }

        case 'direct_to': {
            var ident = (cmd.payload && cmd.payload.ident) || '';
            if (typeof fms.createDirectToRandom === 'function')
                fms.createDirectToRandom(ident);
            else if (typeof fms.createDirectTo === 'function')
                fms.createDirectTo(ident);
            else { done({ ok: false, err: 'no directTo API' }); break; }
            done({ ok: true });
            break;
        }

        case 'set_origin': {
            var fac = _g1000.facilities['dep'];
            if (!fac) { done({ ok: false, err: 'dep airport not loaded' }); break; }
            if (typeof fms.setOrigin === 'function') fms.setOrigin(fac);
            done({ ok: true });
            break;
        }

        case 'set_dest': {
            var fac = _g1000.facilities['arr'];
            if (!fac) { done({ ok: false, err: 'arr airport not loaded' }); break; }
            if (typeof fms.setDestination === 'function') fms.setDestination(fac);
            done({ ok: true });
            break;
        }

        case 'clear_fpl': {
            fms.emptyPrimaryFlightPlan().then(function() { done({ ok: true }); },
                                              function(e) { done({ ok: false, err: e.message }); });
            return;
        }

        case 'ping': {
            done({ ok: true, version: _g1000.VERSION });
            break;
        }

        default:
            done({ ok: false, err: 'unknown command: ' + cmd.command });
        }
    } catch(e) {
        _g1000.busy = false;
        _g1000.post('command_result', { command: cmd.command, result: { ok: false, err: e.message } });
    }
};

_g1000.serializeProcs = function(arr, type) {
    return arr.map(function(p, i) {
        var rwys  = (p.runwayTransitions  || []).map(function(r) { return r.name || ('Rwy ' + (r.runwayNumber || i)); });
        var trans = (p.enRouteTransitions || p.transitions || []).map(function(t, j) { return { i: j, name: t.name || ('Trans ' + j) }; });
        return { i: i, name: p.name || ('#' + i), runways: rwys, transitions: trans };
    });
};

// ─── Poll loops ──────────────────────────────────────────────────────────────

_g1000.pollState = function() {
    try {
        var state = _g1000.buildState();
        _g1000.post('fms_state', state).catch(function() {});
    } catch(e) {}
};

_g1000.pollCommands = function() {
    if (_g1000.busy) return;
    _g1000.getCommands().then(function(resp) {
        return resp.json();
    }).then(function(cmds) {
        if (Array.isArray(cmds) && cmds.length > 0)
            _g1000.execCommand(cmds[0]);
    }).catch(function() {});
};

_g1000.heartbeat = function() {
    _g1000.fetchTimeout(_g1000.SERVER + '/ping', {}, 2000).then(function() {
        if (!_g1000.connected) {
            _g1000.connected = true;
            console.log('[G1000 Bridge] connected to MSFS BA');
            _g1000.startPolling();
        }
    }, function() {
        if (_g1000.connected) {
            _g1000.connected = false;
            _g1000.stopPolling();
            console.log('[G1000 Bridge] lost connection, retrying...');
        }
    });
};

_g1000.startPolling = function() {
    if (_g1000.statePollTimer) return;
    _g1000.statePollTimer = setInterval(_g1000.pollState,    2000);
    _g1000.cmdPollTimer   = setInterval(_g1000.pollCommands,  400);
    _g1000.pollState(); // immediate first poll
};

_g1000.stopPolling = function() {
    if (_g1000.statePollTimer) { clearInterval(_g1000.statePollTimer); _g1000.statePollTimer = null; }
    if (_g1000.cmdPollTimer)   { clearInterval(_g1000.cmdPollTimer);   _g1000.cmdPollTimer   = null; }
};

// ─── Start ───────────────────────────────────────────────────────────────────

_g1000.heartbeatTimer = setInterval(_g1000.heartbeat, 5000);
_g1000.heartbeat(); // try immediately

console.log('[G1000 Bridge] loaded, version ' + _g1000.VERSION);

}} // end double-load guard + top-level try-catch
} catch(e) {
    console.error('[G1000 Bridge] fatal error:', e);
}
