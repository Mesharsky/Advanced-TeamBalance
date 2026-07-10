using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace AdvancedTeamBalance;

/// <summary>
/// Wires game events to balancing. Full balancing (including skill swaps and
/// scrambles) only happens at round prestart, the one moment where every player
/// can be switched safely. Mid-round triggers enforce team sizes with dead
/// players only.
/// </summary>
public static class EventManager
{
    private static PluginConfig _config = null!;
    private static CCSGameRulesProxy? _gameRules;
    private static bool _balanceInProgress;
    private static bool _scramblePending;

    public static int TWinStreak { get; private set; }
    public static int CtWinStreak { get; private set; }
    public static bool IsScramblePending => _scramblePending;

    public static void Initialize(PluginConfig config)
    {
        _config = config;
    }

    public static HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
    {
        bool warmup = IsWarmup();

        if (!warmup)
        {
            foreach (var controller in PlayerManager.GetTeamPlayers())
            {
                var data = PlayerManager.GetOrAdd(controller);
                data.RoundsOnCurrentTeam++;
                data.Stats.RoundsPlayed++;
            }
        }

        if (warmup && !_config.TeamSwitch.BalanceDuringWarmup)
            return HookResult.Continue;

        if (_scramblePending)
        {
            _scramblePending = false;
            ExecuteScramble();
            return HookResult.Continue;
        }

        if (HasTrigger("OnRoundStart") || HasTrigger("OnRoundEnd"))
            RunPrestartBalance();

        return HookResult.Continue;
    }

    public static HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (IsWarmup())
            return HookResult.Continue;

        var winner = (CsTeam)@event.Winner;
        if (winner == CsTeam.Terrorist)
        {
            TWinStreak++;
            CtWinStreak = 0;
            CreditRoundWin(CsTeam.Terrorist);
        }
        else if (winner == CsTeam.CounterTerrorist)
        {
            CtWinStreak++;
            TWinStreak = 0;
            CreditRoundWin(CsTeam.CounterTerrorist);
        }
        else
        {
            return HookResult.Continue;
        }

        Log.Debug($"Round end: winner {winner}, streaks T {TWinStreak} / CT {CtWinStreak}");

        int scrambleAt = _config.Scramble.AfterWinStreak;
        if (scrambleAt > 0 && !_scramblePending)
        {
            if (TWinStreak >= scrambleAt)
                QueueScramble("scramble.winstreak.t", TWinStreak);
            else if (CtWinStreak >= scrambleAt)
                QueueScramble("scramble.winstreak.ct", CtWinStreak);
        }

        if (!_scramblePending)
            AnnounceUpcomingBoost();

        return HookResult.Continue;
    }

    /// <summary>Fires when sides are swapped at halftime.</summary>
    public static HookResult OnAnnouncePhaseEnd(EventAnnouncePhaseEnd @event, GameEventInfo info)
    {
        (TWinStreak, CtWinStreak) = (CtWinStreak, TWinStreak);
        Log.Debug("Halftime: sides swapped, win streaks follow the players");

        if (_config.Scramble.AfterHalftime)
            QueueScramble("scramble.halftime");

        return HookResult.Continue;
    }

    public static HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!PlayerManager.IsHumanPlayer(player))
            return HookResult.Continue;

        PlayerManager.GetOrAdd(player!);

        if (HasTrigger("OnPlayerJoin"))
            EnforceTeamSizes();

        return HookResult.Continue;
    }

    public static HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
            return HookResult.Continue;

        // Stats are kept so a reconnect during the same map does not reset them.
        if (HasTrigger("OnPlayerDisconnect"))
            Server.NextFrame(EnforceTeamSizes);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (IsWarmup())
            return HookResult.Continue;

        var victim = @event.Userid;
        var attacker = @event.Attacker;
        var assister = @event.Assister;

        bool victimIsHuman = PlayerManager.IsHumanPlayer(victim);
        if (victimIsHuman)
            PlayerManager.GetOrAdd(victim!).Stats.Deaths++;

        if (PlayerManager.IsHumanPlayer(attacker)
            && !(victimIsHuman && attacker!.SteamID == victim!.SteamID))
        {
            PlayerManager.GetOrAdd(attacker!).Stats.Kills++;
        }

        if (PlayerManager.IsHumanPlayer(assister))
            PlayerManager.GetOrAdd(assister!).Stats.Assists++;

        return HookResult.Continue;
    }

    /// <summary>Queues a scramble for the next round prestart and announces it.</summary>
    public static void QueueScramble(string? reasonKey = null, params object[] args)
    {
        if (_scramblePending)
            return;

        _scramblePending = true;
        Log.Debug($"Scramble queued ({reasonKey ?? "no reason given"})");

        if (!_config.Messages.AnnounceBalancing)
            return;

        if (reasonKey != null && _config.Messages.ExplainBalanceReason)
            ChatHelper.PrintLocalizedChatAll(true, reasonKey, args);
        else
            ChatHelper.PrintLocalizedChatAll(true, "scramble.queued");
    }

    public static void ResetStreaks()
    {
        TWinStreak = 0;
        CtWinStreak = 0;
    }

    /// <summary>Called on map end. Drops all per-map state.</summary>
    public static void ResetMatchState()
    {
        ResetStreaks();
        _scramblePending = false;
        _gameRules = null;
        PlayerManager.Clear();
    }

    /// <summary>Computes the same plan a round prestart would produce. Used by the preview command.</summary>
    public static BalancePlan ComputePrestartPlan(List<PlayerSnapshot> snapshot)
    {
        bool enough = snapshot.Count >= _config.General.MinimumPlayers;
        if (!enough && !_config.TeamSwitch.AlwaysEnforceTeamSizes)
            return new BalancePlan();

        bool sizeOnly = _config.Balancing.OnlyBalanceByTeamSize || !enough;
        return BalanceManager.ComputeBalancePlan(snapshot, TWinStreak, CtWinStreak, allowAliveMoves: true, sizeOnly);
    }

    private static bool HasTrigger(string trigger)
        => _config.TeamSwitch.BalanceTriggers.Contains(trigger, StringComparer.OrdinalIgnoreCase);

    private static void RunPrestartBalance()
    {
        if (_balanceInProgress)
            return;
        _balanceInProgress = true;

        try
        {
            var snapshot = PlayerManager.Snapshot();
            var plan = ComputePrestartPlan(snapshot);
            if (plan.IsEmpty)
                return;

            int tBefore = snapshot.Count(p => p.Team == CsTeam.Terrorist);
            int ctBefore = snapshot.Count - tBefore;

            if (ApplyPlan(plan, allowAliveMoves: true) > 0)
                AnnounceBalance(plan, tBefore, ctBefore);
        }
        finally
        {
            _balanceInProgress = false;
        }
    }

    /// <summary>Mid-round size enforcement. Only moves dead players.</summary>
    private static void EnforceTeamSizes()
    {
        if (_balanceInProgress)
            return;
        _balanceInProgress = true;

        try
        {
            var snapshot = PlayerManager.Snapshot();
            if (snapshot.Count < _config.General.MinimumPlayers && !_config.TeamSwitch.AlwaysEnforceTeamSizes)
                return;

            var plan = BalanceManager.ComputeBalancePlan(snapshot, 0, 0, allowAliveMoves: false, sizeOnly: true);
            if (plan.IsEmpty)
                return;

            int tBefore = snapshot.Count(p => p.Team == CsTeam.Terrorist);
            int ctBefore = snapshot.Count - tBefore;

            if (ApplyPlan(plan, allowAliveMoves: false) > 0)
                AnnounceBalance(plan, tBefore, ctBefore);
        }
        finally
        {
            _balanceInProgress = false;
        }
    }

    private static void ExecuteScramble()
    {
        var snapshot = PlayerManager.Snapshot();
        if (snapshot.Count < 2)
        {
            Log.Debug("Scramble skipped: fewer than 2 players on teams");
            return;
        }

        bool skillMode = _config.Scramble.Mode.Equals("Skill", StringComparison.OrdinalIgnoreCase);
        var plan = BalanceManager.ComputeScramblePlan(snapshot, _config.Scramble.Mode);
        int moved = ApplyPlan(plan, allowAliveMoves: true);

        ResetStreaks();
        if (_config.Scramble.ResetStats)
            PlayerManager.ResetAllStats();

        if (_config.Messages.AnnounceBalancing)
            ChatHelper.PrintLocalizedChatAll(true, skillMode ? "scramble.skill" : "scramble.random");

        Log.Debug($"Scramble applied ({_config.Scramble.Mode}): {moved} players reassigned");
    }

    /// <summary>
    /// Executes a plan against live controllers. SwitchTeam does not kill the
    /// player; at prestart everyone respawns on the new team anyway.
    /// </summary>
    private static int ApplyPlan(BalancePlan plan, bool allowAliveMoves)
    {
        if (plan.IsEmpty)
            return 0;

        var controllers = new Dictionary<ulong, CCSPlayerController>();
        foreach (var controller in PlayerManager.GetTeamPlayers())
            controllers[controller.SteamID] = controller;

        // A plan can move the same player twice (size move, then a swap back).
        // Only the final destination is applied.
        var finalMoves = new Dictionary<ulong, PlannedMove>();
        foreach (var move in plan.Moves)
            finalMoves[move.Player.SteamId] = move;

        int applied = 0;
        foreach (var move in finalMoves.Values)
        {
            if (!controllers.TryGetValue(move.Player.SteamId, out var controller))
                continue;
            if (!controller.IsValid || controller.Team == move.To)
                continue;
            if (!allowAliveMoves && controller.PawnIsAlive)
                continue;

            controller.SwitchTeam(move.To);
            PlayerManager.GetOrAdd(controller).OnSwitchedByPlugin();
            applied++;

            Log.Debug($"Moved {move.Player.Name} to {move.To} ({move.Reason})");

            if (_config.Messages.NotifySwitchedPlayers && move.Reason != MoveReason.Scramble)
            {
                ChatHelper.PrintLocalizedChat(controller, true,
                    move.To == CsTeam.Terrorist ? "player.moved.t" : "player.moved.ct");
            }
        }

        return applied;
    }

    private static void AnnounceBalance(BalancePlan plan, int tBefore, int ctBefore)
    {
        if (!_config.Messages.AnnounceBalancing)
            return;

        if (plan.SizeMoves > 0)
        {
            if (_config.Messages.ExplainBalanceReason)
                ChatHelper.PrintLocalizedChatAll(true, "balance.size.moved.reason", plan.SizeMoves, tBefore, ctBefore);
            else
                ChatHelper.PrintLocalizedChatAll(true, "balance.size.moved", plan.SizeMoves);
        }

        if (plan.SkillSwaps > 0)
        {
            if (_config.Messages.ExplainBalanceReason)
            {
                ChatHelper.PrintLocalizedChatAll(true, "balance.skill.swapped.reason",
                    plan.SkillSwaps, _config.Balancing.BalanceMode, plan.ProjectedGap.ToString("F2"));
            }
            else
            {
                ChatHelper.PrintLocalizedChatAll(true, "balance.skill.swapped", plan.SkillSwaps);
            }
        }
    }

    private static void AnnounceUpcomingBoost()
    {
        if (_config.Balancing.OnlyBalanceByTeamSize || _config.Balancing.BoostAfterLoseStreak <= 0)
            return;
        if (!_config.Messages.AnnounceBalancing || !_config.Messages.ExplainBalanceReason)
            return;
        if (!HasTrigger("OnRoundStart") && !HasTrigger("OnRoundEnd"))
            return;

        int threshold = _config.Balancing.BoostAfterLoseStreak;
        if (TWinStreak >= threshold)
            ChatHelper.PrintLocalizedChatAll(true, "balance.boost.losestreak.ct", TWinStreak);
        else if (CtWinStreak >= threshold)
            ChatHelper.PrintLocalizedChatAll(true, "balance.boost.losestreak.t", CtWinStreak);
    }

    private static void CreditRoundWin(CsTeam winner)
    {
        foreach (var controller in PlayerManager.GetTeamPlayers())
        {
            if (controller.Team == winner)
                PlayerManager.GetOrAdd(controller).Stats.RoundsWon++;
        }
    }

    private static bool IsWarmup()
    {
        if (_gameRules?.IsValid is not true)
            _gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();

        return _gameRules?.GameRules?.WarmupPeriod ?? false;
    }
}
