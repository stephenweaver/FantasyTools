namespace FantasyTools.Api.Models;

public class SaveRosterAssignmentRequest
{
    public int RosterId { get; set; }
    public string SleeperUserId { get; set; }
    public string SleeperManagerName { get; set; }
    public string SleeperTeamName { get; set; }
    public string FantasyToolsEmail { get; set; }
}
