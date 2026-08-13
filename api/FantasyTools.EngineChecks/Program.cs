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

