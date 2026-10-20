using RocketWars.Player;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Джингл конца матча (2D): своей команде — Sounds/Win, проигравшей — Sounds/Lose, зрителю без команды — ничего.
    /// Играет только на переходе в MatchEnd, увиденном вживую: зашедший в комнату уже после матча его не слышит.
    /// </summary>
    public class MatchJingle : MonoBehaviour
    {
        const float Volume = 0.8f;

        MatchPhase? _lastPhase; // null — менеджера матча нет или он ещё не заспавнен

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Application.isBatchMode)
                return;
            var go = new GameObject("MatchJingle");
            DontDestroyOnLoad(go);
            go.AddComponent<MatchJingle>();
        }

        void Update()
        {
            var match = MatchManager.Instance;
            if (match == null || !match.IsSpawned)
            {
                _lastPhase = null;
                return;
            }

            var s = match.State;
            if (s.Phase == MatchPhase.MatchEnd && _lastPhase.HasValue && _lastPhase.Value != MatchPhase.MatchEnd)
            {
                var me = NetworkPlayer.LocalPlayer;
                var health = me != null ? me.Health : null;
                if (health != null && health.Team >= 0 && s.Winner >= 0)
                    Sfx.Play2D(health.Team == s.Winner ? Sfx.Win : Sfx.Lose, Volume);
            }
            _lastPhase = s.Phase;
        }
    }
}
