using FantasyTools.Api.Game.Domain;

namespace FantasyTools.Api.Game.Storage;

/// <summary>
/// Transaction boundary for permanent game state. The PostgreSQL implementation must lock the card-copy
/// and weekly-team rows before validating a play so two simultaneous requests cannot spend the same card.
/// </summary>
public interface IGameRepository
{
    Task<CardCopy?> GetCardCopyForUpdateAsync(Guid cardCopyId, CancellationToken cancellationToken);
    Task<WeekPlayState> GetWeekPlayStateForUpdateAsync(PlayRequest request, CancellationToken cancellationToken);
    Task RecordPreWeekSelectionAsync(PlayRequest request, CancellationToken cancellationToken);
    Task ReturnUnlockedSelectionToHandAsync(Guid weekId, Guid teamId, Guid cardCopyId, CancellationToken cancellationToken);
    Task RecordLivePlayAsync(PlayRequest request, CancellationToken cancellationToken);
    Task AppendEventAsync(Guid leagueId, Guid weekId, string eventType, object payload, Guid? actorUserId, CancellationToken cancellationToken);
}
