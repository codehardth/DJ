// See https://aka.ms/new-console-template for more information

using Codehard.DJ;
using Codehard.DJ.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureAppConfiguration(config =>
{
    config.AddJsonFile("appsettings.json", false);
    config.AddEnvironmentVariables();
});

builder.ConfigureHostConfiguration(config =>
    config.AddEnvironmentVariables());

builder.ConfigureServices((context, services) =>
{
    var configuration = context.Configuration;

    var configSection = configuration.GetSection("Configurations");

    var spotifyConfig = configSection.GetSection("Spotify");

    var clientId = spotifyConfig["ClientId"];
    var clientSecret = spotifyConfig["ClientSecret"];
    var cancelToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    var spotifyClient = Bootstrap.InitializeSpotifyClientAsync(clientId!, clientSecret!, cancelToken).Result;

    services.TryAddSingleton(spotifyClient);
    services.TryAddSingleton<IMusicProvider, SpotifyProvider>();

    var discordConfig = configSection.GetSection("Discord");

    var discordActive = discordConfig.GetValue<bool>("Active");

    if (discordActive)
    {
        services.TryAddSingleton(sp =>
            new DjDiscordClient(
                discordConfig["Token"]!,
                sp,
                sp.GetRequiredService<ILogger<DjDiscordClient>>(),
                sp.GetRequiredService<IMusicProvider>()));

        services.AddHostedService<DiscordBotHostingService>();
    }
});

builder.UseConsoleLifetime();

var app = builder.Build();

await app.RunAsync();