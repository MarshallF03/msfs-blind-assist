# Skyward Citation Sovereign+ (C680) — the variable surface

Companion to [citation680.md](citation680.md). Two halves: the **names** (what exists and how
it groups, from the package XML) and the **measurements** (encodings and behaviour, read live
at the gate with the aircraft powered up through its own switches). Everything in the measured
sections was read or written against the running aircraft; nothing is inferred from a name.

## Method — names

```bash
PKG="$LOCALAPPDATA/Packages/Microsoft.Limitless_8wekyb3d8bbwe/LocalCache/Packages/Community/skyward-cessna-citation-c680"
grep -aohE 'L:[A-Za-z0-9_:. -]+' "$PKG"/SimObjects/Airplanes/cessna-citation-c680/model/*.xml \
     "$PKG"/html_ui/Pages/VCockpit/Instruments/C680/*/*.js \
     "$PKG"/SimObjects/Airplanes/cessna-citation-c680/panel/Instruments/G3000/Plugins/*.js | sed -E 's/[ ,)].*$//' | sort -u   # 792 names
```

⚠️ The MobiFlight enumerator cannot list these: GSX's thousand-plus L:vars crowd them out of its
1000-name cap. Enumerate from the XML; read by name, through a Coherent view
(`tools/coherent-eval.ps1 -Title WTG3000_PFD_1 -ExprFile …`).

## Transports — measured 2026-09-09

| Control kind | Variable | Write that works | Notes |
|---|---|---|---|
| Vendor push button / switch | `L:SW_SOV_<name>` 0/1 | `SetLVar` (calculator path) | Sticks and drives the downstream effect (`SW_SOV_AI_PITOT_L` → `PITOT HEAT SWITCH:1`). |
| BATT buttons | `L:SW_SOV_ELEC_BATT_1/2` | `SetLVar` | ⚠️ NOT the stock B:ELECTRICAL_Battery_n events: those set `ELECTRICAL MASTER BATTERY:n` (25.4 V) but the plugin's own electrical model closes nothing from them — avionics stayed dark until the L:vars were written, then `SW_SOV_AVIONICS_POWER_ACTIVE` went 1 and the PFD's `#Electricity` state "on". |
| AVN, ELEC, TRU, INTERIOR, EMER LTS | `L:SW_SOV_ELEC_AVN_1/2`, `_ELEC_L/R`, `_TRU_1/2`, `_CABIN_PWR`, `SW_SOV_SAFETY_LIGHTS_POSITION` | `SetLVar` | |
| Stock main-bus SimVars | `ELECTRICAL MAIN BUS VOLTAGE:n`, `BATTERY BUS VOLTAGE`, `AVIONICS BUS VOLTAGE` | read-only | ⚠️ Read 0 V with the aircraft fully lit: the plugin does not write them. Bus health comes from `SW_SOV_*` flags and the EIS electrical group, not from these. |
| APU knob | `L:XMLVAR_APU_StarterKnob_Pos` 0 Off / 1 On / 2 Start | knob 1; then 2 + `(>K:APU_STARTER)`; back to 1 after 1 s | RPM 17 → 63 → 100 % within 15 s. The knob L:var write sticks. |
| Generators (L, R, APU) | `LINE CONNECTION ON:409` / `:86` / `:639` (the plugin's own bus lines: L GEN bus 2 → L main, R GEN bus 2 → R main, APU bus 2 → L emer) | `1 1 (>K:2:ALTERNATOR_SET)`, `2 1 (>K:2:ALTERNATOR_SET)`, `3 1 (>K:2:APU_GENERATOR_SWITCH_SET)` (0 for off) | ⚠️ The vendor plugin INTERCEPTS these events with passthrough off and keeps the switch position in a JS subject, so the stock `ELECTRICAL GENERATOR SWITCH:n`, `APU GENERATOR SWITCH`, `APU GENERATOR ACTIVE`, `APU VOLTS` and `ELECTRICAL GENALT BUS *` SimVars never move (measured: all constant while the APU generator went 105 A → 0.1 A → 105 A). The only readable state is the bus line the plugin closes: `LINE CONNECTION ON:639` with `ELECTRICAL GENERATOR AMPS:3`. The plugin puts the APU generator on line only with `APU PCT RPM` > 99.5, the LEFT generator off line (its line 409 open) and no engine start in progress; with the engines running the APU GEN switch does nothing, by the aircraft's own rule. External power is line 637, the bus tie line 473, the TRUs lines 479 / 810. The `L:SW_SOV_*_GEN_CONN` mirrors are the 2020 path and stay 0 on 2024. |
| Standby power | `ELECTRICAL MASTER BATTERY:3` | `3 (>K:TOGGLE_MASTER_BATTERY)` | The stock STBY battery template; `L:XMLVAR_BATTERYSTBY_SWITCHSTATE` writes do not stick (the template owns it). Battery 3 read 1 at load. |
| Beacon / nav | `LIGHT BEACON`, `LIGHT NAV` | `K:TOGGLE_BEACON_LIGHTS` / `K:TOGGLE_NAV_LIGHTS` | Beacon 1 → 0 → 1 measured. |
| Lighting knobs | `L:LIGHTING_Knob_Panel_raw` … 0–100 | `SetLVar` | 90 → 40 → 90 measured. |
| Run/stop | `GENERAL ENG FUEL VALVE:n` | `(>B:FUEL_RunStop_n_Toggle)` | 1 → 0 → 1 measured (assessment). |
| Autopilot values | stock | `HEADING_BUG_SET`, `AP_ALT_VAR_SET_ENGLISH`, `XPNDR_SET` | measured (assessment). |
| GTC touchscreens | Coherent DOM | mousedown/mouseup/click on `.touch-button` / `.bg-img-touch-button`; knobs `H:AS3000_TSC_Vertical_n_RightKnob_{Small,Large}_{INC,DEC}`, `RightKnob_Push`, `MiddleKnob_{INC,DEC,Push}`, `Joystick_*` | Origin KSAT entered, COM1 122.50 set, squawk 1200 set, knob stepped 122.50 → 122.525 → 122.55 (assessment). |
| Vendor EFB | Coherent DOM | `.click()` on `input[type=checkbox]` / `.settings-selector-btn` | chocks 0 → 1 → 0, Meter Overlay 0 → 1 → 0 (assessment). |
| Bus tie / ext power | `L:SW_SOV_ELEC_BUS_TIE_EMIS`, `L:SW_SOV_EXT_GEN_CONN` | `(>H:SW_SOV_ELEC_BUS_TIE)` / `(>H:SW_SOV_ELEC_EXT_PWR)` | The plugin's handler returns early ON THE GROUND for bus tie; the tie closed itself when the APU generator came on line (`SW_SOV_BUS_TIE_CONN` 1, CAS "BUS TIE CLOSED"). |

## CAS — the PFD list (measured with the aircraft lit)

Container `.full-cas-display-2-list`, rows `.cas-display-2-msg`; a visible row carries
`cas-display-2-msg-visible` and its severity as `cas-display-2-msg-advisory` (caution / warning
by the same pattern, matching the scroll-bar shading classes `cas-scroll-bar-shading-caution` /
`-warning`); an empty slot is `display: none`. Seen at the gate on batteries and APU: NO TAKEOFF,
CONTROL LOCK ON, ENGINE SHUTDOWN L (advisory), earlier BUS TIE CLOSED and, with the batteries
off, PARK BRAKE ON, P/S HEAT ON, FUEL LEVEL LOW L-R, FUEL BST PUMP ON L-R.

## Measured, panel by panel

Each panel task appends its rows here: `Control | Variable | Encoding / behaviour (measured)`.

### Electrical (2026-09-09, cold and dark at the gate, KTYQ)

| Control | Variable | Measured |
|---|---|---|
| Left / Right BATT | `L:SW_SOV_ELEC_BATT_1/2` | 0 at load. Write 1 → avionics power active, PFD/MFD/GTC lit within a second. `ELECTRICAL BATTERY VOLTAGE:1` 25.4 V. |
| STBY PWR | `ELECTRICAL MASTER BATTERY:3` | 1 at load. `L:STBY_PWR_LED_AMBER` 1 with the avionics dark, 0 once powered; `_GREEN` 0 throughout. |
| AVN L/R | `L:SW_SOV_ELEC_AVN_1/2` | 0 at load; 1 written; `SW_SOV_AVIONICS_POWER_ACTIVE` 1 only once the batteries' L:vars were 1 too. |
| ELEC L/R | `L:SW_SOV_ELEC_ELEC_L/R` | 1 (Norm) at load. |
| APU GEN | `APU GENERATOR SWITCH` | see Transports. |
| BUS TIE | `L:SW_SOV_BUS_TIE_CONN` 1, `L:SW_SOV_ELEC_BUS_TIE_EMIS` 1 | closed automatically with the APU generator. |
| INTERIOR | `L:SW_SOV_ELEC_CABIN_PWR` | 1 written, sticks. |

### APU

| Control | Variable | Measured |
|---|---|---|
| APU knob | `L:XMLVAR_APU_StarterKnob_Pos` | see Transports; `APU PCT RPM` 100 held for 20 s. `APU SWITCH` 1. |
| APU BLEED AIR | `L:SW_SOV_APU_BLEED` | 1 written, sticks; `L:ELECTRICAL_APU_Bleed` stayed 0 (the vendor's bleed logic uses its own duct flags). |

### Exterior lights and anti-ice

| Control | Variable | Measured |
|---|---|---|
| TAXI | `L:SW_SOV_LIGHTS_TAXI` → `LIGHT TAXI` | followed with the aircraft powered (2026-09-09). |
| Left PITOT/STATIC | `L:SW_SOV_AI_PITOT_L` → `PITOT HEAT SWITCH:1` | 1 → 0 → 1 followed (assessment, batteries only). |

### Right Tilt (2026-09-10, engines running at the gate)

| Control | Variable | Measured |
|---|---|---|
| PRESS SOURCE knob | `L:SW_SOV_PRESS_SRC` | 2 at load; 3 written and read back, 2 restored. 5 positions. |
| L / R BLEED AIR knobs | `L:SW_SOV_L_BLEED_AIR`, `_R_` | 2 at load; 3 written and read back. 4 positions. |
| CABIN ALT switch | `L:SW_SOV_CABIN_ALT_SWITCH_POS` | -1 / 0 / 1 (the model's STATE tests), 0 at rest. |
| Pressurization rate knob | `L:SW_SOV_PRESSURIZATION_RATE` | 50 at load. `SW_SOV_CABIN_ALT` 764 ft, ducts and ECS active with the APU bleed on. |
| AUX HYD PUMP | `CIRCUIT ON:120` ← `120 (>K:ELECTRICAL_CIRCUIT_TOGGLE)` | 0 → 1 → 0; `SW_SOV_HYD_AUX_PUMP` followed 0 → 1 → 0. `SW_SOV_HYD_PRESSURE` 3000, `_RESERVOIR` 265. |
| BOOST PUMP L | `L:SW_SOV_FUEL_PUMP_1_ON_SWITCH` | 1 → `FUELSYSTEM PUMP ACTIVE:1` 1 → 0 restored. |
| CROSSFEED knob | `L:SW_SOV_FUEL_TRANSFER` | 1 = Off (valve 1 closed). 0 = Left Tank, 2 = Right Tank: the crossfeed valve (`FUELSYSTEM VALVE OPEN:1`) opens to 100 percent and the named side's boost pump runs (position 2: pump 2 active, 35 gph through lines 2 and 9). |
| PASS OXY knob | `L:Mask_Selector_Position` | 1 at load; 0 written and read back. |
| ELT | `ELT ACTIVATED` ← `N (>B:SAFETY_ELT_1_Set)` | `(>B:SAFETY_ELT_1_Toggle)` took it 0 → 1 (transmitting); `0 (>B:SAFETY_ELT_1_Set)` back to 0. No L:var carries the switch position (`L:XMLVAR_ELT_STATE` does not exist). |
| Temperatures | `L:SW_SOV_CKPT_TMP_CUR`, `_CABIN_TMP_CUR` | 21.0 each; `_TMP_SEL` 0; fans 178 / 293. Oxygen gauges 12,726,220 (raw); two bottles fitted. |

### ⚠️ Reading trap — MobiFlight's per-client registration cap

Every distinct expression `execute_calculator_code` READS is registered as a MobiFlight
string-simvar slot on the client; the module caps them, and once the cap is hit every NEW
expression silently returns 0 while old expressions keep answering (measured 2026-09-10: a
dozen fresh reads of live L:vars all returned 0, the identical earlier expression still
returned 1111, and a fresh `(L:SW_SOV_ELEC_BATT_1) 1 +` returned 0 with the battery on).
Writes are unaffected. Reconnecting the client clears it. For bulk reads use the Coherent
debugger (`tools/coherent-eval.ps1 -Title WTG3000_MFD -ExprFile …` with
`SimVar.GetSimVarValue`), which has no such cap and gave every value above.

### Glareshield (2026-09-10, engines running at the gate)

| Control | Variable | Measured |
|---|---|---|
| FD L / R | `AUTOPILOT FLIGHT DIRECTOR ACTIVE:1/2` ← `1 (>K:TOGGLE_FLIGHT_DIRECTOR)` (side as the parameter) | 0 → 1 → 0 on side 1, side 2 untouched. |
| YD | `AUTOPILOT YAW DAMPER` ← `(>K:YAW_DAMPER_TOGGLE)` | 0 → 1 → 0. |
| Heading bug / altitude preselect | `AUTOPILOT HEADING LOCK DIR`, `AUTOPILOT ALTITUDE LOCK VAR` ← `HEADING_BUG_SET`, `AP_ALT_VAR_SET_ENGLISH` | 250 and 15000 read back. |
| AP DISC (yoke) | `L:SW_SOV_AUTOPILOT_Push_Disconnect_1/2_Pressed` | The vendor plugin maps these as its `ap_disc` inputs AND intercepts `AUTOPILOT_OFF` / `AUTOPILOT_DISENGAGE_*` (passthrough off): pulse the L:var and fire `AUTOPILOT_OFF`. |
| AT / AT DISC / TO/GA | `K:AUTO_THROTTLE_ARM` (arms, or deactivates when armed/on), `K:AUTO_THROTTLE_DISCONNECT`, `K:AUTO_THROTTLE_TO_GA` | all intercepted by the plugin's FMS speed manager; status published as `L:SW_SOV_Autothrottle_Status` 0 Off / 1 Disconnected / 2 Armed / 3 On (its `STATUS_SIMVAR_ENUM_MAP`). `L:SW_Sovereign_Autothrottle_Status` also exists in the names but is not the one written. |
| VNAV | `(>H:AS1000_VNAV_TOGGLE)` | the only VNAV key in the G3000's loaded sources; no state var (`L:XMLVAR_VNAVButtonValue` does not exist here) — the armed mode reads on the PFD FMA. |
| Bank limit | — | `1 (>K:AP_MAX_BANK_SET)` and `(>K:AP_MAX_BANK_INC)` both left `AUTOPILOT MAX BANK ID` 0 / 30°; the GMC's half-bank lamp is not published. Not exposed. |
| IAS / Mach | `AUTOPILOT MANAGED SPEED IN MACH` | `AP_MANAGED_SPEED_IN_MACH_ON` (the event the sources name) left it 0 on the ground. Readout only; the touchscreen speed-bug page owns the units. |
| CWS | `L:SW_SOV_AUTOPILOT_CVS` | the yoke buttons toggle it (`! (>L:…)` in the model), so it is a switch, not a pulse. |
| MASTER WARNING / CAUTION | `MASTER WARNING ACTIVE`, `MASTER CAUTION ACTIVE` (+ `… ACKNOWLEDGED`) ← `K:MASTER_WARNING_ACKNOWLEDGE` / `K:MASTER_CAUTION_ACKNOWLEDGE` | the model's own push templates; the `XMLVAR_WARNING_n` names in the model are its O: animation vars, not L:vars. All 0 with no fault present. |
| Fire covers | `L:SAFETY_Push_Extinguisher_1_Cover` | 0 → 1 → 0. |
| Standby QNH unit | `L:SW_SOV_GH3900_QNH` | 0 → 1 → 0; `KOHLSMAN SETTING MB:3` 1013.25. `SW_SOV_GH3900_BL` 0.8 at load. |
