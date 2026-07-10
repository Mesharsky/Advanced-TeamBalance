using CounterStrikeSharp.API.Core;

namespace AdvancedTeamBalance;

public class PluginConfig : BasePluginConfig
{
    public PluginConfig()
    {
        Version = 2;
    }

    public GeneralSettings General { get; set; } = new();
    public TeamSwitchSettings TeamSwitch { get; set; } = new();
    public BalancingSettings Balancing { get; set; } = new();
    public ScrambleSettings Scramble { get; set; } = new();
    public MessageSettings Messages { get; set; } = new();
    public AdminSettings Admin { get; set; } = new();
}

public class GeneralSettings
{
    /// <summary>Tag shown in front of chat messages. Supports color tags.</summary>
    public string PluginTag { get; set; } = "{red}[TeamBalance]{default}";

    /// <summary>Minimum players on teams before skill balancing activates.</summary>
    public int MinimumPlayers { get; set; } = 6;

    /// <summary>Verbose logging for troubleshooting.</summary>
    public bool EnableDebug { get; set; } = false;
}

public class TeamSwitchSettings
{
    /// <summary>
    /// When balancing runs. "OnRoundStart" and "OnRoundEnd" evaluate the full balance
    /// at the next round start. "OnPlayerJoin" and "OnPlayerDisconnect" additionally
    /// enforce team sizes right away, moving dead players only.
    /// </summary>
    public List<string> BalanceTriggers { get; set; } = ["OnRoundStart", "OnPlayerJoin"];

    /// <summary>Maximum allowed team size difference. Enforced with highest priority.</summary>
    public int MaxTeamSizeDifference { get; set; } = 1;

    /// <summary>Enforce the size limit even when below MinimumPlayers.</summary>
    public bool AlwaysEnforceTeamSizes { get; set; } = true;

    /// <summary>Rounds a player must stay on a team before qualifying for a switch.</summary>
    public int MinRoundsBeforeSwitch { get; set; } = 2;

    /// <summary>Seconds of immunity after a plugin-initiated switch.</summary>
    public int SwitchImmunityTime { get; set; } = 60;

    /// <summary>Maximum player pairs swapped for skill per balance pass.</summary>
    public int MaxSkillSwapsPerRound { get; set; } = 2;

    /// <summary>Balance during warmup.</summary>
    public bool BalanceDuringWarmup { get; set; } = false;
}

public class BalancingSettings
{
    /// <summary>Rating used for balancing: "KD", "KDA", "Score" or "WinRate".</summary>
    public string BalanceMode { get; set; } = "KDA";

    /// <summary>
    /// Relative team strength difference that triggers skill swaps.
    /// 0.2 means teams may differ by up to 20 percent before the plugin steps in.
    /// </summary>
    public double SkillDifferenceThreshold { get; set; } = 0.2;

    /// <summary>Skip skill swaps entirely and only enforce team sizes.</summary>
    public bool OnlyBalanceByTeamSize { get; set; } = false;

    /// <summary>Consecutive lost rounds before the losing team gets a boost. 0 disables.</summary>
    public int BoostAfterLoseStreak { get; set; } = 5;

    /// <summary>How much stronger (in percent) the losing team is allowed to end up.</summary>
    public int BoostPercentage { get; set; } = 20;

    /// <summary>Scale the boost with the length of the losing streak using BoostTiers.</summary>
    public bool ProgressiveBoost { get; set; } = false;

    /// <summary>Losing streak length mapped to boost percentage.</summary>
    public Dictionary<int, int> BoostTiers { get; set; } = new()
    {
        { 3, 10 },
        { 5, 20 },
        { 7, 30 }
    };
}

public class ScrambleSettings
{
    /// <summary>"Random" shuffles teams, "Skill" deals players out evenly by rating.</summary>
    public string Mode { get; set; } = "Random";

    /// <summary>Scramble after one team wins this many rounds in a row. 0 disables.</summary>
    public int AfterWinStreak { get; set; } = 5;

    /// <summary>Scramble at halftime.</summary>
    public bool AfterHalftime { get; set; } = false;

    /// <summary>Reset player statistics after a scramble.</summary>
    public bool ResetStats { get; set; } = true;
}

public class MessageSettings
{
    /// <summary>Announce balance operations in chat.</summary>
    public bool AnnounceBalancing { get; set; } = true;

    /// <summary>Tell switched players they were moved.</summary>
    public bool NotifySwitchedPlayers { get; set; } = true;

    /// <summary>Include the reason (metric, streaks, team sizes) in announcements.</summary>
    public bool ExplainBalanceReason { get; set; } = true;
}

public class AdminSettings
{
    /// <summary>Exclude admins from automatic switches.</summary>
    public bool ExcludeAdmins { get; set; } = true;

    /// <summary>Flag that grants exemption from automatic switches.</summary>
    public string AdminExemptFlag { get; set; } = "@css/ban";

    /// <summary>Flag required for this plugin's admin commands.</summary>
    public string AdminCommandFlag { get; set; } = "@css/generic";
}
