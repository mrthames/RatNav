using RatNav.Core.Maps;

namespace RatNav.Core.Tests;

public class MapInkTests
{
    /// <summary>A miniature of a real tarkovdata map: styled classes, then shapes using them.</summary>
    private const string SampleMap = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
          <style id="style_common">
            .trees { fill:#144043 }
            .building { fill:#1a2632 }
            .road_tarmac { fill:none;stroke:#888 }
            .shadow { filter:drop-shadow(0 0 2px #000) }
          </style>
          <g>
            <path class="trees" d="M0 0h10v10H0z"/>
            <path class="building shadow" d="M20 20h10v10H20z"/>
            <path class="road_tarmac road_small" d="M0 50h100"/>
          </g>
        </svg>
        """;

    [Fact]
    public void Graphical_ink_keeps_the_map_as_drawn_and_only_dims_it()
    {
        var result = MapInk.Apply(
            SampleMap, new MapInkOptions { Level = MapInkLevel.Graphical, Opacity = 0.5 });

        Assert.Contains("style_common", result);
        Assert.Contains("#144043", result);
        Assert.DoesNotContain("ratnav_ink", result);
        Assert.Contains("opacity=\"0.5\"", result);
    }

    /// <summary>
    /// Full and Structure are the same recolouring with the ground turned up or down. Both put the
    /// map into one palette; only Full leaves it standing on something.
    /// </summary>
    [Fact]
    public void Full_ink_recolours_by_role_but_keeps_the_ground_visible()
    {
        var full = MapInk.Apply(SampleMap, new MapInkOptions { Level = MapInkLevel.Full });
        var structure = MapInk.Apply(SampleMap, new MapInkOptions { Level = MapInkLevel.Structure });

        Assert.Contains("ratnav_ink", full);
        Assert.DoesNotContain("#144043", full);

        Assert.Contains("fill-opacity: 0.45", full);
        Assert.DoesNotContain("fill-opacity: 0.45", structure);
    }

    [Fact]
    public void Structure_ink_pushes_terrain_back_and_brings_buildings_forward()
    {
        var result = MapInk.Apply(SampleMap, new MapInkOptions
        {
            Level = MapInkLevel.Structure,
            Accent = "#8ec8ff",
        });

        // The map's own palette is gone...
        Assert.DoesNotContain("#144043", result);
        Assert.DoesNotContain("style_common", result);

        // ...replaced by roles.
        Assert.Contains("ratnav_ink", result);
        Assert.Contains(".trees", result);
        Assert.Contains("#8ec8ff", result);

        // The shapes themselves are untouched — only the stylesheet changed.
        Assert.Contains("""<path class="trees" d="M0 0h10v10H0z"/>""", result);
    }

    [Fact]
    public void Outline_ink_hides_terrain_entirely()
    {
        var result = MapInk.Apply(SampleMap, new MapInkOptions { Level = MapInkLevel.Outline });

        var terrainRule = ExtractRule(result, ".trees");
        Assert.Contains("display: none", terrainRule);

        var buildingRule = ExtractRule(result, ".building");
        Assert.Contains("fill: none", buildingRule);
        Assert.Contains("stroke:", buildingRule);
    }

    [Fact]
    public void Drop_shadows_are_switched_off_in_every_ink_mode()
    {
        // A blur filter over live gameplay is the most expensive thing in the file and blurs
        // exactly what needs to stay crisp.
        foreach (var level in new[] { MapInkLevel.Structure, MapInkLevel.Outline })
        {
            var result = MapInk.Apply(SampleMap, new MapInkOptions { Level = level });
            Assert.Contains(".shadow { filter: none !important; }", result);
        }
    }

    [Fact]
    public void Declarations_are_important_so_inline_styles_cannot_win()
    {
        // Some shapes carry inline styles, which outrank a plain class rule. Without this,
        // a few buildings would stay opaque while the rest of the map faded.
        var result = MapInk.BuildStylesheet(new MapInkOptions { Level = MapInkLevel.Structure });

        Assert.Contains("!important", result);
    }

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(0.55, "0.55")]
    [InlineData(1.0, "1")]
    [InlineData(2.5, "1")]     // clamped
    [InlineData(-1.0, "0")]    // clamped
    public void Opacity_is_clamped_and_written_invariantly(double input, string expected)
    {
        var result = MapInk.Apply(SampleMap, new MapInkOptions { Opacity = input });

        // Written with an invariant decimal point — a comma here would silently break the SVG
        // for anyone on a European locale.
        Assert.Contains($"opacity=\"{expected}\"", result);
    }

    [Fact]
    public void Hazards_can_be_kept_or_dropped()
    {
        var kept = MapInk.Apply(SampleMap, new MapInkOptions { ShowHazards = true });
        Assert.DoesNotContain("display: none !important; }", ExtractRule(kept, ".danger"));

        var dropped = MapInk.Apply(SampleMap, new MapInkOptions { ShowHazards = false });
        Assert.Contains("display: none", ExtractRule(dropped, ".danger"));
    }

    [Fact]
    public void The_halo_is_optional()
    {
        Assert.Contains("drop-shadow", MapInk.Apply(SampleMap, new MapInkOptions { Halo = true }));
        Assert.DoesNotContain("drop-shadow(0 0 1.2px", MapInk.Apply(SampleMap, new MapInkOptions { Halo = false }));
    }

    [Fact]
    public void A_map_with_no_stylesheet_still_gets_one()
    {
        const string bare = """<svg xmlns="http://www.w3.org/2000/svg"><path class="building" d="M0 0h1v1H0z"/></svg>""";

        var result = MapInk.Apply(bare, new MapInkOptions { Level = MapInkLevel.Outline });

        Assert.Contains("ratnav_ink", result);
        Assert.Contains(".building", result);
    }

    /// <summary>Pulls out the generated rule containing a selector, for asserting on its contents.</summary>
    private static string ExtractRule(string markup, string selector)
    {
        var index = markup.IndexOf(selector, StringComparison.Ordinal);
        if (index < 0) return "";

        var close = markup.IndexOf('}', index);
        return close < 0 ? markup[index..] : markup[index..(close + 1)];
    }
}
