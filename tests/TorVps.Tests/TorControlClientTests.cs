using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

public class TorControlClientTests
{
    private const string A = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string B = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string C = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private const string D = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";
    private const string E = "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE";

    private static string Response(params string[] guardLines) =>
        "250-status/bootstrap-phase=NOTICE BOOTSTRAP PROGRESS=100 TAG=done\r\n"
        + "250+entry-guards=\r\n"
        + string.Join("\r\n", guardLines) + "\r\n"
        + ".\r\n"
        + "250 OK\r\n";

    [Fact]
    public void ParseEntryGuards_ClassifiesByStatus_NotByPresence()
    {
        var response = Response(
            $"${A}~alice up",
            $"${B} down 2026-07-06 04:55:23",
            $"${C} never-connected",
            $"${D}~x unusable 2026-07-03 01:52:26",
            $"${E} up");

        var (up, down) = TorControlClient.ParseEntryGuards(response);

        Assert.Equal(new[] { A, E }, up);
        Assert.Equal(new[] { B, D }, down);          // down + unusable
        Assert.DoesNotContain(C, up);                // never-connected is neither
        Assert.DoesNotContain(C, down);
    }

    [Fact]
    public void ParseEntryGuards_UppercasesFingerprints()
    {
        var (up, _) = TorControlClient.ParseEntryGuards(Response($"${A.ToLowerInvariant()} up"));
        Assert.Equal(new[] { A }, up);
    }

    [Fact]
    public void ParseEntryGuards_StopsAtBlockTerminator()
    {
        // A fingerprint after the closing '.' (e.g. in circuit-status) must not be treated as a guard.
        var response = Response($"${A} up") + $"250+circuit-status=\r\n1 BUILT ${B}~relay\r\n.\r\n250 OK\r\n";

        var (up, down) = TorControlClient.ParseEntryGuards(response);

        Assert.Equal(new[] { A }, up);
        Assert.Empty(down);
    }

    [Fact]
    public void ParseEntryGuards_ReturnsEmpty_WhenNoBlock()
    {
        var (up, down) = TorControlClient.ParseEntryGuards("250 OK\r\n");
        Assert.Empty(up);
        Assert.Empty(down);
    }
}
