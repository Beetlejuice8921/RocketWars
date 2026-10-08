using System;
using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Fx;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Combat
{
    /// <summary>
    /// Взрыв ракеты: ≤5 м — 500, 5–8 м — 100, 8–14 м — 25. Бьёт по всем игрокам, кубикам и шахтам без учёта команд.
    /// Урон по сущностям считает только сервер (Detonate), а разрушение кубиков и эффекты применяются
    /// у всех одинаково (ApplyLocal) — сервер рассылает факт взрыва через Broadcast.
    /// </summary>
    public static class Explosion
    {
        public const float InnerRadius = 5f;
        public const float MiddleRadius = 8f;
        public const float OuterRadius = 14f;

        /// <summary>Рассылка взрыва всем клиентам (ставит MatchNet). Если не задана — применяем локально.</summary>
        public static Action<Vector3> Broadcast;

        public static float DamageAtDistance(float distance)
        {
            if (distance <= InnerRadius) return 500f;
            if (distance <= MiddleRadius) return 100f;
            if (distance <= OuterRadius) return 25f;
            return 0f;
        }

        /// <summary>Только на сервере: урон по игрокам и шахтам + рассылка взрыва.</summary>
        public static void Detonate(Vector3 position, GameObject source, int sourceTeam)
        {
            var damaged = new HashSet<IDamageable>();
            foreach (var col in Physics.OverlapSphere(position, OuterRadius, ~0, QueryTriggerInteraction.Collide))
            {
                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || !damaged.Add(target))
                    continue;

                float distance = Vector3.Distance(position, col.bounds.ClosestPoint(position));
                float amount = DamageAtDistance(distance);
                if (amount > 0f)
                    target.TakeDamage(new DamageInfo(amount, position, source, sourceTeam, DamageKind.Explosion));
            }

            if (Broadcast != null)
                Broadcast(position);
            else
                ApplyLocal(position, true);
        }

        static readonly List<Vector3> DestroyedBlocks = new List<Vector3>();
        static readonly List<BlockKind> DestroyedKinds = new List<BlockKind>();

        /// <summary>У всех: разрушение кубиков (детерминированно) и эффекты — гриб, обломки, вспышка, тряска, звук.</summary>
        public static void ApplyLocal(Vector3 position, bool effects)
        {
            DestroyedBlocks.Clear();
            DestroyedKinds.Clear();
            foreach (var island in VoxelIsland.All.ToArray())
                island.DamageSphere(position, OuterRadius, DamageAtDistance,
                    effects ? DestroyedBlocks : null, effects ? DestroyedKinds : null);

            if (!effects)
                return;
            VoxelFx.Mushroom(position);
            VoxelFx.Debris(position, DestroyedBlocks, DestroyedKinds);
            CameraShake.AddAt(position);
            Sfx.PlayAt(Sfx.Explosion, position, 1f, 300f);
        }
    }
}