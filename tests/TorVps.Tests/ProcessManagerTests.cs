using Moq;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;
using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

/// <summary>
/// ProcessManager's only dependency is ILogService, so these tests mock it with Moq and exercise the
/// real Process lifecycle against short-lived Windows commands (cmd.exe/ping) rather than tor.exe/mihomo.exe.
/// </summary>
public class ProcessManagerTests
{
    private readonly Mock<ILogService> _logService = new();
    private readonly ProcessManager _manager;

    public ProcessManagerTests()
    {
        _manager = new ProcessManager(_logService.Object);
    }

    [Fact]
    public async Task Start_ReturnsPositiveProcessId_AndProcessExitsOnItsOwn()
    {
        var pid = _manager.Start("test-exit", "cmd.exe", ["/c", "exit", "0"], Path.GetTempPath(), Path.GetTempPath(), "TEST");

        Assert.True(pid > 0);
        Assert.True(await WaitUntilAsync(() => !_manager.IsAlive("test-exit")), "process should have exited on its own");
    }

    [Fact]
    public async Task Start_ForwardsStdoutLinesToLogService()
    {
        _manager.Start("test-echo", "cmd.exe", ["/c", "echo", "hello-from-test"], Path.GetTempPath(), Path.GetTempPath(), "TEST");

        await WaitUntilAsync(() => !_manager.IsAlive("test-echo"));

        _logService.Verify(
            l => l.Append(Path.GetTempPath(), LogLevel.Info, It.Is<string>(s => s.Contains("hello-from-test"))),
            Times.AtLeastOnce);
    }

    [Fact]
    public void IsAlive_ReturnsFalseForUnknownKey()
    {
        Assert.False(_manager.IsAlive("no-such-slot"));
    }

    [Fact]
    public async Task StopAsync_LogsStopMessage_AndKillsRunningProcess()
    {
        _manager.Start("test-long", "ping", ["-n", "50", "127.0.0.1"], Path.GetTempPath(), Path.GetTempPath(), "TEST");
        Assert.True(_manager.IsAlive("test-long"));

        await _manager.StopAsync("test-long", Path.GetTempPath(), "test-process");

        Assert.False(_manager.IsAlive("test-long"));
        _logService.Verify(
            l => l.Append(Path.GetTempPath(), LogLevel.Info, It.Is<string>(s => s.StartsWith("STOP test-process pid="))),
            Times.Once);
    }

    [Fact]
    public async Task StopAsync_OnUnknownKey_DoesNotLogOrThrow()
    {
        await _manager.StopAsync("no-such-slot", Path.GetTempPath(), "irrelevant");

        _logService.Verify(l => l.Append(It.IsAny<string>(), It.IsAny<LogLevel>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task KillByImageNameAsync_DoesNotThrow_ForNonExistentImage()
    {
        await _manager.KillByImageNameAsync("definitely-not-a-real-process-12345.exe");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(25);
        }
        return condition();
    }
}
