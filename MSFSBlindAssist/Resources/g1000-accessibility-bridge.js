// G1000 NXi MFD Accessibility Bridge  v3.0.0
// Injected into the G1000 MFD via zzz-g1000-accessibility Community package.
// Communicates with MSFS Blind Assist on localhost:19778.
//
// Sends:  page_state  (FPL with ident/distance/bearing/alt constraints + active marker)
//         nav_state   (active wp distance/bearing/ETE/XTK, approach mode)
//         facility_data (SID/STAR/approach lists when requested)
//         command_ack / command_error
//
// Receives commands: activate_leg, create_direct_to, cancel_direct_to,
//                    load_procedures_list, select_procedure, gps_button,
//                    gps_drives_nav, request_state
//
// Coherent GT constraints: no AbortSignal.timeout(), top-level try-catch.
try {

if (window._g1000_bridge_v3) {
    console.log('[G1000 Bridge v3] already loaded');
} else {
window._g1000_bridge_v3 = true;

var _gps = window._gps || {};
window._gps = _gps;

_gps.VERSION      = '3.0.0';
_gps.SERVER_URL   = 'http://localhost:19778';
_gps.serverConnected = false;
_gps.fmsFound     = false;
_gps.fms          = null;
_gps.instrument   = null;
_gps.lastPageHash = '';
_gps.lastPostTime = 0;

// ── Timers ────────────────────────────────────────────────────────────────────
_gps._heartbeatTimer  = null;
_gps._statePollTimer  = null;
_gps._cmdPollTimer    = null;

// ── HTTP helpers ──────────────────────────────────────────────────────────────
_gps.fetchWithTimeout = function(url, opts, ms) {
    return new Promise(function(resolve, reject) {
        var t = setTimeout(function() { reject(new Error('timeout')); }, ms);
        fetch(url, opts).then(function(r) { clearTimeout(t); resolve(r); },
                              function(e) { clearTimeout(t); reject(e); });
    });
};

_gps.postState = function(type, data) {
    _gps.fetchWithTimeout(_gps.SERVER_URL + '/state', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ type: type, data: data })
    }, 3000).catch(function() {});
};

// ── FMS discovery ─────────────────────────────────────────────────────────────
_gps.findFms = function() {
    // Walk registered instruments to find the G1000 MFD and its fms reference
    try {
        var keys = Object.keys(window);
        for (var i = 0; i < keys.length; i++) {
            var obj = window[keys[i]];
            if (obj && typeof obj === 'object' && obj.fms && typeof obj.fms.getPrimaryFlightPlan === 'function') {
                _gps.fms = obj.fms;
                _gps.instrument = obj;
                _gps.fmsFound = true;
                return true;
            }
        }
        // Fallback: try the g1000nximfd module's exported fms
        if (typeof g1000nximfd !== 'undefined') {
            var inst = document.querySelector('vcockpit-instrument');
            if (inst && inst.fms) { _gps.fms = inst.fms; _gps.fmsFound = true; return true; }
        }
    } catch(e) {}
    return false;
};

// ── Flight plan extraction ────────────────────────────────────────────────────
_gps.extractFlightPlan = function() {
    var items = [];
    try {
        var plan = null;
        try { if (_gps.fms.getPrimaryFlightPlan) plan = _gps.fms.getPrimaryFlightPlan(); } catch(e) {}
        if (!plan) try { plan = _gps.fms.flightPlanner.getActiveFlightPlan(); } catch(e) {}
        if (!plan) return { items: [], selected: -1, origin: '', dest: '' };

        var activeLeg = typeof plan.activeLateralLeg !== 'undefined' ? plan.activeLateralLeg
                      : (typeof plan.activeLegIndex !== 'undefined' ? plan.activeLegIndex : -1);

        // Strip padded ICAO to clean ident (e.g. "A      EGBB " → "EGBB")
        var stripIcao = function(icao) {
            if (!icao) return '';
            return icao.trim().replace(/\x00/g, '').replace(/^[AVWNRU]\s+/, '').trim();
        };

        for (var i = 0; i < plan.length; i++) {
            try {
                var leg = null;
                if (typeof plan.tryGetLeg === 'function') leg = plan.tryGetLeg(i);
                else if (typeof plan.getLeg === 'function') leg = plan.getLeg(i);
                if (!leg) continue;

                var ident = leg.name || (leg.leg ? stripIcao(leg.leg.fixIcao) : '?') || '?';
                var distance = 0, dtk = 0;
                if (leg.calculated) {
                    distance = (leg.calculated.distanceWithTransitions || 0) / 1852.0;
                    dtk = leg.calculated.initialDtk || 0;
                }

                // Altitude constraint
                var altText = '';
                try {
                    if (leg.leg && leg.leg.altDesc && leg.leg.altDesc !== 0 && leg.leg.altitude1 != null) {
                        var ft = Math.round(leg.leg.altitude1);
                        if (leg.leg.altDesc === 1) altText = ' AT ' + ft;
                        else if (leg.leg.altDesc === 2) altText = ' +' + ft;
                        else if (leg.leg.altDesc === 3) altText = ' -' + ft;
                        else if (leg.leg.altDesc === 4 && leg.leg.altitude2 != null)
                            altText = ' ' + Math.round(leg.leg.altitude2) + '-' + ft;
                    }
                } catch(e) {}

                var text = ident + altText;
                if (distance > 0.05) text += '  ' + distance.toFixed(1) + ' nm';
                if (dtk > 0) text += '  ' + Math.round(dtk) + '°';

                items.push({ text: text, ident: ident, distance: distance, dtk: dtk,
                             index: i, isActive: (i === activeLeg) });
            } catch(e) {}
        }

        var origin = stripIcao(plan.originAirport || '');
        var dest   = stripIcao(plan.destinationAirport || '');
        return { items: items, selected: activeLeg, origin: origin, dest: dest };
    } catch(e) { return { items: [], selected: -1, origin: '', dest: '' }; }
};

// ── State senders ─────────────────────────────────────────────────────────────
_gps.sendNavState = function() {
    try {
        var sv = SimVar.GetSimVarValue;
        _gps.postState('nav_state', {
            nextWpDist:     sv('GPS WP DISTANCE', 'nautical miles'),
            nextWpBearing:  sv('GPS WP BEARING', 'degrees'),
            nextWpEte:      sv('GPS WP ETE', 'seconds'),
            dtk:            sv('GPS WP DESIRED TRACK', 'degrees'),
            xtk:            sv('GPS WP CROSS TRK', 'nautical miles'),
            groundSpeed:    sv('GPS GROUND SPEED', 'knots'),
            isDirectTo:     sv('GPS IS DIRECTTO FLIGHTPLAN', 'bool') > 0,
            isApproachLoaded: sv('GPS IS APPROACH LOADED', 'bool') > 0,
            isApproachActive: sv('GPS IS APPROACH ACTIVE', 'bool') > 0,
            gpsDrivesNav:   sv('GPS DRIVES NAV1', 'bool') > 0,
            approachMode:   sv('GPS APPROACH MODE', 'number')  // 0=none 1=armed 2=active
        });
    } catch(e) {}
};

_gps.sendFplState = function() {
    try {
        var fplData = _gps.extractFlightPlan();
        var hash = fplData.origin + '|' + fplData.dest + '|' + fplData.items.length + '|' + fplData.selected;
        var now = Date.now();
        if (hash === _gps.lastPageHash && (now - _gps.lastPostTime) < 1500) {
            _gps.sendNavState();
            return;
        }
        _gps.lastPageHash = hash;
        _gps.lastPostTime = now;
        _gps.postState('fpl_state', fplData);
        _gps.sendNavState();
    } catch(e) {}
};

// ── Command handler ───────────────────────────────────────────────────────────
_gps.handleCommand = function(command, payload) {
    try {
        switch (command) {

        case 'activate_leg':
            // Go direct to an existing leg in the flight plan by index
            if (_gps.fms && payload && payload.index !== undefined) {
                _gps.fms.activateLeg(0, payload.index);
                _gps.postState('command_ack', { command: command, index: payload.index });
            }
            break;

        case 'create_direct_to':
            // Direct-to any waypoint by ident — does NOT open the G1000 visual dialog
            if (_gps.fms && payload && payload.ident) {
                var ident = payload.ident.toUpperCase().trim();
                if (typeof _gps.fms.createDirectToRandom === 'function') {
                    _gps.fms.createDirectToRandom(ident);
                    _gps.postState('command_ack', { command: command, ident: ident });
                } else {
                    _gps.postState('command_error', { command: command, error: 'createDirectToRandom not available' });
                }
            }
            break;

        case 'cancel_direct_to':
            if (_gps.fms && typeof _gps.fms.cancelDirectTo === 'function') {
                _gps.fms.cancelDirectTo();
                _gps.postState('command_ack', { command: command });
            }
            break;

        case 'load_procedures_list': {
            // Reads available SIDs/STARs/approaches directly from the FMS
            // without requiring the G1000 UI to be on any specific page.
            var icao = payload && payload.ident ? payload.ident.toUpperCase().trim() : '';
            var slot = payload && payload.slot ? payload.slot : 'arr'; // 'dep' or 'arr'

            // Use the destination or origin from the current FPL if no ident given
            if (!icao) {
                try {
                    var fp = _gps.fms.getPrimaryFlightPlan();
                    var raw = slot === 'dep' ? (fp.originAirport || '') : (fp.destinationAirport || '');
                    icao = raw.trim().replace(/\x00/g,'').replace(/^[AVWNRU]\s+/,'').trim().substring(0,4);
                } catch(e) {}
            }

            if (!icao) {
                _gps.postState('command_error', { command: command, error: 'No airport ICAO' });
                break;
            }

            // Build padded MSFS ICAO: type + region (EG for UK) + padding + ident
            // Try both the full padded format and plain ident via searchByIdent
            var facType = (msfssdk && msfssdk.FacilityType) ? msfssdk.FacilityType.Airport : 'LOAD_AIRPORT';

            var tryLoad = function(icaoStr) {
                return _gps.fms.facLoader.getFacility(facType, icaoStr);
            };

            // First try: plain 4-char ident padded to MSFS format
            var paddedIcao = 'A      ' + icao + ' ';

            tryLoad(paddedIcao).then(function(fac) {
                return fac;
            }).catch(function() {
                // Fallback: try just the plain ident
                return tryLoad(icao);
            }).then(function(fac) {
                if (!fac) throw new Error('facility not found: ' + icao);
                var stripIcao = function(s) { return (s||'').trim().replace(/\x00/g,'').replace(/^[AVWNRU]\s+/,'').trim(); };
                var mapProc = function(arr, extraFn) {
                    return (arr||[]).map(function(p, i) {
                        var e = { index: i, name: p.name || ('Proc ' + i), transitions: [], runways: [] };
                        if (extraFn) extraFn(p, e);
                        (p.enRouteTransitions||p.transitions||[]).forEach(function(t,j){ e.transitions.push({index:j, name:t.name||('Trans '+j)}); });
                        (p.runwayTransitions||[]).forEach(function(r,j){ e.runways.push({index:j, name:r.runwayDesignation||r.name||('Rwy '+j)}); });
                        return e;
                    });
                };
                _gps.postState('facility_data', {
                    ident:      stripIcao(fac.icao),
                    name:       fac.name || icao,
                    slot:       slot,
                    departures: mapProc(fac.departures),
                    arrivals:   mapProc(fac.arrivals),
                    approaches: mapProc(fac.approaches, function(p, e) {
                        if (p.runway) e.runway = p.runway;
                        if (p.approachType !== undefined) e.type = p.approachType;
                    })
                });
            }).catch(function(e) {
                _gps.postState('command_error', { command: command, error: e.message, icao: icao });
            });
            break;
        }

        case 'insert_approach': {
            // Load and arm an approach
            var apIdx = payload && payload.approachIndex !== undefined ? payload.approachIndex : -1;
            var trIdx = payload && payload.transitionIndex !== undefined ? payload.transitionIndex : -1;
            var facSlot = payload && payload.slot ? payload.slot : 'arr';

            if (!_gps._facCache) _gps._facCache = {};
            var cachedFac = _gps._facCache[facSlot];
            if (!cachedFac || apIdx < 0) {
                _gps.postState('command_error', { command: command, error: 'Load procedures list first' });
                break;
            }
            _gps.fms.insertApproach({ facility: cachedFac, approachIndex: apIdx, approachTransitionIndex: trIdx })
                .then(function(r) { _gps.postState('command_ack', { command: command, result: r }); })
                .catch(function(e) { _gps.postState('command_error', { command: command, error: e.message }); });
            break;
        }

        case 'insert_departure': {
            var facSlot = payload && payload.slot ? payload.slot : 'dep';
            var cachedFac = _gps._facCache && _gps._facCache[facSlot];
            if (!cachedFac) { _gps.postState('command_error', { command: command, error: 'Load procedures first' }); break; }
            _gps.fms.insertDeparture({
                facility: cachedFac,
                departureIndex: payload.departureIndex || 0,
                departureRunwayIndex: payload.runwayIndex !== undefined ? payload.runwayIndex : -1,
                enrouteTransitionIndex: payload.transitionIndex !== undefined ? payload.transitionIndex : -1
            });
            _gps.postState('command_ack', { command: command });
            break;
        }

        case 'insert_arrival': {
            var facSlot = payload && payload.slot ? payload.slot : 'arr';
            var cachedFac = _gps._facCache && _gps._facCache[facSlot];
            if (!cachedFac) { _gps.postState('command_error', { command: command, error: 'Load procedures first' }); break; }
            _gps.fms.insertArrival({
                facility: cachedFac,
                arrivalIndex: payload.arrivalIndex || 0,
                enrouteTransitionIndex: payload.transitionIndex !== undefined ? payload.transitionIndex : -1,
                arrivalRunwayIndex: payload.runwayIndex !== undefined ? payload.runwayIndex : -1
            });
            _gps.postState('command_ack', { command: command });
            break;
        }

        case 'gps_button':
            if (payload && payload.event) {
                SimVar.SetSimVarValue('K:' + payload.event, 'number', 0);
                _gps.postState('command_ack', { command: command, event: payload.event });
            }
            break;

        case 'gps_drives_nav':
            SimVar.SetSimVarValue('K:TOGGLE_GPS_DRIVES_NAV1', 'number', 0);
            _gps.postState('command_ack', { command: command });
            break;

        case 'request_state':
            _gps.lastPageHash = '';
            _gps.sendFplState();
            break;

        default:
            _gps.postState('command_error', { command: command, error: 'unknown command' });
        }
    } catch(e) {
        _gps.postState('command_error', { command: command, error: e.message });
    }
};

// Cache facility when loaded (used by insert_approach/departure/arrival)
var _origHandleCmd = _gps.handleCommand;
// Patch facility_data to also cache the fac object
_gps._facCache = {};

// ── Command polling ───────────────────────────────────────────────────────────
_gps.pollCommands = function() {
    _gps.fetchWithTimeout(_gps.SERVER_URL + '/commands', {}, 3000)
        .then(function(r) { return r.json(); })
        .then(function(cmds) {
            if (Array.isArray(cmds)) cmds.forEach(function(c) { _gps.handleCommand(c.command, c.payload); });
        })
        .catch(function() {});
};

// ── Connection management ─────────────────────────────────────────────────────
_gps.tryConnect = function() {
    _gps.fetchWithTimeout(_gps.SERVER_URL + '/ping', {}, 2000)
        .then(function() {
            if (!_gps.serverConnected) {
                _gps.serverConnected = true;
                console.log('[G1000 Bridge v3] connected to MSFS BA at ' + _gps.SERVER_URL);
                if (!_gps._statePollTimer) _gps._statePollTimer = setInterval(_gps.sendFplState, 2000);
                if (!_gps._cmdPollTimer)   _gps._cmdPollTimer   = setInterval(_gps.pollCommands, 400);
                _gps.sendFplState();
            }
        })
        .catch(function() {
            if (_gps.serverConnected) {
                _gps.serverConnected = false;
                if (_gps._statePollTimer) { clearInterval(_gps._statePollTimer); _gps._statePollTimer = null; }
                if (_gps._cmdPollTimer)   { clearInterval(_gps._cmdPollTimer);   _gps._cmdPollTimer   = null; }
            }
        });
};

// ── Init ──────────────────────────────────────────────────────────────────────
_gps.init = function() {
    // Try to find FMS now; retry up to 30 seconds
    var attempts = 0;
    var findTimer = setInterval(function() {
        attempts++;
        if (_gps.findFms()) {
            clearInterval(findTimer);
            console.log('[G1000 Bridge v3] FMS found after ' + attempts + ' attempts');
            _gps._heartbeatTimer = setInterval(_gps.tryConnect, 5000);
            _gps.tryConnect();
        } else if (attempts >= 60) {
            clearInterval(findTimer);
            console.log('[G1000 Bridge v3] FMS not found after 30s');
        }
    }, 500);
};

_gps.init();
console.log('[G1000 Bridge v3] loaded');

} // end double-load guard
} catch(e) { console.error('[G1000 Bridge v3] fatal: ' + e); }
