using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RatNav.Service;

/// <summary>
/// Reaching RatNav from another device on the same network.
///
/// <para>The whole feature is which address Kestrel answers on, plus a hole in the Windows
/// Firewall. There is no account, no token and no login: the network is the boundary, and the
/// switch that opens it is off until somebody decides otherwise. Nothing here touches the router,
/// and nothing here can — port forwarding is a router-to-internet thing and is deliberately not
/// part of this.</para>
///
/// <para>What this file is actually for is telling the truth on the Setup page. "Turn it on and
/// hope" is a bad experience when a firewall silently eats the connection, so it works out the
/// address to type, whether a rule already exists, and whether the port genuinely answers.</para>
/// </summary>
public static class LanAccess
{
    /// <summary>The name the firewall rule is created under, and looked up by.</summary>
    private const string RuleName = "RatNav";

    /// <summary>
    /// This machine's addresses on the local network, as a phone would have to type them.
    ///
    /// <para>Every operational interface rather than a guess at the right one: a machine with
    /// wifi and ethernet both up has two, and which one the iPad is on is not knowable from
    /// here.</para>
    /// </summary>
    public static IReadOnlyList<string> Addresses()
    {
        try
        {
            return
            [
                .. NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Select(a => a.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Where(a => !IPAddress.IsLoopback(a))

                    // Link-local, which Windows hands out when DHCP fails. It is an address, but
                    // it is not one anything else on the network can reach.
                    .Where(a => !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    .Select(a => a.ToString())
                    .Distinct(StringComparer.Ordinal),
            ];
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>
    /// Whether a firewall rule for RatNav's port already exists.
    ///
    /// <para>Reading rules needs no elevation, which is what makes it worth asking: the Setup page
    /// can stay quiet when there is nothing to fix rather than offering to fix what is not
    /// broken.</para>
    /// </summary>
    public static bool RuleExists(int port)
    {
        var output = Netsh($"advfirewall firewall show rule name=\"{RuleName}\" verbose");

        // netsh prints the rule when it finds one and "No rules match" when it does not. The port
        // is checked as well, because a rule left over from a different port is not this one.
        return output is { Length: > 0 }
            && output.Contains(RuleName, StringComparison.OrdinalIgnoreCase)
            && output.Contains(port.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The command to run by hand, for anyone who would rather not be prompted.</summary>
    public static string RuleCommand(int port) =>
        $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow "
        + $"protocol=TCP localport={port}";

    /// <summary>
    /// Adds the rule, which needs administrator rights and therefore a prompt.
    ///
    /// <para>There is no way around the prompt — a process cannot open a firewall port without
    /// elevation, and it should not be able to. What it can do is only ask when asking is
    /// warranted, and never be the only way through: the same command is printed alongside.</para>
    /// </summary>
    public static bool AddRule(int port, out string? problem)
    {
        problem = null;

        try
        {
            var elevated = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = RuleCommand(port)["netsh ".Length..],
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (elevated is null)
            {
                problem = "Windows did not start the command.";
                return false;
            }

            elevated.WaitForExit(20_000);
            return elevated.HasExited && elevated.ExitCode == 0;
        }
        catch (Exception ex)
        {
            // Declining the prompt throws rather than returning a code, and declining is a
            // perfectly ordinary thing to do — so it is reported, not logged as a fault.
            problem = ex.Message;
            return false;
        }
    }

    private static string? Netsh(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);

            return output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
