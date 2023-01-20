// See https://aka.ms/new-console-template for more information

using Codehard.DJ;
using Codehard.DJ.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

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

    var spotifyConfig =
        configuration.GetSection("Configurations").GetSection("Spotify");

    var clientId = spotifyConfig["ClientId"];

    var spotifyClient = Bootstrap.InitializeSpotifyClientAsync(clientId!).Result;

    services.TryAddSingleton(spotifyClient);
    services.TryAddSingleton<IMusicProvider, SpotifyProvider>();

    services.AddHostedService<TestingHostApp>();
});

builder.UseConsoleLifetime();

var app = builder.Build();

await app.RunAsync();