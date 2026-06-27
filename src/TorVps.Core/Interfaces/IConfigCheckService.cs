using TorVps.Core.Models;

namespace TorVps.Core.Interfaces;

/// <summary>Runs startup/config diagnostics (file presence, executable paths, key settings) for the Conf tab.</summary>
public interface IConfigCheckService
{
    IReadOnlyList<ConfigCheck> Run(AppConfig config);
}
