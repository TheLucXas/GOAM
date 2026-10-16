namespace Goam.App;

/// <summary>Maps a Windows key to the name the Source engine uses in <c>bind</c> commands.</summary>
internal static class KeyNames
{
    /// <summary>Returns null for modifier-only presses and keys without a Source name.</summary>
    public static string? FromKeys(Keys key)
    {
        Keys code = key & Keys.KeyCode;

        if (code is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.CapsLock or Keys.LShiftKey or Keys.RShiftKey or Keys.LControlKey or Keys.RControlKey
            or Keys.LMenu or Keys.RMenu or Keys.None)
        {
            return null;
        }

        if (code is >= Keys.A and <= Keys.Z)
        {
            return ((char)('a' + (code - Keys.A))).ToString();
        }

        if (code is >= Keys.D0 and <= Keys.D9)
        {
            return ((char)('0' + (code - Keys.D0))).ToString();
        }

        if (code is >= Keys.F1 and <= Keys.F12)
        {
            return "f" + (code - Keys.F1 + 1);
        }

        return code switch
        {
            Keys.NumPad0 => "kp_ins",
            Keys.NumPad1 => "kp_end",
            Keys.NumPad2 => "kp_downarrow",
            Keys.NumPad3 => "kp_pgdn",
            Keys.NumPad4 => "kp_leftarrow",
            Keys.NumPad5 => "kp_5",
            Keys.NumPad6 => "kp_rightarrow",
            Keys.NumPad7 => "kp_home",
            Keys.NumPad8 => "kp_uparrow",
            Keys.NumPad9 => "kp_pgup",
            Keys.Decimal => "kp_del",
            Keys.Add => "kp_plus",
            Keys.Subtract => "kp_minus",
            Keys.Multiply => "kp_multiply",
            Keys.Divide => "kp_slash",
            Keys.Space => "space",
            Keys.Return => "enter",
            Keys.Tab => "tab",
            Keys.Back => "backspace",
            Keys.Escape => "escape",
            Keys.Insert => "ins",
            Keys.Delete => "del",
            Keys.Home => "home",
            Keys.End => "end",
            Keys.PageUp => "pgup",
            Keys.PageDown => "pgdn",
            Keys.Up => "uparrow",
            Keys.Down => "downarrow",
            Keys.Left => "leftarrow",
            Keys.Right => "rightarrow",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemPipe => "\\",
            Keys.OemSemicolon => "semicolon",
            Keys.OemQuotes => "'",
            Keys.Oemtilde => "`",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            _ => null,
        };
    }
}
