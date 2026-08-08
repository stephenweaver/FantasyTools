using DotNetEnv;

namespace FantasyTools.Api;

public class Program
{
    public static void Main(string[] args)
    {
#if DEBUG
        // Walks up from the bin folder to the repo root and loads .env.
        // NoClobber so a real environment variable beats the file -- that is the precedence you want,
        // and it lets the e2e suite run against Turnstile's test keys without editing .env.
        Env.TraversePath().NoClobber().Load();
#endif

        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
}
