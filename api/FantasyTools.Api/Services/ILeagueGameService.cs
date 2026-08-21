using FantasyTools.Api.Documents;
using FantasyTools.Api.Models;

namespace FantasyTools.Api.Services;

public interface ILeagueGameService
{
    Task<LeagueGameDocument> Sync(string leagueId, string actorUserId, int week);
    Task<object> GetWeek(string leagueId, string userId, int week);
    Task<object> GetUsageReport(string leagueId, string userId);
    Task<object> Deal(string leagueId, string actorUserId, int week);
    Task<object> Draw(string leagueId, string userId, int week);
    Task<object> Select(string leagueId, string userId, int week, SaveSelectionRequest request);
    Task<object> Return(string leagueId, string userId, int week, string copyId);
    Task<object> Discard(string leagueId, string userId, int week, string copyId);
    Task<object> SetChallengeTarget(string leagueId,string userId,int week,string copyId,string cancelledCopyId);
    Task<object> SetMiniBattlePlayers(string leagueId,string userId,int week,IReadOnlyList<string> playerIds);
    Task<object> SetDeadline(string leagueId, string actorUserId, int week, DateTime deadlineUtc);
    Task<object> Reveal(string leagueId, string actorUserId, int week);
}
