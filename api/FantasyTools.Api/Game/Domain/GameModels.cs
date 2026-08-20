namespace FantasyTools.Api.Game.Domain;

public enum CardCategory { Attack, Boost, Unique, Defense } // Defense remains for legacy saved cards; it is treated as Unique.
public enum CardTiming { PreWeek }
public enum TargetType { StartingSlot, PositionGroup, Team, SpecificPlayer, Dynamic }
public enum EffectType
{
    FlatPoints,
    Percentage,
    BlockAttack,
    ReduceAttack,
    ReferencedPlayerReplacesSlot,
    Custom
}
public enum CardCopyState { Deck, Hand, SecretSelection, Locked, Revealed, Played, WeeklyDiscard }
public enum CardWorkflowStatus { Idea, ArtworkReady, NeedsReview, Active, Archived }
public enum WeekStatus { Setup, SelectionOpen, Locked, Revealed, Live, Finalized }

public sealed record CardDefinition(
    Guid Id,
    string Name,
    CardCategory Category,
    string OfficialDescription,
    string ArtworkKey,
    string Rarity,
    bool IsSpecial,
    bool IsActive,
    CardWorkflowStatus WorkflowStatus,
    string CommissionerNotes,
    TargetType TargetType,
    EffectType EffectType,
    decimal Amount,
    string? CustomHandler,
    string? ReferencedPlayerId,
    string? DestinationSlot,
    decimal? Multiplier);

public sealed record CardCopy(Guid Id, Guid DefinitionId, Guid LeagueId, CardCopyState State, Guid? OwnerTeamId);

public sealed record CardTarget(
    TargetType Type,
    Guid TargetTeamId,
    string? StartingSlot,
    string? Position,
    string? NflPlayerId,
    string? DynamicRule);

public sealed record CardPlay(
    Guid Id,
    Guid WeekId,
    Guid MatchupId,
    Guid TeamId,
    Guid CardCopyId,
    CardTiming Timing,
    CardTarget Target,
    DateTimeOffset SelectedAt,
    DateTimeOffset? LockedAt,
    DateTimeOffset? RevealedAt,
    DateTimeOffset? PlayedAt);

public sealed record SlotScore(string Slot, string PlayerId, string PlayerName, string Position, decimal RawPoints);

// The scoring provider supplies these raw weekly totals. Specialty cards use them
// instead of trying to infer football statistics from a fantasy-point total.
public sealed record PlayerWeekStats(
    int Receptions = 0,
    int Targets = 0,
    int Completions = 0,
    int PassingAttempts = 0,
    int SacksTaken = 0,
    int Fumbles = 0,
    int PassingInterceptions = 0,
    int DefensiveSacks = 0,
    int DefensiveInterceptions = 0,
    int DefensiveFumbleRecoveries = 0,
    decimal TouchdownPoints = 0m,
    decimal RushingYards = 0m,
    decimal PassingYards = 0m,
    decimal ReceivingYards = 0m,
    decimal RushingYardPoints = 0m,
    decimal PassingYardPoints = 0m,
    decimal ReceivingYardPoints = 0m,
    decimal ReceptionPoints = 0m,
    decimal CompletionPoints = 0m,
    decimal PassingTouchdownPoints = 0m,
    decimal DefensiveSackPoints = 0m,
    decimal DefensiveInterceptionPoints = 0m,
    decimal PassingTouchdowns = 0m,
    decimal RushingTouchdowns = 0m,
    decimal ReceivingTouchdowns = 0m,
    decimal FieldGoalYards = 0m,
    decimal FieldGoalPoints = 0m,
    decimal BonusPoints = 0m,
    bool LeftGameInjuredAndDidNotReturn = false,
    string InjuryStatus = "");

public sealed record ActiveEffect(
    Guid CardPlayId,
    string CardName,
    CardCategory Category,
    EffectType Type,
    CardTarget Target,
    decimal Amount,
    string? ReferencedPlayerId = null,
    string? DestinationSlot = null,
    decimal? Multiplier = null,
    string? CustomHandler = null);

public sealed record TeamScoreInput(
    Guid TeamId,
    IReadOnlyList<SlotScore> Starters,
    IReadOnlyDictionary<string, decimal> ReferencedPlayerScores,
    IReadOnlyList<ActiveEffect> Effects)
{
    public IReadOnlyDictionary<string, PlayerWeekStats> PlayerStats { get; init; } =
        new Dictionary<string, PlayerWeekStats>();
    public decimal? ScoreEnteringMonday { get; init; }
    public decimal? OpponentScoreEnteringMonday { get; init; }
    public decimal? LeagueHighestPlayerScore { get; init; }
    public IReadOnlyDictionary<string, decimal> LeagueHighestStarterScoreByPosition { get; init; } =
        new Dictionary<string, decimal>();
    public IReadOnlyList<SlotScore> Bench { get; init; } = [];
    public IReadOnlyList<SlotScore> OpponentStarters { get; init; } = [];
    public IReadOnlyList<SlotScore> OpponentBench { get; init; } = [];
    public IReadOnlyDictionary<string, decimal> Projections { get; init; } = new Dictionary<string, decimal>();
}

public sealed record CalculationLine(
    int Stage,
    string Kind,
    string Description,
    decimal Before,
    decimal Change,
    decimal After,
    Guid? CardPlayId = null);

public sealed record ChaosScoreResult(decimal SleeperScore, decimal ChaosScore, IReadOnlyList<CalculationLine> Lines);

public sealed record PlayRequest(
    Guid WeekId,
    Guid MatchupId,
    Guid ActingTeamId,
    Guid OpponentTeamId,
    Guid CardCopyId,
    CardCategory Category,
    CardTiming Timing,
    CardTarget Target,
    DateTimeOffset RequestedAt);

public sealed record WeekPlayState(
    WeekStatus Status,
    DateTimeOffset Deadline,
    int ExistingPreWeekSelections,
    IReadOnlyList<CardCategory> SelectedCategories,
    bool CardIsOwnedByActingTeam,
    CardCopyState CardState,
    bool ActingTeamIsInMatchup,
    bool TargetIsAllowed);

public sealed record RuleDecision(bool Allowed, string Code, string Message)
{
    public static RuleDecision Permit() => new(true, "allowed", "The play is valid.");
    public static RuleDecision Reject(string code, string message) => new(false, code, message);
}

