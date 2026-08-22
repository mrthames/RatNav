using System.Net;
using Microsoft.AspNetCore.Http;
using RatNav.Service;

namespace RatNav.Core.Tests;

/// <summary>
/// The boundary around network access.
///
/// <para>RatNav can answer on the local network so a tablet can read a plan. There is no password
/// — the network is the whole boundary, and Setup says so out loud. That is a fair bargain for
/// reading a quest list and a poor one for anything that reaches back out and acts on the machine
/// itself, so those are refused to anything that is not this machine.</para>
///
/// <para>These are about the decision rather than the endpoints, because the decision is the part
/// that has to be right: every guarded endpoint asks this one question, so a mistake here is a
/// mistake everywhere at once.</para>
/// </summary>
public class LanBoundaryTests
{
    private static HttpContext From(string? address)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
        return http;
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]   // the whole 127/8 block is this machine, not only .1
    [InlineData("::1")]         // and so is IPv6 loopback, which is what a browser often uses
    public void The_machine_RatNav_runs_on_is_allowed(string address)
    {
        Assert.True(LanAccess.FromThisMachine(From(address)));
    }

    [Theory]
    [InlineData("192.168.1.50")]    // the tablet the feature exists for
    [InlineData("10.0.0.7")]
    [InlineData("172.16.4.9")]
    [InlineData("8.8.8.8")]
    public void Anything_else_on_the_network_is_not(string address)
    {
        // Being on the same wifi is not the same as being this machine. The tablet may read a
        // plan; it may not wipe a character or close the app.
        Assert.False(LanAccess.FromThisMachine(From(address)));
    }

    [Fact]
    public void A_request_with_no_address_at_all_is_refused()
    {
        // Absence of evidence is not evidence of loopback. If it cannot be shown to have come from
        // here, it did not.
        Assert.False(LanAccess.FromThisMachine(From(null)));
    }

    [Fact]
    public void An_address_that_merely_looks_local_is_still_refused()
    {
        // 127.0.0.1 written as an IPv4-mapped IPv6 address is genuinely loopback and passes; a
        // private address is not, however much it looks like home.
        Assert.True(LanAccess.FromThisMachine(From("::ffff:127.0.0.1")));
        Assert.False(LanAccess.FromThisMachine(From("::ffff:192.168.1.50")));
    }
}
