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

var rules = new CardPlayRules();
var request = new PlayRequest(weekId, matchupId, teamId, opponentId, cardCopyId, CardTiming.PreWeek, qbTarget, DateTimeOffset.UtcNow);
var validState = new WeekPlayState(WeekStatus.SelectionOpen, DateTimeOffset.UtcNow.AddHours(1), 0, 0, true, CardCopyState.Hand, true, true);
AssertTrue(rules.Validate(request, validState).Allowed, "valid pre-week play");
AssertEqual("preweek_limit", rules.Validate(request, validState with { ExistingPreWeekSelections = 2 }).Code, "two-card pre-week limit");
AssertEqual("live_limit", rules.Validate(request with { Timing = CardTiming.Live }, validState with { Status = WeekStatus.Live, ExistingLivePlays = 1 }).Code, "one-card live limit");
AssertEqual("card_not_in_hand", rules.Validate(request, validState with { CardState = CardCopyState.Played }).Code, "card ownership/state enforcement");

var lifecycle = new CardLifecycleRules();
AssertTrue(lifecycle.ValidateTransition(CardCopyState.SecretSelection, CardCopyState.Hand).Allowed, "unlocked selection returns to hand");
AssertEqual("invalid_card_transition", lifecycle.ValidateTransition(CardCopyState.Locked, CardCopyState.Hand).Code, "locked card cannot return to hand");
var draws = lifecycle.CalculateReplacementDraws(3, [CardCategory.Attack, CardCategory.Defense]);
AssertEqual(1, draws.Single(draw => draw.Category == CardCategory.Attack).Quantity, "attack replacement draw");
AssertEqual(1, draws.Single(draw => draw.Category == CardCategory.Defense).Quantity, "defense replacement draw");

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
