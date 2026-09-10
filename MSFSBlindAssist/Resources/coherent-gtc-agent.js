// coherent-gtc-agent.js — MSFSBA in-page agent for a Working Title G3000 GTC (touchscreen
// controller) view on the Skyward Citation Sovereign+. Installed by CoherentDisplayClient
// through Runtime.evaluate (NO injection, no Community package) and polled through the shared
// __MSFSBA_DISP.scrape() contract.
//
// Measured 2026-09-09/10 on the live aircraft: buttons are .touch-button / .bg-img-touch-button,
// disabled ones carry touch-button-disabled, hidden ones touch-button-hidden or a zero-size
// rect; the page title is the visible .gtc-view-title-inner-text; a click is a
// mousedown / mouseup / click triple at the button's centre (navigation, the keyboard, the
// keypad and the transponder all answer); knobs are the WT H: events
// AS3000_TSC_Vertical_<n>_<Name> (the four Sovereign GTCs are all VERTICAL units).
// ES5 ONLY (Chromium 49). Returns "MSFSBA_DISP_INSTALLED".
(function () {
  "use strict";
  var A = {}; A.VERSION = 1; A._buttons = [];
  function txt(e) { return (e.innerText || e.textContent || "").replace(/\s+/g, " ").trim(); }
  // A zero-size rect OR visibility:hidden (the frequency buttons' digit-entry slots keep their
  // size while hidden) means the pilot cannot see it.
  function visible(e) {
    var r = e.getBoundingClientRect(); if (!(r.width > 0 && r.height > 0)) return false;
    try { return window.getComputedStyle(e).visibility !== "hidden"; } catch (x) { return true; }
  }
  function hasClass(e, c) { return (" " + String(e.className) + " ").indexOf(" " + c + " ") >= 0; }
  function isDisabled(e) { return hasClass(e, "touch-button-disabled"); }
  function isHidden(e) { return hasClass(e, "touch-button-hidden") || !visible(e); }
  function insideButton(e) {
    var p = e.parentNode;
    while (p && p !== document) { if (hasClass(p, "touch-button") || hasClass(p, "bg-img-touch-button")) return true; p = p.parentNode; }
    return false;
  }
  function gtcIndex() { var m = /WTG3000_GTC_(\d)/.exec(document.title || ""); return m ? m[1] : "1"; }
  function main() { return document.querySelector(".gtc-main-content") || document.body; }

  // The title bar keeps one slot per open view (title-1, title-2 …) and its container names
  // the slot in front with show-title-N; only that slot is the current page.
  A.title = function () {
    var bar = document.querySelector(".gtc-view-title");
    if (bar) {
      var m = /show-title-(\d+)/.exec(String(bar.className));
      if (m) { var slot = bar.querySelector(".gtc-view-title-inner-text.title-" + m[1]); if (slot && txt(slot)) return txt(slot); }
    }
    var ts = document.querySelectorAll(".gtc-view-title-inner-text"); var last = "";
    for (var i = 0; i < ts.length; i++) if (visible(ts[i]) && txt(ts[i])) last = txt(ts[i]);
    return last;
  };
  // A button's label with a space between its parts ("COM1 124.850", not "COM1124.850").
  function labelOf(e) {
    var parts = []; var els = e.querySelectorAll("*"); var any = false;
    for (var i = 0; i < els.length; i++) {
      if (els[i].children.length || !visible(els[i])) continue;   // hidden digit placeholders inside the frequency buttons are skipped
      var t = txt(els[i]); if (t) { parts.push(t); any = true; }
    }
    return any ? parts.join(" ").replace(/\s+/g, " ").trim() : txt(e);
  }

  A.buttons = function () {
    var out = []; var bs = document.querySelectorAll(".touch-button, .bg-img-touch-button");
    for (var i = 0; i < bs.length; i++) {
      var e = bs[i]; if (isHidden(e)) continue;
      if (e.querySelector(".touch-button, .bg-img-touch-button")) continue;   // keep the innermost button
      var t = labelOf(e); if (!t) continue;
      out.push({ i: out.length, el: e, label: t, enabled: !isDisabled(e) });
    }
    A._buttons = out; return out;
  };

  A.rows = function () {
    var items = []; var els = main().querySelectorAll("*");
    for (var i = 0; i < els.length; i++) {
      var e = els[i]; if (e.children.length) continue;
      var t = txt(e); if (!t) continue;
      var r = e.getBoundingClientRect(); if (r.width === 0 || r.height === 0) continue;
      if (insideButton(e)) continue;   // buttons are listed separately
      items.push({ t: t, x: Math.round(r.left), y: Math.round(r.top + r.height / 2) });
    }
    items.sort(function (a, b) { return (Math.round(a.y / 14) - Math.round(b.y / 14)) || (a.x - b.x); });
    var lines = []; var cur = null; var cy = -999;
    for (var j = 0; j < items.length; j++) {
      if (Math.abs(items[j].y - cy) > 14) { if (cur !== null) lines.push(cur); cur = items[j].t; cy = items[j].y; }
      else cur += " | " + items[j].t;
    }
    if (cur !== null) lines.push(cur);
    return lines;
  };

  A.knobLabel = function () {
    var d = document.querySelector(".label-bar-label.dual-knob");
    var c = document.querySelector(".label-bar-label.center-knob");
    return (d ? txt(d) : "") + " / " + (c ? txt(c) : "");
  };

  A.scrape = function () {
    try {
      var rows = ["Page: " + A.title()];
      var text = A.rows(); for (var i = 0; i < text.length; i++) rows.push(text[i]);
      var bs = A.buttons(); for (var k = 0; k < bs.length; k++) rows.push("[" + bs[k].label + "]" + (bs[k].enabled ? "" : " (disabled)"));
      rows.push("Knobs: " + A.knobLabel());
      return JSON.stringify({ ok: true, rows: rows });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e), rows: [] }); }
  };

  function clickEl(e) {
    var r = e.getBoundingClientRect(); var x = r.left + r.width / 2, y = r.top + r.height / 2;
    function ev(type) { var m = document.createEvent("MouseEvents"); m.initMouseEvent(type, true, true, window, 1, x, y, x, y, false, false, false, false, 0, null); return m; }
    e.dispatchEvent(ev("mousedown")); e.dispatchEvent(ev("mouseup")); e.dispatchEvent(ev("click"));
  }
  A.click = function (i) { var b = A._buttons[i]; if (!b || !b.el || isHidden(b.el)) return "stale"; clickEl(b.el); return "ok"; };
  A.press = function (label) {
    var bs = A.buttons();
    for (var i = 0; i < bs.length; i++) if (bs[i].enabled && bs[i].label === label) { clickEl(bs[i].el); return "ok"; }
    return "none";
  };
  A.knob = function (name) { var ev = "H:AS3000_TSC_Vertical_" + gtcIndex() + "_" + name; SimVar.SetSimVarValue(ev, "number", 1); return ev; };
  A.state = function () { return JSON.stringify({ title: A.title(), knobs: A.knobLabel(), gtc: gtcIndex() }); };

  window.__MSFSBA_GTC = A; window.__MSFSBA_DISP = A;
  return "MSFSBA_DISP_INSTALLED";
})();
