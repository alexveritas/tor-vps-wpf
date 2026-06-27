using System.Net.Sockets;

namespace TorVps.Core.Services;

/// <summary>Shared "is this TCP port accepting connections" probe used by TorService and MihomoService.</summary>
internal static class TcpPortCheck
{
    public static async Task<bool> IsOpenAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, linkedCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
