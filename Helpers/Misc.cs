using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace Mesharsky_TeamBalance;

public partial class Mesharsky_TeamBalance
{
    private static int GetTeamPlayerCount(CsTeam team)
    {
        return Utilities.GetPlayers().Count(p => p.Team == team);
    }

    public static int GetTeamScore(CsTeam team)
    {
      var teamManagers = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");

      foreach (var teamManager in teamManagers)
      {
        if ((int)team == teamManager.TeamNum)
        {
          return teamManager.Score;
        }
      }

      return 0;
    }


    private static void PrintDebugMessage(string message)
    {
        Console.WriteLine($"[Team Balance] {message}");
    }

    private static bool ChangePlayerTeam(ulong steamId, CsTeam newTeam)
    {
        var player = Utilities.GetPlayerFromSteamId(steamId);
        if (player == null || !player.IsValid)
        {
            PrintDebugMessage($"Failed to switch team for player with SteamID: {steamId}. Player not found or invalid.");
            return false;
        }

        if (player.TeamNum == (int)newTeam)
        {
            PrintDebugMessage($"Player {player.PlayerName} is already in team {newTeam}. No switch needed.");
            return true;
        }

        player.SwitchTeam(newTeam);

        playerCache.AddOrUpdate(steamId, 
            (key) =>
            {
                var newPlayer = new Player
                {
                    PlayerName = player.PlayerName,
                    PlayerSteamID = player.SteamID,
                    Team = (int)newTeam,
                    Score = player.Score
                };
                PrintDebugMessage($"Added {newPlayer.PlayerName} to cache with team {newPlayer.Team}");
                return newPlayer;
            }, 
            (key, cachedPlayer) =>
            {
                cachedPlayer.Team = (int)newTeam;
                PrintDebugMessage($"Player {cachedPlayer.PlayerName} switched to {newTeam} and updated in cache.");
                return cachedPlayer;
            });

        return true;
    }


    private HookResult Command_JoinTeam(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && player.IsValid)
        {
            int startIndex = 0;
            if (info.ArgCount > 0 && info.ArgByIndex(0).ToLower() == "jointeam")
            {
                startIndex = 1;
            }

            if (info.ArgCount > startIndex)
            {
                string teamArg = info.ArgByIndex(startIndex);

                if (int.TryParse(teamArg, out int teamId))
                {
                    if (teamId >= (int)CsTeam.Spectator && teamId <= (int)CsTeam.CounterTerrorist)
                    {
                        if (playerCache.TryGetValue(player.SteamID, out var cachedPlayer))
                        {
                            cachedPlayer.Team = teamId;
                            PrintDebugMessage($"Player {cachedPlayer.PlayerName} updated to team {teamId} in cache.");
                        }
                    }
                }
            }
        }

        return HookResult.Continue;
    }

    private static void UpdatePlayerTeamsInCache()
    {
        PrintDebugMessage("Updating player teams in cache...");

        var allPlayers = Utilities.GetPlayers();

        foreach (var player in allPlayers)
        {
            if (player == null || !player.IsValid || player.IsBot)
                continue;

            if (playerCache.TryGetValue(player.SteamID, out var cachedPlayer))
            {
                cachedPlayer.Team = player.TeamNum;
                PrintDebugMessage($"Updated {cachedPlayer.PlayerName} in cache to team {cachedPlayer.Team}");
            }
            else
            {
                var newPlayer = new Player
                {
                    PlayerName = player.PlayerName,
                    PlayerSteamID = player.SteamID,
                    Team = player.TeamNum,
                    Score = player.Score
                };

                playerCache.TryAdd(player.SteamID, newPlayer);
                PrintDebugMessage($"Added {newPlayer.PlayerName} to cache with team {newPlayer.Team}");
            }
        }
    }
}