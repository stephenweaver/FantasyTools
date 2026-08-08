using FantasyTools.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FantasyTools.Api.Controllers;

/// <summary>
/// Which commit this container was built from.
/// </summary>
/// <remarks>
/// `GIT_SHA` is baked into the image as an ENV by `api/Dockerfile` from the build arg the workflow
/// passes. That is the point: the moving tags (`latest`, `beta-latest`) are reassigned on every
/// release, so the tag a container was pulled by does not identify the code inside it. This does.
///
/// Reports `unknown` outside a built image -- a local `dotnet run` has no GIT_SHA, and that is the
/// honest answer rather than shelling out to git for a working-copy sha that means something else.
/// </remarks>
[ApiController]
[Route("api/version")]
public class VersionController : ControllerBase
{
    // Image-level ENV: it cannot change while the process is alive, so read it once.
    private static readonly string GitSha = EnvironmentHelper.GetVar("GIT_SHA") ?? "unknown";

    [HttpGet]
    [AllowAnonymous]
    public ActionResult<VersionResponseModel> Get() => Ok(new VersionResponseModel
    {
        GitSha = GitSha
    });
}
