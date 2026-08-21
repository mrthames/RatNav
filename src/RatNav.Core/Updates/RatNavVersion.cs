using System.Reflection;

namespace RatNav.Core.Updates;

/// <summary>
/// What version this is, as the build stamped it.
///
/// <para>Read from the assembly rather than written down somewhere, because a version in two
/// places is a version that disagrees with itself. The release workflow passes it to
/// <c>dotnet publish</c> and to the installer from the same tag, so all three say the same
/// thing.</para>
/// </summary>
public static class RatNavVersion
{
/// <summary>What a build was published as, or that it was not.</summary>
    /// <summary>
    /// The running version, without any build metadata.
    ///
    /// <para>Reads <c>0.0.0</c> for a build nobody released — the project sets that deliberately,
    /// because .NET's own default of 1.0.0 is not any RatNav that has ever existed and is newer
    /// than every real release by every comparison.</para>
    /// </summary>
    public static string Current
    {
        get
        {
            // The assembly this code lives in, not whatever started the process.
            //
            // GetEntryAssembly is the host, which is RatNav when RatNav runs and the test runner
            // when tests run — so the version was whatever Microsoft had stamped on testhost.dll,
            // and a property about *this* program reported somebody else's number.
            //
            // The build stamps every project in the graph from one -p:Version, so this carries the
            // release's version in a release and the local 0.0.0 otherwise.
            var assembly = typeof(RatNavVersion).Assembly;

            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (informational is { Length: > 0 } text)
            {
                // "0.2.0+9a1b2c3" — the commit is for a build log, not for a version compare.
                var plus = text.IndexOf('+');
                return plus >= 0 ? text[..plus] : text;
            }

            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    /// <summary>
    /// Whether this build came from a release, rather than from somebody's own compiler.
    ///
    /// <para>Worth reporting rather than hiding: "you are running a local build" is true and
    /// useful, where "you are on the newest release" said of an unreleased build is neither.</para>
    /// </summary>
    public static bool IsRelease => UpdateCheck.IsReleaseVersion(Current);
}
