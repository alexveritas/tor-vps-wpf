using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>
/// Speaks the Tor Control Protocol over a fresh TCP connection per query: cookie AUTHENTICATE followed
/// by a batch of GETINFO commands and QUIT. Tracks the previous traffic sample internally to report
/// DownMbit/UpMbit as a rate rather than a cumulative counter.
/// </summary>
public sealed class TorControlClient : ITorControlClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMilliseconds(900);

    private static readonly Regex ProgressPattern = new(@"PROGRESS=(\d+)");
    private static readonly Regex BuiltPattern = new(@"\bBUILT\b");
    private static readonly Regex FingerprintPattern = new(@"\$([0-9A-Fa-f]{40})");
    private static readonly Regex TrafficReadPattern = new(@"traffic/read=(\d+)");
    private static readonly Regex TrafficWrittenPattern = new(@"traffic/written=(\d+)");

    private readonly Lock _trafficLock = new();
    private ulong? _previousReadBytes;
    private ulong? _previousWrittenBytes;
    private DateTimeOffset? _previousSampleAt;

    public async Task<TorStatus> GetStatusAsync(string controlHost, int controlPort, string cookieFilePath, CancellationToken cancellationToken = default)
    {
        string response;
        try
        {
            response = await QueryAsync(controlHost, controlPort, cookieFilePath,
            [
                "GETINFO status/bootstrap-phase",
                "GETINFO circuit-status",
                "GETINFO entry-guards",
                "GETINFO traffic/read",
                "GETINFO traffic/written",
            ], cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new TorStatus();
        }

        var controlOk = response.Contains("250 OK", StringComparison.Ordinal)
            || response.Contains("250-status/bootstrap-phase", StringComparison.Ordinal)
            || response.Contains("250+circuit-status", StringComparison.Ordinal);

        var progressMatch = ProgressPattern.Match(response);
        var bootstrap = progressMatch.Success && double.TryParse(progressMatch.Groups[1].Value, out var progress) ? progress : 0.0;

        var builtCircuits = BuiltPattern.Matches(response).Count;
        var (upGuards, downGuards) = ParseEntryGuards(response);

        var readMatch = TrafficReadPattern.Match(response);
        var readBytes = readMatch.Success && ulong.TryParse(readMatch.Groups[1].Value, out var rd) ? rd : (ulong?)null;
        var writtenMatch = TrafficWrittenPattern.Match(response);
        var writtenBytes = writtenMatch.Success && ulong.TryParse(writtenMatch.Groups[1].Value, out var wr) ? wr : (ulong?)null;

        var (downMbit, upMbit) = UpdateTrafficRate(readBytes, writtenBytes);

        return new TorStatus
        {
            ControlAuthOk = controlOk,
            BootstrapPercent = bootstrap,
            BuiltCircuits = builtCircuits,
            ActiveGuardFingerprints = upGuards,
            DownGuardFingerprints = downGuards,
            TrafficReadBytes = readBytes,
            TrafficWrittenBytes = writtenBytes,
            DownMbit = downMbit,
            UpMbit = upMbit,
        };
    }

    /// <summary>
    /// Parses the GETINFO entry-guards block into fingerprints Tor currently reaches (status <c>up</c>) versus
    /// tried-but-unreachable (<c>down</c>/<c>unusable</c>). Guards Tor has never connected to end up in neither list.
    /// Crucially the status word is honoured — every configured bridge appears here regardless of reachability, so
    /// matching bare fingerprints would mark them all as reachable.
    /// </summary>
    public static (IReadOnlyList<string> Up, IReadOnlyList<string> Down) ParseEntryGuards(string response)
    {
        var up = new List<string>();
        var down = new List<string>();
        var inBlock = false;

        foreach (var raw in response.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!inBlock)
            {
                if (line.StartsWith("250+entry-guards=", StringComparison.Ordinal) || line.StartsWith("250-entry-guards=", StringComparison.Ordinal))
                    inBlock = true;
                continue;
            }
            if (line == ".")
                break;

            var fingerprintMatch = FingerprintPattern.Match(line);
            if (!fingerprintMatch.Success)
                continue;
            var fingerprint = fingerprintMatch.Groups[1].Value.ToUpperInvariant();

            // Line shape: "$FINGERPRINT[~nickname] <status> [<timestamp>]" — status is the second token.
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var status = tokens.Length >= 2 ? tokens[1].ToLowerInvariant() : string.Empty;
            if (status == "up")
                up.Add(fingerprint);
            else if (status is "down" or "unusable")
                down.Add(fingerprint);
        }

        return (up, down);
    }

    private (double DownMbit, double UpMbit) UpdateTrafficRate(ulong? readBytes, ulong? writtenBytes)
    {
        if (readBytes is null || writtenBytes is null)
            return (0.0, 0.0);

        var now = DateTimeOffset.UtcNow;
        var downMbit = 0.0;
        var upMbit = 0.0;

        lock (_trafficLock)
        {
            if (_previousReadBytes is { } prevRead && _previousWrittenBytes is { } prevWritten && _previousSampleAt is { } prevAt)
            {
                var elapsedSeconds = Math.Max(0.25, (now - prevAt).TotalSeconds);
                downMbit = SubtractSaturating(readBytes.Value, prevRead) * 8.0 / 1_000_000.0 / elapsedSeconds;
                upMbit = SubtractSaturating(writtenBytes.Value, prevWritten) * 8.0 / 1_000_000.0 / elapsedSeconds;
            }

            _previousReadBytes = readBytes;
            _previousWrittenBytes = writtenBytes;
            _previousSampleAt = now;
        }

        return (downMbit, upMbit);
    }

    private static double SubtractSaturating(ulong current, ulong previous) => current >= previous ? current - previous : 0;

    private static async Task<string> QueryAsync(string controlHost, int controlPort, string cookieFilePath, string[] commands, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();

        using (var connectTimeoutCts = new CancellationTokenSource(ConnectTimeout))
        using (var connectLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectTimeoutCts.Token))
        {
            await client.ConnectAsync(controlHost, controlPort, connectLinkedCts.Token).ConfigureAwait(false);
        }

        var stream = client.GetStream();

        var commandText = BuildAuthenticateCommand(cookieFilePath);
        foreach (var command in commands)
            commandText += command + "\r\n";
        commandText += "QUIT\r\n";

        using var responseTimeoutCts = new CancellationTokenSource(ResponseTimeout);
        using var responseLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, responseTimeoutCts.Token);

        await stream.WriteAsync(Encoding.ASCII.GetBytes(commandText), responseLinkedCts.Token).ConfigureAwait(false);
        return await ReadAvailableAsync(stream, responseLinkedCts.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads until the peer closes the connection (the normal case, once Tor answers QUIT) or the
    /// response timeout elapses. On timeout, returns whatever was read so far instead of throwing,
    /// matching the original's "discard the read error, parse the partial buffer" behavior.
    /// </summary>
    private static async Task<string> ReadAvailableAsync(NetworkStream stream, CancellationToken linkedToken, CancellationToken externalToken)
    {
        var buffer = new byte[4096];
        using var received = new MemoryStream();
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, linkedToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                received.Write(buffer, 0, read);
            }
        }
        catch (OperationCanceledException) when (!externalToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        return Encoding.ASCII.GetString(received.ToArray());
    }

    private static string BuildAuthenticateCommand(string cookieFilePath)
    {
        try
        {
            var cookie = File.ReadAllBytes(cookieFilePath);
            return $"AUTHENTICATE {Convert.ToHexString(cookie).ToLowerInvariant()}\r\n";
        }
        catch (Exception)
        {
            return "AUTHENTICATE\r\n";
        }
    }
}
