// GNS 530 Accessibility Bridge — v2
// Injected into WT530.html via mod package override.
// Provides full mirror of GPS screen and bidirectional command routing via
// HTTP on localhost:19778 + diagnostic L-vars (readable even when HTTP fails).
//
// Runs in MSFS Coherent GT (older Chromium). No modern APIs like AbortSignal.timeout().
// Top-level try-catch ensures errors here never break the GPS instrument.
try {

// IMMEDIATE: Write loaded L-var before anything else so we can confirm script executed
try { SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_Loaded', 'number', 1); } catch (e0) {}
try { SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_InstrumentType', 'number', 1); } catch (e0) {}

var _gps = {
    VERSION: '2.0.0',
    SERVER_URL: 'http://localhost:19778',
    COMMAND_POLL_INTERVAL: 500,
    STATE_UPDATE_INTERVAL: 1000,
    HEARTBEAT_INTERVAL: 5000,
    RECONNECT_INTERVAL: 5000,
    READY_CHECK_INTERVAL: 500,
    LVAR_WRITE_INTERVAL: 1000,

    // Instrument refs
    instrument: null,
    mainScreen: null,
    fms: null,
    planner: null,
    pageContainer: null,
    bus: null,
    instrumentType: 1,  // 1=GNS530, 2=GNS430, 3=G1000MFD

    // State tracking
    scriptLoaded: true,
    fmsFound: false,
    serverConnected: false,
    lastErrorCode: 0,
    lastPostTime: 0,
    lastPageHash: '',

    // Timers
    readyCheckTimer: null,
    commandPollTimer: null,
    stateUpdateTimer: null,
    heartbeatTimer: null,
    lvarWriteTimer: null
};

// --- Diagnostic L-vars (work without HTTP) ---

_gps.writeDiagnosticLVars = function() {
    try {
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_Loaded', 'number', 1);
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_FmsFound', 'number', _gps.fmsFound ? 1 : 0);
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_ServerConnected', 'number', _gps.serverConnected ? 1 : 0);
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_ErrorCode', 'number', _gps.lastErrorCode);
        SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_InstrumentType', 'number', _gps.instrumentType);
    } catch (e) {}
};

// --- HTTP Communication ---

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
                console.log('[GPS Bridge] Connected to accessibility server');
                _gps.startPolling();
                _gps.postState('connected', {
                    version: _gps.VERSION,
                    instrumentType: _gps.instrumentType
                });
                _gps.sendPageState();
            }
            return true;
        }
    } catch (e) {
        if (_gps.serverConnected) {
            _gps.serverConnected = false;
            _gps.stopPolling();
            _gps.lastErrorCode = 4;
            console.log('[GPS Bridge] Disconnected from server');
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
    _gps.heartbeatTimer = setInterval(function() {
        _gps.postState('heartbeat');
    }, _gps.HEARTBEAT_INTERVAL);
};

_gps.stopPolling = function() {
    if (_gps.commandPollTimer) { clearInterval(_gps.commandPollTimer); _gps.commandPollTimer = null; }
    if (_gps.stateUpdateTimer) { clearInterval(_gps.stateUpdateTimer); _gps.stateUpdateTimer = null; }
    if (_gps.heartbeatTimer) { clearInterval(_gps.heartbeatTimer); _gps.heartbeatTimer = null; }
};

// --- Readiness Chain ---

_gps.findInstrument = function() {
    try {
        // Method 1: BaseInstrument.allInstruments
        if (typeof BaseInstrument !== 'undefined' && BaseInstrument.allInstruments) {
            for (var i = 0; i < BaseInstrument.allInstruments.length; i++) {
                var inst = BaseInstrument.allInstruments[i];
                if (inst && (inst.templateID === 'AS530' || inst.templateID === 'AS430' ||
                    (inst.constructor && (inst.constructor.name === 'WT530' || inst.constructor.name === 'WT430')))) {
                    _gps.instrumentType = (inst.templateID === 'AS430' || inst.constructor.name === 'WT430') ? 2 : 1;
                    return inst;
                }
            }
        }
        // Method 2: allInstrumentsLoaded
        if (typeof BaseInstrument !== 'undefined' && BaseInstrument.allInstrumentsLoaded) {
            for (var j = 0; j < BaseInstrument.allInstrumentsLoaded.length; j++) {
                var inst2 = BaseInstrument.allInstrumentsLoaded[j];
                if (inst2 && inst2.mainScreen) return inst2;
            }
        }
        // Method 3: DOM query
        var el = document.querySelector('wt530, as530, wt-gns530');
        if (el) return el;
    } catch (e) {
        console.log('[GPS Bridge] findInstrument error: ' + e.message);
    }
    return null;
};

_gps.checkReady = function() {
    try {
        if (!_gps.instrument) {
            var inst = _gps.findInstrument();
            if (!inst) { _gps.lastErrorCode = 1; return false; }
            _gps.instrument = inst;
            console.log('[GPS Bridge] Instrument found: ' + (inst.templateID || (inst.constructor && inst.constructor.name)));
        }

        if (!_gps.mainScreen) {
            if (_gps.instrument.mainScreen && _gps.instrument.mainScreen.instance) {
                _gps.mainScreen = _gps.instrument.mainScreen.instance;
                console.log('[GPS Bridge] MainScreen instance ready');
            } else {
                return false;
            }
        }

        if (!_gps.fms) {
            if (_gps.mainScreen.fms) {
                _gps.fms = _gps.mainScreen.fms;
                _gps.planner = _gps.mainScreen.planner;
                _gps.bus = _gps.mainScreen.props ? _gps.mainScreen.props.bus : null;
                _gps.fmsFound = true;
                _gps.lastErrorCode = 0;
                console.log('[GPS Bridge] FMS and FlightPlanner ready');
            } else {
                _gps.lastErrorCode = 2;
                return false;
            }
        }

        if (!_gps.pageContainer) {
            if (_gps.mainScreen.pageContainer && _gps.mainScreen.pageContainer.instance) {
                _gps.pageContainer = _gps.mainScreen.pageContainer.instance;
                console.log('[GPS Bridge] PageContainer ready — bridge fully initialized');
                _gps.subscribeToEvents();
            } else {
                _gps.lastErrorCode = 3;
                return false;
            }
        }

        return true;
    } catch (e) {
        console.log('[GPS Bridge] checkReady error: ' + e.message);
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
        console.log('[GPS Bridge] Event subscriptions set up');
    } catch (e) {
        console.log('[GPS Bridge] subscribeToEvents error: ' + e.message);
    }
};

// --- Helpers ---

_gps.unpackArray = function(arraySubject) {
    try {
        if (!arraySubject) return [];
        if (typeof arraySubject.getArray === 'function') return arraySubject.getArray();
        if (Array.isArray(arraySubject)) return arraySubject;
        return [];
    } catch (e) {
        return [];
    }
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
    } catch (e) {
        return '';
    }
};

_gps.getPageTypeName = function(page) {
    try {
        if (!page) return '';
        if (page.constructor && page.constructor.name) return page.constructor.name;
        return '';
    } catch (e) { return ''; }
};

_gps.getCurrentPageEntry = function() {
    try {
        if (!_gps.pageContainer || !_gps.pageContainer.pageStack) return null;
        var stack = _gps.pageContainer.pageStack;
        if (stack.length === 0) return null;
        return stack[stack.length - 1];
    } catch (e) { return null; }
};

// --- Page content extractors ---

_gps.extractFPLPage = function() {
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
                    if (leg.calculated.initialDtk !== undefined) {
                        dtk = leg.calculated.initialDtk;
                    }
                }

                var text = ident;
                if (distance > 0) text += '  ' + distance.toFixed(1) + ' NM';
                if (dtk > 0) text += '  ' + Math.round(dtk) + '°';

                items.push({
                    text: text, ident: ident, distance: distance, dtk: dtk,
                    index: i, isActive: (i === activeLeg)
                });
            } catch (legErr) { continue; }
        }

        return { items: items, selected: activeLeg };
    } catch (e) {
        return { items: [], selected: -1, error: e.message };
    }
};

_gps.extractNearestPage = function(page) {
    var items = [];
    try {
        var facilities = page.facilities ? _gps.unpackArray(page.facilities) : [];
        for (var i = 0; i < facilities.length; i++) {
            var f = facilities[i];
            var ident = f.ident || _gps.extractIdent(f.icao);
            items.push({ text: ident, ident: ident, icao: f.icao });
        }
    } catch (e) {}
    return { items: items, selected: -1 };
};

_gps.sendPageState = function() {
    if (!_gps.serverConnected || !_gps.pageContainer) return;
    try {
        var entry = _gps.getCurrentPageEntry();
        if (!entry) {
            _gps.postState('page_state', { groupLabel: '', pageTitle: '', items: [] });
            return;
        }

        var groupLabel = '';
        var pageIndex = 0;
        var page = null;

        if (entry.isDialog && entry.page) {
            groupLabel = 'DIALOG';
            page = entry.page;
        } else if (entry.pageGroup) {
            groupLabel = (entry.pageGroup.props && entry.pageGroup.props.label) ? entry.pageGroup.props.label : '';
            page = entry.pageGroup.currentPage || entry.pageGroup.activePage;
            if (entry.pageGroup.pages && page) {
                pageIndex = entry.pageGroup.pages.indexOf(page);
            }
        } else if (entry.page) {
            page = entry.page;
        }

        var pageType = _gps.getPageTypeName(page);
        var pageTitle = '';
        var items = [];
        var selected = -1;

        if (pageType === 'FPLPage') {
            pageTitle = 'Active Flight Plan';
            var fplData = _gps.extractFPLPage();
            items = fplData.items;
            selected = fplData.selected;
        } else if (pageType.indexOf('Nearest') === 0) {
            pageTitle = pageType.replace('Nearest', 'Nearest ');
            var nrstData = _gps.extractNearestPage(page);
            items = nrstData.items;
        } else if (pageType === 'GnsDirectTo' || pageType.indexOf('DirectTo') >= 0) {
            pageTitle = 'Direct-To';
        } else if (pageType === 'MapPage' || pageType.indexOf('NavMap') >= 0) {
            pageTitle = 'Navigation Map';
        } else if (pageType.indexOf('GpsStatus') >= 0 || pageType.indexOf('GPSStatus') >= 0) {
            pageTitle = 'GPS Status';
        } else if (pageType.indexOf('Proc') === 0) {
            pageTitle = 'Procedures';
        } else if (pageType.indexOf('Waypoint') === 0) {
            pageTitle = 'Waypoint Info';
        } else if (pageType === 'VnavPage' || pageType === 'VNavPage') {
            pageTitle = 'Vertical Navigation';
        } else if (pageType.indexOf('Aux') === 0) {
            pageTitle = 'Auxiliary';
        } else {
            pageTitle = pageType || 'Unknown';
        }

        var pageHash = groupLabel + '|' + pageType + '|' + items.length + '|' + selected;
        if (pageHash === _gps.lastPageHash && (Date.now() - _gps.lastPostTime) < 2000) {
            _gps.sendNavState();
            return;
        }
        _gps.lastPageHash = pageHash;

        _gps.postState('page_state', {
            groupLabel: groupLabel, pageIndex: pageIndex,
            pageTitle: pageTitle, pageType: pageType,
            items: items, selected: selected,
            isDialog: !!entry.isDialog
        });

        _gps.sendNavState();
    } catch (e) {
        console.log('[GPS Bridge] sendPageState error: ' + e.message);
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

// --- Command handling ---

// InteractionEvent enum mapping (from WT530B.js line 1920)
_gps.IE = {
    'LeftKnobPush': 0, 'LeftInnerDec': 1, 'LeftInnerInc': 2, 'LeftOuterDec': 3, 'LeftOuterInc': 4,
    'RightKnobPush': 5, 'RightInnerDec': 6, 'RightInnerInc': 7, 'RightOuterDec': 8, 'RightOuterInc': 9,
    'CLRLong': 10, 'CLR': 11, 'ENT': 12, 'MENU': 13, 'DirectTo': 14,
    'RangeDecrease': 15, 'RangeIncrease': 16,
    'PROC': 17, 'VNAV': 18, 'FPL': 19, 'MSG': 20, 'OBS': 21,
    'NavSwap': 22, 'ComSwap': 23, 'CDI': 24
};

_gps.handleCommand = function(command, payload) {
    try {
        switch (command) {
            case 'interaction_event':
                var eventName = payload && payload.event;
                if (eventName && _gps.IE.hasOwnProperty(eventName)) {
                    if (_gps.pageContainer && typeof _gps.pageContainer.onInteractionEvent === 'function') {
                        _gps.pageContainer.onInteractionEvent(_gps.IE[eventName]);
                        _gps.postState('command_ack', { command: command, event: eventName });
                        setTimeout(_gps.sendPageState, 150);
                    }
                }
                break;

            case 'gps_button':
                var btn = payload && payload.event;
                if (btn) {
                    if (_gps.IE.hasOwnProperty(btn)) {
                        _gps.pageContainer.onInteractionEvent(_gps.IE[btn]);
                    } else {
                        SimVar.SetSimVarValue('K:' + btn, 'number', 0);
                    }
                    _gps.postState('command_ack', { command: command, event: btn });
                    setTimeout(_gps.sendPageState, 150);
                }
                break;

            case 'direct_to':
                if (_gps.pageContainer) {
                    _gps.pageContainer.onInteractionEvent(_gps.IE.DirectTo);
                    _gps.postState('command_ack', { command: command });
                }
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

            case 'request_state':
                _gps.lastPageHash = '';
                _gps.sendPageState();
                break;

            default:
                console.log('[GPS Bridge] Unknown command: ' + command);
        }
    } catch (e) {
        console.log('[GPS Bridge] Command error (' + command + '): ' + e.message);
        _gps.postState('command_error', { command: command, error: e.message });
    }
};

// --- Initialization ---

_gps.init = function() {
    console.log('[GPS Bridge] Initializing v' + _gps.VERSION);

    _gps.writeDiagnosticLVars();
    _gps.lvarWriteTimer = setInterval(_gps.writeDiagnosticLVars, _gps.LVAR_WRITE_INTERVAL);

    _gps.readyCheckTimer = setInterval(function() {
        if (_gps.checkReady()) {
            if (_gps.readyCheckTimer) {
                clearInterval(_gps.readyCheckTimer);
                _gps.readyCheckTimer = null;
            }
            console.log('[GPS Bridge] Ready');
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

} catch (e) {
    console.error('[GPS Bridge] Fatal error: ' + e.message);
    try { SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_ErrorCode', 'number', 99); } catch (e2) {}
}
