using UnityEngine;

namespace RocketWars.Combat
{
    public enum DamageKind
    {
        Explosion,
        Fall,
        Bullet
    }

    /// <summary>Описание одного факта урона. Источник нужен для статистики (кто кого положил).</summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 Origin;
        public readonly GameObject Source;
        public readonly int SourceTeam;
        public readonly DamageKind Kind;

        public DamageInfo(float amount, Vector3 origin, GameObject source, int sourceTeam, DamageKind kind)
        {
            Amount = amount;
            Origin = origin;
            Source = source;
            SourceTeam = sourceTeam;
            Kind = kind;
        }
    }

    public interface IDamageable
    {
        void TakeDamage(in DamageInfo info);
    }
}
