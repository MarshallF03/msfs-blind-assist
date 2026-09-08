namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>What kind of bezel key was pressed, which decides what the GNS window says back.</summary>
public enum Lj35GnsKeyKind
{
    /// <summary>A right-knob turn: moves the cursor, changes an entry character, or turns the page.</summary>
    Knob,
    /// <summary>A named button or the cursor push: the layer on top may have changed.</summary>
    Button,
    /// <summary>A left-knob turn: the standby frequency of the selected radio changed.</summary>
    Radio,
}

/// <summary>
/// The spoken feedback after a GNS bezel key, composed from the agent's state string.
///
/// The window used to announce the KEY ("next page", "enter") and leave the pilot to
/// re-read the list to learn what it did. The agent now reports what is on the screen
/// after the press — "ok|kind|context|cursor line|entry char|tuning" — and this picks
/// the one thing worth saying for that key: the character under the cursor after a
/// knob turn in an ident entry, the highlighted row after a knob turn in a list, the
/// page or dialog after a button, the standby frequency after a radio knob.
/// </summary>
public static class Lj35GnsSpeech
{
    public sealed record State(string Kind, string Context, string Cursor, string EntryChar, string Tuning);

    /// <summary>Parses the agent's "ok|kind|context|cursor|entry|tuning" string; null when it is anything else.</summary>
    public static State? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split('|');
        if (parts.Length < 6 || parts[0] != "ok") return null;
        return new State(parts[1].Trim(), parts[2].Trim(), parts[3].Trim(), parts[4].Trim(), parts[5].Trim());
    }

    /// <summary>
    /// What to say after a key. <paramref name="fallback"/> is the key's own name, spoken
    /// only when the display could not be read.
    /// </summary>
    public static string Compose(Lj35GnsKeyKind kind, State? state, string fallback)
    {
        if (state == null) return fallback;
        switch (kind)
        {
            case Lj35GnsKeyKind.Radio:
                return state.Tuning.Length > 0 ? state.Tuning : fallback;
            case Lj35GnsKeyKind.Knob:
                if (state.EntryChar.Length > 0) return state.EntryChar;
                if (state.Cursor.Length > 0) return state.Cursor;
                return state.Context.Length > 0 ? state.Context : fallback;
            default:
                if (state.Context.Length == 0) return state.Cursor.Length > 0 ? state.Cursor : fallback;
                return state.Cursor.Length > 0 ? state.Context + ". Cursor on " + state.Cursor : state.Context;
        }
    }
}
