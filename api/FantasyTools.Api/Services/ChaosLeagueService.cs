using FantasyTools.Api.Documents;
using FantasyTools.Api.Models;
using System.Collections.Concurrent;

namespace FantasyTools.Api.Services;

public class ChaosLeagueService(IFileService fileService) : IChaosLeagueService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public async Task<ChaosLeagueDocument> GetCurrent(string userId)
    {
        var index = await fileService.Retrieve(new UserChaosLeagueDocument { UserId = userId });
        if (index == null) throw new KeyNotFoundException("No Chaos Cards league has been created for this account.");
        return await fileService.Retrieve(new ChaosLeagueDocument { LeagueId = index.LeagueId })
            ?? throw new KeyNotFoundException("The saved Chaos Cards league could not be found.");
    }

    public async Task<ChaosLeagueDocument> Create(string userId, CreateChaosLeagueRequest request)
    {
        var sleeperLeagueId = request?.SleeperLeagueId?.Trim();
        if (string.IsNullOrWhiteSpace(sleeperLeagueId) || !sleeperLeagueId.All(char.IsDigit))
            throw new ArgumentException("Enter a valid numeric Sleeper league ID.");

        var gate = Locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var existing = await fileService.Retrieve(new UserChaosLeagueDocument { UserId = userId });
            if (existing != null) return await GetCurrent(userId);

            var leagueId = $"chaos-{Guid.NewGuid():N}";
            var now = DateTime.UtcNow;
            var league = new ChaosLeagueDocument
            {
                LeagueId = leagueId,
                SleeperLeagueId = sleeperLeagueId,
                Name = string.IsNullOrWhiteSpace(request.Name) ? "Chaos Cards League" : request.Name.Trim(),
                PrimaryCommissionerUserId = userId,
                CreatedAt = now,
                At = now
            };

            await fileService.Upsert(league);
            await fileService.Upsert(new UserChaosLeagueDocument { UserId = userId, LeagueId = leagueId, At = now });
            await fileService.Upsert(new LeagueRosterDocument { LeagueId = leagueId, PrimaryCommissionerUserId = userId, At = now });
            await fileService.Upsert(new CardWorkspaceDocument { LeagueId = leagueId, PrimaryCommissionerUserId = userId, At = now });
            await fileService.Upsert(new LeagueGameDocument { LeagueId = leagueId, SleeperLeagueId = sleeperLeagueId, At = now });
            return league;
        }
        finally { gate.Release(); }
    }

    public async Task<ChaosLeagueDocument> GetInvite(string leagueId) => await fileService.Retrieve(new ChaosLeagueDocument { LeagueId=leagueId }) ?? throw new KeyNotFoundException("Invitation not found.");
    public async Task Join(string userId, string leagueId) { await GetInvite(leagueId); await fileService.Upsert(new UserChaosLeagueDocument { UserId=userId, LeagueId=leagueId, At=DateTime.UtcNow }); }
}
