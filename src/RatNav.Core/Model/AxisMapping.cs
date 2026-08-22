namespace RatNav.Core.Model;

/// <summary>
/// How a map's world axes lay onto its image.
///
/// This replaces the earlier attempt to derive orientation from tarkovdata's
/// <c>coordinateRotation</c>. That field turned out not to describe the mapping: a rule fitted to
/// two maps got a third badly wrong, because orientation is a per-map property of how each
/// drawing was made rather than something computed. See docs/calibration.md.
/// </summary>
/// <param name="Swapped">
/// False: the horizontal axis of the image follows world X, the vertical follows world Z.
/// True: they are exchanged, which is the case on Factory and Reserve.
/// </param>
/// <param name="SignU">+1 or −1, applied to the source axis before normalizing through bounds.</param>
/// <param name="SignV">+1 or −1, likewise for the vertical axis.</param>
public readonly record struct AxisMapping(bool Swapped, int SignU, int SignV)
{
    /// <summary>World X across, world Z down, unmodified. The most common arrangement.</summary>
    public static AxisMapping Direct { get; } = new(false, 1, 1);

    /// <summary>Applies the mapping to a ground-plane position, before bounds normalization.</summary>
    public (double U, double V) Apply(double x, double z) =>
        Swapped ? (SignU * z, SignV * x) : (SignU * x, SignV * z);

    /// <summary>Reverses <see cref="Apply"/>.</summary>
    public (double X, double Z) Reverse(double u, double v) =>
        Swapped ? (v / SignV, u / SignU) : (u / SignU, v / SignV);

    public override string ToString() =>
        Swapped
            ? $"({(SignU < 0 ? "-" : "")}z, {(SignV < 0 ? "-" : "")}x)"
            : $"({(SignU < 0 ? "-" : "")}x, {(SignV < 0 ? "-" : "")}z)";

    /// <summary>
    /// Reads back what <see cref="ToString"/> wrote, so a mapping somebody confirmed can be
    /// written to a settings file and still mean the same thing next time.
    /// </summary>
    public static bool TryParse(string? text, out AxisMapping mapping)
    {
        mapping = Direct;

        if (text is not { Length: > 0 }) return false;

        var parts = text.Trim('(', ')', ' ').Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        var swapped = parts[0].EndsWith('z');

        // Both halves have to name different axes, or it is not a mapping of anything.
        if (swapped != parts[1].EndsWith('x')) return false;

        mapping = new AxisMapping(
            swapped,
            parts[0].StartsWith('-') ? -1 : 1,
            parts[1].StartsWith('-') ? -1 : 1);

        return true;
    }
}

/// <summary>How much a map's calibration can be trusted.</summary>
public enum CalibrationConfidence
{
    /// <summary>Nothing determined it; the default mapping is a guess.</summary>
    Unknown,

    /// <summary>Derived from published data, but the evidence was weak or self-contradictory.</summary>
    Weak,

    /// <summary>Derived from published data with a decisive margin.</summary>
    Derived,

    /// <summary>Checked against a real in-game position that a player identified on the map.</summary>
    Verified,
}
