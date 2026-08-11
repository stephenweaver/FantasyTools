using FantasyTools.Api.Documents;
using FantasyTools.Api.HttpClients;
using FantasyTools.Api.Game.Engine;
using FantasyTools.Api.Game.Rules;
using FantasyTools.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;

namespace FantasyTools.Api;

public class Startup(IConfiguration configuration)
{
    public const string DevCorsPolicy = "dev";

    public IConfiguration Configuration { get; } = configuration;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        // File/directory wrappers, gzip, R2 vs local disk -- all decided by FILE_SERVICE / R2_* env vars.
        services.RegisterStephenWeaverCommon();

        services.AddSingleton<IPasswordHasher<UserDocument>, PasswordHasher<UserDocument>>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<IChaosScoringEngine, ChaosScoringEngine>();
        services.AddSingleton<ICardPlayRules, CardPlayRules>();
        services.AddSingleton<ICardLifecycleRules, CardLifecycleRules>();
        services.AddSingleton<ICommissionerAuthorization, CommissionerAuthorization>();
        services.AddSingleton<ICardWorkspaceService, CardWorkspaceService>();
        services.AddSingleton<ILeagueRosterService, LeagueRosterService>();

        services.AddHttpClient<ITurnstileHttpClient, TurnstileHttpClient>();
        services.AddSingleton<ITurnstileService, TurnstileService>();

        // Fail at startup rather than silently letting every captcha through.
        if (TurnstileService.IsEnabled)
        {
            TurnstileService.GetSecretKey();
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = AuthService.Issuer,
                    ValidateAudience = true,
                    ValidAudience = AuthService.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = AuthService.GetSigningKey(),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization();

        services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()));
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        if (!TurnstileService.IsEnabled)
        {
            app.ApplicationServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<Startup>()
                .LogWarning("TURNSTILE_ENABLED=false -- captcha checks are DISABLED. Never run production like this.");
        }

        app.UseRouting();

        app.UseCors(DevCorsPolicy);

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}
