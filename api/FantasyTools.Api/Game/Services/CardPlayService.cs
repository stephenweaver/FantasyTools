using FantasyTools.Api.Game.Domain;
using FantasyTools.Api.Game.Rules;
using FantasyTools.Api.Game.Storage;

namespace FantasyTools.Api.Game.Services;

public sealed class CardPlayService(ICardPlayRules rules, IGameRepository repository)
{
    public async Task<RuleDecision> PlayAsync(PlayRequest request, Guid actorUserId, Guid leagueId, CancellationToken cancellationToken)
    {
        // Repository implementation wraps the read/validate/write sequence in one serializable transaction.
        var state = await repository.GetWeekPlayStateForUpdateAsync(request, cancellationToken);
        var decision = rules.Validate(request, state);
        if (!decision.Allowed) return decision;

        if (request.Timing == CardTiming.PreWeek)
            await repository.RecordPreWeekSelectionAsync(request, cancellationToken);
        else
            await repository.RecordLivePlayAsync(request, cancellationToken);

        await repository.AppendEventAsync(leagueId, request.WeekId,
            request.Timing == CardTiming.PreWeek ? "card.selected" : "card.played_live",
            request, actorUserId, cancellationToken);
        return decision;
    }

    public async Task ReturnToHandAsync(Guid leagueId, Guid weekId, Guid teamId, Guid cardCopyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        await repository.ReturnUnlockedSelectionToHandAsync(weekId, teamId, cardCopyId, cancellationToken);
        await repository.AppendEventAsync(leagueId, weekId, "card.selection_returned",
            new { teamId, cardCopyId }, actorUserId, cancellationToken);
    }
}
