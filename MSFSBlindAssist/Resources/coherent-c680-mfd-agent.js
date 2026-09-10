// coherent-c680-mfd-agent.js — MSFSBA in-page agent for the Skyward Citation Sovereign+ MFD's
// engine indication strip. Installed by CoherentDisplayClient through Runtime.evaluate (no
// injection) and read on demand through the shared __MSFSBA_DISP.scrape() contract.
//
// Measured 2026-09-10 on the live aircraft: the strip is .eis; the primary gauges are
// .arc-gauge (title .gauge-title, values .arc-gauge-digital-readout), the secondary rows
// .sec-eng-data-row (.sec-eng-data-title, .sec-eng-data-value), and each system group is a
// *-section carrying an .eis-title-text (TRIM, FUEL QTY, FLAPS, GEAR, APU, HYDRAULICS,
// ELECTRICAL). A group's row is its visible leaf texts read left to right, line by line, so
// the left engine's value comes before the label and the right engine's after it, as on the screen.
// ES5 ONLY (Chromium 49). Returns "MSFSBA_DISP_INSTALLED".
(function () {
  "use strict";
  var A = {}; A.VERSION = 1;
  function txt(e) { return (e.innerText || e.textContent || "").replace(/\s+/g, " ").trim(); }
  function visible(e) {
    var r = e.getBoundingClientRect(); if (!(r.width > 0 && r.height > 0)) return false;
    try { return window.getComputedStyle(e).visibility !== "hidden"; } catch (x) { return true; }
  }
  // Every visible leaf inside `root`, sorted top-to-bottom then left-to-right, joined line by line.
  function leavesInReadingOrder(root, skipText) {
    var items = []; var els = root.querySelectorAll("*");
    for (var i = 0; i < els.length; i++) {
      var e = els[i]; if (e.children.length || !visible(e)) continue;
      var t = txt(e); if (!t || t === skipText) continue;
      var r = e.getBoundingClientRect(); items.push({ t: t, x: Math.round(r.left), y: Math.round(r.top + r.height / 2) });
    }
    items.sort(function (a, b) { return (Math.round(a.y / 10) - Math.round(b.y / 10)) || (a.x - b.x); });
    var lines = []; var cur = null; var cy = -999;
    for (var j = 0; j < items.length; j++) {
      if (Math.abs(items[j].y - cy) > 10) { if (cur !== null) lines.push(cur); cur = items[j].t; cy = items[j].y; }
      else cur += " " + items[j].t;
    }
    if (cur !== null) lines.push(cur);
    return lines.join(", ");
  }
  A.eis = function () {
    try {
      var rows = [];
      // The primary arcs come in pairs (left engine, right engine) per kind; their titles ("N1%",
      // "ITT°C") are separate .gauge-title elements in the strip, so pair them by kind.
      var titles = {}; var tl = document.querySelectorAll(".gauge-title");
      for (var q = 0; q < tl.length; q++) { var tt = txt(tl[q]); if (/^N1/i.test(tt)) titles.n1 = tt; else if (/^ITT/i.test(tt)) titles.itt = tt; }
      var byKind = {}; var order = [];
      var arcs = document.querySelectorAll(".arc-gauge");
      for (var i = 0; i < arcs.length; i++) {
        var g = arcs[i]; var c = String(g.className);
        var kind = /n1-gauge/.test(c) ? "n1" : /itt-gauge/.test(c) ? "itt" : c.replace(/\s+/g, " ").trim();
        var reads = g.querySelectorAll(".arc-gauge-digital-readout"); var val = "";
        for (var k = 0; k < reads.length; k++) { var rv = txt(reads[k]); if (rv) { val = rv; break; } }
        if (!byKind[kind]) { byKind[kind] = []; order.push(kind); }
        byKind[kind].push(val || "--");
      }
      for (var o = 0; o < order.length; o++) rows.push((titles[order[o]] || order[o].toUpperCase()) + " " + byKind[order[o]].join(" "));
      var secs = document.querySelectorAll(".sec-eng-data-row");
      for (var s = 0; s < secs.length; s++) {
        var row = secs[s]; if (!visible(row)) continue;
        var st = row.querySelector(".sec-eng-data-title"); var sn = st ? txt(st) : "";
        var sv = []; var svs = row.querySelectorAll(".sec-eng-data-value");
        for (var v = 0; v < svs.length; v++) if (visible(svs[v])) sv.push(txt(svs[v]));
        if (sn || sv.length) rows.push((sn ? sn + " " : "") + sv.join(" "));
      }
      var titles = document.querySelectorAll(".eis-title-text");
      for (var t = 0; t < titles.length; t++) {
        var el = titles[t]; if (!visible(el)) continue;
        var group = el.parentElement; if (!group) continue;
        rows.push(txt(el) + ": " + leavesInReadingOrder(group, txt(el)));
      }
      return JSON.stringify({ ok: true, rows: rows });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e), rows: [] }); }
  };
  A.scrape = A.eis;
  window.__MSFSBA_C680_EIS = A; window.__MSFSBA_DISP = A;
  return "MSFSBA_DISP_INSTALLED";
})();
