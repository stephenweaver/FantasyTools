namespace FantasyTools.Api.Documents;

public class CardWorkspaceDocument : BaseDocument
{
    public override string Id { get => LeagueId; set { } }
    public override string Pk { get => "chaos-card-workspaces"; set { } }
    public string LeagueId { get; set; }
    public string PrimaryCommissionerUserId { get; set; }
    public List<CardCollaboratorDocument> Collaborators { get; set; } = [];
    public List<CardDraftDocument> Cards { get; set; } = [];
    public List<CardAuditDocument> Audit { get; set; } = [];
}

public class CardCollaboratorDocument
{
    public string UserId { get; set; }
    public HashSet<string> Permissions { get; set; } = [];
}

public class CardDraftDocument
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public string Rarity { get; set; }
    public bool IsSpecial { get; set; }
    public string ArtworkDataUrl { get; set; }
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
    public string Status { get; set; }
    public string CreatedByUserId { get; set; }
    public string UpdatedByUserId { get; set; }
    public string UpdatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class CardAuditDocument
{
    public string CardId { get; set; }
    public string ActorUserId { get; set; }
    public string ActorName { get; set; }
    public string Action { get; set; }
    public DateTime At { get; set; }
}
