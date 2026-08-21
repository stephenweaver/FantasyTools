using FantasyTools.Api.Documents;
using FantasyTools.Api.Models;
using FantasyTools.Api.Game.Domain;
using FantasyTools.Api.Game.Engine;
using System.Collections.Concurrent;
using System.Text.Json;

namespace FantasyTools.Api.Services;

public class LeagueGameService(IFileService files, HttpClient sleeper, IChaosScoringEngine scoring) : ILeagueGameService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static Dictionary<string, PlayerInfo> PlayerCache = [];
    private static DateTime PlayerCacheAt;

    public async Task<LeagueGameDocument> Sync(string leagueId, string actorUserId, int week)
    {
        var league = await League(leagueId);
        await EnsureMember(leagueId, actorUserId);
        var leagueInfo = await GetJson($"https://api.sleeper.app/v1/league/{league.SleeperLeagueId}");
        var season = Text(leagueInfo,"season");
        var scoringSettings = DecimalMap(leagueInfo,"scoring_settings");
        var rosterPositions = Strings(leagueInfo,"roster_positions");
        var users = await GetJson($"https://api.sleeper.app/v1/league/{league.SleeperLeagueId}/users");
        var rosters = await GetJson($"https://api.sleeper.app/v1/league/{league.SleeperLeagueId}/rosters");
        var matchups = await GetJson($"https://api.sleeper.app/v1/league/{league.SleeperLeagueId}/matchups/{Math.Clamp(week,1,18)}", allowMissing:true);
        var playerMap = await Players();
        var rawStats = await GetPlayerData($"https://api.sleeper.com/stats/nfl/{season}/{Math.Clamp(week,1,18)}?season_type=regular");
        var rawProjections = await GetPlayerData($"https://api.sleeper.com/projections/nfl/{season}/{Math.Clamp(week,1,18)}?season_type=regular");
        var userMap = users.EnumerateArray().ToDictionary(x=>Text(x,"user_id"),x=>x);
        var snapshot = await LoadOrCreate(league);
        snapshot.Season=season; snapshot.ScoringSettings=scoringSettings;
        snapshot.Teams = rosters.EnumerateArray().Select(roster =>
        {
            var owner=Text(roster,"owner_id"); userMap.TryGetValue(owner,out var user);
            var starters=Strings(roster,"starters").ToHashSet();
            return new SleeperTeamSnapshot {
                RosterId=Number(roster,"roster_id"), OwnerId=owner,
                ManagerName=user.ValueKind==JsonValueKind.Object ? (Text(user,"display_name") is { Length:>0 } d ? d : Text(user,"username")) : $"Roster {Number(roster,"roster_id")}",
                TeamName=user.ValueKind==JsonValueKind.Object && user.TryGetProperty("metadata",out var meta) && Text(meta,"team_name") is { Length:>0 } n ? n : (user.ValueKind==JsonValueKind.Object ? Text(user,"display_name") : $"Roster {Number(roster,"roster_id")}"),
                Wins=NestedNumber(roster,"settings","wins"), Losses=NestedNumber(roster,"settings","losses"),
                Players=Strings(roster,"players").Select(id=>ToPlayer(id,playerMap,starters.Contains(id),0,
                    rawStats.GetValueOrDefault(id),rawProjections.GetValueOrDefault(id),scoringSettings)).ToList()
            };
        }).ToList();
        var currentMatchups = matchups.ValueKind==JsonValueKind.Array ? matchups.EnumerateArray().Select(item=>new SleeperMatchupSnapshot {
            Week=week, MatchupId=Number(item,"matchup_id"), RosterId=Number(item,"roster_id"), Points=Decimal(item,"points"),
            Starters=Strings(item,"starters"), PlayerPoints=DecimalMap(item,"players_points")
        }).ToList() : [];
        snapshot.Matchups.RemoveAll(x=>x.Week==week);
        snapshot.Matchups.AddRange(currentMatchups);
        foreach(var team in snapshot.Teams)
        {
            var matchup=snapshot.Matchups.FirstOrDefault(x=>x.RosterId==team.RosterId);
            if(matchup==null) continue;
            var all=team.Players.ToDictionary(x=>x.PlayerId);
            for(var i=0;i<matchup.Starters.Count;i++) if(all.TryGetValue(matchup.Starters[i],out var player)){player.Starter=true;player.StartingSlot=i<rosterPositions.Count?rosterPositions[i]:player.Position;}
            foreach(var pair in matchup.PlayerPoints) if(all.TryGetValue(pair.Key,out var player)) player.Points=pair.Value;
        }
        var openWeek=snapshot.Weeks.FirstOrDefault(x=>x.Week==week);
        if(openWeek is not null&&openWeek.TuesdayInjuryStatuses.Count==0&&EasternNow().DayOfWeek==DayOfWeek.Tuesday)
            openWeek.TuesdayInjuryStatuses=snapshot.Teams.SelectMany(x=>x.Players).GroupBy(x=>x.PlayerId).ToDictionary(x=>x.Key,x=>x.Last().Stats.InjuryStatus??"");
        if(openWeek is not null&&openWeek.MondaySnapshotAtUtc is null&&DateTime.UtcNow.DayOfWeek==DayOfWeek.Monday)
        {
            openWeek.MondayScores=snapshot.Matchups.Where(x=>x.Week==week).ToDictionary(x=>x.RosterId,x=>x.Points);
            openWeek.MondaySnapshotAtUtc=DateTime.UtcNow;
        }
        snapshot.SleeperStatus=snapshot.Teams.Any(x=>x.Players.Count>0)?(snapshot.Matchups.Count>0?"in_season":"draft_complete"):"pre_draft";
        snapshot.LastSleeperSyncAt=DateTime.UtcNow; snapshot.At=DateTime.UtcNow; await files.Upsert(snapshot); return snapshot;
    }

    public async Task<object> GetWeek(string leagueId,string userId,int week)
    {
        var game=await Load(leagueId); var roster=await UserRoster(leagueId,userId); var state=game.Weeks.FirstOrDefault(x=>x.Week==week);
        if(state==null) return new { week, status="setup", sleeperStatus=game.SleeperStatus, team=game.Teams.FirstOrDefault(x=>x.RosterId==roster), hand=Array.Empty<object>(), selections=Array.Empty<object>(), canDraw=false, cardsNeeded=0, teams=game.Teams, matchups=game.Matchups.Where(x=>x.Week==week), chaosScores=Array.Empty<object>() };
        if(state.Status=="selection_open"&&DateTime.UtcNow>=state.DeadlineUtc)
        {
            var deadlineWorkspace=await Workspace(leagueId);var deck=Deck(deadlineWorkspace); EnsureDeck(deck);
            var chaosWeek=IsChaosWeek(deadlineWorkspace,week);
            foreach(var teamState in state.Teams){PrepareDraw(game,state,teamState,deck);if(!chaosWeek)AutoFill(game,state,teamState,week);}
            LockProjections(game,state);
            state.Status="revealed";state.RevealedAtUtc=DateTime.UtcNow;game.At=DateTime.UtcNow;await files.Upsert(game);
        }
        var own=state.Teams.FirstOrDefault(x=>x.RosterId==roster);
        var seasonHand=SeasonHand(game,roster);
        var workspace=await Workspace(leagueId);
        if(ResolveExpiredChallenges(game,state,workspace,week)){game.At=DateTime.UtcNow;await files.Upsert(game);}
        var chaosScores=CalculateScores(game,state,workspace,week);
        object publicSelections=state.Status is "revealed" or "live" or "finalized" ? state.Teams.Select(x=>new {x.RosterId,x.Selections}).ToList() : Array.Empty<object>();
        return new { state.Week,state.Status,state.DeadlineUtc,state.RevealedAtUtc,sleeperStatus=game.SleeperStatus,team=game.Teams.FirstOrDefault(x=>x.RosterId==roster),hand=own?.DrawnAtUtc is null?seasonHand.Cards:own.Hand,selections=own?.Selections??[],miniBattlePlayerIds=own?.MiniBattlePlayerIds??[],canDraw=state.Status=="selection_open"&&own?.DrawnAtUtc is null,cardsNeeded=Math.Max(0,8-seasonHand.Cards.Count),publicSelections,teams=game.Teams,matchups=game.Matchups.Where(x=>x.Week==week),chaosScores };
    }

    public async Task<object> GetUsageReport(string leagueId,string userId)
    {
        var workspace=await Workspace(leagueId);
        var isCommissioner=workspace.PrimaryCommissionerUserId==userId||workspace.Collaborators.Any(x=>x.UserId==userId&&x.Permissions.Count>0);
        if(!isCommissioner)throw new UnauthorizedAccessException("Commissioner access is required.");
        var game=await Load(leagueId);
        var plays=(from week in game.Weeks
                   from teamWeek in week.Teams
                   from selection in teamWeek.Selections
                   let card=workspace.Cards.FirstOrDefault(x=>x.Id==selection.CardId)
                   let team=game.Teams.FirstOrDefault(x=>x.RosterId==teamWeek.RosterId)
                   let targetRoster=int.TryParse(selection.TargetRosterId,out var parsed)?game.Teams.FirstOrDefault(x=>x.RosterId==parsed):null
                   let targetPlayer=targetRoster?.Players.FirstOrDefault(x=>x.PlayerId==selection.TargetPlayerId)
                   select new {week=week.Week,status=week.Status,selectedAtUtc=selection.SelectedAtUtc,rosterId=teamWeek.RosterId,manager=team?.ManagerName??$"Roster {teamWeek.RosterId}",teamName=team?.TeamName??"",cardId=selection.CardId,cardName=card?.Name??"Unknown card",category=selection.Category,targetTeam=targetRoster?.TeamName??targetRoster?.ManagerName??"",targetPlayer=targetPlayer?.Name??"",targetSlot=selection.TargetSlot??"",cancelledCopyId=selection.CancelledCopyId??""})
                   .OrderByDescending(x=>x.week).ThenByDescending(x=>x.selectedAtUtc).ToList();
        var summary=plays.GroupBy(x=>new{x.cardId,x.cardName,x.category}).Select(group=>new{group.Key.cardId,group.Key.cardName,group.Key.category,plays=group.Count(),managers=group.Select(x=>x.rosterId).Distinct().Count(),lastPlayedWeek=group.Max(x=>x.week)}).OrderByDescending(x=>x.plays).ThenBy(x=>x.cardName).ToList();
        return new {totalPlays=plays.Count,uniqueCards=summary.Count,summary,plays};
    }

    public async Task<object> Deal(string leagueId,string actorUserId,int week) => await Mutate(leagueId,async game =>
    {
        await Commissioner(leagueId,actorUserId);
        if(game.Weeks.Any(x=>x.Week==week))return (object)new{opened=true,week,alreadyOpen=true};
        var workspace=await Workspace(leagueId); EnsureDeck(Deck(workspace));
        foreach(var hand in game.Hands)DiscardPreviouslyPlayed(game,hand,week);
        var state=new WeeklyGameDocument{Week=week,DeadlineUtc=NextWednesdayAtEightEastern(),Status="selection_open",Teams=game.Teams.Select(team=>new TeamWeekDocument{RosterId=team.RosterId}).ToList()};
        game.Weeks.Add(state); return (object)new {opened=true,week,teams=state.Teams.Count,state.DeadlineUtc};
    });

    public async Task<object> Draw(string leagueId,string userId,int week)=>await Mutate(leagueId,async game=>
    {
        var roster=await UserRoster(leagueId,userId);var state=Week(game,week);EnsureOpen(state);var team=state.Teams.Single(x=>x.RosterId==roster);
        if(team.DrawnAtUtc is not null)throw new InvalidOperationException("You already drew cards for this week.");
        var deck=Deck(await Workspace(leagueId));EnsureDeck(deck);var hand=SeasonHand(game,roster);
        DiscardPreviouslyPlayed(game,hand,week);var before=hand.Cards.Count;DrawToEight(hand,deck,leagueId,week,roster);
        hand.LastDrawnWeek=week;team.Hand=hand.Cards.Select(CloneCard).ToList();team.DrawnAtUtc=DateTime.UtcNow;
        return (object)new{drawn=true,week,drawnCards=hand.Cards.Count-before,hand=team.Hand,cardsInHand=team.Hand.Count};
    });

    public async Task<object> Select(string leagueId,string userId,int week,SaveSelectionRequest request)=>await Mutate(leagueId,async game=>
    {
        var roster=await UserRoster(leagueId,userId); var state=Week(game,week); EnsureOpen(state); var team=state.Teams.Single(x=>x.RosterId==roster);
        if(team.DrawnAtUtc is null)throw new InvalidOperationException("Draw your cards for this week before making selections.");
        var card=team.Hand.SingleOrDefault(x=>x.CopyId==request.CopyId)??throw new KeyNotFoundException("That card is not in your hand.");
        if(team.Selections.Any(x=>x.CopyId==card.CopyId)) return (object)new {selected=true};
        var category=Normalize(card.Category);var workspace=await Workspace(leagueId);var chaosWeek=IsChaosWeek(workspace,week);var limit=category=="UNIQUE"?2:1;
        if(!chaosWeek&&(team.Selections.Count>=4||team.Selections.Count(x=>Normalize(x.Category)==category)>=limit)) throw new InvalidOperationException($"Your {category} selection slots are full.");
        if(chaosWeek&&team.Selections.Count>=team.Hand.Count)throw new InvalidOperationException("Every card in your Chaos Week hand is already selected.");
        ValidateTarget(game,week,roster,card,request);
        team.Selections.Add(new CardSelectionDocument{CopyId=card.CopyId,CardId=card.CardId,Category=category,TargetRosterId=request.TargetRosterId,TargetPlayerId=request.TargetPlayerId,TargetSlot=request.TargetSlot,SelectedAtUtc=DateTime.UtcNow});
        return (object)new {selected=true,selections=team.Selections};
    });

    public async Task<object> Return(string leagueId,string userId,int week,string copyId)=>await Mutate(leagueId,async game=>{var roster=await UserRoster(leagueId,userId);var state=Week(game,week);EnsureOpen(state);state.Teams.Single(x=>x.RosterId==roster).Selections.RemoveAll(x=>x.CopyId==copyId);return (object)new{returned=true};});
    public async Task<object> Discard(string leagueId,string userId,int week,string copyId)=>await Mutate(leagueId,async game=>
    {
        var roster=await UserRoster(leagueId,userId);var state=Week(game,week);EnsureOpen(state);var team=state.Teams.Single(x=>x.RosterId==roster);
        if(team.DrawnAtUtc is null)throw new InvalidOperationException("Draw this week's cards before discarding.");
        if(!string.IsNullOrWhiteSpace(team.DiscardedCopyId))throw new InvalidOperationException("You already discarded one card this week.");
        if(team.Selections.Any(x=>x.CopyId==copyId))throw new InvalidOperationException("Return this card from your selections before discarding it.");
        if(team.Hand.All(x=>x.CopyId!=copyId))throw new KeyNotFoundException("That card is not in your hand.");
        team.Hand.RemoveAll(x=>x.CopyId==copyId);var seasonHand=SeasonHand(game,roster);seasonHand.Cards.RemoveAll(x=>x.CopyId==copyId);
        team.DiscardedCopyId=copyId;team.DiscardedAtUtc=DateTime.UtcNow;return (object)new{discarded=true,cardsInHand=team.Hand.Count};
    });
    public async Task<object> SetChallengeTarget(string leagueId,string userId,int week,string copyId,string cancelledCopyId)=>await Mutate(leagueId,async game=>
    {
        var roster=await UserRoster(leagueId,userId);var state=Week(game,week);if(state.Status is not ("revealed" or "live"))throw new InvalidOperationException("Challenge Flag targets are chosen after cards are revealed.");
        if(DateTime.UtcNow>=ChallengeCutoff(state))throw new InvalidOperationException("The Thursday Challenge Flag deadline has passed.");
        var own=state.Teams.Single(x=>x.RosterId==roster);var selection=own.Selections.SingleOrDefault(x=>x.CopyId==copyId)??throw new KeyNotFoundException("Challenge Flag selection not found.");
        var workspace=await Workspace(leagueId);var card=workspace.Cards.FirstOrDefault(x=>x.Id==selection.CardId);if(card is null||!card.Name.Equals("Challenge Flag",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("That selection is not a Challenge Flag.");
        var opponent=OpponentRoster(game,week,roster);var legal=state.Teams.Single(x=>x.RosterId==opponent).Selections.Any(x=>x.CopyId==cancelledCopyId);if(!legal)throw new InvalidOperationException("Choose one revealed card played by your weekly opponent.");
        selection.CancelledCopyId=cancelledCopyId;return (object)new{saved=true,selection.CancelledCopyId};
    });
    public async Task<object> SetMiniBattlePlayers(string leagueId,string userId,int week,IReadOnlyList<string> playerIds)=>await Mutate(leagueId,async game=>
    {
        var roster=await UserRoster(leagueId,userId);var state=Week(game,week);EnsureOpen(state);var workspace=await Workspace(leagueId);
        if(!workspace.WeeklyCards.Any(x=>x.Week==week&&x.Active&&NormalizeName(x.Name)=="minibattle"))throw new InvalidOperationException("Mini Battle is not active this week.");
        var team=game.Teams.Single(x=>x.RosterId==roster);var selected=playerIds.Distinct().ToList();
        if(selected.Count!=4)throw new InvalidOperationException("Choose exactly one starting QB, RB, WR, and TE.");
        var players=selected.Select(id=>team.Players.FirstOrDefault(x=>x.PlayerId==id&&x.Starter)??throw new InvalidOperationException("Every Mini Battle choice must be one of your Sleeper starters.")).ToList();
        foreach(var position in new[]{"QB","RB","WR","TE"})if(players.Count(x=>x.Position==position)!=1)throw new InvalidOperationException($"Choose exactly one starting {position}.");
        state.Teams.Single(x=>x.RosterId==roster).MiniBattlePlayerIds=selected;return (object)new{saved=true,playerIds=selected};
    });
    public async Task<object> SetDeadline(string leagueId,string actorUserId,int week,DateTime deadlineUtc)=>await Mutate(leagueId,async game=>{await Commissioner(leagueId,actorUserId);var state=Week(game,week);state.DeadlineUtc=deadlineUtc.ToUniversalTime();return (object)new{state.Week,state.DeadlineUtc};});
    public async Task<object> Reveal(string leagueId,string actorUserId,int week)=>await Mutate(leagueId,async game=>{await Commissioner(leagueId,actorUserId);var state=Week(game,week);var chaosWeek=IsChaosWeek(await Workspace(leagueId),week);if(!chaosWeek&&state.Teams.Any(x=>!Complete(x.Selections)))throw new InvalidOperationException("Every team must have 1 Boost, 1 Attack, and 2 Unique cards selected.");LockProjections(game,state);state.Status="revealed";state.RevealedAtUtc=DateTime.UtcNow;return (object)new{revealed=true,state.RevealedAtUtc};});

    private object[] CalculateScores(LeagueGameDocument game,WeeklyGameDocument state,CardWorkspaceDocument workspace,int week)
    {
        var playerScores=game.Matchups.Where(x=>x.Week==week).SelectMany(x=>x.PlayerPoints).GroupBy(x=>x.Key).ToDictionary(x=>x.Key,x=>x.Last().Value);
        var weekly=workspace.WeeklyCards.FirstOrDefault(x=>x.Week==week&&x.Active);
        List<SlotScore> Slots(SleeperTeamSnapshot source,bool starters)
        {
            var counters=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            return source.Players.Where(x=>x.Starter==starters).Select(player=>
            {
                counters[player.Position]=counters.GetValueOrDefault(player.Position)+1;
                var baseSlot=starters?(string.IsNullOrWhiteSpace(player.StartingSlot)?player.Position:player.StartingSlot):player.Position;
                counters[baseSlot]=counters.GetValueOrDefault(baseSlot)+1;
                var slot=starters?(counters[baseSlot]==1?baseSlot:$"{baseSlot}{counters[baseSlot]}"):$"BENCH-{baseSlot}{counters[baseSlot]}";
                return new SlotScore(slot,player.PlayerId,player.Name,player.Position,player.Points);
            }).ToList();
        }
        return game.Teams.Select(team=>
        {
            var effects=new List<ActiveEffect>();
            foreach(var teamWeek in state.Teams)
            foreach(var selection in teamWeek.Selections)
            {
                var card=workspace.Cards.FirstOrDefault(x=>x.Id==selection.CardId);if(card is null)continue;
                var targetRoster=int.TryParse(selection.TargetRosterId,out var parsed)?parsed:teamWeek.RosterId;
                var normalizedName=NormalizeName(card.Name);
                if(normalizedName=="picksix")
                {
                    if(team.RosterId==targetRoster)effects.Add(ToEffect(card,selection,targetRoster,CardCategory.Attack,"pick-six"));
                    if(team.RosterId==teamWeek.RosterId)
                    {
                        var defense=team.Players.FirstOrDefault(x=>x.Starter&&x.Position=="DEF");if(defense is not null)effects.Add(new ActiveEffect(EffectId(selection.CopyId,"pick-six-owner"),card.Name,CardCategory.Boost,EffectType.Custom,new(TargetType.SpecificPlayer,TeamGuid(team.RosterId),defense.StartingSlot,null,defense.PlayerId,null),card.Amount,selection.TargetPlayerId,CustomHandler:"pick-six"));
                    }
                }
                else if(normalizedName.StartsWith("traded",StringComparison.Ordinal)&&team.RosterId==teamWeek.RosterId)
                {
                    var source=game.Teams.SelectMany(x=>x.Players).FirstOrDefault(x=>x.PlayerId==selection.TargetPlayerId);var destination=source is null?null:team.Players.Where(x=>x.Starter&&x.Position==source.Position).OrderBy(x=>x.Projection).ThenBy(x=>x.PlayerId).FirstOrDefault();
                    if(source is not null&&destination is not null)effects.Add(new ActiveEffect(EffectId(selection.CopyId,"traded"),card.Name,CardCategory.Unique,EffectType.Custom,new(TargetType.SpecificPlayer,TeamGuid(team.RosterId),destination.StartingSlot,null,destination.PlayerId,null),0m,source.PlayerId,CustomHandler:card.Name));
                }
                else if(normalizedName=="1v1mebro")
                {
                    var ownerPlayer=game.Teams.First(x=>x.RosterId==teamWeek.RosterId).Players.FirstOrDefault(x=>x.PlayerId==selection.TargetPlayerId);var duelOpponentRoster=OpponentRoster(game,week,teamWeek.RosterId);var opponentPlayer=ownerPlayer is null?null:game.Teams.FirstOrDefault(x=>x.RosterId==duelOpponentRoster)?.Players.Where(x=>x.Starter&&x.Position==ownerPlayer.Position).OrderByDescending(x=>x.Projection).ThenBy(x=>x.PlayerId).FirstOrDefault();
                    if(ownerPlayer is not null&&opponentPlayer is not null&&team.RosterId==teamWeek.RosterId)effects.Add(new ActiveEffect(EffectId(selection.CopyId,"1v1-owner"),card.Name,CardCategory.Unique,EffectType.Custom,new(TargetType.SpecificPlayer,TeamGuid(team.RosterId),ownerPlayer.StartingSlot,null,ownerPlayer.PlayerId,null),0m,opponentPlayer.PlayerId,CustomHandler:"1v1-owner"));
                    if(ownerPlayer is not null&&opponentPlayer is not null&&team.RosterId==duelOpponentRoster)effects.Add(new ActiveEffect(EffectId(selection.CopyId,"1v1-opponent"),card.Name,CardCategory.Unique,EffectType.Custom,new(TargetType.SpecificPlayer,TeamGuid(team.RosterId),opponentPlayer.StartingSlot,null,opponentPlayer.PlayerId,null),0m,ownerPlayer.PlayerId,CustomHandler:"1v1-opponent"));
                }
                else if(normalizedName=="mvp"&&team.RosterId==teamWeek.RosterId)
                {
                    var selected=team.Players.FirstOrDefault(x=>x.PlayerId==selection.TargetPlayerId&&x.Starter);var lowest=selected is null?null:team.Players.Where(x=>x.Starter&&x.Position==selected.Position).OrderBy(x=>x.Points).ThenBy(x=>x.PlayerId).FirstOrDefault();
                    if(lowest is not null)effects.Add(new ActiveEffect(EffectId(selection.CopyId,"mvp"),card.Name,CardCategory.Unique,EffectType.Custom,new(TargetType.SpecificPlayer,TeamGuid(team.RosterId),lowest.StartingSlot,null,lowest.PlayerId,null),0m,CustomHandler:"mvp"));
                }
                else if(targetRoster==team.RosterId)effects.Add(ToEffect(card,selection,targetRoster));
            }
            if(weekly is not null)effects.Add(ToWeeklyEffect(weekly,team.RosterId,state.Teams.First(x=>x.RosterId==team.RosterId).MiniBattlePlayerIds));
            var starters=Slots(team,true);var bench=Slots(team,false);
            var ownMatch=game.Matchups.FirstOrDefault(x=>x.Week==week&&x.RosterId==team.RosterId);
            var opponentRoster=ownMatch is null?0:game.Matchups.FirstOrDefault(x=>x.Week==week&&x.MatchupId==ownMatch.MatchupId&&x.RosterId!=team.RosterId)?.RosterId??0;
            var opponent=game.Teams.FirstOrDefault(x=>x.RosterId==opponentRoster);
            var stats=game.Teams.SelectMany(x=>x.Players).GroupBy(x=>x.PlayerId).ToDictionary(x=>x.Key,x=>ToWeekStats(x.Last().Stats,state.TuesdayInjuryStatuses.GetValueOrDefault(x.Key)));
            var projections=state.LockedProjections.Count>0?state.LockedProjections:game.Teams.SelectMany(x=>x.Players).GroupBy(x=>x.PlayerId).ToDictionary(x=>x.Key,x=>x.Last().Projection);
            var leagueStarters=game.Teams.SelectMany(x=>x.Players.Where(p=>p.Starter)).ToList();
            var highest=leagueStarters.Select(x=>x.Points).DefaultIfEmpty().Max();
            var highestByPosition=leagueStarters.GroupBy(x=>x.Position,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.Max(p=>p.Points),StringComparer.OrdinalIgnoreCase);
            var result=scoring.Calculate(new TeamScoreInput(TeamGuid(team.RosterId),starters,playerScores,effects)
            {
                PlayerStats=stats,Bench=bench,OpponentStarters=opponent is null?[]:Slots(opponent,true),OpponentBench=opponent is null?[]:Slots(opponent,false),Projections=projections,
                LeagueHighestPlayerScore=highest,LeagueHighestStarterScoreByPosition=highestByPosition,ScoreEnteringMonday=state.MondayScores.GetValueOrDefault(team.RosterId),OpponentScoreEnteringMonday=state.MondayScores.GetValueOrDefault(opponentRoster)
            });
            return (object)new {rosterId=team.RosterId,result.SleeperScore,result.ChaosScore,result.Lines};
        }).ToArray();
    }

    private static ActiveEffect ToEffect(CardDraftDocument card,CardSelectionDocument selection,int rosterId,CardCategory? categoryOverride=null,string handlerOverride=null)
    {
        var category=Enum.TryParse<CardCategory>(Normalize(card.Category),true,out var parsed)?parsed:CardCategory.Unique;
        var type=ParseEffect(card.EffectType);
        var amount=category==CardCategory.Attack&&type is EffectType.Percentage or EffectType.FlatPoints?-Math.Abs(card.Amount):card.Amount;
        var targetType=!string.IsNullOrWhiteSpace(selection.TargetPlayerId)?TargetType.SpecificPlayer:!string.IsNullOrWhiteSpace(selection.TargetSlot)&&selection.TargetSlot!="AUTO"?TargetType.StartingSlot:TargetType.Team;
        var dynamicRule=card.Name.Equals("Challenge Flag",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(selection.CancelledCopyId)?$"cancel:{selection.CancelledCopyId}":null;
        if(dynamicRule is not null)targetType=TargetType.Dynamic;
        var target=new CardTarget(targetType,TeamGuid(rosterId),selection.TargetSlot,null,selection.TargetPlayerId,dynamicRule);
        var referenced=handlerOverride=="pick-six"?selection.TargetPlayerId:card.SourcePlayerId;
        return new ActiveEffect(Guid.TryParse(selection.CopyId,out var id)?id:Guid.NewGuid(),card.Name,categoryOverride??category,type,target,amount,referenced,card.DestinationSlot,card.Multiplier,handlerOverride??card.Name);
    }

    private static ActiveEffect ToWeeklyEffect(WeeklyCardDocument card,int rosterId,IReadOnlyList<string> miniBattlePlayerIds)
    {
        var dynamicRule=NormalizeName(card.Name)=="minibattle"?string.Join(',',miniBattlePlayerIds):null;
        var target=new CardTarget(TargetType.Team,TeamGuid(rosterId),null,null,null,dynamicRule);
        return new ActiveEffect(Guid.TryParse(card.Id,out var id)?id:Guid.NewGuid(),card.Name,CardCategory.Unique,EffectType.Custom,target,card.Amount,CustomHandler:$"weekly-{card.Name}");
    }

    private static EffectType ParseEffect(string value)
    {
        var text=(value??"").ToLowerInvariant();
        if(text.Contains("referenced player"))return EffectType.ReferencedPlayerReplacesSlot;
        if(text.Contains("block"))return EffectType.BlockAttack;
        if(text.Contains("reduce attack"))return EffectType.ReduceAttack;
        if(text.Contains("percent"))return EffectType.Percentage;
        if(text.Contains("flat")||text.Contains("point"))return EffectType.FlatPoints;
        return EffectType.Custom;
    }
    private static Guid TeamGuid(int rosterId){Span<byte> bytes=stackalloc byte[16];BitConverter.TryWriteBytes(bytes,rosterId);return new Guid(bytes);}
    private static Guid EffectId(string copyId,string suffix)=>Guid.TryParse(copyId,out var id)?new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(id+suffix))):Guid.NewGuid();
    private static string NormalizeName(string value)=>new((value??"").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private async Task<object> Mutate(string leagueId,Func<LeagueGameDocument,Task<object>> action){var gate=Locks.GetOrAdd(leagueId,_=>new(1,1));await gate.WaitAsync();try{var game=await Load(leagueId);var result=await action(game);game.At=DateTime.UtcNow;await files.Upsert(game);return result;}finally{gate.Release();}}
    private async Task<ChaosLeagueDocument> League(string id)=>await files.Retrieve(new ChaosLeagueDocument{LeagueId=id})??throw new KeyNotFoundException("League not found.");
    private async Task<CardWorkspaceDocument> Workspace(string id)=>await files.Retrieve(new CardWorkspaceDocument{LeagueId=id})??throw new KeyNotFoundException("Card workspace not found.");
    private async Task<LeagueGameDocument> Load(string id)=>await files.Retrieve(new LeagueGameDocument{LeagueId=id})??await LoadOrCreate(await League(id));
    private async Task<LeagueGameDocument> LoadOrCreate(ChaosLeagueDocument league)=>await files.Retrieve(new LeagueGameDocument{LeagueId=league.LeagueId})??new(){LeagueId=league.LeagueId,SleeperLeagueId=league.SleeperLeagueId,At=DateTime.UtcNow};
    private async Task EnsureMember(string leagueId,string userId){var index=await files.Retrieve(new UserChaosLeagueDocument{UserId=userId});if(index?.LeagueId!=leagueId)throw new UnauthorizedAccessException("You are not a member of this league.");}
    private async Task Commissioner(string leagueId,string userId){var league=await League(leagueId);if(league.PrimaryCommissionerUserId!=userId)throw new UnauthorizedAccessException("Only the primary commissioner can do that.");}
    private async Task<int> UserRoster(string leagueId,string userId){var doc=await files.Retrieve(new LeagueRosterDocument{LeagueId=leagueId})??throw new KeyNotFoundException("Roster setup not found.");return doc.Assignments.FirstOrDefault(x=>x.FantasyToolsUserId==userId)?.RosterId??throw new InvalidOperationException("Your account is not connected to a Sleeper roster.");}
    private async Task<JsonElement> GetJson(string url,bool allowMissing=false){var response=await sleeper.GetAsync(url);if(allowMissing&&!response.IsSuccessStatusCode)return JsonDocument.Parse("[]").RootElement.Clone();response.EnsureSuccessStatusCode();return (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement.Clone();}
    private async Task<Dictionary<string,PlayerInfo>> Players(){if(PlayerCache.Count>0&&DateTime.UtcNow-PlayerCacheAt<TimeSpan.FromHours(24))return PlayerCache;var json=await GetJson("https://api.sleeper.app/v1/players/nfl");PlayerCache=json.EnumerateObject().ToDictionary(x=>x.Name,x=>new PlayerInfo($"{Text(x.Value,"first_name")} {Text(x.Value,"last_name")}".Trim(),Text(x.Value,"position"),Text(x.Value,"team"),Text(x.Value,"injury_status")));PlayerCacheAt=DateTime.UtcNow;return PlayerCache;}
    private static SleeperPlayerSnapshot ToPlayer(string id,Dictionary<string,PlayerInfo> map,bool starter,decimal points,JsonElement stats,JsonElement projection,Dictionary<string,decimal> scoring)
    {
        map.TryGetValue(id,out var p);var raw=StatsObject(stats);var projected=StatsObject(projection);var stat=ToStats(raw,scoring);
        stat.InjuryStatus=p?.InjuryStatus??"";
        return new(){PlayerId=id,Name=p?.Name??id,Position=p?.Position??(id.Length<=3?"DEF":""),NflTeam=p?.Team??(id.Length<=3?id:""),Starter=starter,Points=points,Projection=FantasyPoints(projected,scoring),Stats=stat};
    }
    private static WeeklyGameDocument Week(LeagueGameDocument game,int week)=>game.Weeks.SingleOrDefault(x=>x.Week==week)??throw new KeyNotFoundException("This week has not been dealt yet.");
    private static void EnsureOpen(WeeklyGameDocument week){if(week.Status!="selection_open"||DateTime.UtcNow>=week.DeadlineUtc)throw new InvalidOperationException("Card selections are locked.");}
    private static bool Complete(List<CardSelectionDocument> s)=>s.Count==4&&s.Count(x=>Normalize(x.Category)=="BOOST")==1&&s.Count(x=>Normalize(x.Category)=="ATTACK")==1&&s.Count(x=>Normalize(x.Category)=="UNIQUE")==2;
    private static void ValidateTarget(LeagueGameDocument game,int week,int ownRoster,DealtCardDocument card,SaveSelectionRequest request)
    {
        var ownMatch=game.Matchups.FirstOrDefault(x=>x.Week==week&&x.RosterId==ownRoster);
        var opponent=ownMatch is null?0:game.Matchups.FirstOrDefault(x=>x.Week==week&&x.MatchupId==ownMatch.MatchupId&&x.RosterId!=ownRoster)?.RosterId??0;
        var targetText=(card.Target??"").ToLowerInvariant();
        var expectedRoster=targetText.Contains("opponent")?opponent:ownRoster;
        if(!int.TryParse(request.TargetRosterId,out var requestedRoster)||requestedRoster!=expectedRoster)throw new InvalidOperationException("That team is not an eligible target for this card.");
        if(targetText.Contains("team")||targetText.Contains("card"))return;
        var targetTeam=game.Teams.FirstOrDefault(x=>x.RosterId==requestedRoster)??throw new InvalidOperationException("The target roster is unavailable.");
        var player=targetTeam.Players.FirstOrDefault(x=>x.PlayerId==request.TargetPlayerId&&x.Starter)??throw new InvalidOperationException("Choose an eligible starting player.");
        var allowed=targetText.Contains("rb/wr/te")||targetText.Contains("w/r/t")?new[]{"RB","WR","TE"}:new[]{"QB","RB","WR","TE","DEF","FLEX"}.Where(position=>targetText.Contains(position.ToLowerInvariant())).ToArray();
        if(allowed.Length>0&&!allowed.Contains(player.Position)&&!(allowed.Contains("FLEX")&&new[]{"RB","WR","TE"}.Contains(player.Position)))throw new InvalidOperationException($"This card requires a {string.Join('/',allowed)} target.");
    }
    private static void AutoFill(LeagueGameDocument game,WeeklyGameDocument weekState,TeamWeekDocument team,int week)
    {
        foreach(var requirement in new[]{("BOOST",1),("ATTACK",1),("UNIQUE",2)})
        foreach(var card in team.Hand.Where(x=>Normalize(x.Category)==requirement.Item1&&!team.Selections.Any(s=>s.CopyId==x.CopyId)).Take(Math.Max(0,requirement.Item2-team.Selections.Count(s=>Normalize(s.Category)==requirement.Item1))))
        {
            var targetText=(card.Target??"").ToLowerInvariant();var opponent=OpponentRoster(game,week,team.RosterId);var targetRoster=targetText.Contains("opponent")?opponent:team.RosterId;
            var targetTeam=game.Teams.FirstOrDefault(x=>x.RosterId==targetRoster);var allowed=targetText.Contains("rb/wr/te")||targetText.Contains("w/r/t")?new[]{"RB","WR","TE"}:new[]{"QB","RB","WR","TE","DEF","FLEX"}.Where(p=>targetText.Contains(p.ToLowerInvariant())).ToArray();
            var eligible=targetTeam?.Players.Where(x=>x.Starter&&(allowed.Length==0||allowed.Contains(x.Position)||(allowed.Contains("FLEX")&&new[]{"RB","WR","TE"}.Contains(x.Position)))).OrderByDescending(x=>x.Projection).ThenBy(x=>x.PlayerId).FirstOrDefault();
            team.Selections.Add(new(){CopyId=card.CopyId,CardId=card.CardId,Category=requirement.Item1,TargetRosterId=targetRoster.ToString(),TargetPlayerId=targetText.Contains("team")||targetText.Contains("card")?"":eligible?.PlayerId??"",TargetSlot=eligible?.StartingSlot??"AUTO",SelectedAtUtc=DateTime.UtcNow});
        }
    }
    private static List<CardDraftDocument> Deck(CardWorkspaceDocument workspace)=>workspace.Cards.Where(x=>string.Equals(x.Status,"ACTIVE",StringComparison.OrdinalIgnoreCase)&&x.Copies>0&&!string.IsNullOrWhiteSpace(x.ArtworkUrl)).SelectMany(x=>Enumerable.Repeat(x,x.Copies)).ToList();
    private static void EnsureDeck(List<CardDraftDocument> deck)
    {
        if(deck.Count<8)throw new InvalidOperationException("The active deck needs at least eight card copies with artwork.");
        foreach(var required in new[]{("BOOST",2),("ATTACK",2),("UNIQUE",4)})if(deck.Count(x=>Normalize(x.Category)==required.Item1)<required.Item2)throw new InvalidOperationException($"The active deck needs at least {required.Item2} {required.Item1} card(s).");
    }
    private static SeasonHandDocument SeasonHand(LeagueGameDocument game,int roster)
    {
        var hand=game.Hands.FirstOrDefault(x=>x.RosterId==roster);
        if(hand is not null)return hand;
        hand=new(){RosterId=roster};game.Hands.Add(hand);return hand;
    }
    private static void DiscardPreviouslyPlayed(LeagueGameDocument game,SeasonHandDocument hand,int week)
    {
        var used=game.Weeks.Where(x=>x.Week<week).SelectMany(x=>x.Teams.Where(t=>t.RosterId==hand.RosterId)).SelectMany(x=>x.Selections).Select(x=>x.CopyId).ToHashSet();
        hand.Cards.RemoveAll(x=>used.Contains(x.CopyId));
    }
    private static void DrawToEight(SeasonHandDocument hand,List<CardDraftDocument> deck,string leagueId,int week,int roster)
    {
        var random=new Random(HashCode.Combine(leagueId,week,roster));
        var candidates=deck.OrderBy(_=>random.Next()).ToList();
        if(week==1&&!hand.Cards.Any(x=>x.Name.Equals("Challenge Flag",StringComparison.OrdinalIgnoreCase)))
        {
            var challenge=candidates.FirstOrDefault(x=>x.Name.Equals("Challenge Flag",StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException("The active deck needs a Challenge Flag so every team can start with one.");
            AddCard(hand,challenge);candidates.Remove(challenge);
        }
        foreach(var required in new[]{("BOOST",2),("ATTACK",2),("UNIQUE",4)})
        {
            var missing=Math.Max(0,required.Item2-hand.Cards.Count(x=>Normalize(x.Category)==required.Item1));
            foreach(var card in candidates.Where(x=>Normalize(x.Category)==required.Item1).Take(missing).ToList()){AddCard(hand,card);candidates.Remove(card);}
        }
        foreach(var card in candidates)if(hand.Cards.Count<8)AddCard(hand,card);
        if(hand.Cards.Count<8)throw new InvalidOperationException("The deck could not refill this hand to eight cards.");
    }
    private static void AddCard(SeasonHandDocument hand,CardDraftDocument card)=>hand.Cards.Add(new(){CopyId=Guid.NewGuid().ToString("N"),CardId=card.Id,Name=card.Name,Category=Normalize(card.Category),ArtworkUrl=card.ArtworkUrl,Description=card.OfficialDescription,Target=card.Target});
    private static DealtCardDocument CloneCard(DealtCardDocument card)=>new(){CopyId=card.CopyId,CardId=card.CardId,Name=card.Name,Category=card.Category,ArtworkUrl=card.ArtworkUrl,Description=card.Description,Target=card.Target};
    private static void PrepareDraw(LeagueGameDocument game,WeeklyGameDocument week,TeamWeekDocument team,List<CardDraftDocument> deck)
    {
        if(team.DrawnAtUtc is not null)return;
        var hand=SeasonHand(game,team.RosterId);DiscardPreviouslyPlayed(game,hand,week.Week);DrawToEight(hand,deck,game.LeagueId,week.Week,team.RosterId);hand.LastDrawnWeek=week.Week;team.Hand=hand.Cards.Select(CloneCard).ToList();team.DrawnAtUtc=DateTime.UtcNow;
    }
    private static string Normalize(string value)=>value?.ToUpperInvariant()=="DEFENSE"?"UNIQUE":value?.ToUpperInvariant()??"UNIQUE";
    private static void AddCategory(List<CardDraftDocument> chosen,List<CardDraftDocument> deck,string category,int count){foreach(var card in deck.Where(x=>Normalize(x.Category)==category).Take(count))chosen.Add(card);if(chosen.Count(x=>Normalize(x.Category)==category)<count)throw new InvalidOperationException($"The active deck needs at least {count} {category} card(s).");}
    private static bool IsChaosWeek(CardWorkspaceDocument workspace,int week)=>workspace.WeeklyCards.Any(x=>x.Week==week&&x.Active&&x.Name.Equals("Chaos",StringComparison.OrdinalIgnoreCase));
    private static int OpponentRoster(LeagueGameDocument game,int week,int roster)
    {
        var match=game.Matchups.FirstOrDefault(x=>x.Week==week&&x.RosterId==roster);return match is null?0:game.Matchups.FirstOrDefault(x=>x.Week==week&&x.MatchupId==match.MatchupId&&x.RosterId!=roster)?.RosterId??0;
    }
    private static DateTime ChallengeCutoff(WeeklyGameDocument state)=>state.DeadlineUtc.AddDays(1).AddMinutes(15);
    private static bool ResolveExpiredChallenges(LeagueGameDocument game,WeeklyGameDocument state,CardWorkspaceDocument workspace,int week)
    {
        if(state.Status is not ("revealed" or "live")||DateTime.UtcNow<ChallengeCutoff(state))return false;var changed=false;
        foreach(var team in state.Teams)
        foreach(var selection in team.Selections.Where(x=>string.IsNullOrWhiteSpace(x.CancelledCopyId)))
        {
            var card=workspace.Cards.FirstOrDefault(x=>x.Id==selection.CardId);if(card is null||!card.Name.Equals("Challenge Flag",StringComparison.OrdinalIgnoreCase))continue;
            var opponent=state.Teams.FirstOrDefault(x=>x.RosterId==OpponentRoster(game,week,team.RosterId));var legal=opponent?.Selections.Where(x=>!workspace.Cards.Any(c=>c.Id==x.CardId&&c.Name.Equals("Challenge Flag",StringComparison.OrdinalIgnoreCase))).OrderBy(x=>x.CopyId).ToList()??[];
            if(legal.Count>0){selection.CancelledCopyId=legal[Math.Abs(HashCode.Combine(game.LeagueId,week,team.RosterId))%legal.Count].CopyId;changed=true;}
        }
        return changed;
    }
    private static DateTime NextWednesdayAtEightEastern()
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");var now=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,zone);
        var days=((int)DayOfWeek.Wednesday-(int)now.DayOfWeek+7)%7;var local=now.Date.AddDays(days).AddHours(20);
        if(local<=now)local=local.AddDays(7);return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),zone);
    }
    private static DateTime EasternNow()
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,zone);
    }
    private static void LockProjections(LeagueGameDocument game,WeeklyGameDocument state)
    {
        if(state.LockedProjections.Count>0)return;
        state.LockedProjections=game.Teams.SelectMany(x=>x.Players).GroupBy(x=>x.PlayerId).ToDictionary(x=>x.Key,x=>x.Last().Projection);
    }
    private static string Text(JsonElement x,string name)=>x.ValueKind==JsonValueKind.Object&&x.TryGetProperty(name,out var v)&&v.ValueKind!=JsonValueKind.Null?v.GetString()??"":"";
    private static int Number(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.TryGetInt32(out var n)?n:0;
    private static decimal Decimal(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.TryGetDecimal(out var n)?n:0;
    private static int NestedNumber(JsonElement x,string parent,string name)=>x.TryGetProperty(parent,out var p)?Number(p,name):0;
    private static List<string> Strings(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Array?v.EnumerateArray().Select(i=>i.GetString()??"").Where(i=>i.Length>0).ToList():[];
    private static Dictionary<string,decimal> DecimalMap(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Object?v.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.TryGetDecimal(out var n)?n:0):[];
    private async Task<Dictionary<string,JsonElement>> GetPlayerData(string url)
    {
        try
        {
            var data=await GetJson(url,true);if(data.ValueKind!=JsonValueKind.Array)return [];
            return data.EnumerateArray().Where(x=>Text(x,"player_id").Length>0).ToDictionary(x=>Text(x,"player_id"),x=>x.Clone());
        }
        catch(HttpRequestException){return [];}
    }
    private static JsonElement StatsObject(JsonElement item)=>item.ValueKind==JsonValueKind.Object&&item.TryGetProperty("stats",out var stats)&&stats.ValueKind==JsonValueKind.Object?stats:item;
    private static decimal Stat(JsonElement x,string name)=>x.ValueKind==JsonValueKind.Object&&x.TryGetProperty(name,out var v)&&v.TryGetDecimal(out var n)?n:0m;
    private static decimal Weight(Dictionary<string,decimal> scoring,string key)=>scoring.GetValueOrDefault(key);
    private static decimal FantasyPoints(JsonElement s,Dictionary<string,decimal> w)
    {
        if(s.ValueKind!=JsonValueKind.Object)return 0m;
        return Stat(s,"pass_yd")*Weight(w,"pass_yd")+Stat(s,"pass_td")*Weight(w,"pass_td")+Stat(s,"pass_int")*Weight(w,"pass_int")+
            Stat(s,"pass_cmp")*Weight(w,"pass_cmp")+Stat(s,"pass_inc")*Weight(w,"pass_inc")+Stat(s,"rush_yd")*Weight(w,"rush_yd")+
            Stat(s,"rush_td")*Weight(w,"rush_td")+Stat(s,"rec")*Weight(w,"rec")+Stat(s,"rec_yd")*Weight(w,"rec_yd")+
            Stat(s,"rec_td")*Weight(w,"rec_td")+Stat(s,"fum_lost")*Weight(w,"fum_lost")+Stat(s,"sack")*Weight(w,"sack")+
            Stat(s,"int")*Weight(w,"int")+Stat(s,"fum_rec")*Weight(w,"fum_rec")+Stat(s,"xpm")*Weight(w,"xpm")+
            Stat(s,"fgm")*Weight(w,"fgm");
    }
    private static PlayerStatDocument ToStats(JsonElement s,Dictionary<string,decimal> w)
    {
        var passTd=Stat(s,"pass_td");var rushTd=Stat(s,"rush_td");var recTd=Stat(s,"rec_td");
        return new(){Receptions=(int)Stat(s,"rec"),Targets=(int)Stat(s,"rec_tgt"),Completions=(int)Stat(s,"pass_cmp"),PassingAttempts=(int)Stat(s,"pass_att"),
            SacksTaken=(int)Stat(s,"pass_sack"),Fumbles=(int)Math.Max(Stat(s,"fum"),Stat(s,"fum_lost")),PassingInterceptions=(int)Stat(s,"pass_int"),
            DefensiveSacks=(int)Stat(s,"sack"),DefensiveInterceptions=(int)Stat(s,"int"),DefensiveFumbleRecoveries=(int)Stat(s,"fum_rec"),
            PassingYards=Stat(s,"pass_yd"),RushingYards=Stat(s,"rush_yd"),ReceivingYards=Stat(s,"rec_yd"),PassingTouchdowns=passTd,RushingTouchdowns=rushTd,ReceivingTouchdowns=recTd,FieldGoalYards=Stat(s,"fgm_yds"),
            FieldGoalPoints=Stat(s,"fgm_0_19")*Weight(w,"fgm_0_19")+Stat(s,"fgm_20_29")*Weight(w,"fgm_20_29")+Stat(s,"fgm_30_39")*Weight(w,"fgm_30_39")+Stat(s,"fgm_40_49")*Weight(w,"fgm_40_49")+Stat(s,"fgm_50p")*Weight(w,"fgm_50p"),
            PassingYardPoints=Stat(s,"pass_yd")*Weight(w,"pass_yd"),RushingYardPoints=Stat(s,"rush_yd")*Weight(w,"rush_yd"),ReceivingYardPoints=Stat(s,"rec_yd")*Weight(w,"rec_yd"),
            ReceptionPoints=Stat(s,"rec")*Weight(w,"rec"),CompletionPoints=Stat(s,"pass_cmp")*Weight(w,"pass_cmp"),PassingTouchdownPoints=passTd*Weight(w,"pass_td"),
            TouchdownPoints=passTd*Weight(w,"pass_td")+rushTd*Weight(w,"rush_td")+recTd*Weight(w,"rec_td"),DefensiveSackPoints=Stat(s,"sack")*Weight(w,"sack"),DefensiveInterceptionPoints=Stat(s,"int")*Weight(w,"int"),
            BonusPoints=w.Where(x=>x.Key.Contains("bonus",StringComparison.OrdinalIgnoreCase)).Sum(x=>Stat(s,x.Key)*x.Value)};
    }
    private static PlayerWeekStats ToWeekStats(PlayerStatDocument s,string lockedInjuryStatus)=>new(s.Receptions,s.Targets,s.Completions,s.PassingAttempts,s.SacksTaken,s.Fumbles,s.PassingInterceptions,s.DefensiveSacks,s.DefensiveInterceptions,s.DefensiveFumbleRecoveries,s.TouchdownPoints,s.RushingYards,s.PassingYards,s.ReceivingYards,s.RushingYardPoints,s.PassingYardPoints,s.ReceivingYardPoints,s.ReceptionPoints,s.CompletionPoints,s.PassingTouchdownPoints,s.DefensiveSackPoints,s.DefensiveInterceptionPoints,s.PassingTouchdowns,s.RushingTouchdowns,s.ReceivingTouchdowns,s.FieldGoalYards,s.FieldGoalPoints,s.BonusPoints,false,string.IsNullOrWhiteSpace(lockedInjuryStatus)?s.InjuryStatus??"":lockedInjuryStatus);
    private sealed record PlayerInfo(string Name,string Position,string Team,string InjuryStatus);
}
