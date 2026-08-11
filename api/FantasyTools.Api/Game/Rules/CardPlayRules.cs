using FantasyTools.Api.Game.Domain;

namespace FantasyTools.Api.Game.Rules;

public interface ICardPlayRules
{
    RuleDecision Validate(PlayRequest request, WeekPlayState state);
}

public sealed class CardPlayRules : ICardPlayRules
{
    public RuleDecision Validate(PlayRequest request, WeekPlayState state)
    {
        if (!state.CardIsOwnedByActingTeam || state.CardState != CardCopyState.Hand)
            return RuleDecision.Reject("card_not_in_hand", "The card is not an available copy in this manager's hand.");
        if (!state.ActingTeamIsInMatchup)
            return RuleDecision.Reject("wrong_matchup", "The manager is not a participant in this matchup.");
        if (!state.TargetIsAllowed)
            return RuleDecision.Reject("invalid_target", "This card cannot affect the selected target.");

        if (request.Timing == CardTiming.PreWeek)
        {
            if (state.Status != WeekStatus.SelectionOpen || request.RequestedAt >= state.Deadline)
                return RuleDecision.Reject("selection_locked", "Pre-week selections are locked.");
            if (state.ExistingPreWeekSelections >= 2)
                return RuleDecision.Reject("preweek_limit", "A manager may select at most two pre-week cards.");
            return RuleDecision.Permit();
        }

        if (state.Status is not (WeekStatus.Revealed or WeekStatus.Live))
            return RuleDecision.Reject("live_closed", "Live card play is not open.");
        if (state.ExistingLivePlays >= 1)
            return RuleDecision.Reject("live_limit", "The manager has already used the one live card allowed this week.");
        return RuleDecision.Permit();
    }
}
