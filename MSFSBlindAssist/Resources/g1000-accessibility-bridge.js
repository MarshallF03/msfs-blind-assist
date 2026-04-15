// G1000 NXi Accessibility Bridge — v2
// Injected into WTG1000_MFD.html via mod package override.
// Reports flight plan + nav state via HTTP on localhost:19778.
// Same protocol as GNS 530 bridge — only one can be active per sim session.

try {

// IMMEDIATE: Write loaded L-var before anything else so we can confirm script executed
try { SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_Loaded', 'number', 1); } catch (e0) {}
try { SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_InstrumentType', 'number', 3); } catch (e0) {}

if (typeof _gps !== 'undefined') {
    console.log('[G1000 Bridge] GPS bridge already loaded by another instrument, skipping');
} else {

var _gps = {
    VERSION: '2.0.0',
    SERVER_URL: 'http://localhost:19778',
    COMMAND_POLL_INTERVAL: 500,
    STATE_UPDATE_INTERVAL: 500,
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
        // Method 1: BaseInstrument.allInstruments (if it exists)
        if (typeof BaseInstrument !== 'undefined' && BaseInstrument.allInstruments) {
            for (var i = 0; i < BaseInstrument.allInstruments.length; i++) {
                var inst = BaseInstrument.allInstruments[i];
                if (inst && (inst.templateID === 'AS1000_MFD' ||
                    (inst.constructor && inst.constructor.name === 'WTG1000_MFD'))) {
                    return inst;
                }
            }
            // If there's only one instrument registered, it's probably us
            if (BaseInstrument.allInstruments.length === 1 && BaseInstrument.allInstruments[0]) {
                return BaseInstrument.allInstruments[0];
            }
        }
        // Method 2: allInstrumentsLoaded
        if (typeof BaseInstrument !== 'undefined' && BaseInstrument.allInstrumentsLoaded) {
            for (var j = 0; j < BaseInstrument.allInstrumentsLoaded.length; j++) {
                var inst2 = BaseInstrument.allInstrumentsLoaded[j];
                if (inst2 && (inst2.fms || inst2.templateID === 'AS1000_MFD')) return inst2;
            }
        }
        // Method 3: DOM query for custom element
        var el = document.querySelector('wtg1000-mfd, [templateid="AS1000_MFD"]');
        if (el) return el;
        // Method 4: DOM query for any BaseInstrument-derived element with an fms property
        var allEls = document.body ? document.body.querySelectorAll('*') : [];
        for (var k = 0; k < allEls.length; k++) {
            if (allEls[k].fms) return allEls[k];
        }
        // Method 5: Global window.AS1000_MFD or similar
        if (typeof window !== 'undefined') {
            if (window.g_modDebugMgr && window.g_modDebugMgr.AddItem) {
                // MSFS has a global debug manager — the instrument might be findable via globals
            }
            // Check all window properties for anything that looks like our instrument
            for (var key in window) {
                try {
                    var val = window[key];
                    if (val && typeof val === 'object' && val.fms && val.bus) {
                        return val;
                    }
                } catch (e) {}
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
            if (!inst) {
                // Check if BaseInstrument exists at all
                if (typeof BaseInstrument === 'undefined') {
                    _gps.lastErrorCode = 10;  // BaseInstrument undefined
                } else if (!BaseInstrument.allInstruments || BaseInstrument.allInstruments.length === 0) {
                    _gps.lastErrorCode = 11;  // BaseInstrument.allInstruments empty
                } else {
                    _gps.lastErrorCode = 1;   // Instrument not found
                }
                return false;
            }
            _gps.instrument = inst;
            console.log('[G1000 Bridge] Instrument found: ' +
                (inst.templateID || (inst.constructor && inst.constructor.name) || 'unknown'));
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
                try { _gps.postStructureProbe(); } catch (e) {}
            } else {
                _gps.lastErrorCode = 2;
                return false;
            }
        }
        return true;
    } catch (e) {
        console.log('[G1000 Bridge] checkReady error: ' + e.message);
        _gps.lastErrorCode = 12;
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
        // Subscribe to viewService page changes — fires whenever the open page changes
        try {
            var vs = _gps.instrument && _gps.instrument.viewService;
            if (vs && vs.openPageKey && typeof vs.openPageKey.sub === 'function') {
                vs.openPageKey.sub(function() {
                    _gps.lastPageHash = '';
                    _gps.sendPageState();
                });
            }
            if (vs && vs.openPage && typeof vs.openPage.sub === 'function') {
                vs.openPage.sub(function() {
                    _gps.lastPageHash = '';
                    _gps.sendPageState();
                });
            }
            if (vs && vs.activeViewKey && typeof vs.activeViewKey.sub === 'function') {
                vs.activeViewKey.sub(function() {
                    _gps.lastPageHash = '';
                    _gps.sendPageState();
                });
            }
            if (vs && vs.activeView && typeof vs.activeView.sub === 'function') {
                vs.activeView.sub(function() {
                    _gps.lastPageHash = '';
                    _gps.sendPageState();
                });
            }
        } catch (e) {}
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
        var extractPath = 0;
        try {
            if (_gps.fms.getPrimaryFlightPlan) {
                plan = _gps.fms.getPrimaryFlightPlan();
                extractPath = 1;
            }
        } catch (e) { extractPath = -1; }
        if (!plan) {
            try {
                plan = _gps.fms.flightPlanner.getActiveFlightPlan();
                extractPath = 2;
            } catch (e) { extractPath = -2; }
        }
        // Report path taken as ErrorCode (debug)
        try { SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_ErrorCode', 'number', 100 + extractPath); } catch (e) {}

        if (!plan) return { items: [], selected: -1 };

        // Report plan length to ErrorCode so we can see it
        try { SimVar.SetSimVarValue('L:MSFSBA_GPSBridge_ErrorCode', 'number', 200 + (plan.length || 0)); } catch (e) {}

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

// Read a MappedSubscribable's current value. Handles several shapes.
_gps._readSub = function(sub) {
    if (!sub) return null;
    try {
        if (typeof sub.get === 'function') return sub.get();
        if (sub.value !== undefined) return sub.value;
        if (sub._value !== undefined) return sub._value;
    } catch (e) {}
    return null;
};

// Extract visible text and list items from a DOM element.
_gps._extractDomContent = function(el) {
    if (!el) return { title: '', lines: [] };
    var title = '';
    try {
        var titleEl = el.querySelector('.page-title, .mfd-page-title, h3, .title');
        if (titleEl && (titleEl.innerText || titleEl.textContent)) {
            title = (titleEl.innerText || titleEl.textContent).trim();
        }
    } catch (e) {}

    var lines = [];
    // Pull text from common list/row patterns first
    try {
        var rowSelectors = [
            '.fpl-lower-container .fpl-leg',
            '.fpl-active-container .fpl-leg',
            '.list-item',
            '.ui-list-item',
            '.menu-item',
            'li',
            'tr',
            '[class*="leg"]',
            '[class*="row"]',
            '[class*="item"]'
        ];
        for (var s = 0; s < rowSelectors.length; s++) {
            var rows = el.querySelectorAll(rowSelectors[s]);
            if (rows && rows.length > 0) {
                for (var r = 0; r < rows.length && r < 200; r++) {
                    var t = rows[r].innerText || rows[r].textContent || '';
                    t = t.replace(/\s+/g, ' ').trim();
                    if (t && t.length > 0 && t.length < 300) lines.push(t);
                }
                if (lines.length > 0) break;
            }
        }
    } catch (e) {}

    // Fallback: innerText preserves visual line breaks better than textContent
    if (lines.length === 0) {
        try {
            var raw = el.innerText || el.textContent || '';
            var parts = raw.split(/\r?\n/);
            for (var p = 0; p < parts.length && lines.length < 60; p++) {
                var t2 = parts[p].replace(/\s+/g, ' ').trim();
                if (t2) lines.push(t2);
            }
        } catch (e) {}
    }
    return { title: title, lines: lines };
};

// Find a visible popup/dialog element if one is on top of the page.
_gps._findActivePopup = function() {
    var selectors = [
        '.popup.open', '.popup-box.open', '.popup-container > *:not([style*="display: none"])',
        '.mfd-popup', '.menu-dialog', '.dialog.open',
        '.view.active:not(.mfd-page)',
        '[class*="Popup"]:not([style*="display: none"])',
        '[class*="Dialog"]:not([style*="display: none"])'
    ];
    for (var i = 0; i < selectors.length; i++) {
        try {
            var el = document.querySelector(selectors[i]);
            if (el && el.offsetParent !== null) return { el: el, selector: selectors[i] };
        } catch (e) {}
    }
    return null;
};

// Walk openViews array and find the topmost visible page/dialog.
// activeViewKey tracks the topmost view including dialogs; openPageKey tracks the base page.
_gps.detectCurrentPage = function() {
    try {
        var inst = _gps.instrument;
        if (!inst) return null;
        var vs = inst.viewService;
        var openPageKey = vs ? _gps._readSub(vs.openPageKey) : null;
        var activeKey = vs ? _gps._readSub(vs.activeViewKey) : null;
        // Prefer activeViewKey — it's the topmost view (dialog or page)
        var visibleKey = activeKey || openPageKey || 'UNKNOWN';
        var isDialog = activeKey && openPageKey && activeKey !== openPageKey;

        // Find the visible DOM element based on the actual G1000 NXi class conventions.
        var contentEl = null;
        var content = { title: '', lines: [] };

        if (isDialog) {
            // Active dialog: find the `.popout-dialog` that's .open (not quickclosed)
            try {
                var dialogs = document.querySelectorAll('.popout-dialog');
                for (var d = dialogs.length - 1; d >= 0; d--) {
                    var dlg = dialogs[d];
                    var dlgCn = (dlg.className || '').toString();
                    if (!/\bopen\b/.test(dlgCn)) continue;
                    if (/quickclosed|closed\b/.test(dlgCn)) continue;
                    if (dlg.offsetParent === null) continue;
                    contentEl = dlg;
                    break;
                }
            } catch (e) {}

            if (contentEl) {
                // Try to get a title from any header element within the dialog
                try {
                    var titleEl = contentEl.querySelector('h1, h2, h3, .dialog-title, .page-title');
                    if (titleEl) content.title = (titleEl.innerText || titleEl.textContent || '').trim();
                } catch (e) {}

                // Extract menu items — try several selector patterns used across dialogs.
                // Also detect which item is currently selected via .highlight-select.
                try {
                    var selectorSets = [
                        '.popout-menu-item',
                        '.mfd-pagemenu-listcontainer > *',
                        '.list-item, .ui-list-item, li',
                        '.menu-item',
                        // Generic: direct children of common list containers
                        '.mfd-proc-options-list > *',
                        '.direct-to-waypoint > *'
                    ];
                    var items = null;
                    for (var ss = 0; ss < selectorSets.length; ss++) {
                        var found = contentEl.querySelectorAll(selectorSets[ss]);
                        if (found && found.length > 0) {
                            // Confirm at least one has non-empty visible text
                            for (var ff = 0; ff < found.length; ff++) {
                                if (found[ff].offsetParent !== null &&
                                    (found[ff].innerText || found[ff].textContent || '').trim().length > 0) {
                                    items = found;
                                    break;
                                }
                            }
                            if (items) break;
                        }
                    }
                    if (items) {
                        for (var mi = 0; mi < items.length && content.lines.length < 100; mi++) {
                            var it = items[mi];
                            if (!it || it.offsetParent === null) continue;
                            var t = (it.innerText || it.textContent || '').replace(/\s+/g, ' ').trim();
                            if (!t || t.length === 0 || t.length >= 200) continue;
                            var isDisabled = /text-disabled/.test((it.className || '').toString());
                            var isSel = false;
                            try {
                                if (/highlight-select/.test((it.className || '').toString())) isSel = true;
                                else if (it.querySelector && it.querySelector('.highlight-select')) isSel = true;
                            } catch(e){}
                            content.lines.push({
                                text: (isDisabled ? '[disabled] ' : '') + t,
                                isSelected: isSel,
                                isDisabled: isDisabled,
                                index: content.lines.length
                            });
                            if (isSel) content.selectedIndex = content.lines.length - 1;
                        }
                    }
                } catch (e) {}

                // If still empty, dump all leaf text within the dialog
                if (content.lines.length === 0) {
                    try {
                        var leafs = contentEl.querySelectorAll('div, span, label, td');
                        var seen = {};
                        for (var li = 0; li < leafs.length && content.lines.length < 80; li++) {
                            var le = leafs[li];
                            if (!le || le.offsetParent === null) continue;
                            if (le.children && le.children.length > 3) continue;
                            var lt = (le.innerText || le.textContent || '').replace(/\s+/g, ' ').trim();
                            if (!lt || lt.length < 2 || lt.length > 160) continue;
                            if (seen[lt]) continue;
                            seen[lt] = true;
                            content.lines.push(lt);
                        }
                    } catch (e) {}
                }
            }
        } else {
            // No dialog: the currently-open MFD page
            try { contentEl = document.querySelector('.mfd-page.open'); } catch (e) {}
            if (contentEl) {
                var pageContent = _gps._extractDomContent(contentEl);
                content.title = pageContent.title;
                content.lines = pageContent.lines;
            }
        }

        var pageTitle = content.title || visibleKey;

        // For the Map page, raw content is noisy. Give a friendly summary.
        var isMapPage = !isDialog && /NavMapPage|Nav(igation)?Page/i.test(visibleKey);
        if (isMapPage) {
            content.lines = [
                'Map view is active.',
                'Press FPL to see the flight plan.',
                'Press Direct-To to enter a waypoint.',
                'Press MENU for options.'
            ];
            pageTitle = 'Navigation / Map';
        }

        return {
            pageKey: visibleKey,
            openPageKey: openPageKey,
            activeViewKey: activeKey,
            pageType: visibleKey,
            pageTitle: pageTitle,
            items: content.lines,
            selectedIndex: (typeof content.selectedIndex === 'number') ? content.selectedIndex : -1,
            isDialog: !!isDialog
        };
    } catch (e) { return null; }
};

// Helper: list keys of an object (name + type), up to N entries
_gps._listKeys = function(obj, limit) {
    var out = [];
    if (!obj) return out;
    try {
        for (var k in obj) {
            try {
                var v = obj[k];
                var t = typeof v;
                var extra = '';
                if (t === 'object' && v !== null) {
                    try {
                        var cn = v.constructor && v.constructor.name;
                        if (cn) extra = ' (' + cn + ')';
                    } catch(e){}
                }
                out.push(k + ':' + t + extra);
            } catch (e) {}
            if (out.length >= (limit || 80)) break;
        }
    } catch(e) {}
    return out;
};

// Post a deeper structure probe so we can map the G1000 MFD internals.
_gps.postStructureProbe = function() {
    try {
        var inst = _gps.instrument;
        var lines = [];

        lines.push('--- instrument (' + (inst ? (inst.constructor && inst.constructor.name) : 'none') + ') ---');
        lines = lines.concat(_gps._listKeys(inst, 100));

        // Interesting sub-objects on the instrument
        var interesting = ['mainScreen', 'systems', 'g1000Systems', 'pluginSystem',
                           'bus', 'baseInstrumentPublisher', 'gnss', 'viewService',
                           'pageManager', 'pages', 'mfdMainPages', 'mfdPages',
                           'controlPublisher', 'menuSystem', 'softKeyMenu',
                           '_children', 'children'];
        for (var i = 0; i < interesting.length; i++) {
            var key = interesting[i];
            var sub = inst ? inst[key] : null;
            if (sub) {
                lines.push('');
                lines.push('--- inst.' + key + ' (' + (sub.constructor && sub.constructor.name) + ') ---');
                lines = lines.concat(_gps._listKeys(sub, 60));
            }
        }

        // Explore viewService more deeply
        try {
            var vs = inst && inst.viewService;
            if (vs) {
                lines.push('');
                lines.push('--- viewService.openPageKey CURRENT VALUE ---');
                var curKey = _gps._readSub(vs.openPageKey);
                lines.push('openPageKey = ' + JSON.stringify(curKey));
                var curPage = _gps._readSub(vs.openPage);
                lines.push('openPage = ' + (curPage && curPage.constructor ? curPage.constructor.name : typeof curPage));
                var curActive = _gps._readSub(vs.activeViewKey);
                lines.push('activeViewKey = ' + JSON.stringify(curActive));

                lines.push('');
                lines.push('--- viewService.registeredViews (Map of pages) ---');
                if (vs.registeredViews && typeof vs.registeredViews.forEach === 'function') {
                    var count = 0;
                    vs.registeredViews.forEach(function(value, key) {
                        if (count++ < 60) {
                            var vname = '';
                            try { vname = (value && value.constructor && value.constructor.name) || typeof value; } catch(e){}
                            lines.push(JSON.stringify(key) + ' => ' + vname);
                        }
                    });
                }

                lines.push('');
                lines.push('--- viewService.pageHistory ---');
                if (Array.isArray(vs.pageHistory)) {
                    for (var ph = 0; ph < vs.pageHistory.length && ph < 20; ph++) {
                        lines.push('[' + ph + '] ' + JSON.stringify(vs.pageHistory[ph]));
                    }
                }

                lines.push('');
                lines.push('--- viewService.fmsEventMap (FMS button → page) ---');
                if (vs.fmsEventMap && typeof vs.fmsEventMap.forEach === 'function') {
                    var fc = 0;
                    vs.fmsEventMap.forEach(function(value, key) {
                        if (fc++ < 30) lines.push(JSON.stringify(key) + ' => ' + JSON.stringify(value));
                    });
                }
            }
        } catch(e) { lines.push('viewService probe error: ' + e.message); }

        // Explore the bus for topic names — look for 'active_page'/'page_' topics
        try {
            if (inst && inst.bus) {
                lines.push('');
                lines.push('--- inst.bus properties ---');
                lines = lines.concat(_gps._listKeys(inst.bus, 40));
                // Try some common G1000 NXi topic publishers
                var topicsTried = [];
                var pubNames = ['ControlPublisher', 'controlPublisher', 'ControlEvents'];
                for (var p = 0; p < pubNames.length; p++) {
                    try {
                        if (typeof inst.bus.getPublisher === 'function' && window[pubNames[p]]) {
                            topicsTried.push(pubNames[p] + ':found');
                        }
                    } catch(e){}
                }
                if (topicsTried.length) lines.push('Publishers tried: ' + topicsTried.join(', '));
            }
        } catch(e){ lines.push('bus probe error: ' + e.message); }

        // Walk the DOM under the instrument element to find page/content elements
        try {
            lines.push('');
            lines.push('--- DOM children of instrument ---');
            var elRoot = null;
            if (inst && inst.tagName) elRoot = inst; // BaseInstrument IS the element
            else if (inst && inst.getAttribute) elRoot = inst;
            if (elRoot && elRoot.querySelectorAll) {
                var uniqueTags = {};
                var elems = elRoot.querySelectorAll('*');
                for (var e = 0; e < elems.length && e < 4000; e++) {
                    var tn = elems[e].tagName ? elems[e].tagName.toLowerCase() : '';
                    if (!uniqueTags[tn]) uniqueTags[tn] = 0;
                    uniqueTags[tn]++;
                }
                var tags = [];
                for (var t in uniqueTags) tags.push(t + ' x' + uniqueTags[t]);
                tags.sort();
                lines = lines.concat(tags.slice(0, 40));
            }
        } catch(e){ lines.push('DOM probe error: ' + e.message); }

        // Look specifically for elements that might represent pages
        try {
            lines.push('');
            lines.push('--- elements matching page/view patterns ---');
            var selectors = ['[class*="page"]', '[class*="Page"]', '[class*="view"]',
                             '[class*="main"]', 'mfd-main-screen', 'mfd-main', 'g1000-pages'];
            for (var s = 0; s < selectors.length; s++) {
                try {
                    var match = document.querySelectorAll(selectors[s]);
                    if (match && match.length) {
                        lines.push(selectors[s] + ': ' + match.length + ' match(es)');
                        for (var m = 0; m < match.length && m < 3; m++) {
                            var el = match[m];
                            lines.push('  [' + m + '] tag=' + el.tagName +
                                ' class="' + (el.className || '') + '"' +
                                ' id="' + (el.id || '') + '"');
                        }
                    }
                } catch(e){}
            }
        } catch(e){ lines.push('selector probe error: ' + e.message); }

        // Dump every currently-visible element under body that looks like a dialog/popup/menu
        try {
            lines.push('');
            lines.push('--- visible dialog-ish elements (current sim state) ---');
            var candidates = document.body ? document.body.querySelectorAll(
                '[class*="dialog"], [class*="Dialog"], [class*="popup"], [class*="Popup"], ' +
                '[class*="menu"], [class*="Menu"], [class*="proc"], [class*="Proc"], ' +
                '[class*="select"], [class*="Select"]'
            ) : [];
            var shown = 0;
            for (var vi = 0; vi < candidates.length && shown < 20; vi++) {
                var ve = candidates[vi];
                if (!ve || ve.offsetParent === null) continue;
                var txt = (ve.innerText || ve.textContent || '').replace(/\s+/g, ' ').trim();
                if (txt.length > 120) txt = txt.substring(0, 120) + '...';
                lines.push('[' + shown + '] <' + ve.tagName.toLowerCase() +
                    '> class="' + (ve.className || '') + '"' +
                    (txt ? ' text="' + txt + '"' : ''));
                shown++;
            }
            if (shown === 0) lines.push('(no visible dialog-ish elements found)');
        } catch (e) { lines.push('dialog probe error: ' + e.message); }

        // Report current viewService state live
        try {
            lines.push('');
            lines.push('--- LIVE view state ---');
            var vs3 = inst && inst.viewService;
            if (vs3) {
                lines.push('openPageKey: ' + JSON.stringify(_gps._readSub(vs3.openPageKey)));
                lines.push('activeViewKey: ' + JSON.stringify(_gps._readSub(vs3.activeViewKey)));
                if (Array.isArray(vs3.openViews)) {
                    lines.push('openViews.length: ' + vs3.openViews.length);
                    for (var ov = 0; ov < vs3.openViews.length && ov < 5; ov++) {
                        var ove = vs3.openViews[ov];
                        var ovkeys = ove ? _gps._listKeys(ove, 15) : [];
                        lines.push('[' + ov + '] ' + ovkeys.join(', '));
                    }
                }

                // Probe methods on viewService, activeView, openPage
                lines.push('');
                lines.push('--- viewService methods ---');
                var vsMethods = [];
                for (var vm in vs3) {
                    try { if (typeof vs3[vm] === 'function') vsMethods.push(vm); } catch(e){}
                }
                lines.push(vsMethods.join(', '));

                var av = _gps._readSub(vs3.activeView);
                lines.push('');
                lines.push('--- activeView methods (' + (av && av.constructor ? av.constructor.name : typeof av) + ') ---');
                if (av) {
                    var avMethods = [];
                    for (var am in av) {
                        try { if (typeof av[am] === 'function') avMethods.push(am); } catch(e){}
                    }
                    // Also include proto methods
                    try {
                        var proto = Object.getPrototypeOf(av);
                        var protoMethods = [];
                        while (proto && proto.constructor && proto.constructor.name !== 'Object') {
                            var pnames = Object.getOwnPropertyNames(proto);
                            for (var pn = 0; pn < pnames.length; pn++) {
                                try {
                                    if (typeof av[pnames[pn]] === 'function' && pnames[pn] !== 'constructor') {
                                        protoMethods.push(pnames[pn]);
                                    }
                                } catch(e){}
                            }
                            proto = Object.getPrototypeOf(proto);
                        }
                        if (protoMethods.length) avMethods.push('[proto: ' + protoMethods.slice(0, 30).join(', ') + ']');
                    } catch(e){}
                    lines.push(avMethods.join(', '));
                }
            }
        } catch (e) {}

        _gps.postState('debug_probe', { text: lines.join('\n') });
    } catch (e) {
        _gps.postState('debug_probe', { text: 'Probe exception: ' + e.message });
    }
};

_gps.sendPageState = function() {
    if (!_gps.serverConnected || !_gps.fms) return;
    try {
        var currentPage = _gps.detectCurrentPage();
        var pageKey = (currentPage && currentPage.pageKey) || 'UNKNOWN';
        var pageTitle = (currentPage && currentPage.pageTitle) || pageKey;

        // Build list items:
        //  - If the page is the flight plan, prefer our structured flight plan extraction
        //    (with distance/bearing/active marker) over raw DOM text.
        //  - Otherwise use DOM text lines from the visible page.
        var items = [];
        var selected = -1;
        // Only use structured flight plan extraction if FPL is the topmost view
        // (not when a dialog is on top of FPL)
        var isFpl = pageKey === 'FPLPage' && !currentPage.isDialog;
        if (isFpl) {
            var fplData = _gps.extractFlightPlan();
            if (fplData.items.length > 0) {
                items = fplData.items.map(function(it) {
                    return { text: it.text, ident: it.ident, index: it.index, isActive: it.isActive };
                });
                selected = fplData.selected;
            }
        }
        if (items.length === 0 && currentPage && currentPage.items) {
            items = currentPage.items.map(function(line, i) {
                if (typeof line === 'string') {
                    return { text: line, index: i, isActive: false };
                }
                // already structured {text, isSelected, ...}
                return {
                    text: line.text,
                    index: (typeof line.index === 'number' ? line.index : i),
                    isActive: !!line.isSelected,
                    isDisabled: !!line.isDisabled
                };
            });
            if (typeof currentPage.selectedIndex === 'number') {
                selected = currentPage.selectedIndex;
            }
        }

        var pageHash = pageKey + '|' + items.length + '|' + selected + '|' + pageTitle;
        if (pageHash === _gps.lastPageHash && (Date.now() - _gps.lastPostTime) < 2000) {
            _gps.sendNavState();
            return;
        }
        _gps.lastPageHash = pageHash;

        _gps.postState('page_state', {
            groupLabel: pageKey,
            pageKey: pageKey,
            pageIndex: 0,
            pageTitle: pageTitle,
            pageType: pageKey,
            items: items,
            selected: selected,
            isDialog: false,
            detectedPage: currentPage ? true : false
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
            case 'probe':
                _gps.postStructureProbe();
                _gps.postState('command_ack', { command: command });
                break;
            case 'interaction_event':
                // G1000 NXi: call viewService.open() directly for page switches,
                // publish fmsEvent on the bus for everything else. This bypasses
                // the H-event path which doesn't cross the instrument boundary.
                if (payload && payload.event) {
                    var inst = _gps.instrument;
                    var vs = inst && inst.viewService;

                    // Map our button names to real viewService page keys
                    var pageMap = {
                        'FPL': 'FPLPage',
                        'PROC': 'PROC',
                        'MENU': 'PageMenuDialog',
                        'DirectTo': 'DirectTo',
                        'VNAV': 'PageMenuDialog',     // G1000 doesn't have VNAV dedicated page on MFD
                        'MSG': 'MessageDialog',
                        'NRST': 'NearestAirports'
                    };
                    // Map our button names to FMS event strings (for knobs/ENT/CLR)
                    var fmsMap = {
                        'ENT': 'Enter',
                        'CLR': 'Clr',
                        'CursorToggle': 'UpperKnobPush',
                        'RightKnobPush': 'UpperKnobPush',
                        'LeftKnobPush': 'UpperKnobPush',
                        'LeftInnerInc': 'LowerKnobInc',
                        'LeftInnerDec': 'LowerKnobDec',
                        'LeftOuterInc': 'UpperKnobInc',
                        'LeftOuterDec': 'UpperKnobDec',
                        'RightInnerInc': 'LowerKnobInc',
                        'RightInnerDec': 'LowerKnobDec',
                        'RightOuterInc': 'UpperKnobInc',
                        'RightOuterDec': 'UpperKnobDec',
                        'RangeIncrease': 'RangeInc',
                        'RangeDecrease': 'RangeDec'
                    };

                    var targetPage = pageMap[payload.event];
                    var fmsEvent = fmsMap[payload.event];
                    var actionsTried = [];
                    var succeeded = false;

                    // Path 1: viewService.open() for page switches
                    if (targetPage && vs && typeof vs.open === 'function') {
                        try {
                            vs.open(targetPage);
                            actionsTried.push('viewService.open("' + targetPage + '")');
                            succeeded = true;
                        } catch (e1) {
                            actionsTried.push('open FAIL: ' + e1.message);
                        }
                    }

                    // Path 2: try multiple ways to deliver FMS events.
                    // Try in order, stop at first that reports success.
                    if (!succeeded && fmsEvent && inst) {
                        var vs2 = inst.viewService;

                        // 2a. Call viewService.onInteractionEvent if it exists
                        try {
                            if (vs2 && typeof vs2.onInteractionEvent === 'function') {
                                vs2.onInteractionEvent([fmsEvent]);
                                actionsTried.push('vs.onInteractionEvent("' + fmsEvent + '")');
                                succeeded = true;
                            }
                        } catch (e2a) { actionsTried.push('vs.onInteractionEvent FAIL: ' + e2a.message); }

                        // 2b. Call activeView.onInteractionEvent — the topmost view usually has this
                        if (!succeeded) {
                            try {
                                var activeV = _gps._readSub(vs2 && vs2.activeView);
                                if (activeV && typeof activeV.onInteractionEvent === 'function') {
                                    activeV.onInteractionEvent([fmsEvent]);
                                    actionsTried.push('activeView.onInteractionEvent("' + fmsEvent + '")');
                                    succeeded = true;
                                }
                            } catch (e2b) { actionsTried.push('activeView.onInteractionEvent FAIL: ' + e2b.message); }
                        }

                        // 2c. Call openPage.onInteractionEvent
                        if (!succeeded) {
                            try {
                                var openP = _gps._readSub(vs2 && vs2.openPage);
                                if (openP && typeof openP.onInteractionEvent === 'function') {
                                    openP.onInteractionEvent([fmsEvent]);
                                    actionsTried.push('openPage.onInteractionEvent("' + fmsEvent + '")');
                                    succeeded = true;
                                }
                            } catch (e2c) { actionsTried.push('openPage.onInteractionEvent FAIL: ' + e2c.message); }
                        }

                        // 2d. Publish on the bus. Try a few topic names.
                        if (!succeeded && inst.bus && typeof inst.bus.pub === 'function') {
                            var topics = ['fmsEvent', 'fms_event', 'mfd_fms_event', 'g1000_fms_event'];
                            for (var tt = 0; tt < topics.length; tt++) {
                                try {
                                    inst.bus.pub(topics[tt], fmsEvent, true, false);
                                    actionsTried.push('bus.pub("' + topics[tt] + '","' + fmsEvent + '")');
                                } catch (e2d) {}
                            }
                            // No way to know which actually worked; mark tried
                            succeeded = true;
                        }
                    }

                    // Path 3: last-resort H-event (original path)
                    if (!succeeded) {
                        var hEventMap = {
                            'FPL': 'AS1000_MFD_FPL_Push',
                            'PROC': 'AS1000_MFD_PROC_Push',
                            'MENU': 'AS1000_MFD_MENU_Push',
                            'DirectTo': 'AS1000_MFD_DIRECTTO',
                            'CLR': 'AS1000_MFD_CLR',
                            'ENT': 'AS1000_MFD_ENT_Push',
                            'RangeIncrease': 'AS1000_MFD_RANGE_INC',
                            'RangeDecrease': 'AS1000_MFD_RANGE_DEC'
                        };
                        var he = hEventMap[payload.event];
                        if (he) {
                            try {
                                if (typeof Coherent !== 'undefined' && Coherent.trigger) {
                                    Coherent.trigger('H:' + he);
                                } else {
                                    SimVar.SetSimVarValue('H:' + he, 'number', 0);
                                }
                                actionsTried.push('H-event "' + he + '"');
                                succeeded = true;
                            } catch (e3) {
                                actionsTried.push('H-event FAIL: ' + e3.message);
                            }
                        }
                    }

                    if (succeeded) {
                        _gps.postState('command_ack', {
                            command: command, event: payload.event,
                            actions: actionsTried.join(' | ')
                        });
                        // Force a page-state resend so the form picks up the new page
                        _gps.lastPageHash = '';
                        setTimeout(function() { _gps.sendPageState(); }, 100);
                    } else {
                        _gps.postState('command_error', {
                            command: command, event: payload.event,
                            error: 'no path worked: ' + actionsTried.join(' | ')
                        });
                    }
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
