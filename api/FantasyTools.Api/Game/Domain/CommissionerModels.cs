namespace FantasyTools.Api.Game.Domain;

public enum CommissionerPermission
{
    CreateCardDrafts,
    EditCardRules,
    ApproveCards,
    ManageDeck,
    InviteManagers,
    AssignRosters,
    ManageDeadlines,
    LockWeeks,
    CorrectScores,
    ViewPrivateHands,
    ManageCoCommissioners
}

public sealed record LeagueAccess(
    Guid LeagueId,
    string UserId,
    bool IsPrimaryCommissioner,
    IReadOnlySet<CommissionerPermission> Permissions);

public sealed record PermissionChangeRequest(
    Guid LeagueId,
    string ActorUserId,
    string TargetUserId,
    CommissionerPermission Permission,
    bool Grant);
