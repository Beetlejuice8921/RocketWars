using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RocketWars.Core;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Services.Relay.Models;

namespace RocketWars.Net
{
    /// <summary>
    /// Internet-комнаты через Unity Multiplayer Services (Sessions = Lobby + Relay).
    /// Сессия сама запускает NetworkManager как хост/клиент через Relay.
    /// Требует, чтобы проект был привязан к Unity Cloud (Edit → Project Settings → Services).
    /// </summary>
    public static class SessionService
    {
        /// <summary>Свойство комнаты с меткой NetProtocol.Tag (индексируется, чтобы фильтровать поиск).</summary>
        const string TagProperty = "rwTag";

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
            var options = new SessionOptions
            {
                Name = name,
                MaxPlayers = maxPlayers,
                IsPrivate = false,
                SessionProperties =
                {
                    [TagProperty] = new SessionProperty(NetProtocol.Tag, VisibilityPropertyOptions.Public, PropertyIndex.String1)
                }
            }.WithRelayNetwork().WithNetworkOptions(NetworkSettings());
            Current = await MultiplayerService.Instance.CreateSessionAsync(options);
            return Current;
        }

        public static async Task<ISession> JoinByCodeAsync(string code)
        {
            await EnsureSignedInAsync();
            Current = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(), JoinOptions());
            return Current;
        }

        public static async Task<ISession> JoinByIdAsync(string id)
        {
            await EnsureSignedInAsync();
            Current = await MultiplayerService.Instance.JoinSessionByIdAsync(id, JoinOptions());
            return Current;
        }

        public static async Task<IList<ISessionInfo>> QueryAsync()
        {
            await EnsureSignedInAsync();
            // только комнаты нашей версии и нашей транспортной группы (pc / web)
            var query = new QuerySessionsOptions
            {
                FilterOptions = { new FilterOption(FilterField.StringIndex1, NetProtocol.Tag, FilterOperation.Equal) }
            };
            var results = await MultiplayerService.Instance.QuerySessionsAsync(query);
            return results.Sessions;
        }

        /// <summary>Протокол Relay: в браузере только WebSocket (wss), на ПК — DTLS, как было по умолчанию.</summary>
        static NetworkOptions NetworkSettings() => new NetworkOptions
        {
            RelayProtocol = GamePlatform.Current.IsWeb ? RelayProtocol.WSS : RelayProtocol.DTLS
        };

        static JoinSessionOptions JoinOptions() => new JoinSessionOptions().WithNetworkOptions(NetworkSettings());

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
                return Loc.T("Internet недоступен: привяжите проект к Unity Cloud (Project Settings → Services)");
            // неверный код или комната уже закрылась
            if (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return Loc.T("Комната не найдена");
            return Loc.F("Ошибка сети: {0}", e.Message);
        }
    }
}
