using Codehard.DJ.Providers;
using Microsoft.Extensions.Hosting;

namespace Codehard.DJ;

public class TestingHostApp : IHostedService
{
    private readonly IMusicProvider _provider;

    public TestingHostApp(IMusicProvider provider)
    {
        this._provider = provider;
        this._provider.PlayStartEvent += (sender, args) => { Console.WriteLine($"{args.Music.Name} started"); };
        this._provider.PlayEndEvent += (sender, args) => { Console.WriteLine($"{args.Music.Name} ended"); };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.Write("Search keyword: ");

            var keyword = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            var searchResult = await this._provider.SearchAsync(keyword, cancellationToken);

            var music = searchResult.FirstOrDefault();

            if (music == null)
            {
                continue;
            }

            await this._provider.EnqueueAsync(music, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        this._provider.Dispose();

        return Task.CompletedTask;
    }
}