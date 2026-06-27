using System.Diagnostics;
using System.Threading;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>
/// Tracks named slots of long-running child processes (tor.exe, mihomo.exe), always started with
/// CreateNoWindow=true and UseShellExecute=false, forwarding their stdout/stderr lines to the log.
/// </summary>
public sealed class ProcessManager : IProcessManager
{
    private readonly ILogService _logService;
    private readonly Dictionary<string, Process> _slots = new();
    private readonly Lock _slotsLock = new();

    public ProcessManager(ILogService logService)
    {
        _logService = logService;
    }

    public int Start(string key, string fileName, IReadOnlyList<string> arguments, string workingDirectory, string baseDirectory, string logPrefix)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => LogProcessLine(baseDirectory, logPrefix, e.Data);
        process.ErrorDataReceived += (_, e) => LogProcessLine(baseDirectory, logPrefix, e.Data);

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"failed to start {fileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Process? previous;
        lock (_slotsLock)
        {
            _slots.TryGetValue(key, out previous);
            _slots[key] = process;
        }
        // Mirrors Rust's slot overwrite: the previous handle is released without being killed.
        previous?.Dispose();

        return process.Id;
    }

    public bool IsAlive(string key)
    {
        lock (_slotsLock)
        {
            if (!_slots.TryGetValue(key, out var process))
                return false;

            if (!process.HasExited)
                return true;

            _slots.Remove(key);
            process.Dispose();
            return false;
        }
    }

    public async Task StopAsync(string key, string baseDirectory, string processLabel, CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_slotsLock)
        {
            if (!_slots.Remove(key, out process))
                return;
        }

        using (process)
        {
            _logService.Append(baseDirectory, LogLevel.Info, $"STOP {processLabel} pid={SafeProcessId(process)}");
            try
            {
                if (!process.HasExited)
                    process.Kill();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the check and the kill.
            }
        }
    }

    public async Task KillByImageNameAsync(string imageName, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "taskkill",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/F");
        startInfo.ArgumentList.Add("/T");
        startInfo.ArgumentList.Add("/IM");
        startInfo.ArgumentList.Add(imageName);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is not null)
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // taskkill is best-effort cleanup; failures here must not bubble up.
        }
    }

    private void LogProcessLine(string baseDirectory, string prefix, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        _logService.Append(baseDirectory, LogLevel.Info, $"[{prefix}] {line.TrimEnd()}");
    }

    private static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}
