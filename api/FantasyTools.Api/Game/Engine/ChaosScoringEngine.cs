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

        foreach (var effect in Ordered(activeEffects.Where(e => e.Type == EffectType.Custom && IsWeeklyHandler(NormalizeHandler(e.CustomHandler ?? e.CardName)))))
        {
            var before=running;
            var change=ApplyWeeklyRule(NormalizeHandler(effect.CustomHandler ?? effect.CardName),input,effectiveSlotScores,out var description);
            running+=change;
            lines.Add(new(2,"weekly",$"{effect.CardName}: {description}",before,change,running,effect.CardPlayId));
        }

        // Stage 2: specialty rules that change a starter's effective contribution.
        // These run before percentage cards so later boosts/attacks use the resolved score.
        foreach (var effect in Ordered(activeEffects.Where(e => e.Type == EffectType.Custom && !IsWeeklyHandler(NormalizeHandler(e.CustomHandler ?? e.CardName)) && NormalizeHandler(e.CustomHandler ?? e.CardName) is not "caphit" and not "bighit")))
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
                ApplyPercentageToSlots(target,input.Starters,effectiveSlotScores,netPercentage);
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

        foreach(var effect in Ordered(activeEffects.Where(e=>e.Type==EffectType.Custom&&NormalizeHandler(e.CustomHandler??e.CardName) is "bighit")))
        {
            var slot=ResolveTargetSlot(effect.Target,input.Starters);if(slot is null)continue;var current=effectiveSlotScores.GetValueOrDefault(slot.Slot,slot.RawPoints);var change=Math.Min(current,10m)-current;var before=running;running+=change;effectiveSlotScores[slot.Slot]=Math.Min(current,10m);lines.Add(new(7,"final-cap",$"{effect.CardName}: capped {slot.PlayerName} at 10 points after all other effects",before,change,running,effect.CardPlayId));
        }
        foreach(var effect in Ordered(activeEffects.Where(e=>e.Type==EffectType.Custom&&NormalizeHandler(e.CustomHandler??e.CardName) is "caphit" or "weeklycaphit")))
        {
            var before=running;
            var change=input.Starters.Sum(s=>Math.Min(effectiveSlotScores.GetValueOrDefault(s.Slot,s.RawPoints),15m)-effectiveSlotScores.GetValueOrDefault(s.Slot,s.RawPoints));
            running+=change;
            lines.Add(new(7,"final-cap",$"{effect.CardName}: capped every starter at 15 points after all other effects",before,change,running,effect.CardPlayId));
        }

        return new(sleeperScore, decimal.Round(running, 2, MidpointRounding.AwayFromZero), lines);
    }

    private static bool IsWeeklyHandler(string handler)=>handler.StartsWith("weekly",StringComparison.Ordinal);

    private static void ApplyPercentageToSlots(CardTarget target,IReadOnlyList<SlotScore> starters,Dictionary<string,decimal> effective,decimal percentage)
    {
        IEnumerable<SlotScore> slots=target.Type switch
        {
            TargetType.Team=>starters,
            TargetType.StartingSlot=>starters.Where(x=>x.Slot.Equals(target.StartingSlot,StringComparison.OrdinalIgnoreCase)),
            TargetType.PositionGroup=>starters.Where(x=>x.Position.Equals(target.Position,StringComparison.OrdinalIgnoreCase)),
            TargetType.SpecificPlayer=>starters.Where(x=>x.PlayerId==target.NflPlayerId),
            _=>[]
        };
        foreach(var slot in slots)effective[slot.Slot]=effective.GetValueOrDefault(slot.Slot,slot.RawPoints)*(1m+percentage/100m);
    }

    private static decimal ApplyWeeklyRule(string handler,TeamScoreInput input,Dictionary<string,decimal> effective,out string description)
    {
        decimal total=0m;
        void Replace(SlotScore slot,decimal next){var current=effective.GetValueOrDefault(slot.Slot,slot.RawPoints);total+=next-current;effective[slot.Slot]=next;}
        PlayerWeekStats Stats(SlotScore slot)=>input.PlayerStats.GetValueOrDefault(slot.PlayerId)??new();
        decimal Frenzy(SlotScore slot,string position)
        {
            var s=Stats(slot);return position switch
            {
                "TE"=>s.Receptions*3m+(s.RushingYards+s.ReceivingYards)*.5m+(s.RushingTouchdowns+s.ReceivingTouchdowns)*10m,
                "WR" or "RB"=>(s.RushingYards+s.ReceivingYards)*.5m+(s.RushingTouchdowns+s.ReceivingTouchdowns)*10m,
                _=>slot.RawPoints
            };
        }
        switch(handler)
        {
            case "weeklyqualityquantity": foreach(var slot in input.Starters.Where(x=>x.Position is "QB" or "RB" or "WR" or "TE")){var s=Stats(slot);Replace(slot,effective.GetValueOrDefault(slot.Slot,slot.RawPoints)-s.PassingYardPoints-s.RushingYardPoints-s.ReceivingYardPoints);}description="removed normal passing, rushing, and receiving yardage points from QB, RB, WR, and TE starters";break;
            case "weeklyquantityquality": foreach(var slot in input.Starters.Where(x=>x.Position is "QB" or "RB" or "WR" or "TE")){var s=Stats(slot);Replace(slot,s.PassingYardPoints+s.RushingYardPoints+s.ReceivingYardPoints+s.BonusPoints);}description="counted only normal yardage scoring and existing scoring bonuses for QB, RB, WR, and TE starters";break;
            case "weeklyhalfpoint": foreach(var slot in input.Starters){var s=Stats(slot);Replace(slot,effective.GetValueOrDefault(slot.Slot,slot.RawPoints)+s.Receptions*.5m-s.ReceptionPoints);}description="made every reception worth 0.5 points";break;
            case "weeklyminibattle":
                var requested=(input.Effects.FirstOrDefault(x=>NormalizeHandler(x.CustomHandler??x.CardName)=="weeklyminibattle")?.Target.DynamicRule??"").Split(',',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                var chosen=new HashSet<string>(StringComparer.Ordinal);foreach(var pos in new[]{"QB","RB","WR","TE"}){var pick=input.Starters.Where(x=>x.Position==pos&&requested.Contains(x.PlayerId)).FirstOrDefault()??input.Starters.Where(x=>x.Position==pos).OrderByDescending(x=>input.Projections.GetValueOrDefault(x.PlayerId)).ThenBy(x=>x.PlayerId).FirstOrDefault();if(pick is not null)chosen.Add(pick.PlayerId);}foreach(var slot in input.Starters.Where(x=>!chosen.Contains(x.PlayerId)))Replace(slot,0m);description="counted the selected starting QB, RB, WR, and TE; missing choices used the highest projected eligible starter";break;
            case "weeklydeckswap":
                foreach(var slot in input.Starters){var replacement=input.OpponentStarters.FirstOrDefault(x=>x.Slot.Equals(slot.Slot,StringComparison.OrdinalIgnoreCase));if(replacement is null)replacement=input.OpponentBench.Where(x=>x.Position==slot.Position).OrderBy(x=>input.Projections.GetValueOrDefault(x.PlayerId)).ThenBy(x=>x.PlayerId).FirstOrDefault();Replace(slot,replacement?.RawPoints??0m);}description="exchanged lineup-slot scores with the weekly opponent";break;
            case "weeklytefrenzy": foreach(var slot in input.Starters.Where(x=>x.Position=="TE"))Replace(slot,Frenzy(slot,"TE"));description="applied TE Frenzy scoring";break;
            case "weeklydasboot": foreach(var slot in input.Starters.Where(x=>x.Position=="K")){var s=Stats(slot);Replace(slot,effective.GetValueOrDefault(slot.Slot,slot.RawPoints)-s.FieldGoalPoints+s.FieldGoalYards);}description="made made field goals worth one point per kick yard";break;
            case "weeklywrfrenzy": ApplyFrenzy("WR");description="filled flex slots with WRs and applied WR Frenzy scoring to every starting WR";break;
            case "weeklyrbfrenzy": ApplyFrenzy("RB");description="filled flex slots with RBs and applied RB Frenzy scoring to every starting RB";break;
            case "weeklyqbfrenzy": foreach(var slot in input.Starters.Where(x=>x.Position=="QB")){var s=Stats(slot);var incompletions=Math.Max(0,s.PassingAttempts-s.Completions);Replace(slot,s.Completions*3m+incompletions+(s.PassingTouchdowns+s.RushingTouchdowns)*10m+(s.PassingYards+s.RushingYards)*.5m+s.PassingInterceptions*15m+s.Fumbles*25m);}description="applied QB Frenzy scoring to the normal QB slot";break;
            case "weeklydeffrenzy": foreach(var slot in input.Starters.Where(x=>x.Position=="DEF")){var s=Stats(slot);Replace(slot,s.DefensiveSacks*10m+s.DefensiveInterceptions*10m+s.DefensiveFumbleRecoveries*10m);}description="made sacks, interceptions, and recovered fumbles worth 10 each";break;
            case "weeklypprfrenzy": foreach(var slot in input.Starters.Where(x=>x.Position is "RB" or "WR" or "TE")){var s=Stats(slot);Replace(slot,effective.GetValueOrDefault(slot.Slot,slot.RawPoints)+s.ReceivingYards);}description="added one bonus point for every receiving yard, so each catch is worth the yards gained on that catch";break;
            case "weeklydoubletd": foreach(var slot in input.Starters){var s=Stats(slot);Replace(slot,effective.GetValueOrDefault(slot.Slot,slot.RawPoints)+s.TouchdownPoints);}description="doubled all touchdown points";break;
            case "weeklydeepend": total+=input.Bench.Sum(x=>x.RawPoints);description="added every bench player's score";break;
            case "weeklyppy": foreach(var slot in input.Starters.Where(x=>x.Position is "QB" or "RB" or "WR" or "TE")){var s=Stats(slot);Replace(slot,effective.GetValueOrDefault(slot.Slot,slot.RawPoints)-s.PassingYardPoints-s.RushingYardPoints-s.ReceivingYardPoints+s.PassingYards+s.RushingYards+s.ReceivingYards);}description="made each passing, rushing, and receiving yard worth one point";break;
            case "weeklychaos": description="removed weekly card-count and category restrictions";break;
            case "weeklycaphit": description="scheduled the final 15-point starter cap";break;
            default: description="has no executable handler";break;
        }
        return total;

        void ApplyFrenzy(string position)
        {
            foreach(var slot in input.Starters.Where(x=>x.Position==position&&!x.Slot.StartsWith("FLEX",StringComparison.OrdinalIgnoreCase)))Replace(slot,Frenzy(slot,position));
            foreach(var slot in input.Starters.Where(x=>x.Slot.StartsWith("FLEX",StringComparison.OrdinalIgnoreCase)))
            {
                var chosen=slot.Position==position?slot:input.Bench.Where(x=>x.Position==position).OrderByDescending(x=>input.Projections.GetValueOrDefault(x.PlayerId)).ThenBy(x=>x.PlayerId).FirstOrDefault();
                Replace(slot,chosen is null?0m:Frenzy(chosen,position));
            }
        }
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
            if (slot is null || !input.LeagueHighestStarterScoreByPosition.TryGetValue(slot.Position,out var leagueHigh)) return false;
            var lowest=input.Starters.Where(x=>x.Position==slot.Position).OrderBy(x=>effectiveScores.GetValueOrDefault(x.Slot,x.RawPoints)).ThenBy(x=>x.PlayerId).First();
            var existing = effectiveScores.GetValueOrDefault(lowest.Slot, lowest.RawPoints);
            change = leagueHigh - existing;
            description = $"replaced the lowest-scoring starting {slot.Position}, {lowest.PlayerName} ({existing:0.##}), with the league-high starting {slot.Position} score of {leagueHigh:0.##}";
            return true;
        }
        if (handler == "offsides")
        {
            change=20m;description="started the card owner's team at 20 points";return true;
        }
        if (handler == "spygate")
        {
            if(slot is null)return false;var replacement=input.Bench.Where(x=>x.Position==slot.Position&&!string.Equals(input.PlayerStats.GetValueOrDefault(x.PlayerId)?.InjuryStatus,"Out",StringComparison.OrdinalIgnoreCase)&&!string.Equals(input.PlayerStats.GetValueOrDefault(x.PlayerId)?.InjuryStatus,"IR",StringComparison.OrdinalIgnoreCase)).OrderBy(x=>input.Projections.GetValueOrDefault(x.PlayerId)).ThenBy(x=>x.PlayerId).FirstOrDefault();
            var existing=effectiveScores.GetValueOrDefault(slot.Slot,slot.RawPoints);change=(replacement?.RawPoints??0m)-existing;description=replacement is null?$"replaced {slot.PlayerName} with an empty slot because no eligible bench player existed":$"replaced {slot.PlayerName} with {replacement.PlayerName}'s {replacement.RawPoints:0.##}";return true;
        }
        if (handler is "tradedwr" or "tradedrb" or "tradedte")
        {
            if (slot is null || effect.ReferencedPlayerId is null || !input.ReferencedPlayerScores.TryGetValue(effect.ReferencedPlayerId, out var replacement)) return false;
            var existing = effectiveScores.GetValueOrDefault(slot.Slot, slot.RawPoints);
            change = replacement - existing;
            description = $"replaced {slot.PlayerName}'s {existing:0.##} with {replacement:0.##}";
            return true;
        }
        if (handler is "1v1mebro" or "1v1owner" or "1v1opponent")
        {
            if (slot is null || effect.ReferencedPlayerId is null || !input.ReferencedPlayerScores.TryGetValue(effect.ReferencedPlayerId, out var opposing)) return false;
            var own = effectiveScores.GetValueOrDefault(slot.Slot, slot.RawPoints);
            var wins=handler=="1v1opponent"?own>opposing:own>=opposing;
            change = wins ? opposing : -own;
            description = wins ? $"{slot.PlayerName} won {own:0.##} to {opposing:0.##} and claimed both scores" : $"{slot.PlayerName} lost {own:0.##} to {opposing:0.##}, so the opponent claimed both scores";
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
        if(handler=="bromance")
        {
            if(slot is null)return false;
            var sourceId=effect.ReferencedPlayerId??"4046";
            if(!input.ReferencedPlayerScores.TryGetValue(sourceId,out var sourceScore))return false;
            var currentScore=effectiveScores.GetValueOrDefault(slot.Slot,slot.RawPoints);
            change=sourceScore*2m-currentScore;
            description=$"replaced {slot.PlayerName}'s {currentScore:0.##} with two times Patrick Mahomes's {sourceScore:0.##} points";
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
            case "tdsaboteur":
                change = -stats.TouchdownPoints;
                description = $"removed {stats.TouchdownPoints:0.##} touchdown points from {slot.PlayerName}";
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

