using Unity.Netcode;
using UnityEngine;

namespace RocketWars.Net
{
    /// <summary>Общее время: серверное, если сеть запущена (на клиентах — оценка серверного), иначе локальное.</summary>
    public static class NetClock
    {
        public static double Now
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening ? nm.ServerTime.Time : Time.timeAsDouble;
            }
        }

        public static bool IsServer
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening || nm.IsServer;
            }
        }

        public static ulong LocalClientId
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening ? nm.LocalClientId : 0;
            }
        }
    }
}
