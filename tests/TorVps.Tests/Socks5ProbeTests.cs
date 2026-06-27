using System.Net;
using System.Net.Sockets;
using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

public class Socks5ProbeTests
{
    private readonly Socks5Probe _probe = new();

    [Fact]
    public async Task ProbeAsync_ReturnsFailure_WhenProxyPortIsClosed()
    {
        var port = GetLikelyFreePort();

        var result = await _probe.ProbeAsync("127.0.0.1", port, "example.com", 80, timeoutMs: 2000);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsSuccess_WhenServerCompletesHandshake()
    {
        using var server = new FakeSocksServer(FakeSocksServer.Outcome.Success);
        var result = await _probe.ProbeAsync("127.0.0.1", server.Port, "example.com", 443, timeoutMs: 3000);

        Assert.True(result.Success);
        Assert.Equal("OK", result.Message);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFailure_WhenServerRejectsAuth()
    {
        using var server = new FakeSocksServer(FakeSocksServer.Outcome.RejectAuth);
        var result = await _probe.ProbeAsync("127.0.0.1", server.Port, "example.com", 443, timeoutMs: 3000);

        Assert.False(result.Success);
        Assert.Contains("auth failed", result.Message);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFailure_WhenConnectRequestIsRejected()
    {
        using var server = new FakeSocksServer(FakeSocksServer.Outcome.RejectConnect);
        var result = await _probe.ProbeAsync("127.0.0.1", server.Port, "example.com", 443, timeoutMs: 3000);

        Assert.False(result.Success);
        Assert.Contains("rep=", result.Message);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFailure_OnTimeout()
    {
        using var server = new FakeSocksServer(FakeSocksServer.Outcome.Hang);
        var result = await _probe.ProbeAsync("127.0.0.1", server.Port, "example.com", 443, timeoutMs: 150);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Message);
    }

    private static int GetLikelyFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Minimal scripted SOCKS5 server: accepts one connection and plays out a fixed response.</summary>
    private sealed class FakeSocksServer : IDisposable
    {
        public enum Outcome
        {
            Success,
            RejectAuth,
            RejectConnect,
            Hang,
        }

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptTask;

        public int Port { get; }

        public FakeSocksServer(Outcome outcome)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptTask = AcceptOnceAsync(outcome, _cts.Token);
        }

        private async Task AcceptOnceAsync(Outcome outcome, CancellationToken token)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                using var stream = client.GetStream();

                var greeting = new byte[3];
                await stream.ReadExactlyAsync(greeting, token).ConfigureAwait(false);

                if (outcome == Outcome.RejectAuth)
                {
                    await stream.WriteAsync(new byte[] { 0x05, 0xFF }, token).ConfigureAwait(false);
                    return;
                }
                await stream.WriteAsync(new byte[] { 0x05, 0x00 }, token).ConfigureAwait(false);

                var head = new byte[5];
                await stream.ReadExactlyAsync(head, token).ConfigureAwait(false);
                var domainLength = head[4];
                var rest = new byte[domainLength + 2];
                await stream.ReadExactlyAsync(rest, token).ConfigureAwait(false);

                switch (outcome)
                {
                    case Outcome.Hang:
                        await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                        break;
                    case Outcome.Success:
                        await stream.WriteAsync(new byte[] { 0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }, token).ConfigureAwait(false);
                        break;
                    case Outcome.RejectConnect:
                        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }, token).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
