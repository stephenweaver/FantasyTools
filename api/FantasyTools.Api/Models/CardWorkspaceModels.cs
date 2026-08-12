namespace FantasyTools.Api.Models;

public class SaveCardDraftRequest
{
    public string Name { get; set; }
    public string Category { get; set; }
    public string Rarity { get; set; }
    public bool IsSpecial { get; set; }

    /// <summary>URL returned by POST /api/images.</summary>
    public string ArtworkUrl { get; set; }

    public string OfficialDescription { get; set; }
    public string CommissionerNotes { get; set; }
    public string Target { get; set; }
    public string EffectType { get; set; }
    public decimal Amount { get; set; }
    public int Copies { get; set; }
    public string SourcePlayer { get; set; }
    public string SourcePlayerId { get; set; }
    public string DestinationSlot { get; set; }
    public decimal? Multiplier { get; set; }
    public bool SubmitForReview { get; set; }
}

public class ChangeCardStatusRequest { public string Status { get; set; } }
public class ChangeCardCollaboratorRequest
{
    public string UserId { get; set; }
    public HashSet<string> Permissions { get; set; } = [];
}
