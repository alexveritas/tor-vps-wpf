namespace TorVps.Core.Interfaces;

/// <summary>
/// Manages named slots of long-running child processes (tor.exe, mihomo.exe) started with
/// CreateNoWindow=true. Stdout/stderr lines are forwarded line-by-line to the log under a prefix.
/// </summary>
public interface IProcessManager
{
    /// <summary>Starts a process under the given slot key and returns its process id. Throws on failure to start.</summary>
    int Start(string key, string fileName, IReadOnlyList<string> arguments, string workingDirectory, string baseDirectory, string logPrefix);

    /// <summary>True if the process tracked under this slot is still running. Clears the slot once it has exited.</summary>
    bool IsAlive(string key);

    /// <summary>Kills the process tracked under this slot (if any), awaits its exit, and clears the slot.</summary>
    Task StopAsync(string key, string baseDirectory, string processLabel, CancellationToken cancellationToken = default);

    /// <summary>Force-kills every process matching an image name (e.g. "mihomo.exe"), independent of slot tracking.</summary>
    Task KillByImageNameAsync(string imageName, CancellationToken cancellationToken = default);
}
