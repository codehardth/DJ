using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

namespace Codehard.DJ;

public static class Bootstrap
{
    public static async Task<SpotifyClient> InitializeSpotifyClientAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        var callbackUri = new Uri("http://localhost:8800/callback");

        var server = new EmbedIOAuthServer(callbackUri, 8800);

        await server.Start();

        SpotifyClient? client = default;

        server.AuthorizationCodeReceived += async (sender, response) =>
        {
            var server = (EmbedIOAuthServer)sender;

            await server.Stop();

            var tokenResponse = await new OAuthClient()
                .RequestToken(
                    new AuthorizationCodeTokenRequest(clientId, clientSecret, response.Code, callbackUri),
                    cancellationToken);

            var config = SpotifyClientConfig
                .CreateDefault()
                .WithAuthenticator(new AuthorizationCodeAuthenticator(clientId, clientSecret, tokenResponse));

            client = new SpotifyClient(config);
        };

        server.ErrorReceived += async (sender, error, state) =>
        {
            Console.WriteLine($"Aborting authorization, error received: {error}");

            var server = (EmbedIOAuthServer)sender;

            await server.Stop();
        };

        var request = new LoginRequest(
            server.BaseUri,
            clientId,
            LoginRequest.ResponseType.Code)
        {
            Scope = new List<string>
            {
                Scopes.UserReadCurrentlyPlaying,
                Scopes.UserModifyPlaybackState,
                Scopes.UserReadPlaybackPosition,
                Scopes.UserReadPlaybackState,
                Scopes.AppRemoteControl,
            },
        };

        BrowserUtil.Open(request.ToUri());

        while (true)
        {
            try
            {
                if (client != null)
                {
                    return client;
                }

                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Spotify authorization has been cancelled.");

                throw;
            }
        }
    }

    public static async Task ApplyMigrationsAsync<T>(this IHost host, CancellationToken cancellationToken = default)
        where T : DbContext
    {
        using var scope = host.Services.CreateScope();

        await using var dbContext = scope.ServiceProvider.GetService<T>();

        if (dbContext == null)
        {
            throw new InvalidOperationException($"Unable to resolve '{typeof(T)}' from host.");
        }

        var migrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);

        if (migrations.Any())
        {
            await dbContext.Database.MigrateAsync(CancellationToken.None);
        }

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}