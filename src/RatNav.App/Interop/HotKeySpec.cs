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
                        problem = $"'{part}' is not a key RatNav recognises.";
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
        // A few spellings people actually use, which Enum.TryParse does not know.
        var normalized = text.ToLowerInvariant() switch
        {
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

        parts.Add(Key switch
        {
            Key.OemTilde => "`",
            Key.OemBackslash => "\\",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            _ => Key.ToString(),
        });

        return string.Join("+", parts);
    }
}
