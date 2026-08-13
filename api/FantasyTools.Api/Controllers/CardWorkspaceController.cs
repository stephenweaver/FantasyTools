using FantasyTools.Api.Models;
using FantasyTools.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FantasyTools.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/leagues/{leagueId}/cards")]
public class CardWorkspaceController(ICardWorkspaceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(string leagueId) => await Run(() => service.Get(leagueId, UserId));

    [HttpPost]
    public async Task<ActionResult> Create(string leagueId, [FromBody] SaveCardDraftRequest request) => await Run(() => service.Create(leagueId, UserId, UserName, request));

    [HttpPut("{cardId}")]
    public async Task<ActionResult> Update(string leagueId, string cardId, [FromBody] SaveCardDraftRequest request) => await Run(() => service.Update(leagueId, cardId, UserId, UserName, request));

    [HttpPost("{cardId}/status")]
    public async Task<ActionResult> ChangeStatus(string leagueId, string cardId, [FromBody] ChangeCardStatusRequest request) => await Run(() => service.ChangeStatus(leagueId, cardId, UserId, UserName, request.Status));

    [HttpPut("collaborators")]
    public async Task<ActionResult> SetCollaborator(string leagueId, [FromBody] ChangeCardCollaboratorRequest request) => await Run(async () => { await service.SetCollaborator(leagueId, UserId, request); return new { saved = true }; });

    [HttpPut("weekly/{week:int}")]
    public async Task<ActionResult> SaveWeekly(string leagueId, int week, [FromBody] SaveWeeklyCardRequest request)
    {
        request.Week = week;
        return await Run(() => service.SaveWeeklyCard(leagueId, UserId, UserName, request));
    }

    [HttpDelete("weekly/{week:int}")]
    public async Task<ActionResult> DeleteWeekly(string leagueId, int week) => await Run(async () => { await service.DeleteWeeklyCard(leagueId, week, UserId); return new { deleted = true }; });

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private string UserName => User.FindFirstValue(ClaimTypes.Name) ?? "Commissioner";

    private async Task<ActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }
}
