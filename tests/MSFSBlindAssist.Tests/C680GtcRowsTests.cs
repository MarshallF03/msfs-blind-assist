using MSFSBlindAssist.Aircraft.Citation680;
using System.Windows.Forms;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The touchscreen window's row model and key map, against rows the live agent produced 2026-09-10.</summary>
public class C680GtcRowsTests
{
    private static readonly string[] SpeedBugs =
    {
        "Page: Speed Bugs", "Takeoff", "Landing", "Pilot COM1 Volume | COM1 Freq Push:1-2 Hold:", "[Audio & Radios]", "[Intercom] (disabled)",
        "[All On]", "[All Off] (disabled)", "[V1]", "[110 KT]", "[Back]", "[Home]", "Knobs: COM1 Freq Push:1-2 Hold: / Pilot COM1 Volume"
    };

    [Fact]
    public void ParsesTitleTextButtonsAndKnobs()
    {
        var rows = C680GtcRows.Parse(SpeedBugs);
        Assert.Equal(C680GtcRows.Kind.Title, rows[0].Kind);
        Assert.Equal("Speed Bugs", rows[0].Label);
        Assert.Equal("Speed Bugs", C680GtcRows.TitleOf(rows));
        Assert.Equal(C680GtcRows.Kind.Text, rows[1].Kind);
        var allOff = rows.Single(r => r.Label == "All Off");
        Assert.Equal(C680GtcRows.Kind.Button, allOff.Kind);
        Assert.False(allOff.Enabled);
        Assert.Equal(3, allOff.ButtonIndex);   // fourth [ ] row → index 3 in the agent's button list
        Assert.Equal(C680GtcRows.Kind.Knobs, rows[^1].Kind);
        Assert.Equal("COM1 Freq Push:1-2 Hold: / Pilot COM1 Volume", rows[^1].Label);
    }

    [Fact]
    public void KeyboardPageIsRecognisedByItsLetters()
    {
        var kb = C680GtcRows.Parse(new[] { "Page: Add Origin", "[A]", "[B]", "[C]", "[Backspace]", "[Enter]" });
        Assert.True(C680GtcRows.IsKeyboardPage(kb));
        var pad = C680GtcRows.Parse(new[] { "Page: Transponder", "[0]", "[9]", "[Enter]" });
        Assert.True(C680GtcRows.IsKeyboardPage(pad));
        Assert.False(C680GtcRows.IsKeyboardPage(C680GtcRows.Parse(SpeedBugs)));
    }

    [Fact]
    public void TypedKeysMapToOnScreenLabels()
    {
        Assert.Equal("K", C680GtcRows.KeyToButtonLabel(Keys.K, keyboardUp: true));
        Assert.Equal("7", C680GtcRows.KeyToButtonLabel(Keys.D7, keyboardUp: true));
        Assert.Equal("7", C680GtcRows.KeyToButtonLabel(Keys.NumPad7, keyboardUp: true));
        Assert.Equal("Backspace", C680GtcRows.KeyToButtonLabel(Keys.Back, keyboardUp: true));
        Assert.Equal("Enter", C680GtcRows.KeyToButtonLabel(Keys.Enter, keyboardUp: true));
        Assert.Null(C680GtcRows.KeyToButtonLabel(Keys.K, keyboardUp: false));
        Assert.Null(C680GtcRows.KeyToButtonLabel(Keys.Control | Keys.K, keyboardUp: true));   // chords are never typing
    }

    [Fact]
    public void KnobChordsMapToTheVerticalGtcEvents()
    {
        Assert.Equal("RightKnob_Small_INC", C680GtcRows.KeyToKnob(Keys.Control | Keys.Right));
        Assert.Equal("RightKnob_Large_DEC", C680GtcRows.KeyToKnob(Keys.Control | Keys.Down));
        Assert.Equal("RightKnob_Push", C680GtcRows.KeyToKnob(Keys.Control | Keys.Enter));
        Assert.Equal("RightKnob_Push_Long", C680GtcRows.KeyToKnob(Keys.Control | Keys.Shift | Keys.Enter));
        Assert.Equal("MiddleKnob_INC", C680GtcRows.KeyToKnob(Keys.Alt | Keys.Up));
        Assert.Equal("MiddleKnob_Push", C680GtcRows.KeyToKnob(Keys.Alt | Keys.Enter));
        Assert.Equal("Joystick_Left", C680GtcRows.KeyToKnob(Keys.Control | Keys.Shift | Keys.Left));
        Assert.Null(C680GtcRows.KeyToKnob(Keys.Right));
        Assert.Equal("right knob small increase", C680GtcRows.DescribeKnob("RightKnob_Small_INC"));
    }
}
