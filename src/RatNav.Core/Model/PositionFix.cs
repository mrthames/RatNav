namespace RatNav.Core.Model;

/// <summary>
/// One "where am I" reading, parsed from the filename of a screenshot the game wrote.
/// This is the only position data Escape from Tarkov puts on disk, and the only position
/// data RatNav ever reads.
/// </summary>
public sealed record PositionFix
{
    /// <summary>When the game took the screenshot, from the filename's own timestamp.</summary>
    public required DateTimeOffset TakenAt { get; init; }

    public required GamePosition Position { get; init; }

    /// <summary>Camera rotation as the game recorded it (x, y, z, w).</summary>
    public required Quaternion Rotation { get; init; }

    /// <summary>Compass heading in degrees, 0 = north, clockwise. Derived from <see cref="Rotation"/>.</summary>
    public required double HeadingDegrees { get; init; }

    /// <summary>The file this came from, so it can be archived or deleted after processing.</summary>
    public string? SourceFile { get; init; }
}

public readonly record struct Quaternion(double X, double Y, double Z, double W);
