using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>Performs a raw, no-auth SOCKS5 CONNECT handshake to measure reachability/latency through a proxy.</summary>
public sealed class Socks5Probe : ISocks5Probe
{
    public async Task<ProbeResult> ProbeAsync(string proxyHost, int proxyPort, string destHost, int destPort, int timeoutMs, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var token = linkedCts.Token;

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(proxyHost, proxyPort, token).ConfigureAwait(false);

            var stream = client.GetStream();

            // Greeting: version 5, one method offered, 0x00 = no-auth.
            await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, token).ConfigureAwait(false);

            var greetingResponse = new byte[2];
            await stream.ReadExactlyAsync(greetingResponse, token).ConfigureAwait(false);
            if (greetingResponse[0] != 0x05 || greetingResponse[1] != 0x00)
                return Fail(stopwatch, $"SOCKS auth failed [{greetingResponse[0]:X2} {greetingResponse[1]:X2}]");

            var hostBytes = Encoding.ASCII.GetBytes(destHost);
            if (hostBytes.Length > 255)
                return Fail(stopwatch, "host too long");

            // CONNECT request, address type 0x03 = domain name.
            var request = new byte[5 + hostBytes.Length + 2];
            request[0] = 0x05;
            request[1] = 0x01;
            request[2] = 0x00;
            request[3] = 0x03;
            request[4] = (byte)hostBytes.Length;
            hostBytes.CopyTo(request, 5);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(5 + hostBytes.Length, 2), (ushort)destPort);
            await stream.WriteAsync(request, token).ConfigureAwait(false);

            var head = new byte[4];
            await stream.ReadExactlyAsync(head, token).ConfigureAwait(false);
            if (head[1] != 0)
                return Fail(stopwatch, $"SOCKS rep={head[1]}");

            // Skip over the bound address the proxy reports back (its size depends on the address type).
            var addressLength = head[3] switch
            {
                1 => 4,
                3 => await ReadDomainLengthAsync(stream, token).ConfigureAwait(false),
                4 => 16,
                _ => 0,
            };
            if (addressLength > 0)
                await stream.ReadExactlyAsync(new byte[addressLength], token).ConfigureAwait(false);

            await stream.ReadExactlyAsync(new byte[2], token).ConfigureAwait(false); // bound port, unused

            return new ProbeResult { Success = true, ElapsedMs = stopwatch.Elapsed.TotalMilliseconds, Message = "OK", CheckedAt = DateTimeOffset.UtcNow };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(stopwatch, $"timed out after {timeoutMs}ms");
        }
        catch (Exception ex)
        {
            return Fail(stopwatch, ex.Message);
        }
    }

    private static async Task<int> ReadDomainLengthAsync(NetworkStream stream, CancellationToken token)
    {
        var lengthByte = new byte[1];
        await stream.ReadExactlyAsync(lengthByte, token).ConfigureAwait(false);
        return lengthByte[0];
    }

    private static ProbeResult Fail(Stopwatch stopwatch, string message) => new()
    {
        Success = false,
        ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow,
    };
}
