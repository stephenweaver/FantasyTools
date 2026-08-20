using FantasyTools.Api.Game.Domain;
using FantasyTools.Api.Game.Engine;
using FantasyTools.Api.Game.Rules;

var teamId = Guid.Parse("00000000-0000-0000-0000-000000000001");
var opponentId = Guid.Parse("00000000-0000-0000-0000-000000000002");
var weekId = Guid.Parse("00000000-0000-0000-0000-000000000003");
var matchupId = Guid.Parse("00000000-0000-0000-0000-000000000004");
var cardCopyId = Guid.Parse("00000000-0000-0000-0000-000000000005");
var engine = new ChaosScoringEngine();

ActiveEffect Percentage(string name, decimal amount, CardCategory category, CardTarget target) =>
    new(Guid.NewGuid(), name, category, EffectType.Percentage, target, amount);

var qbTarget = new CardTarget(TargetType.StartingSlot, teamId, "QB", null, null, null);
var starters = new[]
{
    new SlotScore("QB", "qb-1", "Starting QB", "QB", 30m),
    new SlotScore("RB1", "rb-1", "Starting RB", "RB", 20m),
    new SlotScore("WR1", "wr-1", "Starting WR", "WR", 10m)
};

// +50% and -50% on the same slot cancel additively.
var cancelled = engine.Calculate(new(teamId, starters, new Dictionary<string, decimal>(), new[]
{
    Percentage("QB boost", 50m, CardCategory.Boost, qbTarget),
    Percentage("QB attack", -50m, CardCategory.Attack, qbTarget)
}));
AssertEqual(60m, cancelled.ChaosScore, "additive cancellation");

// A 50% defense turns a -50% attack into -25% of the 30-point QB slot: team becomes 52.5.
var defended = engine.Calculate(new(teamId, starters, new Dictionary<string, decimal>(), new ActiveEffect[]
{
    Percentage("QB attack", -50m, CardCategory.Attack, qbTarget),
    new(Guid.NewGuid(), "Lockdown", CardCategory.Defense, EffectType.ReduceAttack, qbTarget, 50m)
}));
AssertEqual(52.5m, defended.ChaosScore, "defensive reduction");

// Bromance: team 60 - actual QB 30 + Mahomes 24 * 2 = 78.
var bromance = engine.Calculate(new(teamId, starters, new Dictionary<string, decimal> { ["4046"] = 24m }, new[]
{
    new ActiveEffect(Guid.NewGuid(), "Bromance", CardCategory.Boost, EffectType.ReferencedPlayerReplacesSlot,
        qbTarget, 0m, "4046", "QB", 2m)
}));
AssertEqual(78m, bromance.ChaosScore, "referenced-player replacement");

// Double or Nothing targets one starter: five catches doubles the full score,
// while four catches zeroes it out.
var receiverTarget = new CardTarget(TargetType.SpecificPlayer, teamId, null, null, "wr-1", null);
var doubleWin = engine.Calculate(new TeamScoreInput(teamId, starters, new Dictionary<string, decimal>(), new[]
{
    new ActiveEffect(Guid.NewGuid(), "Double or Nothing", CardCategory.Boost, EffectType.Custom,
        receiverTarget, 100m, CustomHandler: "double-or-nothing")
}) { PlayerStats = new Dictionary<string, PlayerWeekStats> { ["wr-1"] = new(Receptions: 5) } });
AssertEqual(70m, doubleWin.ChaosScore, "double or nothing win");

var doubleLoss = engine.Calculate(new TeamScoreInput(teamId, starters, new Dictionary<string, decimal>(), new[]
{
    new ActiveEffect(Guid.NewGuid(), "Double or Nothing", CardCategory.Boost, EffectType.Custom,
        receiverTarget, 100m, CustomHandler: "double-or-nothing")
}) { PlayerStats = new Dictionary<string, PlayerWeekStats> { ["wr-1"] = new(Receptions: 4) } });
AssertEqual(50m, doubleLoss.ChaosScore, "double or nothing loss");

var challengedId = Guid.NewGuid();
var challenge = engine.Calculate(new(teamId, starters, new Dictionary<string, decimal>(), new ActiveEffect[]
{
    new(challengedId, "Two Deep", CardCategory.Attack, EffectType.Percentage, qbTarget, -25m),
    new(Guid.NewGuid(), "Challenge Flag", CardCategory.Defense, EffectType.Custom,
        new(TargetType.Dynamic, teamId, null, null, null, $"cancel:{challengedId}"), 0m, CustomHandler: "challenge-flag")
}));
AssertEqual(60m, challenge.ChaosScore, "challenge flag cancels selected card");

var complete = engine.Calculate(new TeamScoreInput(teamId, starters, new Dictionary<string, decimal>(), new[]
{
    new ActiveEffect(Guid.NewGuid(), "Complete", CardCategory.Boost, EffectType.Custom, qbTarget, 2m, CustomHandler: "complete")
}) { PlayerStats = new Dictionary<string, PlayerWeekStats> { ["qb-1"] = new(Completions: 20) } });
AssertEqual(100m, complete.ChaosScore, "complete reception stat handler");

var capHit = engine.Calculate(new(teamId, starters, new Dictionary<string, decimal>(), new[]
{
    new ActiveEffect(Guid.NewGuid(), "Cap Hit", CardCategory.Attack, EffectType.Custom,
        new(TargetType.Team, teamId, null, null, null, null), 15m, CustomHandler: "cap-hit")
}));
AssertEqual(40m, capHit.ChaosScore, "cap hit limits every starter");

var updatedRules = engine.Calculate(new TeamScoreInput(teamId, starters, new Dictionary<string, decimal>(), new ActiveEffect[]
{
    new(Guid.NewGuid(), "Rough Start", CardCategory.Unique, EffectType.Custom, receiverTarget, -20m, CustomHandler: "rough-start"),
    new(Guid.NewGuid(), "Butt Fumble", CardCategory.Unique, EffectType.Custom, new(TargetType.Team,teamId,null,null,null,null), 10m, CustomHandler: "butt-fumble")
}) { PlayerStats = new Dictionary<string, PlayerWeekStats> { ["qb-1"] = new(Fumbles: 1), ["wr-1"] = new() } });
AssertEqual(50m, updatedRules.ChaosScore, "sheet values use minus twenty and ten per fumble");

var immaculate = engine.Calculate(new TeamScoreInput(teamId, starters, new Dictionary<string, decimal>(), new[]
{
    new ActiveEffect(Guid.NewGuid(), "Immaculate Reception", CardCategory.Unique, EffectType.Custom, receiverTarget, 15m, CustomHandler: "immaculate-reception")
}) { PlayerStats = new Dictionary<string, PlayerWeekStats> { ["wr-1"] = new(Receptions: 4, Targets: 4) } });
AssertEqual(75m, immaculate.ChaosScore, "immaculate reception perfect target bonus");

var richStats=new Dictionary<string,PlayerWeekStats>
{
    ["qb-1"]=new(Completions:20,PassingAttempts:30,PassingInterceptions:1,Fumbles:1,PassingYards:250,RushingYards:20,PassingYardPoints:10,RushingYardPoints:2,PassingTouchdowns:2,TouchdownPoints:8),
    ["rb-1"]=new(Receptions:4,Targets:5,RushingYards:80,ReceivingYards:30,RushingYardPoints:8,ReceivingYardPoints:3,ReceptionPoints:2,RushingTouchdowns:1,TouchdownPoints:6),
    ["wr-1"]=new(Receptions:5,Targets:5,ReceivingYards:100,ReceivingYardPoints:10,ReceptionPoints:2.5m,ReceivingTouchdowns:1,TouchdownPoints:6)
};
TeamScoreInput WeeklyInput(string name)=>new(teamId,starters,new Dictionary<string,decimal>(),new[]{new ActiveEffect(Guid.NewGuid(),name,CardCategory.Unique,EffectType.Custom,new(TargetType.Team,teamId,null,null,null,null),0m,CustomHandler:$"weekly-{name}")})
{
    PlayerStats=richStats,Bench=[new SlotScore("BENCH-WR1","bench-1","Bench WR","WR",7m)],OpponentStarters=starters,OpponentBench=[],Projections=new Dictionary<string,decimal>{{"qb-1",20},{"rb-1",15},{"wr-1",12}}
};
foreach(var weeklyName in new[]{"Quantity > Quality","Quality > Quantity","Half Point","Mini Battle","Deck Swap","TE Frenzy","Das Boot","WR Frenzy","Cap Hit","RB Frenzy","QB Frenzy","DEF Frenzy","PPR Frenzy","Chaos","Double TD","Deep End","PPY"})
{
    var weeklyResult=engine.Calculate(WeeklyInput(weeklyName));
    AssertTrue(!weeklyResult.Lines.Any(x=>x.Kind=="custom-pending"),$"weekly handler {weeklyName}");
}
AssertEqual(190m,engine.Calculate(WeeklyInput("PPR Frenzy")).ChaosScore,"PPR Frenzy adds receiving yards once, not receptions times yards");
AssertEqual(110m,engine.Calculate(WeeklyInput("WR Frenzy")).ChaosScore,"WR Frenzy applies special scoring to normal starting WRs");
AssertEqual(105m,engine.Calculate(WeeklyInput("RB Frenzy")).ChaosScore,"RB Frenzy applies special scoring to normal starting RBs");
var qualityOnlyInput=new TeamScoreInput(teamId,[new("QB","qb-1","QB","QB",30m),new("K","k-1","K","K",8m),new("DEF","def-1","DEF","DEF",9m)],new Dictionary<string,decimal>(),new[]{new ActiveEffect(Guid.NewGuid(),"Quantity > Quality",CardCategory.Unique,EffectType.Custom,new(TargetType.Team,teamId,null,null,null,null),0m,CustomHandler:"weekly-Quantity > Quality")}){PlayerStats=new Dictionary<string,PlayerWeekStats>{{"qb-1",richStats["qb-1"]}}};
AssertEqual(29m,engine.Calculate(qualityOnlyInput).ChaosScore,"only-yardage week leaves kicker and defense scoring unchanged");
var offsides=engine.Calculate(new TeamScoreInput(teamId,starters,new Dictionary<string,decimal>(),new[]{new ActiveEffect(Guid.NewGuid(),"Offsides",CardCategory.Unique,EffectType.Custom,new(TargetType.Team,teamId,null,null,null,null),20m,CustomHandler:"Offsides")}));
AssertEqual(80m,offsides.ChaosScore,"Offsides starts the owner at twenty points");
var touchdownSaboteur=engine.Calculate(new TeamScoreInput(teamId,starters,new Dictionary<string,decimal>(),new[]{new ActiveEffect(Guid.NewGuid(),"TD Saboteur",CardCategory.Unique,EffectType.Custom,receiverTarget,0m,CustomHandler:"TD Saboteur")}){PlayerStats=richStats});
AssertEqual(54m,touchdownSaboteur.ChaosScore,"TD Saboteur removes chosen starter touchdown points");
var mvp=engine.Calculate(new TeamScoreInput(teamId,starters,new Dictionary<string,decimal>(),new[]{new ActiveEffect(Guid.NewGuid(),"MVP",CardCategory.Unique,EffectType.Custom,receiverTarget,0m,CustomHandler:"MVP")}){LeagueHighestStarterScoreByPosition=new Dictionary<string,decimal>{{"WR",40m}}});
AssertEqual(90m,mvp.ChaosScore,"MVP uses the league-high same-position starter score");
var capAfterBoost=engine.Calculate(new(teamId,starters,new Dictionary<string,decimal>(),new ActiveEffect[]{Percentage("QB boost",100m,CardCategory.Boost,qbTarget),new(Guid.NewGuid(),"Cap Hit",CardCategory.Unique,EffectType.Custom,new(TargetType.Team,teamId,null,null,null,null),15m,CustomHandler:"cap-hit")}));
AssertEqual(40m,capAfterBoost.ChaosScore,"cap applies after percentage cards");

var halfPointThenBoost=WeeklyInput("Half Point") with
{
    Effects=[..WeeklyInput("Half Point").Effects,Percentage("WR boost",100m,CardCategory.Boost,receiverTarget)]
};
AssertEqual(70m,engine.Calculate(halfPointThenBoost).ChaosScore,"weekly reception scoring precedes personal boost");

var cancelledAttackId=Guid.NewGuid();
var weeklySurvivesChallenge=WeeklyInput("Quantity > Quality") with
{
    Effects=[..WeeklyInput("Quantity > Quality").Effects,
        new(cancelledAttackId,"Two Deep",CardCategory.Attack,EffectType.Percentage,qbTarget,-25m),
        new(Guid.NewGuid(),"Challenge Flag",CardCategory.Defense,EffectType.Custom,new(TargetType.Dynamic,teamId,null,null,null,$"cancel:{cancelledAttackId}"),0m,CustomHandler:"challenge-flag")]
};
AssertEqual(engine.Calculate(WeeklyInput("Quantity > Quality")).ChaosScore,engine.Calculate(weeklySurvivesChallenge).ChaosScore,"challenge flag never cancels league weekly rule");

var rules = new CardPlayRules();
var request = new PlayRequest(weekId, matchupId, teamId, opponentId, cardCopyId, CardCategory.Boost, CardTiming.PreWeek, qbTarget, DateTimeOffset.UtcNow);
var validState = new WeekPlayState(WeekStatus.SelectionOpen, DateTimeOffset.UtcNow.AddHours(1), 0, [], true, CardCopyState.Hand, true, true);
AssertTrue(rules.Validate(request, validState).Allowed, "valid pre-week play");
AssertEqual("weekly_limit", rules.Validate(request, validState with { ExistingPreWeekSelections = 4 }).Code, "four-card weekly limit");
AssertEqual("category_limit", rules.Validate(request, validState with { SelectedCategories = [CardCategory.Boost] }).Code, "one boost limit");
AssertTrue(CardPlayRules.ValidateCompleteSelection([CardCategory.Boost, CardCategory.Attack, CardCategory.Unique, CardCategory.Defense]).Allowed, "weekly category mix");
AssertEqual("card_not_in_hand", rules.Validate(request, validState with { CardState = CardCopyState.Played }).Code, "card ownership/state enforcement");

var lifecycle = new CardLifecycleRules();
AssertTrue(lifecycle.ValidateTransition(CardCopyState.SecretSelection, CardCopyState.Hand).Allowed, "unlocked selection returns to hand");
AssertEqual("invalid_card_transition", lifecycle.ValidateTransition(CardCopyState.Locked, CardCopyState.Hand).Code, "locked card cannot return to hand");
var draws = lifecycle.CalculateReplacementDraws(6, [CardCategory.Attack, CardCategory.Unique]);
AssertEqual(1, draws.Single(draw => draw.Category == CardCategory.Attack).Quantity, "attack replacement draw");
AssertEqual(1, draws.Single(draw => draw.Category == CardCategory.Unique).Quantity, "unique replacement draw");

var authorization = new CommissionerAuthorization();
var primary = new LeagueAccess(teamId, "primary", true, new HashSet<CommissionerPermission>());
var cardManager = new LeagueAccess(teamId, "card-manager", false, new HashSet<CommissionerPermission> { CommissionerPermission.CreateCardDrafts });
var delegatedManager = new LeagueAccess(teamId, "delegated-manager", false, new HashSet<CommissionerPermission> { CommissionerPermission.ManageCoCommissioners });
var ordinaryPlayer = new LeagueAccess(teamId, "player", false, new HashSet<CommissionerPermission>());
AssertTrue(authorization.Authorize(cardManager, CommissionerPermission.CreateCardDrafts).Allowed, "card manager can add card drafts");
AssertEqual("permission_denied", authorization.Authorize(cardManager, CommissionerPermission.CorrectScores).Code, "card manager cannot correct scores");
AssertTrue(authorization.Authorize(primary, CommissionerPermission.CorrectScores).Allowed, "primary commissioner has all permissions");
AssertEqual("primary_protected", authorization.AuthorizePermissionChange(primary, primary,
    new(teamId, "primary", "primary", CommissionerPermission.CreateCardDrafts, false)).Code, "primary commissioner is protected");
AssertEqual("cannot_delegate_admin", authorization.AuthorizePermissionChange(delegatedManager, ordinaryPlayer,
    new(teamId, "delegated-manager", "player", CommissionerPermission.ManageCoCommissioners, true)).Code, "co-commissioner cannot delegate permission management");

Console.WriteLine("All Chaos engine checks passed.");

static void AssertEqual<T>(T expected, T actual, string name) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{name}: expected {expected}, got {actual}");
    Console.WriteLine($"PASS {name}");
}

static void AssertTrue(bool actual, string name)
{
    if (!actual) throw new Exception($"{name}: expected true");
    Console.WriteLine($"PASS {name}");
}

