using FantasyTools.Api.Game.Domain;

namespace FantasyTools.Api.Game.Rules;

public interface ICommissionerAuthorization
{
    RuleDecision Authorize(LeagueAccess access, CommissionerPermission required);
    RuleDecision AuthorizePermissionChange(LeagueAccess actor, LeagueAccess target, PermissionChangeRequest request);
}

public sealed class CommissionerAuthorization : ICommissionerAuthorization
{
    public RuleDecision Authorize(LeagueAccess access, CommissionerPermission required)
    {
        if (access.IsPrimaryCommissioner) return RuleDecision.Permit();
        return access.Permissions.Contains(required)
            ? RuleDecision.Permit()
            : RuleDecision.Reject("permission_denied", $"This account does not have {required} permission.");
    }

    public RuleDecision AuthorizePermissionChange(LeagueAccess actor, LeagueAccess target, PermissionChangeRequest request)
    {
        if (actor.LeagueId != request.LeagueId || target.LeagueId != request.LeagueId)
            return RuleDecision.Reject("wrong_league", "Both accounts must belong to this league.");
        if (!actor.IsPrimaryCommissioner && !actor.Permissions.Contains(CommissionerPermission.ManageCoCommissioners))
            return RuleDecision.Reject("permission_denied", "Only an authorized commissioner can manage co-commissioners.");
        if (target.IsPrimaryCommissioner)
            return RuleDecision.Reject("primary_protected", "Primary Commissioner access cannot be changed through co-commissioner permissions.");
        if (!actor.IsPrimaryCommissioner && request.Permission == CommissionerPermission.ManageCoCommissioners)
            return RuleDecision.Reject("cannot_delegate_admin", "Only the Primary Commissioner can grant permission-management access.");
        if (actor.UserId == target.UserId && !request.Grant)
            return RuleDecision.Reject("cannot_revoke_self", "A co-commissioner cannot revoke their own access.");
        return RuleDecision.Permit();
    }
}
