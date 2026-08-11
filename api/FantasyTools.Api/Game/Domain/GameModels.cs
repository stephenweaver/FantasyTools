namespace FantasyTools.Api.Game.Domain;

public enum CardCategory { Attack, Boost, Defense }
public enum CardTiming { PreWeek, Live }
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
    IReadOnlyList<ActiveEffect> Effects);

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
    CardTiming Timing,
    CardTarget Target,
    DateTimeOffset RequestedAt);

public sealed record WeekPlayState(
    WeekStatus Status,
    DateTimeOffset Deadline,
    int ExistingPreWeekSelections,
    int ExistingLivePlays,
    bool CardIsOwnedByActingTeam,
    CardCopyState CardState,
    bool ActingTeamIsInMatchup,
    bool TargetIsAllowed);

public sealed record RuleDecision(bool Allowed, string Code, string Message)
{
    public static RuleDecision Permit() => new(true, "allowed", "The play is valid.");
    public static RuleDecision Reject(string code, string message) => new(false, code, message);
}
