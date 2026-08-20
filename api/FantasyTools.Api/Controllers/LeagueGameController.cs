using FantasyTools.Api.Models;
using FantasyTools.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FantasyTools.Api.Controllers;

[ApiController,Authorize,Route("api/leagues/{leagueId}/game")]
public class LeagueGameController(ILeagueGameService service):ControllerBase
{
    [HttpPost("sync/{week:int}")] public Task<ActionResult> Sync(string leagueId,int week)=>Run(()=>service.Sync(leagueId,UserId,week));
    [HttpGet("weeks/{week:int}")] public Task<ActionResult> Week(string leagueId,int week)=>Run(()=>service.GetWeek(leagueId,UserId,week));
    [HttpPost("weeks/{week:int}/deal")] public Task<ActionResult> Deal(string leagueId,int week)=>Run(()=>service.Deal(leagueId,UserId,week));
    [HttpPost("weeks/{week:int}/draw")] public Task<ActionResult> Draw(string leagueId,int week)=>Run(()=>service.Draw(leagueId,UserId,week));
    [HttpPut("weeks/{week:int}/deadline")] public Task<ActionResult> Deadline(string leagueId,int week,[FromBody]SetWeekDeadlineRequest request)=>Run(()=>service.SetDeadline(leagueId,UserId,week,request.DeadlineUtc));
    [HttpPost("weeks/{week:int}/selections")] public Task<ActionResult> Select(string leagueId,int week,[FromBody]SaveSelectionRequest request)=>Run(()=>service.Select(leagueId,UserId,week,request));
    [HttpDelete("weeks/{week:int}/selections/{copyId}")] public Task<ActionResult> Return(string leagueId,int week,string copyId)=>Run(()=>service.Return(leagueId,UserId,week,copyId));
    [HttpDelete("weeks/{week:int}/hand/{copyId}")] public Task<ActionResult> Discard(string leagueId,int week,string copyId)=>Run(()=>service.Discard(leagueId,UserId,week,copyId));
    [HttpPut("weeks/{week:int}/challenge/{copyId}")] public Task<ActionResult> Challenge(string leagueId,int week,string copyId,[FromBody]SetChallengeTargetRequest request)=>Run(()=>service.SetChallengeTarget(leagueId,UserId,week,copyId,request.CancelledCopyId));
    [HttpPut("weeks/{week:int}/mini-battle")] public Task<ActionResult> MiniBattle(string leagueId,int week,[FromBody]SetMiniBattlePlayersRequest request)=>Run(()=>service.SetMiniBattlePlayers(leagueId,UserId,week,request.PlayerIds));
    [HttpPost("weeks/{week:int}/reveal")] public Task<ActionResult> Reveal(string leagueId,int week)=>Run(()=>service.Reveal(leagueId,UserId,week));
    private string UserId=>User.FindFirstValue(ClaimTypes.NameIdentifier);
    private async Task<ActionResult> Run<T>(Func<Task<T>> action){try{return Ok(await action());}catch(ArgumentException e){return BadRequest(e.Message);}catch(KeyNotFoundException e){return NotFound(e.Message);}catch(UnauthorizedAccessException e){return Forbid();}catch(InvalidOperationException e){return Conflict(e.Message);}catch(HttpRequestException e){return StatusCode(502,$"Sleeper is temporarily unavailable: {e.Message}");}}
}
