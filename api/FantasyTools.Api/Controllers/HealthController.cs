using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FantasyTools.Api.Controllers;

/// <summary>
/// Liveness target for the container healthcheck and Traefik.
/// </summary>
/// <remarks>
/// Deliberately checks nothing downstream. A readiness probe that reaches R2 or MailerSend turns a
/// third-party blip into Docker killing a process that is serving requests perfectly well.
/// </remarks>
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public ActionResult Get() => Ok(new { status = "ok" });
}
