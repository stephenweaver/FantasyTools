using FantasyTools.Api.Models;
using FantasyTools.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FantasyTools.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/chaos-leagues")]
public class ChaosLeagueController(IChaosLeagueService service) : ControllerBase
{
    [HttpGet("current")]
    public async Task<ActionResult> Current() => await Run(() => service.GetCurrent(UserId));

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateChaosLeagueRequest request) => await Run(() => service.Create(UserId, request));

    [HttpGet("invite/{leagueId}")]
    public async Task<ActionResult> Invite(string leagueId) => await Run(() => service.GetInvite(leagueId));

    [HttpPost("invite/{leagueId}/join")]
    public async Task<ActionResult> Join(string leagueId) => await Run(async () => { await service.Join(UserId,leagueId); return new { joined=true }; });

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<ActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }
}
