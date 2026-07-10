# Advanced Team Balance

Team balancing plugin for CS2 servers running [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp). It keeps team sizes in check, evens out skill differences, scrambles teams when one side runs away with the game.

[![Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/mesharsky)

## Features

- Balances by K/D, KDA, scoreboard score or round win rate
- Hard team size limit that is always enforced
- Skill balancing through player swaps, applied at round start so nobody gets yanked mid-fight
- Team scramble (random or skill based): automatically after a win streak, at halftime, or on demand with `css_scramble`
- Losing streak boost: after a team loses several rounds in a row, the next balance hands them the better end of the swaps
- Blocks team joins that would make teams uneven and puts the joining player on the right team instead
- Admin exemption from automatic switching
- Translations: en, de, es, fr, pl, pt, ru, tr
- Spectators and AFK players are never moved onto a team and never count toward player limits

## How it works

The plugin does not keep its own copy of team assignments. Every decision starts from the live server state.

- Full balancing (size moves plus skill swaps) happens at round start, before players spawn. Moves never kill anyone and never flip someone to the other side mid-round.
- When a player joins or leaves mid-round, only team sizes are corrected, and only dead players are moved. Skill swaps wait for the next round.
- Scrambles are queued and applied at the next round start.
- Players in spectator are ignored completely. They are not balanced, not scrambled and do not count toward `MinimumPlayers`.

Priority order: the team size limit always wins, then the skill difference threshold decides whether swaps happen, and the losing streak boost shapes which players get swapped.

## Installation

1. Install [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)
2. Download the latest release and extract it into `game/csgo/addons/counterstrikesharp/plugins/`
3. Configure the plugin at `addons/counterstrikesharp/configs/plugins/Mesharsky_AdvancedTeamBalance/Mesharsky_AdvancedTeamBalance.toml`. A commented example ships with the repository.

Recommended server settings so the game's own balancer does not fight the plugin:

```
mp_autoteambalance 0
mp_limitteams 0
```

## Configuration

### General

| Option | Default | Description |
|---|---|---|
| `PluginTag` | `{red}[TeamBalance]{default}` | Chat prefix, supports color tags |
| `MinimumPlayers` | `6` | Players needed on teams before skill balancing kicks in |
| `EnableDebug` | `false` | Verbose logging |

### TeamSwitch

| Option | Default | Description |
|---|---|---|
| `BalanceTriggers` | `["OnRoundStart", "OnPlayerJoin"]` | See below |
| `MaxTeamSizeDifference` | `1` | Hard cap on the size difference between teams |
| `AlwaysEnforceTeamSizes` | `true` | Keep enforcing the size cap even below `MinimumPlayers` |
| `MinRoundsBeforeSwitch` | `2` | Rounds a player stays on a team before qualifying for a switch |
| `SwitchImmunityTime` | `60` | Seconds of immunity after being switched by the plugin |
| `MaxSkillSwapsPerRound` | `2` | Cap on skill swap pairs per balance pass |
| `BalanceDuringWarmup` | `false` | Whether to balance during warmup |

Triggers: `OnRoundStart` and `OnRoundEnd` both mean a full balance at the next round start. `OnPlayerJoin` and `OnPlayerDisconnect` additionally correct team sizes right away, moving dead players only.

If eligible players are not enough to satisfy the size cap, the plugin overrides `MinRoundsBeforeSwitch` and `SwitchImmunityTime` for the size correction. Admin exemption is never overridden.

### Balancing

| Option | Default | Description |
|---|---|---|
| `BalanceMode` | `KDA` | `KD`, `KDA`, `Score` or `WinRate` |
| `SkillDifferenceThreshold` | `0.2` | Relative strength gap that triggers swaps (0.2 = 20%) |
| `OnlyBalanceByTeamSize` | `false` | Skip skill swaps entirely |
| `BoostAfterLoseStreak` | `5` | Lost rounds in a row before the boost activates, 0 disables |
| `BoostPercentage` | `20` | How much stronger the losing team may end up |
| `ProgressiveBoost` | `false` | Scale the boost with the streak length |
| `BoostTiers` | `3=10, 5=20, 7=30` | Streak length to boost percentage |

### Scramble

| Option | Default | Description |
|---|---|---|
| `Mode` | `Random` | `Random` shuffles, `Skill` deals players out evenly by rating |
| `AfterWinStreak` | `5` | Scramble after one team wins this many rounds in a row, 0 disables |
| `AfterHalftime` | `false` | Scramble when sides swap at halftime |
| `ResetStats` | `true` | Reset tracked stats after a scramble |

### Messages

| Option | Default | Description |
|---|---|---|
| `AnnounceBalancing` | `true` | Announce balance operations in chat |
| `NotifySwitchedPlayers` | `true` | Tell moved players they were switched |
| `ExplainBalanceReason` | `true` | Include the reason in announcements |

### Admin

| Option | Default | Description |
|---|---|---|
| `ExcludeAdmins` | `true` | Exempt admins from automatic switching |
| `AdminExemptFlag` | `@css/ban` | Flag that grants the exemption |
| `AdminCommandFlag` | `@css/generic` | Flag required for the admin commands |

## Commands

| Command | Access | Description |
|---|---|---|
| `css_tbstats` | everyone | Shows the stats the plugin tracks about you |
| `css_balancepreview` | admin | Dry run: shows what the next balance would do without changing anything |
| `css_scramble` | admin or console | Queues a scramble for the next round |
| `css_tbreset` | admin or console | Resets tracked stats and win streaks |

## Upgrading from 5.x

Version 6.0.0 is a rewrite of the balancing logic. Config changes:

- `BalanceMode = "ScrambleRandom"` / `"ScrambleSkill"` no longer exist. Scramble has its own `[Scramble]` section. Old values are mapped automatically and a warning is logged.
- `AutoScrambleAfterWinStreak` and `ResetStatsAfterScramble` moved to `[Scramble]` as `AfterWinStreak` and `ResetStats`.
- `SwitchImmunityTime` now actually counts seconds. It used to count rounds despite the name, so 60 meant 60 rounds.
- `SkillDifferenceThreshold` is now relative to team strength, so it behaves the same for every balance mode.
- `EnableBalanceHistory` and `ShowBoostOnApplication` were removed.
- New options: `AlwaysEnforceTeamSizes`, `MaxSkillSwapsPerRound`, `Scramble.Mode`, `Scramble.AfterHalftime`, `Admin.AdminCommandFlag`.

Notable fixes in 6.0.0:

- Scrambles work now. They used to only reassign dead players and the changes were silently reverted a round later.
- Spectators can no longer be pulled onto a team by a scramble and no longer count toward `MinimumPlayers`.
- `Score` mode works now. The score was never actually tracked before, so it compared zeros.
- The balance preview no longer corrupts player state when you run it.

## Translations

Language files live in the `lang` folder. To add a language, copy `en.json`, translate the values and name the file after the language code.

## Building

```
dotnet publish -c Release
```

The plugin lands in `bin/Mesharsky_AdvancedTeamBalance/`.

## Support

Found a bug or have an idea? Open an issue on GitHub.

If you like my work and want to support me by donating you can do it here:

- Ko-fi: https://ko-fi.com/mesharsky
- PayPal: https://paypal.me/mesharskyh2k
