using FantasyTools.Api.Models;
using FantasyTools.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FantasyTools.Api.Controllers;

/// <summary>
/// Public front-end configuration. Keeps the root .env as the single source of truth so the web app
/// needs no build-time environment of its own.
/// </summary>
[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public ActionResult<ConfigResponseModel> Get() => Ok(new ConfigResponseModel
    {
        CaptchaEnabled = TurnstileService.IsEnabled,
        TurnstileSiteKey = TurnstileService.GetSiteKey()
    });
}
