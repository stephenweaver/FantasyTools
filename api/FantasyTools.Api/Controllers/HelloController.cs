using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FantasyTools.Api.Controllers;

[ApiController]
[Route("api/hello")]
public class HelloController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public ActionResult Get() => Ok(new { message = "Hello, world" });

    [HttpGet("secure")]
    [Authorize]
    public ActionResult Secure() => Ok(new { message = $"Hello, {User.FindFirstValue(ClaimTypes.Name)}" });
}
