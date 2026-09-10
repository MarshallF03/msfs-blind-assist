using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// The Sovereign's hotkeys on the house layout. Input mode: Shift+M the MFD touchscreen and
/// Ctrl+Shift+R the PFD touchscreen, both on the Crew Seat; Ctrl+Shift+M and Alt+Shift+R the
/// other seat's. Output mode: Ctrl+Shift+C opens the MFD touchscreen on its Checklist page.
/// </summary>
public partial class SkywardC680Definition
{
    private bool HandleC680Hotkey(HotkeyAction action, SimConnectManager sc, ScreenReaderAnnouncer ann, Form parent, HotkeyManager hk)
    {
        switch (action)
        {
            case HotkeyAction.ShowFenixMCDU: hk.ExitInputHotkeyMode(); ShowGtc(isMfd: true, CurrentSeat, ann); return true;
            case HotkeyAction.ShowRMP: hk.ExitInputHotkeyMode(); ShowGtc(isMfd: false, CurrentSeat, ann); return true;
            case HotkeyAction.ShowC680OtherMfdTouchscreen: hk.ExitInputHotkeyMode(); ShowGtc(isMfd: true, C680Seat.Other(CurrentSeat), ann); return true;
            case HotkeyAction.ShowC680OtherPfdTouchscreen: hk.ExitInputHotkeyMode(); ShowGtc(isMfd: false, C680Seat.Other(CurrentSeat), ann); return true;
            case HotkeyAction.ShowChecklistECL: hk.ExitOutputHotkeyMode(); ShowGtc(isMfd: true, CurrentSeat, ann, openPage: "Checklist"); return true;
            case HotkeyAction.FCUSetAutopilot: hk.ExitInputHotkeyMode(); ShowAutopilotWindow(sc, ann); return true;
        }
        return false;
    }

    private void ShowGtc(bool isMfd, C680Seat.Side seat, ScreenReaderAnnouncer ann, string? openPage = null)
    {
        string id = (isMfd ? "MFD" : "PFD") + (int)seat;
        ShowWindow(id, () => new Forms.Citation680.C680GtcForm(isMfd, seat, ann, _ => { }));
        if (openPage != null && _windows.TryGetValue(id, out var w) && w is Forms.Citation680.C680GtcForm f) f.OpenPageWhenReady(openPage);
    }
}
