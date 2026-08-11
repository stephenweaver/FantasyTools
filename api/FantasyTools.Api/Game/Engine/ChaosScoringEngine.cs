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
        var sleeperScore = input.Starters.Sum(slot => slot.RawPoints);
        var running = sleeperScore;
        var effectiveSlotScores = input.Starters.ToDictionary(slot => slot.Slot, slot => slot.RawPoints, StringComparer.OrdinalIgnoreCase);
        var lines = new List<CalculationLine>
        {
            new(0, "raw", "Sleeper starting-lineup score", 0m, sleeperScore, sleeperScore)
        };

        // Stage 1: special slot replacements establish the base contribution for that slot.
        foreach (var effect in Ordered(input.Effects.Where(e => e.Type == EffectType.ReferencedPlayerReplacesSlot)))
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
                $"{effect.CardName}: replace {slot.Slot} ({slot.RawPoints:0.##}) with player {effect.ReferencedPlayerId} ({referencedScore:0.##}) × {effect.Multiplier:0.##}",
                before, change, running, effect.CardPlayId));
        }

        // Stages 2-4: defenses modify applicable incoming percentage attacks.
        var percentageEffects = input.Effects.Where(e => e.Type == EffectType.Percentage).ToList();
        var defenses = input.Effects.Where(e => e.Type is EffectType.BlockAttack or EffectType.ReduceAttack).ToList();
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
        foreach (var effect in Ordered(input.Effects.Where(e => e.Type == EffectType.FlatPoints)))
        {
            var before = running;
            running += effect.Amount;
            lines.Add(new(6, "flat", effect.CardName, before, effect.Amount, running, effect.CardPlayId));
        }

        // Custom handlers intentionally require an explicit registered implementation; they never silently execute here.
        foreach (var effect in Ordered(input.Effects.Where(e => e.Type == EffectType.Custom)))
            lines.Add(new(7, "custom-pending", $"{effect.CardName} requires handler '{effect.CustomHandler}'.", running, 0m, running, effect.CardPlayId));

        return new(sleeperScore, decimal.Round(running, 2, MidpointRounding.AwayFromZero), lines);
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
