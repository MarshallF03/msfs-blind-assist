// G1000 NXi Accessibility Bridge — v2
// Injected into WTG1000_MFD.html via mod package override.
// Reports flight plan + nav state via HTTP on localhost:19778.
// Same protocol as GNS 530 bridge — only one can be active per sim session.

try {

if (typeof _gps !== 'undefined') {
    console.log('[G1000 Bridge] GPS bridge already loaded by another instrument, skipping');
} else {

var _gps = {
    VERSION: '2.0.0',
    SERVER_URL: 'http://localhost:19778',
    COMMAND_POLL_INTERVAL: 500,
    STATE_UPDATE_INTERVAL: 1000,
    HEARTBEAT_INTERVAL: 5000,
    RECONNECT_INTERVAL: 5000,
    READY_CHECK_INTERVAL: 500,
    LVAR_WRITE_INTERVAL: 1000,

    instrument: null,
    fms: null,
    planner: null,
    bus: null,
    instrumentType: 3,  // G1000 MFD

    scriptLoaded: true,
    fmsFound: false,
    serverConnected: false,
    lastErrorCode: 0,
    lastPostTime: 0,
    lastPageHash: '',

    readyCheckTimer: null,
    commandPollTimer: null,
    stateUpdateTimer: null,
    heartbeatTimer: null,
    lvarWriteTimer: null
};

_gps.writeDiagnosticLVars = function() {
    try {
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_Loaded', 'number', 1);
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_FmsFound', 'number', _gps.fmsFound ? 1 : 0);
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_ServerConnected', 'number', _gps.serverConnected ? 1 : 0);
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_ErrorCode', 'number', _gps.lastErrorCode);
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_InstrumentType', 'number', _gps.instrumentType);
    } catch (e) {}
};

_gps.postState = async function(type, data) {
    if (!_gps.serverConnected) return;
    try {
        await fetch(_gps.SERVER_URL + '/state', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: type, data: data || {} })
        });
        _gps.lastPostTime = Date.now();
    } catch (e) {}
};

_gps.pollCommands = async function() {
    if (!_gps.serverConnected) return;
    try {
        var response = await fetch(_gps.SERVER_URL + '/commands');
        var commands = await response.json();
        for (var i = 0; i < commands.length; i++) {
            _gps.handleCommand(commands[i].command, commands[i].payload);
        }
    } catch (e) {}
};

_gps.fetchWithTimeout = function(url, options, timeoutMs) {
    return new Promise(function(resolve, reject) {
        var timer = setTimeout(function() { reject(new Error('Timeout')); }, timeoutMs);
        fetch(url, options).then(function(response) {
            clearTimeout(timer); resolve(response);
        }).catch(function(err) { clearTimeout(timer); reject(err); });
    });
};

_gps.tryConnect = async function() {
    try {
        var response = await _gps.fetchWithTimeout(_gps.SERVER_URL + '/ping', {}, 2000);
        if (response.ok) {
            if (!_gps.serverConnected) {
                _gps.serverConnected = true;
                console.log('[G1000 Bridge] Connected to accessibility server');
                _gps.startPolling();
                _gps.postState('connected', { version: _gps.VERSION, instrumentType: _gps.instrumentType });
                _gps.sendPageState();
            }
            return true;
        }
    } catch (e) {
        if (_gps.serverConnected) {
            _gps.serverConnected = false;
            _gps.stopPolling();
            _gps.lastErrorCode = 4;
        }
    }
    return false;
};

_gps.startPolling = function() {
    if (_gps.commandPollTimer) clearInterval(_gps.commandPollTimer);
    if (_gps.stateUpdateTimer) clearInterval(_gps.stateUpdateTimer);
    if (_gps.heartbeatTimer) clearInterval(_gps.heartbeatTimer);
    _gps.commandPollTimer = setInterval(_gps.pollCommands, _gps.COMMAND_POLL_INTERVAL);
    _gps.stateUpdateTimer = setInterval(_gps.sendPageState, _gps.STATE_UPDATE_INTERVAL);
    _gps.heartbeatTimer = setInterval(function() { _gps.postState('heartbeat'); }, _gps.HEARTBEAT_INTERVAL);
};

_gps.stopPolling = function() {
    if (_gps.commandPollTimer) { clearInterval(_gps.commandPollTimer); _gps.commandPollTimer = null; }
    if (_gps.stateUpdateTimer) { clearInterval(_gps.stateUpdateTimer); _gps.stateUpdateTimer = null; }
    if (_gps.heartbeatTimer) { clearInterval(_gps.heartbeatTimer); _gps.heartbeatTimer = null; }
};

_gps.findInstrument = function() {
    try {
        if (typeof BaseInstrument !== 'undefined' && BaseInstrument.allInstruments) {
            for (var i = 0; i < BaseInstrument.allInstruments.length; i++) {
                var inst = BaseInstrument.allInstruments[i];
                if (inst && (inst.templateID === 'AS1000_MFD' ||
                    (inst.constructor && inst.constructor.name === 'WTG1000_MFD'))) {
                    return inst;
                }
            }
        }
        if (typeof BaseInstrument !== 'undefined' && BaseInstrument.allInstrumentsLoaded) {
            for (var j = 0; j < BaseInstrument.allInstrumentsLoaded.length; j++) {
                var inst2 = BaseInstrument.allInstrumentsLoaded[j];
                if (inst2 && inst2.fms) return inst2;
            }
        }
    } catch (e) {
        console.log('[G1000 Bridge] findInstrument error: ' + e.message);
    }
    return null;
};

_gps.checkReady = function() {
    try {
        if (!_gps.instrument) {
            var inst = _gps.findInstrument();
            if (!inst) { _gps.lastErrorCode = 1; return false; }
            _gps.instrument = inst;
            console.log('[G1000 Bridge] Instrument found');
        }

        if (!_gps.fms) {
            if (_gps.instrument.fms) {
                _gps.fms = _gps.instrument.fms;
                _gps.planner = _gps.instrument.planner;
                _gps.bus = _gps.instrument.bus;
                _gps.fmsFound = true;
                _gps.lastErrorCode = 0;
                console.log('[G1000 Bridge] FMS ready');
                _gps.subscribeToEvents();
            } else {
                _gps.lastErrorCode = 2;
                return false;
            }
        }
        return true;
    } catch (e) {
        console.log('[G1000 Bridge] checkReady error: ' + e.message);
        return false;
    }
};

_gps.subscribeToEvents = function() {
    try {
        if (_gps.fms && _gps.fms.flightPlanner) {
            try {
                _gps.fms.flightPlanner.onEvent('fplActiveLegChange').handle(function() { _gps.sendPageState(); });
                _gps.fms.flightPlanner.onEvent('fplLegChange').handle(function() { _gps.sendPageState(); });
                _gps.fms.flightPlanner.onEvent('fplLoaded').handle(function() { _gps.sendPageState(); });
            } catch (e) {}
        }
    } catch (e) {}
};

_gps.extractIdent = function(icao) {
    try {
        if (!icao) return '';
        if (icao.length >= 7) {
            var s = icao.substring(7).trim();
            if (!s) s = icao.substring(3, 7).trim();
            return s || icao;
        }
        return icao;
    } catch (e) { return ''; }
};

_gps.extractFlightPlan = function() {
    var items = [];
    try {
        var plan = null;
        try { plan = _gps.fms.getPrimaryFlightPlan && _gps.fms.getPrimaryFlightPlan(); } catch (e) {}
        if (!plan) {
            try { plan = _gps.fms.flightPlanner.getActiveFlightPlan(); } catch (e) {}
        }
        if (!plan) return { items: [], selected: -1 };

        var activeLeg = plan.activeLateralLeg || 0;

        for (var i = 0; i < plan.length; i++) {
            try {
                var leg = plan.tryGetLeg(i);
                if (!leg) continue;

                var ident = '';
                if (leg.name) ident = leg.name;
                else if (leg.leg && leg.leg.fixIcao) ident = _gps.extractIdent(leg.leg.fixIcao);

                var distance = 0, dtk = 0;
                if (leg.calculated) {
                    if (leg.calculated.distanceWithTransitions !== undefined) {
                        distance = leg.calculated.distanceWithTransitions / 1852.0;
                    }
                    if (leg.calculated.initialDtk !== undefined) dtk = leg.calculated.initialDtk;
                }

                var text = ident;
                if (distance > 0) text += '  ' + distance.toFixed(1) + ' NM';
                if (dtk > 0) text += '  ' + Math.round(dtk) + '°';

                items.push({ text: text, ident: ident, distance: distance, dtk: dtk, index: i, isActive: (i === activeLeg) });
            } catch (legErr) { continue; }
        }

        return { items: items, selected: activeLeg };
    } catch (e) {
        return { items: [], selected: -1 };
    }
};

_gps.sendPageState = function() {
    if (!_gps.serverConnected || !_gps.fms) return;
    try {
        // G1000 MFD doesn't have the same page stack as GNS — always show active flight plan
        var fplData = _gps.extractFlightPlan();

        var pageHash = 'G1000_FPL|' + fplData.items.length + '|' + fplData.selected;
        if (pageHash === _gps.lastPageHash && (Date.now() - _gps.lastPostTime) < 2000) {
            _gps.sendNavState();
            return;
        }
        _gps.lastPageHash = pageHash;

        _gps.postState('page_state', {
            groupLabel: 'FPL',
            pageIndex: 0,
            pageTitle: 'Active Flight Plan',
            pageType: 'G1000_FPL',
            items: fplData.items,
            selected: fplData.selected,
            isDialog: false
        });

        _gps.sendNavState();
    } catch (e) {
        console.log('[G1000 Bridge] sendPageState error: ' + e.message);
    }
};

_gps.sendNavState = function() {
    try {
        var nav = {
            nextWpDist: SimVar.GetSimVarValue('GPS WP DISTANCE', 'nautical miles') || 0,
            nextWpBearing: SimVar.GetSimVarValue('GPS WP BEARING', 'degrees') || 0,
            nextWpEte: SimVar.GetSimVarValue('GPS WP ETE', 'seconds') || 0,
            dtk: SimVar.GetSimVarValue('GPS WP DESIRED TRACK', 'degrees') || 0,
            xtk: SimVar.GetSimVarValue('GPS WP CROSS TRK', 'nautical miles') || 0,
            groundSpeed: SimVar.GetSimVarValue('GPS GROUND SPEED', 'knots') || 0,
            isDirectTo: SimVar.GetSimVarValue('GPS IS DIRECTTO FLIGHTPLAN', 'bool') > 0,
            isApproachLoaded: SimVar.GetSimVarValue('GPS IS APPROACH LOADED', 'bool') > 0,
            isApproachActive: SimVar.GetSimVarValue('GPS IS APPROACH ACTIVE', 'bool') > 0,
            gpsDrivesNav: SimVar.GetSimVarValue('GPS DRIVES NAV1', 'bool') > 0
        };
        _gps.postState('nav_state', nav);
    } catch (e) {}
};

_gps.handleCommand = function(command, payload) {
    try {
        switch (command) {
            case 'direct_to':
                SimVar.SetSimVarValue('K:GPS_DIRECTTO_BUTTON', 'number', 0);
                _gps.postState('command_ack', { command: command });
                break;
            case 'cancel_direct_to':
                if (_gps.fms && typeof _gps.fms.cancelDirectTo === 'function') {
                    _gps.fms.cancelDirectTo();
                    _gps.postState('command_ack', { command: command });
                }
                break;
            case 'activate_leg':
                if (_gps.fms && payload && payload.index !== undefined && typeof _gps.fms.activateLeg === 'function') {
                    _gps.fms.activateLeg(0, payload.index);
                    _gps.postState('command_ack', { command: command, index: payload.index });
                }
                break;
            case 'gps_drives_nav':
                SimVar.SetSimVarValue('K:TOGGLE_GPS_DRIVES_NAV1', 'number', 0);
                _gps.postState('command_ack', { command: command });
                break;
            case 'gps_button':
                if (payload && payload.event) {
                    SimVar.SetSimVarValue('K:' + payload.event, 'number', 0);
                    _gps.postState('command_ack', { command: command, event: payload.event });
                }
                break;
            case 'request_state':
                _gps.lastPageHash = '';
                _gps.sendPageState();
                break;
            case 'interaction_event':
                // G1000 uses K-events for button presses, not internal InteractionEvents
                if (payload && payload.event) {
                    SimVar.SetSimVarValue('K:' + payload.event, 'number', 0);
                }
                break;
            default:
                console.log('[G1000 Bridge] Unknown command: ' + command);
        }
    } catch (e) {
        _gps.postState('command_error', { command: command, error: e.message });
    }
};

_gps.init = function() {
    console.log('[G1000 Bridge] Initializing v' + _gps.VERSION);
    _gps.writeDiagnosticLVars();
    _gps.lvarWriteTimer = setInterval(_gps.writeDiagnosticLVars, _gps.LVAR_WRITE_INTERVAL);

    _gps.readyCheckTimer = setInterval(function() {
        if (_gps.checkReady()) {
            if (_gps.readyCheckTimer) {
                clearInterval(_gps.readyCheckTimer);
                _gps.readyCheckTimer = null;
            }
        }
    }, _gps.READY_CHECK_INTERVAL);

    _gps.tryConnect();
    setInterval(function() {
        if (!_gps.serverConnected) _gps.tryConnect();
    }, _gps.RECONNECT_INTERVAL);
};

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', _gps.init);
} else {
    setTimeout(_gps.init, 100);
}

} // end else

} catch (e) {
    console.error('[G1000 Bridge] Fatal error: ' + e.message);
    try { SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_ErrorCode', 'number', 99); } catch (e2) {}
}
