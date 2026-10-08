using System;
using RocketWars.Core;
using RocketWars.Player;
using RocketWars.Terrain;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace RocketWars.Flags
{
    public enum FlagState : byte
    {
        /// <summary>Стоит на своей каменной площадке.</summary>
        Home,
        /// <summary>Висит за спиной у противника.</summary>
        Carried,
        /// <summary>Несущего положили — флаг стоит там, где он упал.</summary>
        Dropped
    }

    /// <summary>Сетевой снимок флага (NetworkList у MatchNet).</summary>
    public struct FlagSync : INetworkSerializeByMemcpy, IEquatable<FlagSync>
    {
        public byte State;
        public ulong Carrier;
        public Vector3 DropPos;

        public bool Equals(FlagSync o) => State == o.State && Carrier == o.Carrier && DropPos == o.DropPos;
        public override bool Equals(object obj) => obj is FlagSync o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(State, Carrier, DropPos);
    }

    /// <summary>
    /// Флаг команды высотой 2,5 м на неразрушимой каменной площадке у дальнего края своего острова.
    /// Забрать его может только противник (кнопка действия рядом): флаг вешается ему за спину во весь рост.
    /// Несущего положили — флаг падает и стоит на земле; несущий сорвался с острова или вышел — флаг возвращается.
    /// Доставил на свой остров — событие FlagDelivered (что именно происходит — решим позже), флаг возвращается домой.
    /// Решения принимает сервер (IsAuthority), клиенты получают состояние через FlagSync.
    /// </summary>
    public class TeamFlag : MonoBehaviour
    {
        /// <summary>С какого расстояния (от ног до флага) можно забрать флаг.</summary>
        public const float PickupRange = 2.2f;
        const float DropRaycast = 80f;
        // за спиной: чуть ниже плеч и позади, слегка наклонён назад
        static readonly Vector3 BackOffset = new Vector3(0f, 0.35f, -0.32f);
        static readonly Quaternion BackTilt = Quaternion.Euler(-12f, 0f, 0f);
        /// <summary>Стоящий флаг: полотнище вдоль ширины острова — с вражеского острова его видно плоскостью, а не ребром.</summary>
        static readonly Quaternion StandRotation = Quaternion.identity;

        public int Team { get; private set; }
        public VoxelIsland Island { get; private set; }
        public FlagState State { get; private set; } = FlagState.Home;
        public ulong CarrierId { get; private set; } = ulong.MaxValue;
        public Vector3 DropPos { get; private set; }
        public bool IsAuthority { get; set; } = true;
        public Vector3 HomePos => Island.FlagBase;

        /// <summary>Сервер: противник донёс флаг до своего острова (флаг, кто донёс). Событие придумаем позже.</summary>
        public static event Action<TeamFlag, PlayerHealth> FlagDelivered;
        /// <summary>Сервер: изменилось состояние флага (для ленты событий): флаг, что случилось, кто.</summary>
        public static event Action<TeamFlag, string, PlayerHealth> FlagEvent;

        Transform[] _strips;
        Renderer[] _renderers;
        bool _hiddenForMe;

        public static TeamFlag Create(VoxelIsland island, Transform parent)
        {
            var go = new GameObject($"Flag_T{island.Team}");
            go.transform.SetParent(parent, false);
            var flag = go.AddComponent<TeamFlag>();
            flag.Team = island.Team;
            flag.Island = island;
            go.transform.SetPositionAndRotation(island.FlagBase, StandRotation);

            var (poleMesh, poleMats) = Models.FlagPole();
            BoxModel.Spawn(poleMesh, poleMats, go.transform, Vector3.zero, "Pole");
            var (stripMesh, stripMats) = Models.FlagStrip(flag.Team);
            flag._strips = new Transform[Models.FlagClothStrips];
            float w = Models.FlagClothLength / Models.FlagClothStrips;
            for (int i = 0; i < flag._strips.Length; i++)
                flag._strips[i] = BoxModel.Spawn(stripMesh, stripMats, go.transform,
                    new Vector3(0.045f + i * w, Models.FlagClothBottom, 0f), $"Strip{i}").transform;
            flag._renderers = go.GetComponentsInChildren<Renderer>();
            return flag;
        }

        // ---------- сервер ----------

        /// <summary>Сервер: противник забирает флаг (проверки дистанции — у вызывающего).</summary>
        public bool ServerTake(PlayerHealth taker)
        {
            if (!IsAuthority || State == FlagState.Carried || taker == null || taker.Team == Team || taker.IsDowned)
                return false;
            State = FlagState.Carried;
            CarrierId = taker.NetworkObjectId;
            FlagEvent?.Invoke(this, "забрал", taker);
            return true;
        }

        public void ServerReturnHome()
        {
            State = FlagState.Home;
            CarrierId = ulong.MaxValue;
        }

        /// <summary>Сервер, каждый кадр: несущего положили/он ушёл, доставка, флаг на земле без опоры.</summary>
        public void ServerUpdate()
        {
            if (!IsAuthority)
                return;

            if (State == FlagState.Carried)
            {
                var carrier = FindCarrier();
                if (carrier == null)
                {
                    ServerReturnHome(); // вышел из игры
                    FlagEvent?.Invoke(this, "вернулся на базу", null);
                }
                else if (carrier.IsDowned)
                {
                    if (TryGround(carrier.transform.position, out var ground))
                    {
                        State = FlagState.Dropped;
                        DropPos = ground;
                        CarrierId = ulong.MaxValue;
                        FlagEvent?.Invoke(this, "уронил", carrier);
                    }
                    else
                    {
                        ServerReturnHome();
                        FlagEvent?.Invoke(this, "вернулся на базу", null);
                    }
                }
                else if (StandsOnOwnIsland(carrier))
                {
                    FlagEvent?.Invoke(this, "доставил", carrier);
                    FlagDelivered?.Invoke(this, carrier);
                    ServerReturnHome();
                }
            }
            else if (State == FlagState.Dropped)
            {
                // землю под флагом могли выбить — опускаем на новую опору или возвращаем домой
                if (TryGround(DropPos + Vector3.up * 0.5f, out var ground))
                    DropPos = ground;
                else
                {
                    ServerReturnHome();
                    FlagEvent?.Invoke(this, "вернулся на базу", null);
                }
            }
        }

        /// <summary>Сервер: несущий сорвался с острова — флаг сразу возвращается (иначе его «телепортировали» бы домой).</summary>
        public void ServerCarrierFell(PlayerHealth player)
        {
            if (State == FlagState.Carried && player != null && player.NetworkObjectId == CarrierId)
            {
                ServerReturnHome();
                FlagEvent?.Invoke(this, "вернулся на базу", null);
            }
        }

        /// <summary>Несущий стоит на земле своего острова.</summary>
        bool StandsOnOwnIsland(PlayerHealth carrier)
        {
            var own = VoxelIsland.ForTeam(carrier.Team);
            if (own == null)
                return false;
            var ray = new Ray(carrier.transform.position + Vector3.up * 0.3f, Vector3.down);
            foreach (var hit in Physics.RaycastAll(ray, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<VoxelIsland>() == own)
                    return true;
            return false;
        }

        /// <summary>Ближайшая сверху земля острова под точкой (на шахтах и ящиках флаг не стоит).</summary>
        static bool TryGround(Vector3 from, out Vector3 ground)
        {
            ground = default;
            bool found = false;
            var ray = new Ray(from + Vector3.up * 0.5f, Vector3.down);
            foreach (var hit in Physics.RaycastAll(ray, DropRaycast, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<VoxelIsland>() == null)
                    continue;
                if (!found || hit.point.y > ground.y)
                {
                    ground = hit.point;
                    found = true;
                }
            }
            return found;
        }

        PlayerHealth FindCarrier()
        {
            foreach (var p in PlayerHealth.All)
                if (p.IsSpawned && p.NetworkObjectId == CarrierId)
                    return p;
            return null;
        }

        public FlagSync ToSync() => new FlagSync { State = (byte)State, Carrier = CarrierId, DropPos = DropPos };

        // ---------- клиент ----------

        public void ApplySync(in FlagSync sync)
        {
            State = (FlagState)sync.State;
            CarrierId = sync.Carrier;
            DropPos = sync.DropPos;
        }

        /// <summary>Где флаг сейчас (для подсказок и мини-карты): на базе, на земле или у несущего.</summary>
        public Vector3 Position => transform.position;

        // ---------- вид ----------

        void LateUpdate()
        {
            bool hideForMe = false;
            switch (State)
            {
                case FlagState.Home:
                    transform.SetPositionAndRotation(HomePos, StandRotation);
                    break;
                case FlagState.Dropped:
                    transform.SetPositionAndRotation(DropPos, StandRotation);
                    break;
                case FlagState.Carried:
                    var carrier = FindCarrier();
                    var body = carrier != null ? carrier.transform.Find("Body") : null;
                    if (body != null)
                    {
                        // за спиной во весь рост; полотнище (повёрнуто на 90°) развевается назад
                        transform.SetPositionAndRotation(body.TransformPoint(BackOffset), body.rotation * BackTilt * Quaternion.Euler(0f, 90f, 0f));
                        // своему несущему флаг не мешает обзору: от него только тень
                        hideForMe = NetworkPlayer.LocalPlayer != null && NetworkPlayer.LocalPlayer.Health == carrier;
                    }
                    break;
            }

            if (hideForMe != _hiddenForMe)
            {
                _hiddenForMe = hideForMe;
                foreach (var r in _renderers)
                    r.shadowCastingMode = hideForMe ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            }

            // полотнище колышется: волна от шеста к свободному краю, у края сильнее
            float t = Time.time;
            for (int i = 0; i < _strips.Length; i++)
            {
                float k = (i + 1f) / _strips.Length;
                float wave = Mathf.Sin(t * 4f - i * 0.9f) * 0.09f * k;
                var p = _strips[i].localPosition;
                _strips[i].localPosition = new Vector3(p.x, p.y, wave);
                _strips[i].localRotation = Quaternion.Euler(0f, Mathf.Cos(t * 4f - i * 0.9f) * 12f * k, 0f);
            }
        }
    }
}
