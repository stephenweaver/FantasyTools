namespace FantasyTools.Api.Documents;

public class LeagueGameDocument : BaseDocument
{
    public override string Id { get => LeagueId; set { } }
    public override string Pk { get => "chaos-league-games"; set { } }
    public string LeagueId { get; set; }
    public string SleeperLeagueId { get; set; }
    public DateTime LastSleeperSyncAt { get; set; }
    public string SleeperStatus { get; set; } = "pre_draft";
    public string Season { get; set; }
    public Dictionary<string, decimal> ScoringSettings { get; set; } = [];
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
    public string StartingSlot { get; set; }
    public decimal Points { get; set; }
    public decimal Projection { get; set; }
    public PlayerStatDocument Stats { get; set; } = new();
}

public class PlayerStatDocument
{
    public int Receptions { get; set; }
    public int Targets { get; set; }
    public int Completions { get; set; }
    public int PassingAttempts { get; set; }
    public int SacksTaken { get; set; }
    public int Fumbles { get; set; }
    public int PassingInterceptions { get; set; }
    public int DefensiveSacks { get; set; }
    public int DefensiveInterceptions { get; set; }
    public int DefensiveFumbleRecoveries { get; set; }
    public decimal PassingYards { get; set; }
    public decimal RushingYards { get; set; }
    public decimal ReceivingYards { get; set; }
    public decimal PassingTouchdowns { get; set; }
    public decimal RushingTouchdowns { get; set; }
    public decimal ReceivingTouchdowns { get; set; }
    public decimal FieldGoalYards { get; set; }
    public decimal FieldGoalPoints { get; set; }
    public decimal TouchdownPoints { get; set; }
    public decimal PassingYardPoints { get; set; }
    public decimal RushingYardPoints { get; set; }
    public decimal ReceivingYardPoints { get; set; }
    public decimal ReceptionPoints { get; set; }
    public decimal CompletionPoints { get; set; }
    public decimal PassingTouchdownPoints { get; set; }
    public decimal DefensiveSackPoints { get; set; }
    public decimal DefensiveInterceptionPoints { get; set; }
    public decimal BonusPoints { get; set; }
    public string InjuryStatus { get; set; }
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
    public DateTime? MondaySnapshotAtUtc { get; set; }
    public Dictionary<int, decimal> MondayScores { get; set; } = [];
    public Dictionary<string, decimal> LockedProjections { get; set; } = [];
    public Dictionary<string, string> TuesdayInjuryStatuses { get; set; } = [];
    public List<TeamWeekDocument> Teams { get; set; } = [];
}

public class TeamWeekDocument
{
    public int RosterId { get; set; }
    public DateTime? DrawnAtUtc { get; set; }
    public string DiscardedCopyId { get; set; }
    public DateTime? DiscardedAtUtc { get; set; }
    public List<DealtCardDocument> Hand { get; set; } = [];
    public List<CardSelectionDocument> Selections { get; set; } = [];
    public List<string> MiniBattlePlayerIds { get; set; } = [];
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
    public string CancelledCopyId { get; set; }
    public DateTime SelectedAtUtc { get; set; }
}
