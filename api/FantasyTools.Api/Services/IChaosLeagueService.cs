using FantasyTools.Api.Documents;
using FantasyTools.Api.Models;

namespace FantasyTools.Api.Services;

public interface IChaosLeagueService
{
    Task<ChaosLeagueDocument> GetCurrent(string userId);
    Task<ChaosLeagueDocument> Create(string userId, CreateChaosLeagueRequest request);
    Task<ChaosLeagueDocument> GetInvite(string leagueId);
    Task Join(string userId, string leagueId);
}
