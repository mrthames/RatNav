using RatNav.Core.Model;
using RatNav.Core.Planning;
using RatNav.Core.Sharing;

namespace RatNav.Core.Tests;

/// <summary>
/// Sharing a plan as a pasteable code.
///
/// <para>The cases that matter are what happens to text between one person and another: a chat
/// client wraps a long line, someone copies with a stray space, someone pastes into a URL. A code
/// that only works when handled perfectly is a code that does not work.</para>
/// </summary>
public class PlanCodeTests
{
    private static readonly MapDef Customs = new()
    {
        Id = "customs",
        Name = "Customs",
        Image = new MapImage
        {
            SourceUrl = "https://example.invalid/customs.svg",
            CoordinateRotation = 0,
            Bounds = [[-400, -400], [400, 400]],
            PixelWidth = 1000,
            PixelHeight = 1000,
        },
    };

    private static PlanDocument Document(string owner = "you") => PlanDocument.From(
        RaidPlanner.Plan(Customs,
        [
            new Waypoint
            {
                ObjectiveId = "o1",
                TaskId = "t1",
                TaskName = "Debut",
                Description = "Kill five Scavs",
                Position = new GamePosition(120, 2, -80),
                NeededKeyItemIds = ["dorm-114"],
            },
            new Waypoint
            {
                ObjectiveId = "o2",
                TaskId = "t2",
                TaskName = "Checking",
                Description = "Find the stash",
                Position = new GamePosition(-45, 1, 210),
            },
        ]),
        owner,
        ["salewa", "watch"]);

    [Fact]
    public void A_plan_survives_the_round_trip()
    {
        var restored = PlanCode.Decode(PlanCode.Encode(Document()), out var problem);

        Assert.Null(problem);
        Assert.NotNull(restored);
        Assert.Equal("you", restored.Owner);
        Assert.Equal("customs", restored.MapId);
        Assert.Equal(["o1", "o2"], restored.Stops.Select(s => s.ObjectiveId));
        Assert.Contains("dorm-114", restored.RequiredKeyItemIds);
    }

    [Fact]
    public void Positions_survive_exactly()
    {
        // The whole point of sharing is that a pin lands in the same place for both of you. A
        // rounding loss here would put a friend's waypoint meters from yours with nothing to
        // explain why.
        var restored = PlanCode.Decode(PlanCode.Encode(Document()), out _)!;
        var stop = restored.Stops.Single(s => s.ObjectiveId == "o1");

        Assert.Equal(120, stop.X);
        Assert.Equal(2, stop.Y);
        Assert.Equal(-80, stop.Z);
    }

    [Fact]
    public void Whitespace_and_line_breaks_do_not_break_it()
    {
        // Chat clients wrap long messages, and people copy with whatever came along.
        var code = PlanCode.Encode(Document());
        var mangled = $"  {code[..20]}\n{code[20..40]}\r\n {code[40..]}  ";

        Assert.NotNull(PlanCode.Decode(mangled, out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void The_code_uses_only_characters_that_travel()
    {
        // Base64's '+' and '/' get mangled by URLs and by chat clients trying to be helpful, and
        // a trailing '=' gets treated as punctuation. None of those appear here.
        var code = PlanCode.Encode(Document());

        Assert.DoesNotContain('+', code);
        Assert.DoesNotContain('/', code);
        Assert.DoesNotContain('=', code);
    }

    [Fact]
    public void A_code_is_small_enough_to_paste()
    {
        // Two stops should be a line, not a page. If this ever fails the encoding has stopped
        // compressing and the feature has quietly become unusable in the place it is used.
        Assert.True(
            PlanCode.Encode(Document()).Length < 600,
            "Share codes are getting too long to paste comfortably.");
    }

    [Fact]
    public void Nonsense_is_refused_with_a_reason()
    {
        Assert.Null(PlanCode.Decode("hello", out var problem));
        Assert.Contains("RatNav code", problem);
    }

    [Fact]
    public void An_empty_paste_says_so_rather_than_failing_obscurely()
    {
        Assert.Null(PlanCode.Decode("   ", out var problem));
        Assert.Contains("paste the code", problem);
    }

    [Fact]
    public void A_truncated_code_is_reported_as_incomplete()
    {
        // The commonest real failure: a chat client cut the message, or the copy missed the end.
        var code = PlanCode.Encode(Document());

        Assert.Null(PlanCode.Decode(code[..(code.Length / 2)], out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void A_code_from_another_version_says_so()
    {
        Assert.Null(PlanCode.Decode("RATNAV9-abcdef", out var problem));
        Assert.Contains("different version", problem);
    }

    /// <summary>
    /// A code that arrived without its prefix still imports.
    ///
    /// <para>The old rule refused anything prefix-less containing a hyphen as "a different version
    /// of RatNav" — and base64url uses '-' in place of '+', so most bodies contain one. A partial
    /// copy, or a chat client eating the start of a line, was therefore reported as a version
    /// mismatch: a wrong answer that sends somebody looking for a problem that does not exist.</para>
    /// </summary>
    [Fact]
    public void A_code_that_lost_its_prefix_still_imports()
    {
        var code = PlanCode.Encode(Document());
        var body = code[PlanCode.Prefix.Length..];

        // The body genuinely contains the character the old rule tripped on, or this proves nothing.
        Assert.Contains('-', body);

        var decoded = PlanCode.Decode(body, out var problem);

        Assert.Null(problem);
        Assert.NotNull(decoded);
        Assert.Equal(Document().MapId, decoded.MapId);
    }

    /// <summary>Only a RatNav code with another number on it earns the version message.</summary>
    [Theory]
    [InlineData("RATNAV2-abcdef")]
    [InlineData("ratnav10-abcdef")]
    public void Another_versions_prefix_is_recognised(string code)
    {
        Assert.Null(PlanCode.Decode(code, out var problem));
        Assert.Contains("different version", problem);
    }

    /// <summary>
    /// And a damaged code says it is damaged, in words rather than in parser output.
    ///
    /// <para>It used to surface the serializer's own message — "Expected end of string, but instead
    /// reached end of data. Path: $.stops[0] | LineNumber: 9" — which is true, and no help at all
    /// to somebody who pasted half a code out of a chat window.</para>
    /// </summary>
    [Fact]
    public void A_damaged_code_does_not_leak_parser_text()
    {
        var code = PlanCode.Encode(Document());

        Assert.Null(PlanCode.Decode(code[..(code.Length / 2)], out var problem));
        Assert.NotNull(problem);

        Assert.DoesNotContain("LineNumber", problem);
        Assert.DoesNotContain("BytePositionInLine", problem);
        Assert.DoesNotContain("$.", problem);
    }
}
