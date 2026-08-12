using FantasyTools.Api.Documents;
using FantasyTools.Api.Models;
using System.Collections.Concurrent;

namespace FantasyTools.Api.Services;

public class CardWorkspaceService(IFileService fileService) : ICardWorkspaceService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly HashSet<string> AllowedPermissions = ["create_card_drafts", "edit_card_rules", "approve_cards"];
    private static readonly HashSet<string> AllowedStatuses = ["IDEA", "ARTWORK READY", "NEEDS REVIEW", "ACTIVE", "ARCHIVED"];

    public async Task<CardWorkspaceDocument> Get(string leagueId, string userId)
    {
        var workspace = await Load(leagueId);
        EnsureMember(workspace, userId);
        return workspace;
    }

    public Task<CardDraftDocument> Create(string leagueId, string userId, string userName, SaveCardDraftRequest request) =>
        Mutate(leagueId, userId, "create_card_drafts", workspace =>
        {
            Validate(request, request.SubmitForReview);
            var now = DateTime.UtcNow;
            var card = Build(request, Guid.NewGuid().ToString(), userId, userName, now, now);
            workspace.Cards.Add(card);
            Audit(workspace, card.Id, userId, userName, request.SubmitForReview ? "submitted_for_review" : "created_draft");
            return card;
        });

    public Task<CardDraftDocument> Update(string leagueId, string cardId, string userId, string userName, SaveCardDraftRequest request) =>
        Mutate(leagueId, userId, "edit_card_rules", workspace =>
        {
            Validate(request, request.SubmitForReview);
            var index = workspace.Cards.FindIndex(card => card.Id == cardId);
            if (index < 0) throw new KeyNotFoundException("Card draft not found.");
            var current = workspace.Cards[index];
            if (current.Status == "ACTIVE") throw new InvalidOperationException("Remove this card from the deck before editing its rules.");
            var updated = Build(request, current.Id, current.CreatedByUserId, userName, current.CreatedAt, DateTime.UtcNow);
            updated.UpdatedByUserId = userId;
            workspace.Cards[index] = updated;
            Audit(workspace, cardId, userId, userName, request.SubmitForReview ? "submitted_for_review" : "updated_draft");
            return updated;
        });

    public Task<CardDraftDocument> ChangeStatus(string leagueId, string cardId, string userId, string userName, string status) =>
        Mutate(leagueId, userId, "approve_cards", workspace =>
        {
            status = (status ?? "").Trim().ToUpperInvariant();
            if (!AllowedStatuses.Contains(status)) throw new ArgumentException("Unknown card status.");
            var card = workspace.Cards.SingleOrDefault(item => item.Id == cardId) ?? throw new KeyNotFoundException("Card draft not found.");
            if (status == "ACTIVE")
            {
                ValidateActive(card);
                card.ReviewedByUserId = userId;
                card.ReviewedAt = DateTime.UtcNow;
            }
            card.Status = status;
            card.UpdatedByUserId = userId;
            card.UpdatedByName = userName;
            card.UpdatedAt = DateTime.UtcNow;
            Audit(workspace, cardId, userId, userName, status == "ACTIVE" ? "approved_and_activated" : $"status_changed_to_{status.ToLowerInvariant().Replace(' ', '_')}");
            return card;
        });

    public Task SetCollaborator(string leagueId, string actorUserId, ChangeCardCollaboratorRequest request) =>
        Mutate<object>(leagueId, actorUserId, null, workspace =>
        {
            if (workspace.PrimaryCommissionerUserId != actorUserId) throw new UnauthorizedAccessException("Only the primary commissioner can change card permissions.");
            if (string.IsNullOrWhiteSpace(request.UserId)) throw new ArgumentException("A user ID is required.");
            if (request.Permissions.Any(permission => !AllowedPermissions.Contains(permission))) throw new ArgumentException("Unknown card permission.");
            workspace.Collaborators.RemoveAll(item => item.UserId == request.UserId);
            if (request.Permissions.Count > 0) workspace.Collaborators.Add(new CardCollaboratorDocument { UserId = request.UserId, Permissions = request.Permissions });
            return null;
        });

    private async Task<T> Mutate<T>(string leagueId, string userId, string permission, Func<CardWorkspaceDocument, T> mutation)
    {
        var gate = Locks.GetOrAdd(leagueId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var workspace = await LoadOrCreate(leagueId, userId);
            if (permission != null) EnsurePermission(workspace, userId, permission);
            var result = mutation(workspace);
            workspace.At = DateTime.UtcNow;
            await fileService.Upsert(workspace);
            return result;
        }
        finally { gate.Release(); }
    }

    private async Task<CardWorkspaceDocument> Load(string leagueId) =>
        await fileService.Retrieve(new CardWorkspaceDocument { LeagueId = leagueId }) ?? throw new KeyNotFoundException("Card workspace not found.");

    private async Task<CardWorkspaceDocument> LoadOrCreate(string leagueId, string userId) =>
        await fileService.Retrieve(new CardWorkspaceDocument { LeagueId = leagueId }) ?? new CardWorkspaceDocument { LeagueId = leagueId, PrimaryCommissionerUserId = userId, At = DateTime.UtcNow };

    private static void EnsureMember(CardWorkspaceDocument workspace, string userId)
    {
        if (workspace.PrimaryCommissionerUserId != userId && !workspace.Collaborators.Any(item => item.UserId == userId)) throw new UnauthorizedAccessException("You do not have access to this league's card workspace.");
    }

    private static void EnsurePermission(CardWorkspaceDocument workspace, string userId, string permission)
    {
        if (workspace.PrimaryCommissionerUserId == userId) return;
        if (!workspace.Collaborators.Any(item => item.UserId == userId && item.Permissions.Contains(permission))) throw new UnauthorizedAccessException($"Missing permission: {permission}.");
    }

    private static void Validate(SaveCardDraftRequest request, bool review)
    {
        if (string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.ArtworkUrl)) throw new ArgumentException("A working name or artwork is required.");
        if (!IsUploadedArtwork(request.ArtworkUrl)) throw new ArgumentException("Artwork must be uploaded through POST /api/images before the card is saved.");
        if (review && (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ArtworkUrl) || string.IsNullOrWhiteSpace(request.OfficialDescription))) throw new ArgumentException("Name, artwork, and completed rules are required for review.");
        if (request.Copies is < 1 or > 99) throw new ArgumentException("Deck copies must be between 1 and 99.");
    }

    /// <summary>
    /// Artwork reaches the card as a URL from the upload endpoint, never as the image itself -- the whole
    /// point of the images bucket is that a card document stays small enough to read on every request.
    /// </summary>
    private static bool IsUploadedArtwork(string artwork) =>
        string.IsNullOrEmpty(artwork)
        || artwork.StartsWith("/api/images/")
        || artwork.StartsWith("https://")
        || artwork.StartsWith("http://");

    private static void ValidateActive(CardDraftDocument card)
    {
        if (card.Status != "NEEDS REVIEW") throw new InvalidOperationException("Only a card awaiting review can be activated.");
        if (string.IsNullOrWhiteSpace(card.ArtworkUrl) || string.IsNullOrWhiteSpace(card.OfficialDescription)) throw new InvalidOperationException("Artwork and complete rules are required before activation.");
    }

    private static CardDraftDocument Build(SaveCardDraftRequest request, string id, string creator, string editorName, DateTime createdAt, DateTime updatedAt) => new()
    {
        Id = id, Name = request.Name?.Trim() ?? "Untitled card idea", Category = request.Category ?? "ATTACK", Rarity = request.Rarity ?? "Common",
        IsSpecial = request.IsSpecial, ArtworkUrl = request.ArtworkUrl, OfficialDescription = request.OfficialDescription?.Trim() ?? "", CommissionerNotes = request.CommissionerNotes?.Trim() ?? "",
        Target = request.Target, EffectType = request.EffectType, Amount = request.Amount, Copies = request.Copies, SourcePlayer = request.SourcePlayer, SourcePlayerId = request.SourcePlayerId,
        DestinationSlot = request.DestinationSlot, Multiplier = request.Multiplier, Status = request.SubmitForReview ? "NEEDS REVIEW" : string.IsNullOrWhiteSpace(request.ArtworkUrl) ? "IDEA" : "ARTWORK READY",
        CreatedByUserId = creator, UpdatedByUserId = creator, UpdatedByName = editorName, CreatedAt = createdAt, UpdatedAt = updatedAt
    };

    private static void Audit(CardWorkspaceDocument workspace, string cardId, string userId, string userName, string action)
    {
        workspace.Audit.Insert(0, new CardAuditDocument { CardId = cardId, ActorUserId = userId, ActorName = userName, Action = action, At = DateTime.UtcNow });
        if (workspace.Audit.Count > 500) workspace.Audit.RemoveRange(500, workspace.Audit.Count - 500);
    }
}
