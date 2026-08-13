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
        if (!workspace.Audit.Any(item => item.Action == "imported_card_data_ideas_v1"))
        {
            ImportCardData(workspace, userId);
            workspace.At = DateTime.UtcNow;
            await fileService.Upsert(workspace);
        }
        if (!workspace.Audit.Any(item => item.Action == "applied_verified_card_rules_v2"))
        {
            ApplyVerifiedRules(workspace, userId);
            workspace.At = DateTime.UtcNow;
            await fileService.Upsert(workspace);
        }
        return workspace;
    }

    private static void ApplyVerifiedRules(CardWorkspaceDocument workspace, string userId)
    {
        var verified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Shoestring Tackle"]="Increase your starting defense's final score by 50%.",
            ["Pick Six"]="Remove the opponent QB's passing-touchdown points and add them to your starting defense.",
            ["MVP"]="Replace one starter with the highest-scoring same-position player owned in the league, including benches.",
            ["Spygate"]="Replace an opponent starter with a same-position bench player. Out and IR players are ineligible.",
            ["Traded WR"]="Transfer an opponent WR's score to replace your starting WR; both original scores are removed.",
            ["Traded RB"]="Transfer an opponent RB's score to replace your starting RB; both original scores are removed.",
            ["Traded TE"]="Transfer an opponent TE's score to replace your starting TE; both original scores are removed.",
            ["1v1 me bro"]="Choose any two starters. The higher scorer earns both scores; the card player wins a tie.",
            ["Injured"]="Add 50 points once if a starter leaves injured and does not return. Pre-game Out players do not qualify.",
            ["28-3"]="If down by at least 50 immediately before the first Monday game, add 40 points to the team score.",
            ["Sticky Hands"]="Replace normal reception scoring so the selected starter earns 2 points per catch.",
            ["Beast Mode"]="Replace normal rushing-yard scoring so the selected RB earns 0.3 points per rushing yard.",
            ["Complete"]="Replace normal completion scoring so the selected QB earns 2 points per completion.",
            ["Incomplete"]="Add 3 extra points for every incomplete pass by the selected QB.",
            ["Sacked"]="Subtract 5 points from the opponent starting QB for every sack taken.",
            ["Rough Start"]="The selected opponent starter begins at minus 15, then adds their normal weekly score.",
            ["Double TD"]="Double only the selected player's fantasy points earned from touchdowns.",
            ["FAFB"]="Double only your starting QB's rushing-yard points; rushing touchdowns are unchanged.",
            ["Butt Fumble"]="Add 5 points for every fumble by a starter, whether or not it is lost.",
            ["Cap Hit"]="After all other effects, cap every opponent starter's final score at 15 points.",
            ["Double or Nothing"]="Choose a starting RB, WR, TE, or eligible FLEX: 5+ catches doubles the full score; 4 or fewer scores zero.",
            ["Bromance"]="Replace your QB contribution with two times Patrick Mahomes's weekly score.",
            ["Challenge Flag"]="Select before the week. After reveal, cancel one player-played opponent card before Thursday; no choice means random. League Weekly Cards are immune. If no target exists, discard it unused."
        };
        var now=DateTime.UtcNow;
        foreach (var card in workspace.Cards)
        {
            if (card.Category.Equals("DEFENSE",StringComparison.OrdinalIgnoreCase) || card.IsSpecial) card.Category="UNIQUE";
            if (verified.TryGetValue(card.Name,out var description)) { card.OfficialDescription=description; card.UpdatedAt=now; card.UpdatedByName="Verified rules migration"; card.UpdatedByUserId=userId; }
        }
        workspace.Audit.Insert(0,new CardAuditDocument{CardId="verified-rules-v2",ActorUserId=userId,ActorName="Verified rules migration",Action="applied_verified_card_rules_v2",At=now});
    }

    private static void ImportCardData(CardWorkspaceDocument workspace, string userId)
    {
        var now = DateTime.UtcNow;
        var basics = new[]
        {
            "ATTACK|Two Deep|25|QB|3", "ATTACK|No Fly Zone|50|QB|2", "ATTACK|Air Traffic Control|100|QB|1",
            "UNIQUE|Iron Curtain|25|RB|3", "UNIQUE|Stacked Box|50|RB|2", "UNIQUE|Stuffed|100|RB|1",
            "UNIQUE|Lockdown|25|WR|3", "UNIQUE|Double Teamed|50|WR|2", "UNIQUE|Denial|100|WR|1",
            "UNIQUE|Shutdown|25|TE|3", "UNIQUE|Pressure|50|TE|2", "UNIQUE|The Mike|100|TE|1",
            "BOOST|Launched|25|QB|3", "BOOST|Air It Out|50|QB|2", "BOOST|West Coast|100|QB|1",
            "BOOST|Stiff Arm|25|RB|3", "BOOST|Ankle Breaker|50|RB|2", "BOOST|Trucked|100|RB|1",
            "BOOST|Crosser|25|WR|3", "BOOST|Deep Threat|50|WR|2", "BOOST|Moss'd|100|WR|1",
            "BOOST|Possession|25|TE|3", "BOOST|Shake'n'Bake|50|TE|2", "BOOST|Gronk|100|TE|1"
        };
        foreach (var row in basics)
        {
            var p=row.Split('|');
            var description = p[0] == "ATTACK" ? $"Reduce your opponent's starting {p[3]} points by {p[2]}%." : p[0] == "UNIQUE" ? $"Reduce an incoming attack against your {p[3]} by {p[2]}%." : $"Increase your starting {p[3]} points by {p[2]}%.";
            var effect = p[0] == "ATTACK" ? "Percentage reduction" : p[0] == "UNIQUE" ? "Attack protection" : "Percentage boost";
            if (!workspace.Cards.Any(card => card.Name.Equals(p[1], StringComparison.OrdinalIgnoreCase)))
                workspace.Cards.Add(new CardDraftDocument { Id=Guid.NewGuid().ToString(), Name=p[1], Category=p[0], Rarity="Common", IsSpecial=false, ArtworkUrl="", OfficialDescription=description, CommissionerNotes="Imported from the Card Data sheet. Effect follows the indicated lineup position.", Target=p[3], EffectType=effect, Amount=decimal.Parse(p[2]), Copies=int.Parse(p[4]), Status="IDEA", CreatedByUserId=userId, UpdatedByUserId=userId, UpdatedByName="Card Data import", CreatedAt=now, UpdatedAt=now });
        }

        var specials = new[]
        {
            "Challenge Flag|N/A|N/A|10|Prevent an opponent's card from being played or scored.|One card per team at start of season.",
            "Pick Six|QB/DEF|2|2|Subtract the touchdown value from the opponent QB and add it to your defense.|",
            "Complete|QB|2|2|Add 2 points per completion.|",
            "Incomplete|QB|3|2|Add 3 points per incompletion.|",
            "Rough Start|Single Player|15|2|Opponent begins the week at minus 15 points.|",
            "Double TD|Chosen Player|100|2|Double touchdown points for the chosen player.|",
            "Double or Nothing|W/R/T|100|2|Five or more receptions doubles the player's points; four or fewer scores zero.|",
            "Bromance|QB|100|2|Use double Patrick Mahomes's weekly points as your QB points, regardless of starter.|",
            "Spygate|Chosen Player|0|2|Swap an opponent starter with a bench player of your choice.|Bench replacement cannot be injured.",
            "Traded WR|Chosen Player|0|2|Select another team's WR and use that score in your starting lineup.|",
            "Traded RB|Chosen Player|0|2|Select another team's RB and use that score in your starting lineup.|",
            "Traded TE|Chosen Player|0|2|Select another team's TE and use that score in your starting lineup.|",
            "FAFB|QB|100|2|Double your QB rushing-yardage points for the week.|",
            "Sticky Hands|W/R/T|2|2|Every reception is worth 2 points.|",
            "Beast Mode|RB|0.3|2|Every rushing yard is worth 0.3 points.|",
            "Sacked|QB|-5|2|Every time the targeted QB is sacked, subtract 5 points.|",
            "Shoestring Tackle|Defense|50|2|Apply a 50% effect to defense scoring.|Direction needs commissioner confirmation.",
            "28-3|Team|40|2|If losing by 50 or more points going into Monday Night Football, add 40 points at week's end.|",
            "Injured|Team|50|2|If a starting player is injured and does not return, add 50 points.|",
            "Butt Fumble|Team|5|2|Add 5 points whenever a player on your team fumbles.|",
            "Cap Hit|Team|15|2|Cap every player on the opposing team at 15 points.|",
            "MVP|Chosen Player|0|2|Use the league's highest-scoring player's score in your roster.|Destination slot needs commissioner selection.",
            "1v1 me bro|ALL|0|2|Both teams choose one player at the selected position; the winner receives both players' points.|"
        };
        foreach (var row in specials)
        {
            var p=row.Split('|');
            if (!workspace.Cards.Any(card => card.Name.Equals(p[0], StringComparison.OrdinalIgnoreCase)))
                workspace.Cards.Add(new CardDraftDocument { Id=Guid.NewGuid().ToString(), Name=p[0], Category="UNIQUE", Rarity="Specialty", IsSpecial=true, ArtworkUrl="", OfficialDescription=p[4], CommissionerNotes=$"Imported from the Card Data sheet. {p[5]}".Trim(), Target=p[1], EffectType="Specialty rule", Amount=decimal.Parse(p[2]), Copies=int.Parse(p[3]), Status="IDEA", CreatedByUserId=userId, UpdatedByUserId=userId, UpdatedByName="Card Data import", CreatedAt=now, UpdatedAt=now });
        }
        workspace.Audit.Insert(0, new CardAuditDocument { CardId="catalog-import", ActorUserId=userId, ActorName="Card Data import", Action="imported_card_data_ideas_v1", At=now });
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

    public Task<WeeklyCardDocument> SaveWeeklyCard(string leagueId, string userId, string userName, SaveWeeklyCardRequest request) =>
        Mutate(leagueId, userId, "edit_card_rules", workspace =>
        {
            if (request.Week is < 1 or > 18) throw new ArgumentException("Week must be between 1 and 18.");
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Description))
                throw new ArgumentException("Weekly Card name and full description are required.");
            if (!IsUploadedArtwork(request.ArtworkUrl)) throw new ArgumentException("Upload the Weekly Card artwork first.");
            var card = workspace.WeeklyCards.SingleOrDefault(item => item.Week == request.Week);
            if (card is null)
            {
                card = new WeeklyCardDocument { Id = Guid.NewGuid().ToString("N"), Week = request.Week };
                workspace.WeeklyCards.Add(card);
            }
            card.Name = request.Name.Trim(); card.ArtworkUrl = request.ArtworkUrl ?? "";
            card.Description = request.Description.Trim(); card.RuleType = request.RuleType?.Trim() ?? "custom";
            card.Amount = request.Amount; card.Target = request.Target?.Trim() ?? "League"; card.Active = request.Active;
            card.UpdatedByUserId = userId; card.UpdatedByName = userName; card.UpdatedAt = DateTime.UtcNow;
            workspace.WeeklyCards = workspace.WeeklyCards.OrderBy(item => item.Week).ToList();
            Audit(workspace, card.Id, userId, userName, $"weekly_card_saved_week_{card.Week}");
            return card;
        });

    public Task DeleteWeeklyCard(string leagueId, int week, string userId) =>
        Mutate<object>(leagueId, userId, "edit_card_rules", workspace =>
        {
            workspace.WeeklyCards.RemoveAll(item => item.Week == week);
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
