using System.IO.Compression;
using System.Text;

namespace RatNav.Core.Sharing;

/// <summary>
/// A plan as a line of text you can paste into a chat window.
///
/// <para>Sharing a file works and stays — a file is for keeping. But the way people actually pass
/// a plan to each other is by pasting it wherever they are already talking, and "download this,
/// send it, save it, import it" is four steps where one would do.</para>
///
/// <para><b>This is not a hash.</b> A hash is one-way; it identifies a thing without containing
/// it, so it cannot be turned back into a plan. What this produces is the plan itself, compressed
/// and written in characters that survive being pasted anywhere — which is what makes it work
/// without RatNav needing a server to look anything up.</para>
///
/// <para>The encoding is deflate, then base64url. Base64url rather than plain base64 because
/// <c>+</c> and <c>/</c> get mangled by URLs and by chat clients that try to be helpful, and the
/// padding <c>=</c> gets stripped; the characters used here have no meaning to any of them.</para>
/// </summary>
public static class PlanCode
{
    /// <summary>
    /// Prefix so a pasted code is recognisable as one, and so a future format change can be told
    /// apart from this one rather than failing as corrupt.
    /// </summary>
    public const string Prefix = "RATNAV1-";

    /// <summary>Turns a plan into a code.</summary>
    public static string Encode(PlanDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var json = Encoding.UTF8.GetBytes(document.ToJson());

        using var output = new MemoryStream();

        // Smallest rather than fastest: this runs once when a plan is shared, and every byte saved
        // is a byte less likely to be wrapped or truncated by whatever it is pasted into.
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(json, 0, json.Length);
        }

        return Prefix + ToBase64Url(output.ToArray());
    }

    /// <summary>
    /// Reads a code back into a plan, or explains why it could not.
    ///
    /// <para>Tolerant of what happens to text in transit: surrounding whitespace, line breaks
    /// inserted by a chat client wrapping a long message, and a missing prefix if someone pasted
    /// only part of it.</para>
    /// </summary>
    public static PlanDocument? Decode(string? code, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(code))
        {
            problem = "Nothing to import — paste the code someone sent you.";
            return null;
        }

        // Chat clients wrap long lines, and people paste with whatever came along.
        var cleaned = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());

        if (cleaned.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[Prefix.Length..];
        }
        else if (cleaned.Contains('-', StringComparison.Ordinal))
        {
            problem = "That code was made by a different version of RatNav.";
            return null;
        }

        byte[] compressed;
        try
        {
            compressed = FromBase64Url(cleaned);
        }
        catch (FormatException)
        {
            problem = "That does not look like a RatNav code — check it copied in full.";
            return null;
        }

        try
        {
            using var input = new MemoryStream(compressed);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            deflate.CopyTo(output);

            return PlanDocument.FromJson(Encoding.UTF8.GetString(output.ToArray()), out problem);
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException)
        {
            problem = "That code is incomplete or damaged — ask for it again.";
            return null;
        }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');

        // Base64 works in blocks of four; the padding is what was trimmed off to keep the code
        // short and to stop chat clients treating a trailing '=' as punctuation.
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }
}
