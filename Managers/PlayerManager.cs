using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;

namespace AdvancedTeamBalance;

/// <summary>
/// Tracks per-player statistics for the current map. Team membership is always
/// read live from the server, never cached, so it cannot go stale.
/// </summary>
public static class PlayerManager
{
    private static readonly Dictionary<ulong, PlayerData> _players = [];
    private static PluginConfig _config = null!;

    public static void Initialize(PluginConfig config) => _config = config;

    public static PlayerData GetOrAdd(CCSPlayerController controller)
    {
        if (_players.TryGetValue(controller.SteamID, out var data))
        {
            data.Name = controller.PlayerName;
            return data;
        }

        var created = new PlayerData(controller.SteamID, controller.PlayerName);
        _players[controller.SteamID] = created;
        return created;
    }

    public static bool IsHumanPlayer(CCSPlayerController? controller)
        => controller != null
           && controller.IsValid
           && !controller.IsBot
           && !controller.IsHLTV
           && controller.Connected == PlayerConnectedState.Connected;

    /// <summary>Human players currently on T or CT. Spectators are never included.</summary>
    public static List<CCSPlayerController> GetTeamPlayers()
    {
        var result = new List<CCSPlayerController>();
        foreach (var controller in Utilities.GetPlayers())
        {
            if (IsHumanPlayer(controller)
                && controller.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
            {
                result.Add(controller);
            }
        }
        return result;
    }

    public static (int T, int Ct) GetTeamCounts()
    {
        int t = 0, ct = 0;
        foreach (var controller in GetTeamPlayers())
        {
            if (controller.Team == CsTeam.Terrorist) t++;
            else ct++;
        }
        return (t, ct);
    }

    /// <summary>Builds an immutable view of both teams for balance planning.</summary>
    public static List<PlayerSnapshot> Snapshot()
    {
        var result = new List<PlayerSnapshot>();
        string mode = _config.Balancing.BalanceMode;

        foreach (var controller in GetTeamPlayers())
        {
            var data = GetOrAdd(controller);
            data.Stats.Score = controller.Score;

            bool exempt = _config.Admin.ExcludeAdmins
                && AdminManager.PlayerHasPermissions(controller, _config.Admin.AdminExemptFlag);
            data.IsExemptFromSwitching = exempt;

            bool movable = !exempt
                && !data.IsImmune(_config.TeamSwitch.SwitchImmunityTime)
                && data.RoundsOnCurrentTeam >= _config.TeamSwitch.MinRoundsBeforeSwitch;

            result.Add(new PlayerSnapshot(
                controller.SteamID,
                controller.PlayerName,
                controller.Team,
                BalanceManager.GetRating(data.Stats, mode),
                controller.PawnIsAlive,
                exempt,
                movable,
                data.RoundsOnCurrentTeam));
        }

        return result;
    }

    public static void ResetAllStats()
    {
        foreach (var data in _players.Values)
            data.Stats.Reset();
    }

    public static void Clear() => _players.Clear();
}
