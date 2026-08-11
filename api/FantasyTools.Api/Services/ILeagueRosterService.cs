using FantasyTools.Api.Documents;
using FantasyTools.Api.Models;

namespace FantasyTools.Api.Services;

public interface ILeagueRosterService
{
    Task<LeagueRosterDocument> GetOrCreate(string leagueId, string userId);
    Task<LeagueRosterAssignmentDocument> Assign(string leagueId, string actorUserId, SaveRosterAssignmentRequest request);
    Task Remove(string leagueId, string actorUserId, int rosterId);
}
