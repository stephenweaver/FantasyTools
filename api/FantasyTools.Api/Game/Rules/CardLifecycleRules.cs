using FantasyTools.Api.Game.Domain;

namespace FantasyTools.Api.Game.Rules;

public sealed record ReplacementDraw(CardCategory Category, int Quantity);

public interface ICardLifecycleRules
{
    RuleDecision ValidateTransition(CardCopyState from, CardCopyState to);
    RuleDecision CanReturnSelection(WeekStatus weekStatus, DateTimeOffset now, DateTimeOffset deadline);
    IReadOnlyList<ReplacementDraw> CalculateReplacementDraws(int cardsStillInHand, IReadOnlyList<CardCategory> categoriesPlayed, int maxHandSize = 5);
}

public sealed class CardLifecycleRules : ICardLifecycleRules
{
    private static readonly IReadOnlyDictionary<CardCopyState, CardCopyState[]> AllowedTransitions =
        new Dictionary<CardCopyState, CardCopyState[]>
        {
            [CardCopyState.Deck] = [CardCopyState.Hand],
            [CardCopyState.Hand] = [CardCopyState.SecretSelection, CardCopyState.Played],
            [CardCopyState.SecretSelection] = [CardCopyState.Hand, CardCopyState.Locked],
            [CardCopyState.Locked] = [CardCopyState.Revealed],
            [CardCopyState.Revealed] = [CardCopyState.Played],
            [CardCopyState.Played] = [CardCopyState.WeeklyDiscard],
            [CardCopyState.WeeklyDiscard] = [CardCopyState.Deck]
        };

    public RuleDecision ValidateTransition(CardCopyState from, CardCopyState to) =>
        AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to)
            ? RuleDecision.Permit()
            : RuleDecision.Reject("invalid_card_transition", $"A card copy cannot move from {from} to {to}.");

    public RuleDecision CanReturnSelection(WeekStatus weekStatus, DateTimeOffset now, DateTimeOffset deadline) =>
        weekStatus == WeekStatus.SelectionOpen && now < deadline
            ? RuleDecision.Permit()
            : RuleDecision.Reject("selection_locked", "Only an unlocked pre-week selection can return to the hand.");

    public IReadOnlyList<ReplacementDraw> CalculateReplacementDraws(int cardsStillInHand,
        IReadOnlyList<CardCategory> categoriesPlayed, int maxHandSize = 5)
    {
        if (cardsStillInHand < 0 || cardsStillInHand > maxHandSize)
            throw new ArgumentOutOfRangeException(nameof(cardsStillInHand));

        var openSlots = maxHandSize - cardsStillInHand;
        if (categoriesPlayed.Count != openSlots)
            throw new InvalidOperationException("Replacement draws must exactly match the categories removed from the hand.");

        return categoriesPlayed
            .GroupBy(category => category)
            .OrderBy(group => group.Key)
            .Select(group => new ReplacementDraw(group.Key, group.Count()))
            .ToList();
    }
}
