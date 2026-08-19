using FantasyTools.Api.Game.Domain;

namespace FantasyTools.Api.Game.Engine;

public interface IChaosScoringEngine
{
    ChaosScoreResult Calculate(TeamScoreInput input);
}

/// <summary>
/// Deterministic resolution pipeline. Ordering never depends on database return order.
/// Normal percentage modifiers combine additively; defenses reduce attacks before combination.
/// </summary>
public sealed class ChaosScoringEngine : IChaosScoringEngine
{
    public ChaosScoreResult Calculate(TeamScoreInput input)
    {
        var cancelledPlayIds = input.Effects
            .Where(e => NormalizeHandler(e.CustomHandler ?? e.CardName) == "challengeflag")
            .Select(e => ParseCancelledPlayId(e.Target.DynamicRule))
            .Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        var activeEffects = input.Effects.Where(e => !cancelledPlayIds.Contains(e.CardPlayId)).ToList();
        var sleeperScore = input.Starters.Sum(slot => slot.RawPoints);
        var running = sleeperScore;
        var effectiveSlotScores = input.Starters.ToDictionary(slot => slot.Slot, slot => slot.RawPoints, StringComparer.OrdinalIgnoreCase);
        var lines = new List<CalculationLine>
        {
            new(0, "raw", "Sleeper starting-lineup score", 0m, sleeperScore, sleeperScore)
        };

        // Stage 1: special slot replacements establish the base contribution for that slot.
        foreach (var effect in Ordered(activeEffects.Where(e => e.Type == EffectType.ReferencedPlayerReplacesSlot)))
        {
            var slot = input.Starters.SingleOrDefault(s => s.Slot.Equals(effect.DestinationSlot, StringComparison.OrdinalIgnoreCase));
            if (slot is null || effect.ReferencedPlayerId is null || effect.Multiplier is null) continue;
            if (!input.ReferencedPlayerScores.TryGetValue(effect.ReferencedPlayerId, out var referencedScore)) continue;

            var replacement = referencedScore * effect.Multiplier.Value;
            var before = running;
            var change = replacement - slot.RawPoints;
            running += change;
            effectiveSlotScores[slot.Slot] = replacement;
            lines.Add(new(1, "slot-replacement",
                $"{effect.CardName}: replace {slot.Slot} ({slot.RawPoints:0.##}) with player {effect.ReferencedPlayerId} ({referencedScore:0.##}) Ã— {effect.Multiplier:0.##}",
                before, change, running, effect.CardPlayId));
        }

        // Stage 2: specialty rules that change a starter's effective contribution.
        // These run before percentage cards so later boosts/attacks use the resolved score.
        foreach (var effect in Ordered(activeEffects.Where(e => e.Type == EffectType.Custom)))
        {
            var handler = NormalizeHandler(effect.CustomHandler ?? effect.CardName);
            var slot = ResolveTargetSlot(effect.Target, input.Starters);
            var stats = slot is null ? null : input.PlayerStats.GetValueOrDefault(slot.PlayerId);
            if (TryResolveCustom(handler, effect, slot, stats, input, effectiveSlotScores,
                    out var change, out var description))
            {
                var before = running;
                running += change;
                if (slot is not null)
                    effectiveSlotScores[slot.Slot] = effectiveSlotScores.GetValueOrDefault(slot.Slot, slot.RawPoints) + change;
                lines.Add(new(2, "specialty", $"{effect.CardName}: {description}", before, change, running, effect.CardPlayId));
            }
            else
                lines.Add(new(2, "custom-pending", $"{effect.CardName} is waiting for its required player statistics or rule handler.", running, 0m, running, effect.CardPlayId));
        }

        // Stages 2-4: defenses modify applicable incoming percentage attacks.
        var percentageEffects = activeEffects.Where(e => e.Type == EffectType.Percentage).ToList();
        var defenses = activeEffects.Where(e => e.Type is EffectType.BlockAttack or EffectType.ReduceAttack).ToList();
        foreach (var targetGroup in percentageEffects.GroupBy(effect => TargetKey(effect.Target)).OrderBy(group => group.Key))
        {
            decimal netPercentage = 0m;
            foreach (var effect in Ordered(targetGroup))
            {
                var effective = effect.Amount;
                if (effect.Category == CardCategory.Attack)
                {
                    foreach (var defense in Ordered(defenses.Where(d => TargetsOverlap(d.Target, effect.Target))))
                    {
                        var beforeDefense = effective;
                        effective = defense.Type == EffectType.BlockAttack ? 0m : effective * (1m - defense.Amount / 100m);
                        lines.Add(new(4, "defense",
                            $"{defense.CardName} changed {effect.CardName} from {beforeDefense:0.##}% to {effective:0.##}%",
                            beforeDefense, effective - beforeDefense, effective, defense.CardPlayId));
                    }
                }
                netPercentage += effective;
            }

            // Stage 5: one additive percentage application per logical target.
            if (netPercentage != 0m)
            {
                var target = targetGroup.First().Target;
                var targetBase = ResolveTargetBase(target, input.Starters, effectiveSlotScores, running);
                var before = running;
                var change = targetBase * netPercentage / 100m;
                running += change;
                lines.Add(new(5, "percentage", $"{DescribeTarget(target)}: additive modifier {netPercentage:+0.##;-0.##}% of {targetBase:0.##}", before, change, running));
            }
        }

        // Stage 6: flat modifiers apply after percentages.
        foreach (var effect in Ordered(activeEffects.Where(e => e.Type == EffectType.FlatPoints)))
        {
            var before = running;
            running += effect.Amount;
            lines.Add(new(6, "flat", effect.CardName, before, effect.Amount, running, effect.CardPlayId));
        }

        return new(sleeperScore, decimal.Round(running, 2, MidpointRounding.AwayFromZero), lines);
    }

    private static bool TryResolveCustom(string handler, ActiveEffect effect, SlotScore? slot, PlayerWeekStats? stats,
        TeamScoreInput input, IReadOnlyDictionary<string, decimal> effectiveScores,
        out decimal change, out string description)
    {
        change = 0m;
        description = "";
        if (handler == "caphit")
        {
            change = input.Starters.Sum(s => Math.Min(effectiveScores.GetValueOrDefault(s.Slot, s.RawPoints), 15m)
                                       - effectiveScores.GetValueOrDefault(s.Slot, s.RawPoints));
            description = "capped every starter at 15 points";
            return true;
        }
        if (handler == "challengeflag")
        {
            change = 0m;
            description = "cancelled the selected opposing card before scoring";
            return true;
        }
        if (handler == "buttfumble")
        {
            var fumbles = input.Starters.Sum(s => input.PlayerStats.GetValueOrDefault(s.PlayerId)?.Fumbles ?? 0);
            change = fumbles * 10m;
            description = $"added 10 points for each of the starting lineup's {fumbles} fumbles";
            return true;
        }
        if (handler is "injured" or "medicaltent")
        {
            var injured = input.Starters.Any(s =>
            {
                var playerStats=input.PlayerStats.GetValueOrDefault(s.PlayerId);
                return s.RawPoints < 5m && playerStats is not null &&
                    (playerStats.InjuryStatus.Equals("Out",StringComparison.OrdinalIgnoreCase) || playerStats.InjuryStatus.Equals("IR",StringComparison.OrdinalIgnoreCase));
            });
            change = injured ? 50m : 0m;
            description = injured ? "added 50 points because a sub-five-point starter was listed Out or IR" : "no starter met the Medical Tent condition";
            return true;
        }
        if (handler == "2025derrickhenry")
        {
            var fumbles=input.Starters.Sum(s=>input.PlayerStats.GetValueOrDefault(s.PlayerId)?.Fumbles??0);
            change=fumbles * -10m; description=$"subtracted 10 points for each of the starting lineup's {fumbles} fumbles"; return true;
        }
        if (handler == "283")
        {
            if (input.ScoreEnteringMonday is null || input.OpponentScoreEnteringMonday is null) return false;
            var deficit = input.OpponentScoreEnteringMonday.Value - input.ScoreEnteringMonday.Value;
            change = deficit >= 50m ? 51m : 0m;
            description = deficit >= 50m ? $"added 51 points after entering Monday down {deficit:0.##}" : $"no bonus because the Monday deficit was {Math.Max(0, deficit):0.##}";
            return true;
        }
        if (handler == "mvp")
        {
            if (slot is null || input.LeagueHighestPlayerScore is null) return false;
            var existing = effectiveScores.GetValueOrDefault(slot.Slot, slot.RawPoints);
            change = input.LeagueHighestPlayerScore.Value - existing;
            description = $"replaced {slot.PlayerName}'s {existing:0.##} with the league-high score of {input.LeagueHighestPlayerScore:0.##}";
            return true;
        }
        if (handler is "spygate" or "tradedwr" or "tradedrb" or "tradedte")
        {
            if (slot is null || effect.ReferencedPlayerId is null || !input.ReferencedPlayerScores.TryGetValue(effect.ReferencedPlayerId, out var replacement)) return false;
            var existing = effectiveScores.GetValueOrDefault(slot.Slot, slot.RawPoints);
            change = replacement - existing;
            description = $"replaced {slot.PlayerName}'s {existing:0.##} with {replacement:0.##}";
            return true;
        }
        if (handler == "1v1mebro")
        {
            if (slot is null || effect.ReferencedPlayerId is null || !input.ReferencedPlayerScores.TryGetValue(effect.ReferencedPlayerId, out var opposing)) return false;
            var own = effectiveScores.GetValueOrDefault(slot.Slot, slot.RawPoints);
            change = own >= opposing ? opposing : -own;
            description = own >= opposing ? $"{slot.PlayerName} won {own:0.##} to {opposing:0.##} and claimed both scores" : $"{slot.PlayerName} lost {own:0.##} to {opposing:0.##}, so the opponent claimed both scores";
            return true;
        }
        if (handler == "picksix")
        {
            // The play service emits one attack effect for the opponent QB and one
            // boost effect for the card owner's defense, both carrying the same QB id.
            var qbId = effect.ReferencedPlayerId ?? slot?.PlayerId;
            if (qbId is null || !input.PlayerStats.TryGetValue(qbId, out var quarterbackStats)) return false;
            change = effect.Category == CardCategory.Attack ? -quarterbackStats.PassingTouchdownPoints : quarterbackStats.PassingTouchdownPoints;
            description = effect.Category == CardCategory.Attack
                ? $"removed {quarterbackStats.PassingTouchdownPoints:0.##} passing-touchdown points"
                : $"awarded {quarterbackStats.PassingTouchdownPoints:0.##} passing-touchdown points to the starting defense";
            return true;
        }
        if (slot is null || stats is null) return false;
        var current = effectiveScores.GetValueOrDefault(slot.Slot, slot.RawPoints);
        switch (handler)
        {
            case "doubleornothing":
                var multiplier = stats.Receptions >= 5 ? 2m : 0m;
                change = current * multiplier - current;
                description = stats.Receptions >= 5
                    ? $"{slot.PlayerName} had {stats.Receptions} catches, doubling {current:0.##} to {current * 2m:0.##}"
                    : $"{slot.PlayerName} had {stats.Receptions} catches, reducing {current:0.##} to zero";
                return true;
            case "complete":
                change = stats.Completions * 2m;
                description = $"added 2 extra points for each of {stats.Completions} completions";
                return true;
            case "incomplete":
                var incompletions = Math.Max(0, stats.PassingAttempts - stats.Completions);
                change = incompletions * 3m;
                description = $"added 3 points for each of {incompletions} incompletions";
                return true;
            case "sacked":
                change = stats.SacksTaken * -5m;
                description = $"subtracted 5 points for each of {stats.SacksTaken} sacks";
                return true;
            case "doubletd":
                change = stats.TouchdownPoints;
                description = $"doubled {stats.TouchdownPoints:0.##} touchdown points for {slot.PlayerName}";
                return true;
            case "roughstart":
                change = -20m;
                description = $"started {slot.PlayerName} at minus 20 points";
                return true;
            case "stickyhands":
                change = stats.Receptions * 2m - stats.ReceptionPoints;
                description = $"replaced normal reception scoring with 2 points for each of {stats.Receptions} receptions";
                return true;
            case "beastmode":
                change = stats.RushingYards * 0.3m - stats.RushingYardPoints;
                description = $"replaced normal rushing-yard scoring with 0.3 points for each of {stats.RushingYards:0.##} yards";
                return true;
            case "fafb":
                change = stats.RushingYardPoints;
                description = $"doubled {stats.RushingYardPoints:0.##} quarterback rushing-yard points";
                return true;
            case "shoestringtackle":
                change = current * 0.5m;
                description = $"increased {slot.PlayerName}'s defense score by 50%";
                return true;
            case "aaaaaandnobodysblocking":
                change = stats.SacksTaken * 5m;
                description = $"added 5 points for each of {stats.SacksTaken} sacks taken";
                return true;
            case "bigsack":
                change = stats.DefensiveSacks * 5m - stats.DefensiveSackPoints;
                description = $"made each of {stats.DefensiveSacks} defensive sacks worth 5 total points";
                return true;
            case "interception":
                change = stats.DefensiveInterceptions * 15m - stats.DefensiveInterceptionPoints;
                description = $"made each of {stats.DefensiveInterceptions} defensive interceptions worth 15 total points";
                return true;
            case "immaculatereception":
                var perfect = stats.Targets >= 3 && stats.Receptions == stats.Targets;
                change = perfect ? 15m : 0m;
                description = perfect ? $"caught all {stats.Targets} targets and added 15 points" : $"caught {stats.Receptions} of {stats.Targets} targets, so no bonus applied";
                return true;
            default:
                return false;
        }
    }

    private static SlotScore? ResolveTargetSlot(CardTarget target, IReadOnlyList<SlotScore> starters) => target.Type switch
    {
        TargetType.SpecificPlayer => starters.SingleOrDefault(s => s.PlayerId == target.NflPlayerId),
        TargetType.StartingSlot => starters.SingleOrDefault(s => s.Slot.Equals(target.StartingSlot, StringComparison.OrdinalIgnoreCase)),
        _ => null
    };

    private static string NormalizeHandler(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static Guid? ParseCancelledPlayId(string? dynamicRule)
    {
        const string prefix = "cancel:";
        if (dynamicRule is null || !dynamicRule.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return Guid.TryParse(dynamicRule[prefix.Length..], out var id) ? id : null;
    }

    private static IEnumerable<ActiveEffect> Ordered(IEnumerable<ActiveEffect> effects) =>
        effects.OrderBy(effect => effect.CardPlayId);

    private static bool TargetsOverlap(CardTarget defense, CardTarget attack)
    {
        if (defense.TargetTeamId != attack.TargetTeamId) return false;
        if (defense.Type == TargetType.Team) return true;
        if (defense.Type != attack.Type) return false;
        return defense.Type switch
        {
            TargetType.StartingSlot => string.Equals(defense.StartingSlot, attack.StartingSlot, StringComparison.OrdinalIgnoreCase),
            TargetType.PositionGroup => string.Equals(defense.Position, attack.Position, StringComparison.OrdinalIgnoreCase),
            TargetType.SpecificPlayer => string.Equals(defense.NflPlayerId, attack.NflPlayerId, StringComparison.Ordinal),
            _ => false
        };
    }

    private static string TargetKey(CardTarget target) => target.Type switch
    {
        TargetType.Team => $"team:{target.TargetTeamId}",
        TargetType.StartingSlot => $"slot:{target.TargetTeamId}:{target.StartingSlot?.ToUpperInvariant()}",
        TargetType.PositionGroup => $"position:{target.TargetTeamId}:{target.Position?.ToUpperInvariant()}",
        TargetType.SpecificPlayer => $"player:{target.TargetTeamId}:{target.NflPlayerId}",
        TargetType.Dynamic => $"dynamic:{target.TargetTeamId}:{target.DynamicRule}",
        _ => throw new ArgumentOutOfRangeException()
    };

    private static decimal ResolveTargetBase(CardTarget target, IReadOnlyList<SlotScore> starters,
        IReadOnlyDictionary<string, decimal> effectiveSlots, decimal runningTeamScore) => target.Type switch
    {
        TargetType.Team => runningTeamScore,
        TargetType.StartingSlot when target.StartingSlot is not null && effectiveSlots.TryGetValue(target.StartingSlot, out var points) => points,
        TargetType.PositionGroup => starters.Where(slot => slot.Position.Equals(target.Position, StringComparison.OrdinalIgnoreCase))
            .Sum(slot => effectiveSlots.GetValueOrDefault(slot.Slot, slot.RawPoints)),
        TargetType.SpecificPlayer => starters.Where(slot => slot.PlayerId == target.NflPlayerId)
            .Sum(slot => effectiveSlots.GetValueOrDefault(slot.Slot, slot.RawPoints)),
        _ => 0m
    };

    private static string DescribeTarget(CardTarget target) => target.Type switch
    {
        TargetType.Team => "Team",
        TargetType.StartingSlot => $"Starting slot {target.StartingSlot}",
        TargetType.PositionGroup => $"Starting {target.Position} group",
        TargetType.SpecificPlayer => $"Player {target.NflPlayerId}",
        TargetType.Dynamic => $"Dynamic target {target.DynamicRule}",
        _ => "Target"
    };
}

