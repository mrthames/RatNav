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
    /// <summary>
    /// The running version, without any build metadata.
    ///
    /// <para>A local build has no version stamped on it and reads 1.0.0, which is not any release
    /// and never compares as newer than one — so running from source quietly stops claiming there
    /// is an update rather than claiming there is one every day.</para>
    /// </summary>
    public static string Current
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

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
}
