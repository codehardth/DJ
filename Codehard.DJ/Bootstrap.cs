using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

namespace Codehard.DJ;

public static class Bootstrap
{
    public static async Task<SpotifyClient> InitializeSpotifyClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var server = new EmbedIOAuthServer(new Uri("http://localhost:8800/callback"), 8800);

        await server.Start();

        string? accessToken = default;

        server.ImplictGrantReceived += async (sender, response) =>
        {
            var server = (EmbedIOAuthServer)sender;

            await server.Stop();

            accessToken = response.AccessToken;
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
            LoginRequest.ResponseType.Token)
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
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    return new SpotifyClient(accessToken);
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
}