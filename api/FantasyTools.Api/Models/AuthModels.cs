namespace FantasyTools.Api.Models;

public class RegisterRequestModel
{
    public string Email { get; set; }
    public string Name { get; set; }
    public string Password { get; set; }
    public string TurnstileToken { get; set; }
}

public class LoginRequestModel
{
    public string Email { get; set; }
    public string Password { get; set; }
    public string TurnstileToken { get; set; }
}

public class VerifyRequestModel
{
    public string Email { get; set; }
    public string Token { get; set; }
}

public class ResendVerificationRequestModel
{
    public string Email { get; set; }
    public string TurnstileToken { get; set; }
}

public class UserResponseModel
{
    public string UserId { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public bool EmailVerified { get; set; }
}

public class AuthResponseModel
{
    public string Token { get; set; }
    public UserResponseModel User { get; set; }
}

public class ConfigResponseModel
{
    public bool CaptchaEnabled { get; set; }
    public string TurnstileSiteKey { get; set; }
}

public class VersionResponseModel
{
    public string GitSha { get; set; }
}
