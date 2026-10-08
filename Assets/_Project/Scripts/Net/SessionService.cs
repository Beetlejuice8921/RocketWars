using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace RocketWars.Net
{
    /// <summary>
    /// Internet-комнаты через Unity Multiplayer Services (Sessions = Lobby + Relay).
    /// Сессия сама запускает NetworkManager как хост/клиент через Relay.
    /// Требует, чтобы проект был привязан к Unity Cloud (Edit → Project Settings → Services).
    /// </summary>
    public static class SessionService
    {
        public static ISession Current { get; private set; }

        static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // отдельный профиль на процесс — несколько копий игры на одном ПК не конфликтуют
                var options = new InitializationOptions().SetProfile($"p{Guid.NewGuid().ToString("N").Substring(0, 12)}");
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public static async Task<ISession> CreateAsync(string name, int maxPlayers)
        {
            await EnsureSignedInAsync();
            var options = new SessionOptions { Name = name, MaxPlayers = maxPlayers, IsPrivate = false }.WithRelayNetwork();
            Current = await MultiplayerService.Instance.CreateSessionAsync(options);
            return Current;
        }

        public static async Task<ISession> JoinByCodeAsync(string code)
        {
            await EnsureSignedInAsync();
            Current = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
            return Current;
        }

        public static async Task<ISession> JoinByIdAsync(string id)
        {
            await EnsureSignedInAsync();
            Current = await MultiplayerService.Instance.JoinSessionByIdAsync(id);
            return Current;
        }

        public static async Task<IList<ISessionInfo>> QueryAsync()
        {
            await EnsureSignedInAsync();
            var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
            return results.Sessions;
        }

        public static async Task LeaveAsync()
        {
            var session = Current;
            Current = null;
            if (session != null)
                await session.LeaveAsync();
        }

        public static string Describe(Exception e)
        {
            if (e is ServicesInitializationException || e.Message.Contains("project", StringComparison.OrdinalIgnoreCase))
                return "Internet недоступен: привяжите проект к Unity Cloud (Project Settings → Services)";
            return $"Ошибка сети: {e.Message}";
        }
    }
}
