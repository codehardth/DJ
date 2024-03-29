﻿// See https://aka.ms/new-console-template for more information

using Codehard.DJ;
using Codehard.DJ.Providers;
using Codehard.DJ.Providers.Spotify;
using DJ.Domain.Interfaces;
using DJ.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Models;

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

    var spotifyClient = Bootstrap.InitializeSpotifyClientAsync(clientId!, clientSecret!).Result;

    services.TryAddSingleton(spotifyClient);
    services.TryAddSingleton<IMusicProvider, SpotifyProvider>();

    var chatGptConfig = configSection.GetSection("ChatGPT");
    var api = new OpenAIClient(new OpenAIAuthentication(chatGptConfig["AccessToken"]), Model.GPT3_5_Turbo);
    services.TryAddSingleton(api);

    var discordConfig = configSection.GetSection("Discord");
    var discordActive = discordConfig.GetValue<bool>("Active");

    if (discordActive)
    {
        services.TryAddSingleton(sp =>
            new DjDiscordClient(
                discordConfig["Token"]!,
                sp,
                sp.GetRequiredService<IMemberRepository>(),
                sp.GetRequiredService<ILogger<DjDiscordClient>>(),
                sp.GetRequiredService<IMusicProvider>()));

        services.AddHostedService<DiscordBotHostingService>();
    }

    services.AddDjDbContext(configuration.GetConnectionString("DjDatabase")!);
    services.AddRepositories();
});

builder.UseConsoleLifetime();

var app = builder.Build();

await app.ApplyMigrationsAsync<DjDbContext>();

await app.RunAsync();