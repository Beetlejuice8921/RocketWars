using System;
using Unity.Netcode;

namespace RocketWars.Launcher
{
    /// <summary>
    /// Сетевой снимок шахты и её пульта. Хранится в NetworkList у MatchNet (подключившиеся позже получают его сразу).
    /// Линии пульта не шлются каждый кадр: фазы фиксируются в момент нажатия (Stamp), дальше все экстраполируют.
    /// </summary>
    public struct SiloSync : INetworkSerializeByMemcpy, IEquatable<SiloSync>
    {
        public byte State;
        public byte ConsoleFlags;
        public float XPhase0;
        public float YPhase0;
        public double Stamp;
        public double ReloadEnd;
        public ulong Operator;
        public double LastPress;

        public bool Equals(SiloSync o) =>
            State == o.State && ConsoleFlags == o.ConsoleFlags && XPhase0.Equals(o.XPhase0) && YPhase0.Equals(o.YPhase0) &&
            Stamp.Equals(o.Stamp) && ReloadEnd.Equals(o.ReloadEnd) && Operator == o.Operator && LastPress.Equals(o.LastPress);

        public override bool Equals(object obj) => obj is SiloSync o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(State, ConsoleFlags, Stamp, ReloadEnd, Operator);
    }
}
