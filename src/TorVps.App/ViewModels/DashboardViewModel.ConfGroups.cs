namespace TorVps.App.ViewModels;

public partial class DashboardViewModel
{
    private static readonly string[] CheckGroupOrder = ["torrc_manager.cfg", "mihomo.yaml", "bridges.txt"];

    private static readonly Dictionary<string, string> CheckNameToGroup = new()
    {
        ["torrc_manager.cfg"] = "torrc_manager.cfg",
        ["tor.exe"] = "torrc_manager.cfg",
        ["lyrebird.exe"] = "torrc_manager.cfg",
        ["mihomo.exe"] = "torrc_manager.cfg",
        ["control_auth_cookie"] = "torrc_manager.cfg",
        ["monitor password"] = "torrc_manager.cfg",
        ["mihomo.yaml"] = "mihomo.yaml",
        ["onion target"] = "mihomo.yaml",
        ["bridges.txt"] = "bridges.txt",
    };

    private void UpdateCheckGroups()
    {
        CheckGroups.Clear();
        foreach (var groupName in CheckGroupOrder)
        {
            var items = Checks.Where(c => CheckNameToGroup.GetValueOrDefault(c.Name) == groupName).ToList();
            if (items.Count > 0)
                CheckGroups.Add(new ConfigCheckGroup { GroupName = groupName, Items = items });
        }
    }
}
