using RatNav.Core.Model;

namespace RatNav.Core.Maps;

/// <summary>The mapping chosen for a map, and how much it can be trusted.</summary>
public sealed record SolvedCalibration
{
    public required AxisMapping Mapping { get; init; }
    public required CalibrationConfidence Confidence { get; init; }

    /// <summary>Plain-language account of why, shown in the UI rather than buried in a log.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Works out how a map's world coordinates lay onto its image.
///
/// Two independent signals, because neither is sufficient alone:
///
/// <para><b>Aspect ratio</b> decides whether the axes are swapped. The world span mapped to the
/// image's width and the span mapped to its height must share a metres-per-pixel scale, so the
/// arrangement that makes those agree is the right one. This is decisive on most maps and
/// useless on a square image, where both arrangements fit equally.</para>
///
/// <para><b>Extract positions</b> decide the signs. Extracts are real published coordinates that
/// must land on the map, so the signs that push fewest of them off the edge are the right ones.
/// This is decisive where extracts hug the edges and useless where they all sit comfortably
/// inside — flipping an axis then mirrors the layout without moving anything out of bounds.</para>
///
/// <para>Where both signals are weak the answer is not guessed at: the map is reported as
/// <see cref="CalibrationConfidence.Weak"/> so the UI can say a pin might be wrong. One position
/// marked by a player settles any map outright, and those answers live in
/// <see cref="VerifiedMappings"/> so nobody has to repeat the work.</para>
/// </summary>
public static class CalibrationSolver
{
    /// <summary>
    /// Mappings confirmed against a real in-game position. Keyed by tarkovdata's own map key.
    ///
    /// Customs: a screenshot at world (-14.44, -139.32) marked at 66.7%, 30.9% of the image;
    /// the direct mapping puts it at 66.6%, 30.8%.
    ///
    /// Factory: two marked positions, settled by the bearing between them — 146° measured
    /// against 148° predicted, where the rival candidate predicted 115°.
    ///
    /// Lighthouse: a screenshot at the Northern Checkpoint extract, marked on the image. The
    /// direct mapping missed by 0.3 percentage points and the runner-up by 21.6.
    ///
    /// Woods: a screenshot at the South V-Ex extract. The click alone was not decisive — Woods is
    /// nearly square, so the transposed mapping lands only 1.4pp away and that is within a click's
    /// slip. The 20 published extract positions break the tie 25 to 1 in favour of direct, and
    /// both signals agree.
    ///
    /// Interchange: a marked position at the NW Exfil, 1.2pp against a runner-up at 6.1pp. This
    /// one could only ever be settled by hand — Interchange's image is exactly square, so the
    /// aspect-ratio test has nothing to say about which axis runs which way.
    /// </summary>
    public static IReadOnlyDictionary<string, AxisMapping> VerifiedMappings { get; } =
        new Dictionary<string, AxisMapping>(StringComparer.OrdinalIgnoreCase)
        {
            ["customs"] = new(Swapped: false, SignU: 1, SignV: 1),
            ["factory"] = new(Swapped: true, SignU: -1, SignV: 1),
            ["woods"] = new(Swapped: false, SignU: 1, SignV: 1),
            ["lighthouse"] = new(Swapped: false, SignU: 1, SignV: 1),
            ["interchange"] = new(Swapped: false, SignU: 1, SignV: 1),
        };

    /// <summary>Below this ratio between best and runner-up, the evidence is not worth trusting.</summary>
    private const double DecisiveRatio = 3.0;

    public static SolvedCalibration Solve(
        string? mapKey,
        double[][] bounds,
        int imageWidth,
        int imageHeight,
        IReadOnlyList<GamePosition> extracts)
    {
        if (mapKey is not null && VerifiedMappings.TryGetValue(mapKey, out var verified))
        {
            return new SolvedCalibration
            {
                Mapping = verified,
                Confidence = CalibrationConfidence.Verified,
                Reason = "Checked against a position marked in game.",
            };
        }

        if (bounds is not { Length: 2 } || bounds[0] is not { Length: 2 } || bounds[1] is not { Length: 2 })
        {
            return new SolvedCalibration
            {
                Mapping = AxisMapping.Direct,
                Confidence = CalibrationConfidence.Unknown,
                Reason = "This map has no usable bounds.",
            };
        }

        double x1 = bounds[0][0], y1 = bounds[0][1], x2 = bounds[1][0], y2 = bounds[1][1];
        var spanX = Math.Abs(x2 - x1);
        var spanZ = Math.Abs(y2 - y1);

        // --- 1. orientation, from aspect ratio
        var swapped = false;
        var orientationClear = false;

        if (imageWidth > 0 && imageHeight > 0 && spanX > 0 && spanZ > 0)
        {
            var directError = Math.Abs((spanX / imageWidth) / (spanZ / imageHeight) - 1);
            var swappedError = Math.Abs((spanX / imageHeight) / (spanZ / imageWidth) - 1);

            swapped = swappedError < directError;
            var better = Math.Min(directError, swappedError);
            var worse = Math.Max(directError, swappedError);

            // A near-square image makes both arrangements fit, which is no answer at all.
            orientationClear = worse > better * DecisiveRatio;
        }

        // --- 2. signs, from how far extracts fall off the image
        var candidates = new[]
        {
            new AxisMapping(swapped, 1, 1),
            new AxisMapping(swapped, -1, 1),
            new AxisMapping(swapped, 1, -1),
            new AxisMapping(swapped, -1, -1),
        };

        var scored = candidates
            .Select(m => (Mapping: m, Violation: Violation(m, x1, y1, x2, y2, extracts)))
            .OrderBy(s => s.Violation)
            .ToArray();

        var best = scored[0];
        var runnerUp = scored[1];

        var signsClear = extracts.Count >= 4 &&
            (best.Violation > 0
                ? runnerUp.Violation > best.Violation * DecisiveRatio
                : runnerUp.Violation > 1e-6);

        var confidence = orientationClear && signsClear
            ? CalibrationConfidence.Derived
            : CalibrationConfidence.Weak;

        return new SolvedCalibration
        {
            Mapping = best.Mapping,
            Confidence = confidence,
            Reason = confidence == CalibrationConfidence.Derived
                ? $"Derived from the image's proportions and {extracts.Count} extract positions."
                : Explain(orientationClear, signsClear, extracts.Count),
        };
    }

    /// <summary>
    /// Says which signal fell short, and both when both did. Compound rather than first-match:
    /// a map failing on proportions and on extracts is a different situation from one failing on
    /// either alone, and this text is what the UI shows to explain why a pin might be wrong.
    /// </summary>
    private static string Explain(bool orientationClear, bool signsClear, int extractCount)
    {
        var problems = new List<string>();

        if (!orientationClear)
            problems.Add("the image is close to square, so its proportions don't reveal the axis order");

        if (!signsClear)
        {
            problems.Add(extractCount < 4
                ? $"there are only {extractCount} extract positions to work from"
                : "the extracts sit well inside the map, so they can't tell a mirrored layout from a correct one");
        }

        return $"Uncertain: {string.Join("; and ", problems)}. Mark a position in game to settle it.";
    }

    /// <summary>
    /// How far, squared, the extracts fall outside the image. Zero means every one landed on the
    /// map. Squaring matters: it distinguishes one badly-placed extract from several marginal
    /// ones, and a mapping that throws a single extract clean off the map is more wrong than one
    /// that leaves a few sitting on the border.
    /// </summary>
    private static double Violation(
        AxisMapping mapping, double x1, double y1, double x2, double y2,
        IReadOnlyList<GamePosition> extracts)
    {
        var total = 0.0;

        foreach (var extract in extracts)
        {
            var (a, b) = mapping.Apply(extract.X, extract.Z);
            var u = (a - x1) / (x2 - x1);
            var v = (b - y1) / (y2 - y1);

            total += Overshoot(u) + Overshoot(b: v);
        }

        return total;

        static double Overshoot(double b) => b switch
        {
            < 0 => b * b,
            > 1 => (b - 1) * (b - 1),
            _ => 0,
        };
    }
}
