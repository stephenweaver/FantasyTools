namespace FantasyTools.Api.Documents;

public class LeagueGameDocument : BaseDocument
{
    public override string Id { get => LeagueId; set { } }
    public override string Pk { get => "chaos-league-games"; set { } }
    public string LeagueId { get; set; }
    public string SleeperLeagueId { get; set; }
    public DateTime LastSleeperSyncAt { get; set; }
    public string SleeperStatus { get; set; } = "pre_draft";
    public List<SleeperTeamSnapshot> Teams { get; set; } = [];
    public List<SleeperMatchupSnapshot> Matchups { get; set; } = [];
    public List<SeasonHandDocument> Hands { get; set; } = [];
    public List<WeeklyGameDocument> Weeks { get; set; } = [];
}

public class SeasonHandDocument
{
    public int RosterId { get; set; }
    public int LastDrawnWeek { get; set; }
    public List<DealtCardDocument> Cards { get; set; } = [];
}

public class SleeperTeamSnapshot
{
    public int RosterId { get; set; }
    public string OwnerId { get; set; }
    public string ManagerName { get; set; }
    public string TeamName { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public List<SleeperPlayerSnapshot> Players { get; set; } = [];
}

public class SleeperPlayerSnapshot
{
    public string PlayerId { get; set; }
    public string Name { get; set; }
    public string Position { get; set; }
    public string NflTeam { get; set; }
    public bool Starter { get; set; }
    public decimal Points { get; set; }
}

public class SleeperMatchupSnapshot
{
    public int Week { get; set; }
    public int MatchupId { get; set; }
    public int RosterId { get; set; }
    public decimal Points { get; set; }
    public List<string> Starters { get; set; } = [];
    public Dictionary<string, decimal> PlayerPoints { get; set; } = [];
}

public class WeeklyGameDocument
{
    public int Week { get; set; }
    public string Status { get; set; } = "selection_open";
    public DateTime DeadlineUtc { get; set; }
    public DateTime? RevealedAtUtc { get; set; }
    public List<TeamWeekDocument> Teams { get; set; } = [];
}

public class TeamWeekDocument
{
    public int RosterId { get; set; }
    public DateTime? DrawnAtUtc { get; set; }
    public List<DealtCardDocument> Hand { get; set; } = [];
    public List<CardSelectionDocument> Selections { get; set; } = [];
}

public class DealtCardDocument
{
    public string CopyId { get; set; }
    public string CardId { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public string ArtworkUrl { get; set; }
    public string Description { get; set; }
    public string Target { get; set; }
}

public class CardSelectionDocument
{
    public string CopyId { get; set; }
    public string CardId { get; set; }
    public string Category { get; set; }
    public string TargetRosterId { get; set; }
    public string TargetPlayerId { get; set; }
    public string TargetSlot { get; set; }
    public DateTime SelectedAtUtc { get; set; }
}
