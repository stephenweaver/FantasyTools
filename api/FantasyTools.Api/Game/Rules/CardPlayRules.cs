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

        if (state.Status != WeekStatus.SelectionOpen || request.RequestedAt >= state.Deadline)
            return RuleDecision.Reject("selection_locked", "Weekly card selections are locked.");
        if (state.ExistingPreWeekSelections >= 4)
            return RuleDecision.Reject("weekly_limit", "A manager selects exactly four cards each week.");

        var normalized = Normalize(request.Category);
        var selected = state.SelectedCategories.Select(Normalize).ToList();
        var limit = normalized == CardCategory.Unique ? 2 : 1;
        if (selected.Count(category => category == normalized) >= limit)
            return RuleDecision.Reject("category_limit", normalized == CardCategory.Unique
                ? "Only two Unique cards may be selected."
                : $"Only one {normalized} card may be selected.");
        return RuleDecision.Permit();
    }

    public static RuleDecision ValidateCompleteSelection(IReadOnlyList<CardCategory> categories)
    {
        var normalized = categories.Select(Normalize).ToList();
        return normalized.Count == 4 && normalized.Count(c => c == CardCategory.Boost) == 1
            && normalized.Count(c => c == CardCategory.Attack) == 1
            && normalized.Count(c => c == CardCategory.Unique) == 2
            ? RuleDecision.Permit()
            : RuleDecision.Reject("invalid_weekly_mix", "Select exactly 1 Boost, 1 Attack, and 2 Unique cards.");
    }

    private static CardCategory Normalize(CardCategory category) =>
        category == CardCategory.Defense ? CardCategory.Unique : category;
}
