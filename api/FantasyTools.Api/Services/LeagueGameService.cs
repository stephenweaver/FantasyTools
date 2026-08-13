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
        var users = await GetJson($"https://api.sleeper.app/v1/league/{league.SleeperLeagueId}/users");
        var rosters = await GetJson($"https://api.sleeper.app/v1/league/{league.SleeperLeagueId}/rosters");
        var matchups = await GetJson($"https://api.sleeper.app/v1/league/{league.SleeperLeagueId}/matchups/{Math.Clamp(week,1,18)}", allowMissing:true);
        var playerMap = await Players();
        var userMap = users.EnumerateArray().ToDictionary(x=>Text(x,"user_id"),x=>x);
        var snapshot = await LoadOrCreate(league);
        snapshot.Teams = rosters.EnumerateArray().Select(roster =>
        {
            var owner=Text(roster,"owner_id"); userMap.TryGetValue(owner,out var user);
            var starters=Strings(roster,"starters").ToHashSet();
            return new SleeperTeamSnapshot {
                RosterId=Number(roster,"roster_id"), OwnerId=owner,
                ManagerName=user.ValueKind==JsonValueKind.Object ? (Text(user,"display_name") is { Length:>0 } d ? d : Text(user,"username")) : $"Roster {Number(roster,"roster_id")}",
                TeamName=user.ValueKind==JsonValueKind.Object && user.TryGetProperty("metadata",out var meta) && Text(meta,"team_name") is { Length:>0 } n ? n : (user.ValueKind==JsonValueKind.Object ? Text(user,"display_name") : $"Roster {Number(roster,"roster_id")}"),
                Wins=NestedNumber(roster,"settings","wins"), Losses=NestedNumber(roster,"settings","losses"),
                Players=Strings(roster,"players").Select(id=>ToPlayer(id,playerMap,starters.Contains(id),0)).ToList()
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
            foreach(var id in matchup.Starters) if(all.TryGetValue(id,out var player)) player.Starter=true;
            foreach(var pair in matchup.PlayerPoints) if(all.TryGetValue(pair.Key,out var player)) player.Points=pair.Value;
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
            var deck=Deck(await Workspace(leagueId)); EnsureDeck(deck);
            foreach(var teamState in state.Teams){PrepareDraw(game,state,teamState,deck);AutoFill(teamState);}
            state.Status="revealed";state.RevealedAtUtc=DateTime.UtcNow;game.At=DateTime.UtcNow;await files.Upsert(game);
        }
        var own=state.Teams.FirstOrDefault(x=>x.RosterId==roster);
        var seasonHand=SeasonHand(game,roster);
        var workspace=await Workspace(leagueId);
        var chaosScores=CalculateScores(game,state,workspace,week);
        object publicSelections=state.Status is "revealed" or "live" or "finalized" ? state.Teams.Select(x=>new {x.RosterId,x.Selections}).ToList() : Array.Empty<object>();
        return new { state.Week,state.Status,state.DeadlineUtc,state.RevealedAtUtc,sleeperStatus=game.SleeperStatus,team=game.Teams.FirstOrDefault(x=>x.RosterId==roster),hand=own?.DrawnAtUtc is null?seasonHand.Cards:own.Hand,selections=own?.Selections??[],canDraw=state.Status=="selection_open"&&own?.DrawnAtUtc is null,cardsNeeded=Math.Max(0,8-seasonHand.Cards.Count),publicSelections,teams=game.Teams,matchups=game.Matchups.Where(x=>x.Week==week),chaosScores };
    }

    public async Task<object> Deal(string leagueId,string actorUserId,int week) => await Mutate(leagueId,async game =>
    {
        await Commissioner(leagueId,actorUserId);
        if(game.Weeks.Any(x=>x.Week==week))return (object)new{opened=true,week,alreadyOpen=true};
        var workspace=await Workspace(leagueId); EnsureDeck(Deck(workspace));
        foreach(var hand in game.Hands)DiscardPreviouslyPlayed(game,hand,week);
        var state=new WeeklyGameDocument{Week=week,DeadlineUtc=NextThursday(),Status="selection_open",Teams=game.Teams.Select(team=>new TeamWeekDocument{RosterId=team.RosterId}).ToList()};
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
        var category=Normalize(card.Category); var limit=category=="UNIQUE"?2:1;
        if(team.Selections.Count>=4||team.Selections.Count(x=>Normalize(x.Category)==category)>=limit) throw new InvalidOperationException($"Your {category} selection slots are full.");
        ValidateTarget(game,week,roster,card,request);
        team.Selections.Add(new CardSelectionDocument{CopyId=card.CopyId,CardId=card.CardId,Category=category,TargetRosterId=request.TargetRosterId,TargetPlayerId=request.TargetPlayerId,TargetSlot=request.TargetSlot,SelectedAtUtc=DateTime.UtcNow});
        return (object)new {selected=true,selections=team.Selections};
    });

    public async Task<object> Return(string leagueId,string userId,int week,string copyId)=>await Mutate(leagueId,async game=>{var roster=await UserRoster(leagueId,userId);var state=Week(game,week);EnsureOpen(state);state.Teams.Single(x=>x.RosterId==roster).Selections.RemoveAll(x=>x.CopyId==copyId);return (object)new{returned=true};});
    public async Task<object> SetDeadline(string leagueId,string actorUserId,int week,DateTime deadlineUtc)=>await Mutate(leagueId,async game=>{await Commissioner(leagueId,actorUserId);var state=Week(game,week);state.DeadlineUtc=deadlineUtc.ToUniversalTime();return (object)new{state.Week,state.DeadlineUtc};});
    public async Task<object> Reveal(string leagueId,string actorUserId,int week)=>await Mutate(leagueId,async game=>{await Commissioner(leagueId,actorUserId);var state=Week(game,week);if(state.Teams.Any(x=>!Complete(x.Selections)))throw new InvalidOperationException("Every team must have 1 Boost, 1 Attack, and 2 Unique cards selected.");state.Status="revealed";state.RevealedAtUtc=DateTime.UtcNow;return (object)new{revealed=true,state.RevealedAtUtc};});

    private object[] CalculateScores(LeagueGameDocument game,WeeklyGameDocument state,CardWorkspaceDocument workspace,int week)
    {
        var playerScores=game.Matchups.Where(x=>x.Week==week).SelectMany(x=>x.PlayerPoints).GroupBy(x=>x.Key).ToDictionary(x=>x.Key,x=>x.Last().Value);
        var weekly=workspace.WeeklyCards.FirstOrDefault(x=>x.Week==week&&x.Active);
        return game.Teams.Select(team=>
        {
            var effects=new List<ActiveEffect>();
            foreach(var teamWeek in state.Teams)
            foreach(var selection in teamWeek.Selections)
            {
                var card=workspace.Cards.FirstOrDefault(x=>x.Id==selection.CardId);if(card is null)continue;
                var targetRoster=int.TryParse(selection.TargetRosterId,out var parsed)?parsed:teamWeek.RosterId;
                if(targetRoster==team.RosterId)effects.Add(ToEffect(card,selection,targetRoster));
            }
            if(weekly is not null)effects.Add(ToWeeklyEffect(weekly,team.RosterId));
            var counters=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            var starters=team.Players.Where(x=>x.Starter).Select(player=>
            {
                counters[player.Position]=counters.GetValueOrDefault(player.Position)+1;
                var slot=counters[player.Position]==1?player.Position:$"{player.Position}{counters[player.Position]}";
                return new SlotScore(slot,player.PlayerId,player.Name,player.Position,player.Points);
            }).ToList();
            var result=scoring.Calculate(new TeamScoreInput(TeamGuid(team.RosterId),starters,playerScores,effects));
            return (object)new {rosterId=team.RosterId,result.SleeperScore,result.ChaosScore,result.Lines};
        }).ToArray();
    }

    private static ActiveEffect ToEffect(CardDraftDocument card,CardSelectionDocument selection,int rosterId)
    {
        var category=Enum.TryParse<CardCategory>(Normalize(card.Category),true,out var parsed)?parsed:CardCategory.Unique;
        var type=ParseEffect(card.EffectType);
        var amount=category==CardCategory.Attack&&type is EffectType.Percentage or EffectType.FlatPoints?-Math.Abs(card.Amount):card.Amount;
        var target=new CardTarget(TargetType.Dynamic,TeamGuid(rosterId),selection.TargetSlot,null,selection.TargetPlayerId,null);
        return new ActiveEffect(Guid.TryParse(selection.CopyId,out var id)?id:Guid.NewGuid(),card.Name,category,type,target,amount,card.SourcePlayerId,card.DestinationSlot,card.Multiplier,card.Name);
    }

    private static ActiveEffect ToWeeklyEffect(WeeklyCardDocument card,int rosterId)
    {
        var target=new CardTarget(TargetType.Team,TeamGuid(rosterId),null,null,null,null);
        return new ActiveEffect(Guid.TryParse(card.Id,out var id)?id:Guid.NewGuid(),card.Name,CardCategory.Unique,ParseEffect(card.RuleType),target,card.Amount,CustomHandler:card.Name);
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

    private async Task<object> Mutate(string leagueId,Func<LeagueGameDocument,Task<object>> action){var gate=Locks.GetOrAdd(leagueId,_=>new(1,1));await gate.WaitAsync();try{var game=await Load(leagueId);var result=await action(game);game.At=DateTime.UtcNow;await files.Upsert(game);return result;}finally{gate.Release();}}
    private async Task<ChaosLeagueDocument> League(string id)=>await files.Retrieve(new ChaosLeagueDocument{LeagueId=id})??throw new KeyNotFoundException("League not found.");
    private async Task<CardWorkspaceDocument> Workspace(string id)=>await files.Retrieve(new CardWorkspaceDocument{LeagueId=id})??throw new KeyNotFoundException("Card workspace not found.");
    private async Task<LeagueGameDocument> Load(string id)=>await files.Retrieve(new LeagueGameDocument{LeagueId=id})??await LoadOrCreate(await League(id));
    private async Task<LeagueGameDocument> LoadOrCreate(ChaosLeagueDocument league)=>await files.Retrieve(new LeagueGameDocument{LeagueId=league.LeagueId})??new(){LeagueId=league.LeagueId,SleeperLeagueId=league.SleeperLeagueId,At=DateTime.UtcNow};
    private async Task EnsureMember(string leagueId,string userId){var index=await files.Retrieve(new UserChaosLeagueDocument{UserId=userId});if(index?.LeagueId!=leagueId)throw new UnauthorizedAccessException("You are not a member of this league.");}
    private async Task Commissioner(string leagueId,string userId){var league=await League(leagueId);if(league.PrimaryCommissionerUserId!=userId)throw new UnauthorizedAccessException("Only the primary commissioner can do that.");}
    private async Task<int> UserRoster(string leagueId,string userId){var doc=await files.Retrieve(new LeagueRosterDocument{LeagueId=leagueId})??throw new KeyNotFoundException("Roster setup not found.");return doc.Assignments.FirstOrDefault(x=>x.FantasyToolsUserId==userId)?.RosterId??throw new InvalidOperationException("Your account is not connected to a Sleeper roster.");}
    private async Task<JsonElement> GetJson(string url,bool allowMissing=false){var response=await sleeper.GetAsync(url);if(allowMissing&&!response.IsSuccessStatusCode)return JsonDocument.Parse("[]").RootElement.Clone();response.EnsureSuccessStatusCode();return (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement.Clone();}
    private async Task<Dictionary<string,PlayerInfo>> Players(){if(PlayerCache.Count>0&&DateTime.UtcNow-PlayerCacheAt<TimeSpan.FromHours(24))return PlayerCache;var json=await GetJson("https://api.sleeper.app/v1/players/nfl");PlayerCache=json.EnumerateObject().ToDictionary(x=>x.Name,x=>new PlayerInfo($"{Text(x.Value,"first_name")} {Text(x.Value,"last_name")}".Trim(),Text(x.Value,"position"),Text(x.Value,"team")));PlayerCacheAt=DateTime.UtcNow;return PlayerCache;}
    private static SleeperPlayerSnapshot ToPlayer(string id,Dictionary<string,PlayerInfo> map,bool starter,decimal points){map.TryGetValue(id,out var p);return new(){PlayerId=id,Name=p?.Name??id,Position=p?.Position??(id.Length<=3?"DEF":""),NflTeam=p?.Team??(id.Length<=3?id:""),Starter=starter,Points=points};}
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
        var required=new[]{"QB","RB","WR","TE","DEF","FLEX"}.FirstOrDefault(position=>targetText.Contains(position.ToLowerInvariant()));
        if(required is not null&&player.Position!=required&&!(required=="FLEX"&&new[]{"RB","WR","TE"}.Contains(player.Position)))throw new InvalidOperationException($"This card requires a {required} target.");
    }
    private static void AutoFill(TeamWeekDocument team){foreach(var requirement in new[]{("BOOST",1),("ATTACK",1),("UNIQUE",2)})foreach(var card in team.Hand.Where(x=>Normalize(x.Category)==requirement.Item1&&!team.Selections.Any(s=>s.CopyId==x.CopyId)).Take(requirement.Item2-team.Selections.Count(s=>Normalize(s.Category)==requirement.Item1)))team.Selections.Add(new(){CopyId=card.CopyId,CardId=card.CardId,Category=requirement.Item1,TargetSlot="AUTO",SelectedAtUtc=DateTime.UtcNow});}
    private static List<CardDraftDocument> Deck(CardWorkspaceDocument workspace)=>workspace.Cards.Where(x=>string.Equals(x.Status,"ACTIVE",StringComparison.OrdinalIgnoreCase)&&x.Copies>0&&!string.IsNullOrWhiteSpace(x.ArtworkUrl)).SelectMany(x=>Enumerable.Repeat(x,x.Copies)).ToList();
    private static void EnsureDeck(List<CardDraftDocument> deck)
    {
        if(deck.Count<8)throw new InvalidOperationException("The active deck needs at least eight card copies with artwork.");
        foreach(var required in new[]{("BOOST",1),("ATTACK",1),("UNIQUE",2)})if(deck.Count(x=>Normalize(x.Category)==required.Item1)<required.Item2)throw new InvalidOperationException($"The active deck needs at least {required.Item2} {required.Item1} card(s).");
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
        foreach(var required in new[]{("BOOST",1),("ATTACK",1),("UNIQUE",2)})
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
    private static DateTime NextThursday(){var now=DateTime.UtcNow;var days=((int)DayOfWeek.Thursday-(int)now.DayOfWeek+7)%7;return now.Date.AddDays(days==0?7:days).AddHours(17);}
    private static string Text(JsonElement x,string name)=>x.ValueKind==JsonValueKind.Object&&x.TryGetProperty(name,out var v)&&v.ValueKind!=JsonValueKind.Null?v.GetString()??"":"";
    private static int Number(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.TryGetInt32(out var n)?n:0;
    private static decimal Decimal(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.TryGetDecimal(out var n)?n:0;
    private static int NestedNumber(JsonElement x,string parent,string name)=>x.TryGetProperty(parent,out var p)?Number(p,name):0;
    private static List<string> Strings(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Array?v.EnumerateArray().Select(i=>i.GetString()??"").Where(i=>i.Length>0).ToList():[];
    private static Dictionary<string,decimal> DecimalMap(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Object?v.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.TryGetDecimal(out var n)?n:0):[];
    private sealed record PlayerInfo(string Name,string Position,string Team);
}
