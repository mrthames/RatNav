using System.Windows.Input;

namespace RatNav.App.Interop;

/// <summary>
/// A hotkey written the way a person would write it: <c>F5</c>, <c>Alt+F6</c>,
/// <c>Ctrl+Shift+M</c>.
///
/// <para>Settings hold these as text rather than key codes so the file stays editable by hand and
/// readable a year later. A binding that cannot be parsed is reported rather than silently
/// ignored — a key that does nothing with no explanation is the worst outcome here.</para>
/// </summary>
public sealed record HotKeySpec(ModifierKeys Modifiers, Key Key)
{
    public static bool TryParse(string? text, out HotKeySpec spec, out string? problem)
    {
        spec = new HotKeySpec(ModifierKeys.None, Key.None);
        problem = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            problem = "No key set.";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = ModifierKeys.None;
        Key? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;

                default:
                    if (key is not null)
                    {
                        problem = $"'{text}' names more than one key.";
                        return false;
                    }

                    if (!TryParseKey(part, out var parsed))
                    {
                        problem = $"'{part}' is not a key RatNav recognizes.";
                        return false;
                    }

                    key = parsed;
                    break;
            }
        }

        if (key is null)
        {
            problem = $"'{text}' has modifiers but no key.";
            return false;
        }

        spec = new HotKeySpec(modifiers, key.Value);
        return true;
    }

    private static bool TryParseKey(string text, out Key key)
    {
        // Spellings Enum.TryParse does not know, from two directions.
        //
        // **The browser's, which is where bindings actually come from.** Setup captures
        // `KeyboardEvent.key` and stores it verbatim, and the browser and WPF disagree about the
        // names of a good deal of the keyboard: the browser says "ArrowLeft" where the enum says
        // "Left", and "1" where it says "D1". Those bindings parsed to nothing and were refused, so
        // rebinding worked for letters and function keys and silently failed for the arrows, the
        // whole number row, Backspace, Page Up and Page Down, and the space bar.
        //
        // **And the ones people type**, because settings.json is meant to be editable by hand and
        // readable a year later. Somebody writing "Left" and somebody writing "ArrowLeft" should
        // both get the left arrow.
        var normalized = text.ToLowerInvariant() switch
        {
            // Arrows.
            "arrowleft" => "Left",
            "arrowright" => "Right",
            "arrowup" => "Up",
            "arrowdown" => "Down",

            // The number row. The enum calls these D0 to D9; nothing else does.
            "0" => "D0",
            "1" => "D1",
            "2" => "D2",
            "3" => "D3",
            "4" => "D4",
            "5" => "D5",
            "6" => "D6",
            "7" => "D7",
            "8" => "D8",
            "9" => "D9",

            // Named differently, or arriving as the character itself.
            "backspace" => "Back",
            "pageup" => "Prior",
            "pagedown" => "Next",
            "escape" or "esc" => "Escape",
            "enter" or "return" => "Return",
            " " => "Space",

            "`" or "backtick" or "tilde" or "grave" => "OemTilde",
            "\\" or "backslash" => "OemBackslash",
            "-" or "minus" => "OemMinus",
            "=" or "equals" or "plus" => "OemPlus",
            "[" => "OemOpenBrackets",
            "]" => "OemCloseBrackets",
            "," or "comma" => "OemComma",
            "." or "period" => "OemPeriod",
            "/" or "slash" => "OemQuestion",
            ";" => "OemSemicolon",
            "'" or "quote" => "OemQuotes",
            "space" => "Space",
            _ => text,
        };

        return Enum.TryParse(normalized, ignoreCase: true, out key) && key != Key.None;
    }

    public override string ToString()
    {
        var parts = new List<string>();

        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        // Written the way a person would, not the way the enum spells it. "D1" and "Prior" are
        // implementation details of System.Windows.Input.Key and mean nothing on a Setup page.
        parts.Add(Key switch
        {
            Key.OemTilde => "`",
            Key.OemBackslash => "\\",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.Prior => "Page Up",
            Key.Next => "Page Down",
            Key.Back => "Backspace",
            >= Key.D0 and <= Key.D9 => ((int)(Key - Key.D0)).ToString(),
            _ => Key.ToString(),
        });

        return string.Join("+", parts);
    }
}
