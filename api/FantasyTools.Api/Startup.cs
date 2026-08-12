using FantasyTools.Api.Documents;
using FantasyTools.Api.HttpClients;
using FantasyTools.Api.Game.Engine;
using FantasyTools.Api.Game.Rules;
using FantasyTools.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.FileProviders;
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

        // Card artwork. R2 vs local disk is decided by IMAGE_SERVICE / IMAGES_* the same way the
        // document store is decided by FILE_SERVICE / R2_*.
        services.AddSingleton<IImageStorageService, ImageStorageService>();

        services.AddHttpClient<IResendHttpClient, ResendHttpClient>();
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

        // Resolved here rather than on the first upload: a missing IMAGES_* variable should stop the
        // process at boot, not surface as a 500 the first time somebody saves a card.
        var images = app.ApplicationServices.GetRequiredService<IImageStorageService>();

        // Local artwork is served off disk by the static file middleware rather than read back through
        // a controller -- it brings ETag, If-None-Match and range handling with it, and it is the piece
        // that already knows how to refuse a path that escapes its root. Mounted under /api so the Vite
        // proxy in dev and the nginx /api proxy in prod both reach it with no extra configuration.
        // In R2 mode there is nothing to mount: the images host serves those objects directly.
        if (images.LocalRoot != null)
        {
            Directory.CreateDirectory(images.LocalRoot);

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(images.LocalRoot),
                RequestPath = "/api/images",
                // Every name is a GUID, so a URL's content can never change.
                OnPrepareResponse = context =>
                    context.Context.Response.Headers.CacheControl = "public, immutable, max-age=31536000"
            });
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
