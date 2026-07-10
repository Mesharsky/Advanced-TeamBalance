namespace AdvancedTeamBalance;

/// <summary>
/// Per-player bookkeeping kept for the duration of a map. Survives reconnects
/// so leaving and rejoining does not wipe stats. Team membership is never
/// stored here; it is always read live from the server.
/// </summary>
public class PlayerData(ulong steamId, string name)
{
    public ulong SteamId { get; } = steamId;
    public string Name { get; set; } = name;

    /// <summary>Refreshed from admin flags on every snapshot. Exempt players are never auto-moved.</summary>
    public bool IsExemptFromSwitching { get; set; }

    public PlayerStats Stats { get; } = new();

    public int RoundsOnCurrentTeam { get; set; }
    public int TimesSwitched { get; set; }
    public DateTime? LastPluginSwitch { get; set; }

    public bool IsImmune(int immunitySeconds)
        => LastPluginSwitch is { } switchedAt
           && (DateTime.UtcNow - switchedAt).TotalSeconds < immunitySeconds;

    /// <summary>Called when the plugin moves this player to another team.</summary>
    public void OnSwitchedByPlugin()
    {
        TimesSwitched++;
        RoundsOnCurrentTeam = 0;
        LastPluginSwitch = DateTime.UtcNow;
    }

    /// <summary>Called when the player changes team on their own.</summary>
    public void OnVoluntarySwitch() => RoundsOnCurrentTeam = 0;
}

public class PlayerStats
{
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }

    /// <summary>Scoreboard score, synced from the game before each balance pass.</summary>
    public int Score { get; set; }

    public int RoundsPlayed { get; set; }
    public int RoundsWon { get; set; }

    public double KDRatio => (double)Kills / Math.Max(1, Deaths);
    public double KDARatio => (Kills + Assists * 0.5) / Math.Max(1, Deaths);

    /// <summary>Fraction of played rounds won, 0.0 to 1.0.</summary>
    public double WinRate => RoundsPlayed == 0 ? 0 : (double)RoundsWon / RoundsPlayed;

    public void Reset()
    {
        Kills = 0;
        Deaths = 0;
        Assists = 0;
        Score = 0;
        RoundsPlayed = 0;
        RoundsWon = 0;
    }
}
