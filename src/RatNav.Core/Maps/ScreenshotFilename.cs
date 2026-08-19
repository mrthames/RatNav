using System.Globalization;
using System.Text.RegularExpressions;
using RatNav.Core.Model;

namespace RatNav.Core.Maps;

/// <summary>
/// Parses the position Escape from Tarkov encodes into screenshot filenames.
///
/// The documented shape is:
///   2024-03-17[14-30]_-193.0, 2.6, -111.7_0.0, 0.7, 0.0, -0.7_12.34 (0).png
///   ^ date      ^time  ^ x, y, z          ^ qx, qy, qz, qw    ^ scale
///
/// Rather than pinning one brittle regex to that exact layout, this splits on '_' and
/// identifies segments by how many comma-separated numbers they hold: three means a
/// position, four means a rotation quaternion. BSG has changed this filename format
/// before, and structural parsing survives changes that a literal pattern would not.
/// </summary>
public static class ScreenshotFilename
{
    private static readonly Regex DateTimePrefix = new(
        @"^(?<y>\d{4})-(?<mo>\d{2})-(?<d>\d{2})\[(?<h>\d{2})-(?<mi>\d{2})(?:-(?<s>\d{2}))?\]",
        RegexOptions.Compiled);

    /// <summary>
    /// Attempts to read a position fix out of a screenshot's filename.
    /// Returns false for screenshots taken outside a raid, which carry no coordinates.
    /// </summary>
    public static bool TryParse(string path, out PositionFix fix)
    {
        fix = null!;
        if (string.IsNullOrWhiteSpace(path)) return false;

        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) return false;

        GamePosition? position = null;
        Quaternion? rotation = null;

        foreach (var segment in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseNumberList(segment, out var numbers)) continue;

            // First 3-number group wins as position, first 4-number group as rotation.
            if (numbers.Length == 3 && position is null)
                position = new GamePosition(numbers[0], numbers[1], numbers[2]);
            else if (numbers.Length == 4 && rotation is null)
                rotation = new Quaternion(numbers[0], numbers[1], numbers[2], numbers[3]);
        }

        if (position is null) return false;

        var rot = rotation ?? new Quaternion(0, 0, 0, 1);

        fix = new PositionFix
        {
            TakenAt = ParseTimestamp(name) ?? DateTimeOffset.Now,
            Position = position.Value,
            Rotation = rot,
            HeadingDegrees = HeadingFrom(rot),
            SourceFile = path,
        };
        return true;
    }

    /// <summary>
    /// Compass heading in degrees (0 = north, increasing clockwise) from a camera quaternion.
    /// Yaw about Unity's vertical Y axis.
    /// </summary>
    public static double HeadingFrom(Quaternion q)
    {
        var yaw = Math.Atan2(
            2.0 * (q.W * q.Y + q.X * q.Z),
            1.0 - 2.0 * (q.Y * q.Y + q.Z * q.Z));

        var degrees = yaw * 180.0 / Math.PI;
        return Normalize(degrees);
    }

    /// <summary>Wraps any angle into [0, 360).</summary>
    public static double Normalize(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }

    private static bool TryParseNumberList(string segment, out double[] numbers)
    {
        numbers = [];
        if (!segment.Contains(',')) return false;

        var parts = segment.Split(',');
        var parsed = new double[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
                return false;
        }

        numbers = parsed;
        return true;
    }

    private static DateTimeOffset? ParseTimestamp(string name)
    {
        var m = DateTimePrefix.Match(name);
        if (!m.Success) return null;

        try
        {
            return new DateTimeOffset(
                int.Parse(m.Groups["y"].Value),
                int.Parse(m.Groups["mo"].Value),
                int.Parse(m.Groups["d"].Value),
                int.Parse(m.Groups["h"].Value),
                int.Parse(m.Groups["mi"].Value),
                m.Groups["s"].Success ? int.Parse(m.Groups["s"].Value) : 0,
                DateTimeOffset.Now.Offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
