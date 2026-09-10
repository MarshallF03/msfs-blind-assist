// coherent-c680-efb-agent.js — MSFSBA in-page agent for the Skyward Citation Sovereign+ vendor
// EFB (the "VCockpit - EFB" view). Installed by CoherentDisplayClient through Runtime.evaluate
// (no injection) and read through the shared __MSFSBA_DISP.scrape() contract.
//
// Measured 2026-09-09/10: pages are #menu-bar .menu-item[data-page] (home, ground, flight,
// checklists, settings) and #page-container .page.active; each page's tabs are .sub-nav-item
// (the active one carries "active"); the Services cards are .ground-service-card with an
// input[type=checkbox]; Settings rows are .settings-option-row with a .settings-option-title
// and either a checkbox or .settings-selector-btn choices (the chosen one carries "active").
// A page or tab is reached by a mousedown/mouseup/click triple; a checkbox or selector button
// by .click() (proven: chocks and Meter Overlay flipped their L:vars).
// ES5 ONLY (Chromium 49). Returns "MSFSBA_DISP_INSTALLED".
(function () {
  "use strict";
  var A = {}; A.VERSION = 1; A._acts = [];
  function txt(e) { return (e.innerText || e.textContent || "").replace(/\s+/g, " ").trim(); }
  function visible(e) {
    var r = e.getBoundingClientRect(); if (!(r.width > 0 && r.height > 0)) return false;
    try { return window.getComputedStyle(e).visibility !== "hidden"; } catch (x) { return true; }
  }
  function hasClass(e, c) { return (" " + String(e.className) + " ").indexOf(" " + c + " ") >= 0; }
  function clickEl(e) {
    var r = e.getBoundingClientRect(); var x = r.left + r.width / 2, y = r.top + r.height / 2;
    function ev(type) { var m = document.createEvent("MouseEvents"); m.initMouseEvent(type, true, true, window, 1, x, y, x, y, false, false, false, false, 0, null); return m; }
    e.dispatchEvent(ev("mousedown")); e.dispatchEvent(ev("mouseup")); e.dispatchEvent(ev("click"));
  }
  function page() { return document.querySelector("#page-container .page.active") || document.querySelector(".page.active") || document.body; }
  // A keypad / keyboard popup ("Set SimBrief User ID": digits, Clear, Cancel, Set ID) sits OUTSIDE
  // the page container; while one is up it is what the pilot must see and press.
  function overlay() {
    var ovs = document.querySelectorAll(".payload-keyboard-overlay, .keyboard-overlay, [class*='keyboard-popup'], [class*='-overlay'], [class*='modal']");
    for (var i = 0; i < ovs.length; i++) if (visible(ovs[i]) && txt(ovs[i])) return ovs[i];
    return null;
  }
  // Buttons here are mostly styled DIVs: anything whose class names a button/btn, plus real <button>s.
  var BTN_CLASS = /(^|\s)([a-z0-9]+-)*(btn|button)(-[a-z0-9-]+)?(\s|$)/i;
  function isButton(e) {
    if (e.tagName === "BUTTON") return true;
    if (e.getAttribute && e.getAttribute("role") === "button") return true;
    var c = String(e.className);
    if (!BTN_CLASS.test(c)) return false;
    if (/selector-btn|menu-item|sub-nav/.test(c)) return false;   // selector choices and nav are handled elsewhere
    return true;
  }
  function inside(e, sel) { var p = e; while (p && p !== document) { if (p.matches && p.matches(sel)) return p; p = p.parentNode; } return null; }

  A.pages = function () {
    var out = []; var mi = document.querySelectorAll("#menu-bar .menu-item");
    for (var i = 0; i < mi.length; i++) out.push({ id: mi[i].getAttribute("data-page") || "", label: txt(mi[i]), active: hasClass(mi[i], "active") });
    return JSON.stringify(out);
  };
  A.goPage = function (id) {
    var mi = document.querySelectorAll("#menu-bar .menu-item");
    for (var i = 0; i < mi.length; i++) if (mi[i].getAttribute("data-page") === id) { clickEl(mi[i]); return "ok"; }
    return "none";
  };
  A.tabs = function () {
    var out = []; var sn = page().querySelectorAll(".sub-nav-item");
    for (var i = 0; i < sn.length; i++) if (visible(sn[i])) out.push({ label: txt(sn[i]), active: hasClass(sn[i], "active") });
    return JSON.stringify(out);
  };
  A.goTab = function (label) {
    var sn = page().querySelectorAll(".sub-nav-item");
    for (var i = 0; i < sn.length; i++) if (visible(sn[i]) && txt(sn[i]) === label) { clickEl(sn[i]); return "ok"; }
    return "none";
  };

  // The active page as rows. Cards and settings rows render as "Label: on/off" or
  // "Label: A / B* / C" and are actionable; buttons as "[Label]"; the rest is text in reading order.
  // A popup as rows: its header, its display value, then its buttons in reading order.
  function scrapeOverlay(ov) {
    var rows = []; var acts = [null];
    var head = ov.querySelector("[class*='header'], [class*='title']"); rows.push("Popup: " + (head ? txt(head) : txt(ov).slice(0, 40)));
    var disp = ov.querySelector("[class*='display']"); if (disp) { rows.push("Entry: " + (txt(disp) || "(empty)")); acts.push(null); }
    var items = []; var els = ov.querySelectorAll("*");
    for (var i = 0; i < els.length; i++) {
      var e = els[i]; if (!visible(e) || !isButton(e)) continue;
      if (e.querySelector && Array.prototype.some.call(e.querySelectorAll("*"), isButton)) continue;
      var t = txt(e); if (!t) continue;
      var r = e.getBoundingClientRect(); items.push({ t: "[" + t + "]", x: Math.round(r.left), y: Math.round(r.top + r.height / 2), act: { el: e, kind: "button" } });
    }
    items.sort(function (a, b) { return (Math.round(a.y / 16) - Math.round(b.y / 16)) || (a.x - b.x); });
    for (var j = 0; j < items.length; j++) { rows.push(items[j].t); acts.push(items[j].act); }
    A._acts = acts;
    return JSON.stringify({ ok: true, rows: rows });
  }
  A.scrape = function () {
    try {
      var ov = overlay(); if (ov) return scrapeOverlay(ov);
      var p = page(); var rows = []; var acts = []; var owned = [];
      var pageName = ""; var mi = document.querySelectorAll("#menu-bar .menu-item.active"); if (mi.length) pageName = txt(mi[0]);
      var tab = ""; var sn = p.querySelectorAll(".sub-nav-item"); for (var s = 0; s < sn.length; s++) if (hasClass(sn[s], "active")) tab = txt(sn[s]);
      rows.push("Page: " + pageName + (tab ? ", " + tab : ""));
      var cards = p.querySelectorAll(".ground-service-card");
      for (var c = 0; c < cards.length; c++) {
        var card = cards[c]; if (!visible(card)) continue; owned.push(card);
        var cb = card.querySelector("input[type=checkbox]");
        var title = card.querySelector(".ground-service-title, .card-title, h3, h4"); var name = title ? txt(title) : txt(card).slice(0, 40);
        if (cb) { acts.push({ el: cb, kind: "checkbox" }); rows.push(name + ": " + (cb.checked ? "on" : "off")); }
        else {
          // The Settings tabs reuse the card markup for account and key cards: the title, then each
          // of the card's buttons ("*" opens the SimBrief ID keypad, "Log In / Go to Charts", "Insert Key here").
          acts.push(null); rows.push(name);
          var cbs = card.querySelectorAll("*");
          for (var q = 0; q < cbs.length; q++) {
            var be = cbs[q]; if (!visible(be) || !isButton(be)) continue;
            if (Array.prototype.some.call(be.querySelectorAll("*"), isButton)) continue;
            var bl = txt(be); if (!bl) continue;
            acts.push({ el: be, kind: "button" }); rows.push("[" + bl + "]");
          }
        }
      }
      var srows = p.querySelectorAll(".settings-option-row");
      for (var r = 0; r < srows.length; r++) {
        var row = srows[r]; if (!visible(row)) continue; owned.push(row);
        var t = row.querySelector(".settings-option-title"); var rn = t ? txt(t) : txt(row).slice(0, 40);
        var rcb = row.querySelector("input[type=checkbox]"); var btns = row.querySelectorAll(".settings-selector-btn");
        if (rcb) { acts.push({ el: rcb, kind: "checkbox" }); rows.push(rn + ": " + (rcb.checked ? "on" : "off")); }
        else if (btns.length) {
          var opts = []; var next = null; var activeIdx = -1;
          for (var b = 0; b < btns.length; b++) { opts.push(txt(btns[b]) + (hasClass(btns[b], "active") ? "*" : "")); if (hasClass(btns[b], "active")) activeIdx = b; }
          next = btns[(activeIdx + 1) % btns.length];
          acts.push({ el: next, kind: "selector" }); rows.push(rn + ": " + opts.join(" / "));
        }
        else { acts.push(null); rows.push(rn + ": " + txt(row).replace(rn, "").trim()); }
      }
      // Everything else: buttons as [Label], leaves as text, in reading order, skipping what the cards/rows already said.
      var items = []; var els = p.querySelectorAll("*");
      for (var i = 0; i < els.length; i++) {
        var e = els[i]; if (!visible(e)) continue;
        var skip = false; for (var o = 0; o < owned.length; o++) if (owned[o] === e || owned[o].contains(e)) { skip = true; break; }
        if (skip) continue;
        if (inside(e, ".sub-nav-item") || inside(e, "#menu-bar")) continue;
        if (isButton(e)) {
          if (Array.prototype.some.call(e.querySelectorAll("*"), isButton)) continue;   // keep the innermost button
          var bt = txt(e); if (!bt) continue; var rect = e.getBoundingClientRect();
          items.push({ t: "[" + bt + "]", x: Math.round(rect.left), y: Math.round(rect.top + rect.height / 2), act: { el: e, kind: "button" } }); continue;
        }
        if (e.children.length) continue;
        var pb = e.parentNode, insideBtn = false; while (pb && pb !== document) { if (pb.nodeType === 1 && isButton(pb)) { insideBtn = true; break; } pb = pb.parentNode; }
        if (insideBtn) continue;
        var lt = e.tagName === "INPUT" ? (e.type === "checkbox" ? (e.checked ? "on" : "off") : "input " + (e.value || "")) : txt(e);
        if (!lt) continue;
        var r2 = e.getBoundingClientRect(); items.push({ t: lt, x: Math.round(r2.left), y: Math.round(r2.top + r2.height / 2), act: e.tagName === "INPUT" && e.type === "checkbox" ? { el: e, kind: "checkbox" } : null });
      }
      items.sort(function (a, b) { return (Math.round(a.y / 16) - Math.round(b.y / 16)) || (a.x - b.x); });
      var cur = null; var cy = -999; var curAct = null;
      function flush() { if (cur !== null) { rows.push(cur); acts.push(curAct); } }
      for (var j = 0; j < items.length; j++) {
        if (Math.abs(items[j].y - cy) > 16 || items[j].act) { flush(); cur = items[j].t; cy = items[j].y; curAct = items[j].act; }
        else cur += " | " + items[j].t;
      }
      flush();
      // Align acts to rows: the first row (Page:) has no action; card/setting rows were pushed in step.
      var aligned = [null]; for (var k = 0; k < acts.length; k++) aligned.push(acts[k]);
      A._acts = aligned;
      return JSON.stringify({ ok: true, rows: rows });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e), rows: [] }); }
  };
  A.act = function (rowIndex) {
    var a = A._acts[rowIndex]; if (!a || !a.el) return "none";
    if (a.kind === "checkbox" || a.kind === "selector") { try { a.el.click(); } catch (x) { clickEl(a.el); } return "ok"; }
    clickEl(a.el); return "ok";
  };
  // Press a button by its label — inside the popup while one is up (so typed digits reach the keypad), else on the page.
  A.press = function (label) {
    var root = overlay() || page(); var bs = root.querySelectorAll("*");
    for (var i = 0; i < bs.length; i++) if (visible(bs[i]) && isButton(bs[i]) && txt(bs[i]) === label) { clickEl(bs[i]); return "ok"; }
    return "none";
  };
  A.hasPopup = function () { return overlay() ? "yes" : "no"; };
  window.__MSFSBA_C680_EFB = A; window.__MSFSBA_DISP = A;
  return "MSFSBA_DISP_INSTALLED";
})();
