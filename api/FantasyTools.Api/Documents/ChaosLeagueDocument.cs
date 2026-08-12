namespace FantasyTools.Api.Documents;

public class ChaosLeagueDocument : BaseDocument
{
    public override string Id { get => LeagueId; set { } }
    public override string Pk { get => "chaos-leagues"; set { } }
    public string LeagueId { get; set; }
    public string SleeperLeagueId { get; set; }
    public string Name { get; set; }
    public string PrimaryCommissionerUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserChaosLeagueDocument : BaseDocument
{
    public override string Id { get => UserId; set { } }
    public override string Pk { get => "user-chaos-leagues"; set { } }
    public string UserId { get; set; }
    public string LeagueId { get; set; }
}
