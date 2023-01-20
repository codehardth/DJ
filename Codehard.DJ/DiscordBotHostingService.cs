using Codehard.DJ.Providers;
using Microsoft.Extensions.Hosting;

namespace Codehard.DJ;

public class DiscordBotHostingService : IHostedService
{
    private readonly DjDiscordClient _discordClient;
    private readonly IMusicProvider _provider;

    public DiscordBotHostingService(
        DjDiscordClient discordClient,
        IMusicProvider provider)
    {
        _discordClient = discordClient;
        this._provider = provider;
        this._provider.PlayStartEvent += (sender, args) => { Console.WriteLine($"{args.Music.Title} started"); };
        this._provider.PlayEndEvent += (sender, args) => { Console.WriteLine($"{args.Music.Title} ended"); };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await this._discordClient.ConnectAsync(cancellationToken);

        await Task.Delay(-1, CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        this._provider.Dispose();

        await this._discordClient.DisposeAsync();
    }
}