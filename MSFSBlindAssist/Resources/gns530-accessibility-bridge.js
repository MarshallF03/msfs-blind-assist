// GNS 530 Accessibility Bridge
// Injected into WT530.html via mod package override to expose GPS flight plan
// and navigation data to the MSFS Blind Assist application via HTTP on localhost:19778.
//
// Runs in MSFS Coherent GT (older Chromium). No modern APIs like AbortSignal.timeout().
// Top-level try-catch ensures errors here never break the GPS instrument.
try {

var _gps = {
    SERVER_URL: 'http://localhost:19778',
    COMMAND_POLL_INTERVAL: 500,
    STATE_UPDATE_INTERVAL: 1000,
    HEARTBEAT_INTERVAL: 5000,
    RECONNECT_INTERVAL: 5000,
    instrument: null,
    fms: null,
    planner: null,
    serverConnected: false,
    commandPollTimer: null,
    stateUpdateTimer: null,
    heartbeatTimer: null,
    lastPlanVersion: -1
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
    } catch (e) {
        // Server unreachable
    }
};

_gps.pollCommands = async function() {
    if (!_gps.serverConnected) return;
    try {
        var response = await fetch(_gps.SERVER_URL + '/commands');
        var commands = await response.json();
        for (var i = 0; i < commands.length; i++) {
            _gps.handleCommand(commands[i].command, commands[i].payload);
        }
    } catch (e) {
        // Server unreachable
    }
};

_gps.fetchWithTimeout = function(url, options, timeoutMs) {
    return new Promise(function(resolve, reject) {
        var timer = setTimeout(function() {
            reject(new Error('Request timed out'));
        }, timeoutMs);
        fetch(url, options).then(function(response) {
            clearTimeout(timer);
            resolve(response);
        }).catch(function(err) {
            clearTimeout(timer);
            reject(err);
        });
    });
};

_gps.tryConnect = async function() {
    try {
        var response = await _gps.fetchWithTimeout(_gps.SERVER_URL + '/ping', {}, 2000);
        if (response.ok) {
            if (!_gps.serverConnected) {
                _gps.serverConnected = true;
                console.log('[GNS Bridge] Connected to accessibility server');
                _gps.startPolling();
                _gps.postState('connected');
            }
            return true;
        }
    } catch (e) {
        if (_gps.serverConnected) {
            _gps.serverConnected = false;
            _gps.stopPolling();
            console.log('[GNS Bridge] Disconnected from server');
        }
    }
    return false;
};

_gps.startPolling = function() {
    if (_gps.commandPollTimer) clearInterval(_gps.commandPollTimer);
    if (_gps.stateUpdateTimer) clearInterval(_gps.stateUpdateTimer);
    if (_gps.heartbeatTimer) clearInterval(_gps.heartbeatTimer);
    _gps.commandPollTimer = setInterval(_gps.pollCommands, _gps.COMMAND_POLL_INTERVAL);
    _gps.stateUpdateTimer = setInterval(_gps.sendFlightPlanState, _gps.STATE_UPDATE_INTERVAL);
    _gps.heartbeatTimer = setInterval(function() {
        _gps.postState('heartbeat');
    }, _gps.HEARTBEAT_INTERVAL);
};

_gps.stopPolling = function() {
    if (_gps.commandPollTimer) { clearInterval(_gps.commandPollTimer); _gps.commandPollTimer = null; }
    if (_gps.stateUpdateTimer) { clearInterval(_gps.stateUpdateTimer); _gps.stateUpdateTimer = null; }
    if (_gps.heartbeatTimer) { clearInterval(_gps.heartbeatTimer); _gps.heartbeatTimer = null; }
};

// --- Find the GNS 530 instrument ---

_gps.findInstrument = function() {
    // The WT530 BaseInstrument is registered as a custom element
    // We can find it via the DOM or via the global instrument registry
    try {
        // Try to find the instrument via DOM
        var instruments = document.querySelectorAll('wt530, as530');
        if (instruments.length > 0) {
            return instruments[0];
        }
        // Try BaseInstrument registry
        if (typeof BaseInstrument !== 'undefined' && BaseInstrument.allInstrumentsLoaded) {
            for (var i = 0; i < BaseInstrument.allInstrumentsLoaded.length; i++) {
                var inst = BaseInstrument.allInstrumentsLoaded[i];
                if (inst && inst.mainScreen && inst.mainScreen.instance && inst.mainScreen.instance.fms) {
                    return inst;
                }
            }
        }
    } catch (e) {
        console.log('[GNS Bridge] Error finding instrument: ' + e.message);
    }
    return null;
};

_gps.getFms = function() {
    if (_gps.fms) return _gps.fms;
    try {
        var inst = _gps.findInstrument();
        if (inst) {
            _gps.instrument = inst;
            // The FMS is on the MainScreen component
            if (inst.mainScreen && inst.mainScreen.instance) {
                _gps.fms = inst.mainScreen.instance.fms;
                _gps.planner = inst.mainScreen.instance.planner;
                console.log('[GNS Bridge] FMS found');
                return _gps.fms;
            }
        }
    } catch (e) {
        console.log('[GNS Bridge] Error getting FMS: ' + e.message);
    }
    return null;
};

// --- Flight Plan State Extraction ---

_gps.sendFlightPlanState = function() {
    if (!_gps.serverConnected) return;
    try {
        var fms = _gps.getFms();
        if (!fms) {
            _gps.postState('no_fms');
            return;
        }

        var planner = fms.flightPlanner;
        if (!planner || !planner.hasActiveFlightPlan()) {
            _gps.postState('flight_plan', {
                active: false,
                waypoints: [],
                activeLegIndex: -1,
                directTo: false
            });
            return;
        }

        var plan = planner.getActiveFlightPlan();
        var waypoints = [];
        var activeLegIndex = plan.activeLateralLeg || 0;

        for (var i = 0; i < plan.length; i++) {
            try {
                var leg = plan.tryGetLeg(i);
                if (!leg) continue;

                var wp = {
                    index: i,
                    ident: '???',
                    type: '',
                    lat: 0,
                    lon: 0,
                    distance: 0,
                    dtk: 0,
                    isActive: (i === activeLegIndex),
                    isMissedApproach: false
                };

                // Get waypoint ident from ICAO
                if (leg.leg && leg.leg.fixIcao) {
                    var icao = leg.leg.fixIcao;
                    // ICAO format: first char = type, chars 3-7 = ident (trimmed)
                    if (icao.length >= 7) {
                        wp.ident = icao.substring(7).trim() || icao.substring(3, 7).trim() || icao;
                    } else {
                        wp.ident = icao;
                    }
                    // Type from first char
                    var typeChar = icao.charAt(0);
                    if (typeChar === 'A') wp.type = 'Airport';
                    else if (typeChar === 'V') wp.type = 'VOR';
                    else if (typeChar === 'N') wp.type = 'NDB';
                    else if (typeChar === 'W') wp.type = 'Intersection';
                    else if (typeChar === 'U') wp.type = 'User';
                    else if (typeChar === 'R') wp.type = 'Runway';
                    else wp.type = typeChar;
                }

                // Get name from leg type
                if (leg.name) wp.ident = leg.name;

                // Get calculated data
                if (leg.calculated) {
                    if (leg.calculated.endLat !== undefined) {
                        wp.lat = leg.calculated.endLat;
                        wp.lon = leg.calculated.endLon;
                    }
                    if (leg.calculated.distanceWithTransitions !== undefined) {
                        wp.distance = leg.calculated.distanceWithTransitions;
                    }
                    if (leg.calculated.initialDtk !== undefined) {
                        wp.dtk = leg.calculated.initialDtk;
                    }
                }

                // Check missed approach flag
                if (leg.flags !== undefined) {
                    wp.isMissedApproach = (leg.flags & 64) !== 0; // LegDefinitionFlags.MissedApproach
                }

                waypoints.push(wp);
            } catch (legErr) {
                // Skip problematic legs
                continue;
            }
        }

        // Get Direct-To state
        var directToState = false;
        var directToTarget = '';
        try {
            directToState = fms.getDirectToState() > 0;
            if (directToState) {
                directToTarget = fms.getDirectToTargetIcao() || '';
                if (directToTarget.length >= 7) {
                    directToTarget = directToTarget.substring(7).trim() || directToTarget.substring(3, 7).trim();
                }
            }
        } catch (e) {}

        // Get approach info
        var approachInfo = {
            loaded: false,
            active: false,
            type: '',
            name: '',
            airport: ''
        };
        try {
            var appr = plan.getUserData('approachName');
            if (appr) approachInfo.name = appr;
            // Check via SimVar approach status
            approachInfo.loaded = SimVar.GetSimVarValue('GPS IS APPROACH LOADED', 'bool') > 0;
            approachInfo.active = SimVar.GetSimVarValue('GPS IS APPROACH ACTIVE', 'bool') > 0;
        } catch (e) {}

        // Get origin and destination
        var origin = '';
        var destination = '';
        try {
            if (plan.originAirport) {
                origin = plan.originAirport.substring(7).trim() || plan.originAirport.substring(3, 7).trim();
            }
            if (plan.destinationAirport) {
                destination = plan.destinationAirport.substring(7).trim() || plan.destinationAirport.substring(3, 7).trim();
            }
        } catch (e) {}

        _gps.postState('flight_plan', {
            active: true,
            origin: origin,
            destination: destination,
            waypointCount: waypoints.length,
            activeLegIndex: activeLegIndex,
            waypoints: waypoints,
            directTo: directToState,
            directToTarget: directToTarget,
            approach: approachInfo
        });

        // Also send live navigation data from SimVars
        _gps.sendNavState();

    } catch (e) {
        console.log('[GNS Bridge] Error reading flight plan: ' + e.message);
        _gps.postState('error', { message: e.message });
    }
};

_gps.sendNavState = function() {
    try {
        var nav = {
            nextWpId: SimVar.GetSimVarValue('GPS WP NEXT ID', 'string') || '',
            nextWpDist: SimVar.GetSimVarValue('GPS WP DISTANCE', 'nautical miles') || 0,
            nextWpBearing: SimVar.GetSimVarValue('GPS WP BEARING', 'degrees') || 0,
            nextWpEte: SimVar.GetSimVarValue('GPS WP ETE', 'seconds') || 0,
            dtk: SimVar.GetSimVarValue('GPS WP DESIRED TRACK', 'degrees') || 0,
            xtk: SimVar.GetSimVarValue('GPS WP CROSS TRK', 'nautical miles') || 0,
            courseToSteer: SimVar.GetSimVarValue('GPS COURSE TO STEER', 'degrees') || 0,
            groundSpeed: SimVar.GetSimVarValue('GPS GROUND SPEED', 'knots') || 0,
            groundTrack: SimVar.GetSimVarValue('GPS GROUND TRUE TRACK', 'degrees') || 0,
            isDirectTo: SimVar.GetSimVarValue('GPS IS DIRECTTO FLIGHTPLAN', 'bool') > 0,
            isApproachLoaded: SimVar.GetSimVarValue('GPS IS APPROACH LOADED', 'bool') > 0,
            isApproachActive: SimVar.GetSimVarValue('GPS IS APPROACH ACTIVE', 'bool') > 0,
            gpsDrivesNav: SimVar.GetSimVarValue('GPS DRIVES NAV1', 'bool') > 0,
            prevWpId: SimVar.GetSimVarValue('GPS WP PREV ID', 'string') || '',
            totalEte: SimVar.GetSimVarValue('GPS ETE', 'seconds') || 0,
            totalEta: SimVar.GetSimVarValue('GPS ETA', 'seconds') || 0,
            wpIndex: SimVar.GetSimVarValue('GPS FLIGHT PLAN WP INDEX', 'number') || 0,
            wpCount: SimVar.GetSimVarValue('GPS FLIGHT PLAN WP COUNT', 'number') || 0
        };

        _gps.postState('nav_state', nav);
    } catch (e) {
        // SimVar access may fail before instrument is ready
    }
};

// --- Command Handling ---

_gps.handleCommand = function(command, payload) {
    try {
        switch (command) {
            case 'direct_to':
                // Send GPS Direct-To button event — opens the Direct-To page
                SimVar.SetSimVarValue('K:GPS_DIRECTTO_BUTTON', 'number', 0);
                _gps.postState('command_ack', { command: command });
                break;

            case 'activate_leg':
                // Activate a specific leg in the flight plan
                if (_gps.fms && payload && payload.index !== undefined) {
                    _gps.fms.activateLeg(0, payload.index);
                    _gps.postState('command_ack', { command: command, index: payload.index });
                }
                break;

            case 'cancel_direct_to':
                if (_gps.fms) {
                    _gps.fms.cancelDirectTo();
                    _gps.postState('command_ack', { command: command });
                }
                break;

            case 'gps_drives_nav':
                SimVar.SetSimVarValue('K:TOGGLE_GPS_DRIVES_NAV1', 'number', 0);
                _gps.postState('command_ack', { command: command });
                break;

            case 'obs_toggle':
                SimVar.SetSimVarValue('K:GPS_OBS_BUTTON', 'number', 0);
                _gps.postState('command_ack', { command: command });
                break;

            case 'gps_button':
                // Send any GPS button event
                if (payload && payload.event) {
                    SimVar.SetSimVarValue('K:' + payload.event, 'number', 0);
                    _gps.postState('command_ack', { command: command, event: payload.event });
                }
                break;

            case 'request_state':
                _gps.sendFlightPlanState();
                break;

            default:
                console.log('[GNS Bridge] Unknown command: ' + command);
        }
    } catch (e) {
        _gps.postState('command_error', { command: command, error: e.message });
    }
};

// --- Initialization ---

_gps.init = function() {
    console.log('[GNS Bridge] Initializing GNS 530 accessibility bridge');

    // Try to connect immediately, retry periodically
    _gps.tryConnect();
    setInterval(function() {
        if (!_gps.serverConnected) {
            _gps.tryConnect();
        }
    }, _gps.RECONNECT_INTERVAL);

    // Periodically try to find the FMS (it may not be ready immediately)
    var fmsCheckInterval = setInterval(function() {
        if (_gps.getFms()) {
            console.log('[GNS Bridge] FMS ready, flight plan access available');
            clearInterval(fmsCheckInterval);
            // Send initial state
            if (_gps.serverConnected) {
                _gps.sendFlightPlanState();
            }
        }
    }, 2000);
};

// Start after a short delay to let the instrument initialize
setTimeout(_gps.init, 3000);

} catch (e) {
    console.error('[GNS Bridge] Fatal error: ' + e.message);
}
