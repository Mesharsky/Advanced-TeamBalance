using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;

namespace AdvancedTeamBalance;

public sealed class Plugin : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "Advanced Team Balance";
    public override string ModuleAuthor => "Mesharsky";
    public override string ModuleDescription => "Team balancing for CS2 servers";
    public override string ModuleVersion => "6.0.0";

    public PluginConfig Config { get; set; } = new();

    public static Plugin? Instance { get; private set; }
    public static IStringLocalizer? Localization { get; private set; }

    public override void Load(bool hotReload)
    {
        Instance = this;
        Localization = Localizer;

        InitializeManagers();
        RegisterEventHandlers();
        RegisterCommands();

        Log.Info($"Plugin loaded (version {ModuleVersion})");
    }

    public override void OnAllPluginsLoaded(bool isReload)
    {
        Localization = Localizer;
    }

    public override void Unload(bool hotReload)
    {
        EventManager.ResetMatchState();
        Instance = null;
        Localization = null;
    }

    public void OnConfigParsed(PluginConfig config)
    {
        Config = config;
        ValidateConfiguration();
        InitializeManagers();
    }

    private void InitializeManagers()
    {
        Log.DebugEnabled = Config.General.EnableDebug;
        PlayerManager.Initialize(Config);
        BalanceManager.Initialize(Config);
        EventManager.Initialize(Config);
    }

    private void ValidateConfiguration()
    {
        var general = Config.General;
        var teamSwitch = Config.TeamSwitch;
        var balancing = Config.Balancing;
        var scramble = Config.Scramble;

        if (general.MinimumPlayers < 0)
        {
            Log.Warning("MinimumPlayers cannot be negative, using 0");
            general.MinimumPlayers = 0;
        }

        if (teamSwitch.MaxTeamSizeDifference < 0)
        {
            Log.Warning("MaxTeamSizeDifference cannot be negative, using 1");
            teamSwitch.MaxTeamSizeDifference = 1;
        }

        if (teamSwitch.MinRoundsBeforeSwitch < 0)
            teamSwitch.MinRoundsBeforeSwitch = 0;

        if (teamSwitch.SwitchImmunityTime < 0)
            teamSwitch.SwitchImmunityTime = 0;

        if (teamSwitch.MaxSkillSwapsPerRound < 0)
            teamSwitch.MaxSkillSwapsPerRound = 0;

        // Scramble stopped being a balance mode in 6.0. Map old configs over.
        if (balancing.BalanceMode.StartsWith("Scramble", StringComparison.OrdinalIgnoreCase))
        {
            scramble.Mode = balancing.BalanceMode.Contains("Skill", StringComparison.OrdinalIgnoreCase)
                ? "Skill"
                : "Random";
            balancing.BalanceMode = "KDA";
            Log.Warning($"BalanceMode 'Scramble*' is no longer supported. Scramble.Mode set to '{scramble.Mode}', BalanceMode set to 'KDA'. See the Scramble config section.");
        }

        string[] validModes = ["KD", "KDA", "Score", "WinRate"];
        if (!validModes.Contains(balancing.BalanceMode, StringComparer.OrdinalIgnoreCase))
        {
            Log.Warning($"Invalid BalanceMode '{balancing.BalanceMode}', using 'KDA'");
            balancing.BalanceMode = "KDA";
        }

        if (balancing.SkillDifferenceThreshold is < 0.0 or > 1.0)
        {
            Log.Warning("SkillDifferenceThreshold must be between 0.0 and 1.0, using 0.2");
            balancing.SkillDifferenceThreshold = 0.2;
        }

        if (balancing.BoostPercentage is < 0 or > 100)
        {
            Log.Warning("BoostPercentage must be between 0 and 100, using 20");
            balancing.BoostPercentage = 20;
        }

        if (balancing.BoostAfterLoseStreak < 0)
            balancing.BoostAfterLoseStreak = 0;

        foreach (var tier in balancing.BoostTiers.Where(t => t.Key < 0 || t.Value is < 0 or > 100).ToList())
        {
            Log.Warning($"Removing invalid BoostTier [{tier.Key}: {tier.Value}]");
            balancing.BoostTiers.Remove(tier.Key);
        }

        string[] validScrambleModes = ["Random", "Skill"];
        if (!validScrambleModes.Contains(scramble.Mode, StringComparer.OrdinalIgnoreCase))
        {
            Log.Warning($"Invalid Scramble.Mode '{scramble.Mode}', using 'Random'");
            scramble.Mode = "Random";
        }

        if (scramble.AfterWinStreak < 0)
            scramble.AfterWinStreak = 0;

        string[] validTriggers = ["OnRoundStart", "OnRoundEnd", "OnPlayerJoin", "OnPlayerDisconnect"];
        var invalid = teamSwitch.BalanceTriggers
            .Where(t => !validTriggers.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (invalid.Count > 0)
        {
            Log.Warning($"Removing invalid BalanceTriggers: {string.Join(", ", invalid)}");
            teamSwitch.BalanceTriggers = teamSwitch.BalanceTriggers
                .Where(t => validTriggers.Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void RegisterEventHandlers()
    {
        RegisterEventHandler<EventRoundPrestart>(EventManager.OnRoundPrestart);
        RegisterEventHandler<EventRoundEnd>(EventManager.OnRoundEnd);
        RegisterEventHandler<EventAnnouncePhaseEnd>(EventManager.OnAnnouncePhaseEnd);
        RegisterEventHandler<EventPlayerConnectFull>(EventManager.OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(EventManager.OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerDeath>(EventManager.OnPlayerDeath);

        RegisterListener<Listeners.OnMapEnd>(EventManager.ResetMatchState);
    }

    private void RegisterCommands()
    {
        AddCommand("css_tbstats", "Show your team balance statistics", CommandStats);
        AddCommand("css_balancepreview", "Preview what the next balance pass would do", CommandPreviewBalance);
        AddCommand("css_scramble", "Queue a team scramble for the next round", CommandScramble);
        AddCommand("css_tbreset", "Reset balance statistics and win streaks", CommandReset);

        AddCommandListener("jointeam", CommandJoinTeam, HookMode.Pre);
    }

    private bool HasCommandAccess(CCSPlayerController? player)
        => player == null || AdminManager.PlayerHasPermissions(player, Config.Admin.AdminCommandFlag);

    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    private void CommandStats(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot)
            return;

        var data = PlayerManager.GetOrAdd(player);
        var stats = data.Stats;

        ChatHelper.PrintLocalizedChat(player, true, "stats.header");
        ChatHelper.PrintLocalizedChat(player, true, "stats.kd", stats.KDRatio.ToString("F2"));
        ChatHelper.PrintLocalizedChat(player, true, "stats.kda", stats.KDARatio.ToString("F2"));
        ChatHelper.PrintLocalizedChat(player, true, "stats.score", stats.Score);
        ChatHelper.PrintLocalizedChat(player, true, "stats.winrate", (stats.WinRate * 100).ToString("F1"));
        ChatHelper.PrintLocalizedChat(player, true, "stats.rounds", stats.RoundsPlayed);
        ChatHelper.PrintLocalizedChat(player, true, "stats.switches", data.TimesSwitched);
    }

    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    private void CommandPreviewBalance(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot)
            return;

        if (!HasCommandAccess(player))
        {
            ChatHelper.PrintLocalizedChat(player, true, "command.nopermission");
            return;
        }

        var snapshot = PlayerManager.Snapshot();
        var t = snapshot.Where(p => p.Team == CsTeam.Terrorist).ToList();
        var ct = snapshot.Where(p => p.Team == CsTeam.CounterTerrorist).ToList();

        double avgT = t.Count > 0 ? t.Average(p => p.Rating) : 0;
        double avgCt = ct.Count > 0 ? ct.Average(p => p.Rating) : 0;

        var plan = EventManager.ComputePrestartPlan(snapshot);

        ChatHelper.PrintLocalizedChat(player, true, "preview.header");
        ChatHelper.PrintLocalizedChat(player, true, "preview.current",
            t.Count, avgT.ToString("F2"), ct.Count, avgCt.ToString("F2"));

        if (plan.IsEmpty)
        {
            ChatHelper.PrintLocalizedChat(player, true, "preview.none");
            return;
        }

        foreach (var move in plan.Moves)
        {
            ChatHelper.PrintLocalizedChat(player, true, "preview.move",
                move.Player.Name, move.To == CsTeam.Terrorist ? "T" : "CT");
        }

        ChatHelper.PrintLocalizedChat(player, true, "preview.moves", plan.SizeMoves, plan.SkillSwaps);
    }

    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    private void CommandScramble(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasCommandAccess(player))
        {
            ChatHelper.PrintLocalizedChat(player, true, "command.nopermission");
            return;
        }

        if (EventManager.IsScramblePending)
        {
            if (player != null)
                ChatHelper.PrintLocalizedChat(player, true, "scramble.already_queued");
            else
                info.ReplyToCommand("[TeamBalance] A scramble is already queued.");
            return;
        }

        EventManager.QueueScramble();

        if (player == null)
            info.ReplyToCommand("[TeamBalance] Scramble queued for the next round.");
    }

    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    private void CommandReset(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasCommandAccess(player))
        {
            ChatHelper.PrintLocalizedChat(player, true, "command.nopermission");
            return;
        }

        PlayerManager.ResetAllStats();
        EventManager.ResetStreaks();

        if (player != null)
            ChatHelper.PrintLocalizedChat(player, true, "admin.reset.done");
        else
            info.ReplyToCommand("[TeamBalance] Balance statistics and win streaks were reset.");
    }

    /// <summary>
    /// Steers manual team joins. Joins that keep sizes within the limit pass
    /// through. Joins that would unbalance the teams are blocked; dead players
    /// are redirected to the smaller team instead.
    /// </summary>
    private HookResult CommandJoinTeam(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
            return HookResult.Continue;

        if (info.ArgCount < 2 || !int.TryParse(info.GetArg(1), out int teamArg))
            return HookResult.Continue;

        var desired = (CsTeam)teamArg;

        // Spectator and auto-assign are always allowed.
        if (desired != CsTeam.Terrorist && desired != CsTeam.CounterTerrorist)
            return HookResult.Continue;

        var current = player.Team;
        if (current == desired)
            return HookResult.Continue;

        int t = 0, ct = 0;
        foreach (var other in PlayerManager.GetTeamPlayers())
        {
            if (other.SteamID == player.SteamID)
                continue;
            if (other.Team == CsTeam.Terrorist) t++;
            else ct++;
        }

        int newT = t + (desired == CsTeam.Terrorist ? 1 : 0);
        int newCt = ct + (desired == CsTeam.CounterTerrorist ? 1 : 0);

        // With an odd player count a difference of 1 is unavoidable.
        int effectiveMax = Math.Max(Config.TeamSwitch.MaxTeamSizeDifference, (newT + newCt) % 2);

        if (Math.Abs(newT - newCt) <= effectiveMax)
        {
            PlayerManager.GetOrAdd(player).OnVoluntarySwitch();
            return HookResult.Continue;
        }

        var smaller = t <= ct ? CsTeam.Terrorist : CsTeam.CounterTerrorist;

        if (current == smaller)
        {
            ChatHelper.PrintLocalizedChat(player, true, "jointeam.already_balanced");
            return HookResult.Handled;
        }

        if (player.PawnIsAlive)
        {
            ChatHelper.PrintLocalizedChat(player, true, "jointeam.imbalance");
            return HookResult.Handled;
        }

        player.ChangeTeam(smaller);
        PlayerManager.GetOrAdd(player).OnVoluntarySwitch();
        ChatHelper.PrintLocalizedChat(player, true,
            smaller == CsTeam.Terrorist ? "jointeam.forced.t" : "jointeam.forced.ct");
        return HookResult.Handled;
    }
}
