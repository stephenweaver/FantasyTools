namespace FantasyTools.Api.Documents;

public class LeagueRosterDocument : BaseDocument
{
    public override string Id { get => LeagueId; set { } }
    public override string Pk { get => "chaos-league-rosters"; set { } }
    public string LeagueId { get; set; }
    public string PrimaryCommissionerUserId { get; set; }
    public List<LeagueRosterAssignmentDocument> Assignments { get; set; } = [];
}

public class LeagueRosterAssignmentDocument
{
    public int RosterId { get; set; }
    public string SleeperUserId { get; set; }
    public string SleeperManagerName { get; set; }
    public string SleeperTeamName { get; set; }
    public string FantasyToolsUserId { get; set; }
    public string FantasyToolsEmail { get; set; }
    public string FantasyToolsName { get; set; }
    public DateTime AssignedAt { get; set; }
    public string AssignedByUserId { get; set; }
}
