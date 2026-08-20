namespace FantasyTools.Api.Models;

public class SaveSelectionRequest
{
    public string CopyId { get; set; }
    public string TargetRosterId { get; set; }
    public string TargetPlayerId { get; set; }
    public string TargetSlot { get; set; }
}

public class SetWeekDeadlineRequest { public DateTime DeadlineUtc { get; set; } }
public class SetChallengeTargetRequest { public string CancelledCopyId { get; set; } }
public class SetMiniBattlePlayersRequest { public List<string> PlayerIds { get; set; } = []; }
