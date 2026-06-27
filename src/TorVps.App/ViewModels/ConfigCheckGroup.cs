using TorVps.Core.Models;

namespace TorVps.App.ViewModels;

/// <summary>Groups ConfigCheck rows by the source file they come from, for the Conf tab's per-file blocks.</summary>
public sealed class ConfigCheckGroup
{
    public required string GroupName { get; init; }
    public required IReadOnlyList<ConfigCheck> Items { get; init; }
}
