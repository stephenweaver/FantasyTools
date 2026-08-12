using FantasyTools.Api.Documents;
using FantasyTools.Api.Models;
using System.Collections.Concurrent;

namespace FantasyTools.Api.Services;

public class LeagueRosterService(IFileService fileService) : ILeagueRosterService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public async Task<LeagueRosterDocument> GetOrCreate(string leagueId, string userId)
    {
        if (string.IsNullOrWhiteSpace(leagueId)) throw new ArgumentException("A league ID is required.");
        var gate = Locks.GetOrAdd(leagueId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var document = await fileService.Retrieve(new LeagueRosterDocument { LeagueId = leagueId });
            if (document == null)
            {
                var cardWorkspace = await fileService.Retrieve(new CardWorkspaceDocument { LeagueId = leagueId });
                var primaryCommissioner = cardWorkspace?.PrimaryCommissionerUserId ?? userId;
                if (primaryCommissioner != userId) throw new UnauthorizedAccessException("Only the league's primary commissioner can start roster setup.");
                document = new LeagueRosterDocument { LeagueId = leagueId, PrimaryCommissionerUserId = primaryCommissioner, At = DateTime.UtcNow };
                await fileService.Upsert(document);
            }
            EnsureMember(document, userId);
            return document;
        }
        finally { gate.Release(); }
    }

    public async Task<LeagueRosterAssignmentDocument> Assign(string leagueId, string actorUserId, SaveRosterAssignmentRequest request)
    {
        if (request == null || request.RosterId <= 0 || string.IsNullOrWhiteSpace(request.SleeperUserId)) throw new ArgumentException("A valid Sleeper roster is required.");
        var email = UserDocument.Normalize(request.FantasyToolsEmail);
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Enter the player's FantasyTools email address.");
        var user = await fileService.Retrieve(new UserDocument { Email = email }) ?? throw new KeyNotFoundException("No verified FantasyTools account was found for that email.");
        if (!user.EmailVerified) throw new InvalidOperationException("That player must verify their email before being assigned to a roster.");

        var gate = Locks.GetOrAdd(leagueId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var document = await Load(leagueId);
            EnsurePrimary(document, actorUserId);
            document.Assignments.RemoveAll(item => item.RosterId == request.RosterId || item.FantasyToolsUserId == user.UserId);
            var assignment = new LeagueRosterAssignmentDocument
            {
                RosterId = request.RosterId,
                SleeperUserId = request.SleeperUserId.Trim(),
                SleeperManagerName = request.SleeperManagerName?.Trim() ?? "",
                SleeperTeamName = request.SleeperTeamName?.Trim() ?? "",
                FantasyToolsUserId = user.UserId,
                FantasyToolsEmail = user.Email,
                FantasyToolsName = user.Name,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = actorUserId
            };
            document.Assignments.Add(assignment);
            document.At = DateTime.UtcNow;
            await fileService.Upsert(document);
            return assignment;
        }
        finally { gate.Release(); }
    }

    public async Task Remove(string leagueId, string actorUserId, int rosterId)
    {
        var gate = Locks.GetOrAdd(leagueId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var document = await Load(leagueId);
            EnsurePrimary(document, actorUserId);
            document.Assignments.RemoveAll(item => item.RosterId == rosterId);
            document.At = DateTime.UtcNow;
            await fileService.Upsert(document);
        }
        finally { gate.Release(); }
    }

    public async Task<LeagueRosterClaimDocument> Claim(string leagueId, string userId, string email, string name, CreateRosterClaimRequest request)
    {
        if (request == null || request.RosterId <= 0 || string.IsNullOrWhiteSpace(request.SleeperUserId)) throw new ArgumentException("Choose a valid Sleeper roster.");
        var gate = Locks.GetOrAdd(leagueId, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync();
        try
        {
            var document = await Load(leagueId);
            if (document.Assignments.Any(x => x.RosterId == request.RosterId)) throw new InvalidOperationException("That roster is already claimed.");
            if (document.Assignments.Any(x => x.FantasyToolsUserId == userId)) throw new InvalidOperationException("Your account is already connected to a roster.");
            document.Claims.RemoveAll(x => x.FantasyToolsUserId == userId || x.RosterId == request.RosterId && x.Status == "PENDING");
            var claim = new LeagueRosterClaimDocument { Id=Guid.NewGuid().ToString("N"), RosterId=request.RosterId, SleeperUserId=request.SleeperUserId.Trim(), SleeperManagerName=request.SleeperManagerName?.Trim()??"", SleeperTeamName=request.SleeperTeamName?.Trim()??"", FantasyToolsUserId=userId, FantasyToolsEmail=email, FantasyToolsName=name, Status=document.PrimaryCommissionerUserId==userId?"APPROVED":"PENDING", RequestedAt=DateTime.UtcNow };
            document.Claims.Add(claim);
            if (claim.Status == "APPROVED") AddAssignment(document, claim, userId);
            document.At=DateTime.UtcNow; await fileService.Upsert(document); return claim;
        }
        finally { gate.Release(); }
    }

    public async Task<LeagueRosterDocument> ReviewClaim(string leagueId, string actorUserId, string claimId, bool approve)
    {
        var gate=Locks.GetOrAdd(leagueId,_=>new SemaphoreSlim(1,1)); await gate.WaitAsync();
        try { var document=await Load(leagueId); EnsurePrimary(document,actorUserId); var claim=document.Claims.SingleOrDefault(x=>x.Id==claimId)??throw new KeyNotFoundException("Roster claim not found."); if(claim.Status!="PENDING") throw new InvalidOperationException("That claim has already been reviewed."); claim.Status=approve?"APPROVED":"REJECTED"; if(approve){ if(document.Assignments.Any(x=>x.RosterId==claim.RosterId)) throw new InvalidOperationException("That roster is already connected."); AddAssignment(document,claim,actorUserId); } document.At=DateTime.UtcNow; await fileService.Upsert(document); return document; }
        finally { gate.Release(); }
    }

    private static void AddAssignment(LeagueRosterDocument document, LeagueRosterClaimDocument claim, string actorUserId) => document.Assignments.Add(new LeagueRosterAssignmentDocument { RosterId=claim.RosterId,SleeperUserId=claim.SleeperUserId,SleeperManagerName=claim.SleeperManagerName,SleeperTeamName=claim.SleeperTeamName,FantasyToolsUserId=claim.FantasyToolsUserId,FantasyToolsEmail=claim.FantasyToolsEmail,FantasyToolsName=claim.FantasyToolsName,AssignedAt=DateTime.UtcNow,AssignedByUserId=actorUserId });

    private async Task<LeagueRosterDocument> Load(string leagueId) =>
        await fileService.Retrieve(new LeagueRosterDocument { LeagueId = leagueId }) ?? throw new KeyNotFoundException("League roster setup has not been started.");

    private static void EnsurePrimary(LeagueRosterDocument document, string userId)
    {
        if (document.PrimaryCommissionerUserId != userId) throw new UnauthorizedAccessException("Only the primary commissioner can assign FantasyTools accounts to Sleeper rosters.");
    }

    private static void EnsureMember(LeagueRosterDocument document, string userId)
    {
        if (document.PrimaryCommissionerUserId != userId && !document.Assignments.Any(item => item.FantasyToolsUserId == userId))
            throw new UnauthorizedAccessException("Your FantasyTools account has not been assigned to a roster in this league.");
    }
}
