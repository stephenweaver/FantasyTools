using FantasyTools.Api.Models;
using FantasyTools.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FantasyTools.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/leagues/{leagueId}/rosters")]
public class LeagueRosterController(ILeagueRosterService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(string leagueId) => await Run(() => service.GetOrCreate(leagueId, UserId));

    [HttpPut("{rosterId:int}")]
    public async Task<ActionResult> Assign(string leagueId, int rosterId, [FromBody] SaveRosterAssignmentRequest request)
    {
        request.RosterId = rosterId;
        return await Run(() => service.Assign(leagueId, UserId, request));
    }

    [HttpDelete("{rosterId:int}")]
    public async Task<ActionResult> Remove(string leagueId, int rosterId) => await Run(async () => { await service.Remove(leagueId, UserId, rosterId); return new { removed = true }; });

    [HttpPost("claims")]
    public async Task<ActionResult> Claim(string leagueId, [FromBody] CreateRosterClaimRequest request) => await Run(() => service.Claim(leagueId, UserId, User.FindFirstValue(ClaimTypes.Email), User.FindFirstValue(ClaimTypes.Name), request));

    [HttpGet("claims/mine")]
    public async Task<ActionResult> MyClaim(string leagueId) => await Run(() => service.GetMyClaim(leagueId, UserId));

    [HttpPost("claims/{claimId}")]
    public async Task<ActionResult> Review(string leagueId, string claimId, [FromQuery] bool approve) => await Run(() => service.ReviewClaim(leagueId, UserId, claimId, approve));

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<ActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }
}
