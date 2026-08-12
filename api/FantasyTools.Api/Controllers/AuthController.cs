using FantasyTools.Api.Models;
using FantasyTools.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FantasyTools.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IAuthService authService,
    ITurnstileService turnstileService,
    ILogger<AuthController> logger
    ) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult> Register([FromBody] RegisterRequestModel request)
    {
        if (!await VerifyCaptcha(request?.TurnstileToken))
        {
            return BadRequest("Captcha verification failed. Please try again.");
        }

        try
        {
            await authService.Register(request);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (EmailDeliveryException ex)
        {
            // The account exists by this point -- the document is written before the send and there is
            // no transaction to undo it. Saying so is better than a 500: retrying the same form is the
            // documented recovery, and Register re-sends for an unverified account rather than
            // reporting a duplicate.
            logger.LogError(ex, "Registration for {Email} could not send its verification email.", request?.Email);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "We could not send your verification email just now. Please try again in a moment.");
        }

        // No session yet -- the account is unusable until the emailed link is followed.
        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseModel>> Login([FromBody] LoginRequestModel request)
    {
        if (!await VerifyCaptcha(request?.TurnstileToken))
        {
            return BadRequest("Captcha verification failed. Please try again.");
        }

        var (outcome, response) = await authService.Login(request);

        return outcome switch
        {
            LoginOutcome.Success => Ok(response),

            // Distinct from the 401 below: the UI keys off this to offer a resend.
            LoginOutcome.EmailNotVerified => StatusCode(403, "Please verify your email before signing in."),

            // Deliberately the same answer for "no such account" and "wrong password".
            _ => Unauthorized("Invalid email or password.")
        };
    }

    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<ActionResult> Verify([FromBody] VerifyRequestModel request)
    {
        // Same 400 for an unknown account, a wrong token, and an expired one -- the token is
        // unguessable, so a failure here reveals nothing about whether the address is registered.
        return await authService.Verify(request?.Email, request?.Token)
            ? NoContent()
            : BadRequest("That verification link is invalid or has expired.");
    }

    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<ActionResult> ResendVerification([FromBody] ResendVerificationRequestModel request)
    {
        if (!await VerifyCaptcha(request?.TurnstileToken))
        {
            return BadRequest("Captcha verification failed. Please try again.");
        }

        try
        {
            await authService.ResendVerification(request?.Email);
        }
        catch (EmailDeliveryException ex)
        {
            // Deliberately NOT the 503 that register returns. A send is only attempted here for an
            // address that has an unverified account, so a distinct answer on failure would say that
            // the account exists -- exactly what the unconditional 204 below exists to hide. The
            // failure goes to the log, where it cannot be read by whoever is probing.
            logger.LogError(ex, "A verification resend failed.");
        }

        // Always 204, so this is not an oracle for which addresses are registered.
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponseModel>> Me()
    {
        var user = await authService.GetByEmail(User.FindFirstValue(ClaimTypes.Email));

        return user == null ? Unauthorized() : Ok(user);
    }

    private async Task<bool> VerifyCaptcha(string token) =>
        await turnstileService.Verify(token, HttpContext.Connection.RemoteIpAddress?.ToString());
}
