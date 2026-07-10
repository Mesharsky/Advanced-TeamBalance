using CounterStrikeSharp.API.Modules.Utils;

namespace AdvancedTeamBalance;

public enum MoveReason
{
    TeamSize,
    Skill,
    Scramble
}

/// <summary>Immutable view of one player used for balance planning.</summary>
public sealed record PlayerSnapshot(
    ulong SteamId,
    string Name,
    CsTeam Team,
    double Rating,
    bool IsAlive,
    bool IsExempt,
    bool IsMovable,
    int RoundsOnTeam);

public sealed record PlannedMove(PlayerSnapshot Player, CsTeam To, MoveReason Reason);

/// <summary>Result of a planning pass. Nothing changes until the plan is executed.</summary>
public sealed class BalancePlan
{
    public List<PlannedMove> Moves { get; } = [];
    public int SizeMoves { get; internal set; }
    public int SkillSwaps { get; internal set; }
    public int BoostPercent { get; internal set; }
    public double ProjectedGap { get; internal set; }
    public bool IsEmpty => Moves.Count == 0;
}

/// <summary>
/// Computes balance and scramble plans from team snapshots.
/// Pure planning: never touches controllers or player data.
/// </summary>
public static class BalanceManager
{
    private const double Epsilon = 0.0001;

    private static PluginConfig _config = null!;

    public static void Initialize(PluginConfig config) => _config = config;

    public static double GetRating(PlayerStats stats, string balanceMode) => balanceMode.ToLowerInvariant() switch
    {
        "kd" => stats.KDRatio,
        "score" => stats.Score,
        "winrate" => stats.WinRate,
        _ => stats.KDARatio
    };

    public static BalancePlan ComputeBalancePlan(
        List<PlayerSnapshot> players,
        int tWinStreak,
        int ctWinStreak,
        bool allowAliveMoves,
        bool sizeOnly)
    {
        var plan = new BalancePlan();
        var t = players.Where(p => p.Team == CsTeam.Terrorist).ToList();
        var ct = players.Where(p => p.Team == CsTeam.CounterTerrorist).ToList();

        PlanSizeMoves(plan, t, ct, allowAliveMoves, sizeOnly);

        if (!sizeOnly)
            PlanSkillSwaps(plan, t, ct, tWinStreak, ctWinStreak, allowAliveMoves);

        plan.ProjectedGap = Math.Abs(Average(t) - Average(ct));
        return plan;
    }

    /// <summary>
    /// Reassigns every non-exempt player. Exempt players stay put and count
    /// as anchors for the target team sizes.
    /// </summary>
    public static BalancePlan ComputeScramblePlan(List<PlayerSnapshot> players, string mode)
    {
        var plan = new BalancePlan();
        var movable = players.Where(p => !p.IsExempt).ToList();
        if (movable.Count < 2)
            return plan;

        int targetT = (players.Count + 1) / 2;
        int anchoredT = players.Count(p => p.IsExempt && p.Team == CsTeam.Terrorist);
        int slotsT = Math.Clamp(targetT - anchoredT, 0, movable.Count);

        List<PlayerSnapshot> toT;
        List<PlayerSnapshot> toCt;

        if (mode.Equals("Skill", StringComparison.OrdinalIgnoreCase))
        {
            (toT, toCt) = DealBySkill(players, movable, slotsT);
        }
        else
        {
            var shuffled = movable.ToArray();
            Random.Shared.Shuffle(shuffled);
            toT = [.. shuffled.Take(slotsT)];
            toCt = [.. shuffled.Skip(slotsT)];
        }

        foreach (var p in toT.Where(p => p.Team != CsTeam.Terrorist))
            plan.Moves.Add(new PlannedMove(p, CsTeam.Terrorist, MoveReason.Scramble));
        foreach (var p in toCt.Where(p => p.Team != CsTeam.CounterTerrorist))
            plan.Moves.Add(new PlannedMove(p, CsTeam.CounterTerrorist, MoveReason.Scramble));

        return plan;
    }

    /// <summary>
    /// Deals players strongest-first onto whichever side has the lower total
    /// rating, starting from the exempt players already anchored on each side.
    /// </summary>
    private static (List<PlayerSnapshot> ToT, List<PlayerSnapshot> ToCt) DealBySkill(
        List<PlayerSnapshot> all, List<PlayerSnapshot> movable, int slotsT)
    {
        var toT = new List<PlayerSnapshot>();
        var toCt = new List<PlayerSnapshot>();
        int slotsCt = movable.Count - slotsT;

        double sumT = all.Where(p => p.IsExempt && p.Team == CsTeam.Terrorist).Sum(p => p.Rating);
        double sumCt = all.Where(p => p.IsExempt && p.Team == CsTeam.CounterTerrorist).Sum(p => p.Rating);

        foreach (var p in movable.OrderByDescending(p => p.Rating).ThenBy(p => p.SteamId))
        {
            bool pickT = slotsT > 0 && (slotsCt == 0 || sumT <= sumCt);
            if (pickT)
            {
                toT.Add(p);
                sumT += p.Rating;
                slotsT--;
            }
            else
            {
                toCt.Add(p);
                sumCt += p.Rating;
                slotsCt--;
            }
        }

        return (toT, toCt);
    }

    private static int EffectiveMaxDifference(int totalPlayers)
        => Math.Max(_config.TeamSwitch.MaxTeamSizeDifference, totalPlayers % 2);

    private static void PlanSizeMoves(
        BalancePlan plan, List<PlayerSnapshot> t, List<PlayerSnapshot> ct, bool allowAliveMoves, bool sizeOnly)
    {
        int effectiveMax = EffectiveMaxDifference(t.Count + ct.Count);

        while (Math.Abs(t.Count - ct.Count) > effectiveMax)
        {
            var (from, to, target) = t.Count > ct.Count
                ? (t, ct, CsTeam.CounterTerrorist)
                : (ct, t, CsTeam.Terrorist);

            var candidate = PickSizeMoveCandidate(from, to, allowAliveMoves, sizeOnly);
            if (candidate is null)
            {
                Log.Debug("Size balance: no movable candidates left, teams stay uneven");
                break;
            }

            from.Remove(candidate);
            to.Add(candidate with { Team = target });
            plan.Moves.Add(new PlannedMove(candidate, target, MoveReason.TeamSize));
            plan.SizeMoves++;
        }
    }

    private static PlayerSnapshot? PickSizeMoveCandidate(
        List<PlayerSnapshot> from, List<PlayerSnapshot> to, bool allowAliveMoves, bool sizeOnly)
    {
        var pool = from.Where(p => p.IsMovable && (allowAliveMoves || !p.IsAlive)).ToList();
        if (pool.Count == 0)
        {
            // The size limit outranks immunity and min-rounds, but never admin exemption.
            pool = from.Where(p => !p.IsExempt && (allowAliveMoves || !p.IsAlive)).ToList();
            if (pool.Count == 0)
                return null;
            Log.Debug("Size balance: overriding switch protection to satisfy the size limit");
        }

        if (sizeOnly)
        {
            return pool
                .OrderBy(p => p.RoundsOnTeam)
                .ThenBy(p => p.SteamId)
                .First();
        }

        // Prefer the player whose move leaves the smallest skill gap.
        return pool
            .OrderBy(p => SimulatedGapAfterMove(from, to, p))
            .ThenBy(p => p.RoundsOnTeam)
            .ThenBy(p => p.SteamId)
            .First();
    }

    private static double SimulatedGapAfterMove(
        List<PlayerSnapshot> from, List<PlayerSnapshot> to, PlayerSnapshot mover)
    {
        double fromAvg = Average(from.Where(p => p.SteamId != mover.SteamId));
        double toAvg = Average(to.Append(mover));
        return Math.Abs(fromAvg - toAvg);
    }

    private static void PlanSkillSwaps(
        BalancePlan plan, List<PlayerSnapshot> t, List<PlayerSnapshot> ct,
        int tWinStreak, int ctWinStreak, bool allowAliveMoves)
    {
        int maxSwaps = _config.TeamSwitch.MaxSkillSwapsPerRound;
        if (maxSwaps <= 0 || t.Count == 0 || ct.Count == 0)
            return;
        if (Math.Abs(t.Count - ct.Count) > EffectiveMaxDifference(t.Count + ct.Count))
            return;

        var (losingTeam, boostPercent) = ResolveBoost(tWinStreak, ctWinStreak);
        double boostFactor = 1.0 + boostPercent / 100.0;
        plan.BoostPercent = losingTeam == CsTeam.None ? 0 : boostPercent;

        double gap = Objective(Average(t), Average(ct), losingTeam, boostFactor, out double scale);
        if (gap / scale <= _config.Balancing.SkillDifferenceThreshold)
            return;

        for (int i = 0; i < maxSwaps; i++)
        {
            var best = FindBestSwap(t, ct, losingTeam, boostFactor);
            if (best is null || best.Value.NewGap >= gap - Epsilon)
                break;

            var (fromT, fromCt, newGap) = best.Value;

            t.Remove(fromT);
            ct.Remove(fromCt);
            t.Add(fromCt with { Team = CsTeam.Terrorist });
            ct.Add(fromT with { Team = CsTeam.CounterTerrorist });

            plan.Moves.Add(new PlannedMove(fromT, CsTeam.CounterTerrorist, MoveReason.Skill));
            plan.Moves.Add(new PlannedMove(fromCt, CsTeam.Terrorist, MoveReason.Skill));
            plan.SkillSwaps++;

            gap = Objective(Average(t), Average(ct), losingTeam, boostFactor, out scale);
            if (gap / scale <= _config.Balancing.SkillDifferenceThreshold)
                break;
        }
    }

    private static (PlayerSnapshot FromT, PlayerSnapshot FromCt, double NewGap)? FindBestSwap(
        List<PlayerSnapshot> t, List<PlayerSnapshot> ct, CsTeam losingTeam, double boostFactor)
    {
        double sumT = t.Sum(p => p.Rating);
        double sumCt = ct.Sum(p => p.Rating);

        (PlayerSnapshot, PlayerSnapshot, double)? best = null;

        foreach (var pt in t)
        {
            if (!pt.IsMovable)
                continue;

            foreach (var pc in ct)
            {
                if (!pc.IsMovable)
                    continue;

                double newAvgT = (sumT - pt.Rating + pc.Rating) / t.Count;
                double newAvgCt = (sumCt - pc.Rating + pt.Rating) / ct.Count;
                double candidateGap = Objective(newAvgT, newAvgCt, losingTeam, boostFactor, out _);

                if (best is null || candidateGap < best.Value.Item3)
                    best = (pt, pc, candidateGap);
            }
        }

        return best;
    }

    /// <summary>
    /// Distance from the desired strength relation. Without a boost the teams
    /// should be equal. With a boost the losing team is allowed to end up
    /// boostFactor times stronger, which steers swaps in its favor.
    /// </summary>
    private static double Objective(double avgT, double avgCt, CsTeam losingTeam, double boostFactor, out double scale)
    {
        if (losingTeam == CsTeam.Terrorist)
            avgCt *= boostFactor;
        else if (losingTeam == CsTeam.CounterTerrorist)
            avgT *= boostFactor;

        scale = Math.Max(Math.Max(avgT, avgCt), Epsilon);
        return Math.Abs(avgT - avgCt);
    }

    private static (CsTeam LosingTeam, int Percent) ResolveBoost(int tWinStreak, int ctWinStreak)
    {
        int threshold = _config.Balancing.BoostAfterLoseStreak;
        if (threshold <= 0)
            return (CsTeam.None, 0);

        if (ctWinStreak >= threshold)
            return (CsTeam.Terrorist, BoostPercentFor(ctWinStreak));
        if (tWinStreak >= threshold)
            return (CsTeam.CounterTerrorist, BoostPercentFor(tWinStreak));

        return (CsTeam.None, 0);
    }

    private static int BoostPercentFor(int loseStreak)
    {
        if (!_config.Balancing.ProgressiveBoost || _config.Balancing.BoostTiers.Count == 0)
            return _config.Balancing.BoostPercentage;

        int percent = 0;
        foreach (var tier in _config.Balancing.BoostTiers.OrderBy(x => x.Key))
        {
            if (loseStreak >= tier.Key)
                percent = tier.Value;
        }

        return percent > 0 ? percent : _config.Balancing.BoostPercentage;
    }

    private static double Average(IEnumerable<PlayerSnapshot> players)
    {
        double sum = 0;
        int count = 0;
        foreach (var p in players)
        {
            sum += p.Rating;
            count++;
        }
        return count == 0 ? 0 : sum / count;
    }
}
