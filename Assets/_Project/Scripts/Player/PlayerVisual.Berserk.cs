using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Fx;
using UnityEngine;
using UnityEngine.Rendering;

namespace RocketWars.Player
{
    /// <summary>
    /// Берсерк (награда за доставленный флаг), вид у всех игроков:
    /// 2 с превращения — вокруг бьют лучи света, в середине вспышка: каска → огромная фуражка, очки, сигара,
    /// погоны со звёздами, накачанные грудь и руки, по золотому пулемёту в каждой руке;
    /// 30 с режима — руки с пулемётами вперёд, сигара дымит;
    /// 1 с обратного превращения — облачка «хлоп-хлоп», всё возвращается как было.
    /// </summary>
    public partial class PlayerVisual
    {
        const float BerserkSwapAt = 1f;            // момент «вспышки» внутри 2-секундного превращения
        const float BerserkArmThick = 0.7f;        // руки толще на 70%
        const float BerserkArmSpread = 0.12f;      // и шире в плечах
        const int RayCount = 14;
        const float RayWidth = 0.22f;
        static readonly Color RayColor = new Color(1f, 0.85f, 0.4f);

        Mesh _capMesh;
        Material[] _capMats;
        GameObject _bChest, _bCap, _bGlasses, _bCigar, _bEpaulettes, _bGunL, _bGunR;
        Transform _bGunMuzzleL, _bGunMuzzleR, _rays;
        Light _rayLight;
        Transform[] _rayBeams;
        float[] _rayLength;
        Material _rayMat;
        static Mesh _rayMesh;
        static Texture2D _rayTexture;
        bool _berserkGear, _swapFlashDone, _powerUpPlayed;
        int _revertPops;
        float _morph, _nextCigarPuff;
        bool _gunSide;

        /// <summary>В руках золотые пулемёты (обычное оружие и каска спрятаны).</summary>
        bool BerserkGear => _berserkGear;

        void InitBerserk(int team)
        {
            var (chestMesh, chestMats) = Models.BuffChest(team);
            _bChest = BoxModel.Spawn(chestMesh, chestMats, _body, Vector3.zero, "BuffChest");
            var (capMesh, capMats) = Models.OfficerCap(team);
            _bCap = BoxModel.Spawn(capMesh, capMats, _body, new Vector3(0f, Models.HelmetBaseY, 0f), "OfficerCap");
            _capMesh = capMesh;
            _capMats = capMats;
            var (glassMesh, glassMats) = Models.Sunglasses();
            _bGlasses = BoxModel.Spawn(glassMesh, glassMats, _body, Vector3.zero, "Sunglasses");
            var (cigarMesh, cigarMats) = Models.Cigar();
            _bCigar = BoxModel.Spawn(cigarMesh, cigarMats, _body, Vector3.zero, "Cigar");
            var (epMesh, epMats) = Models.Epaulettes();
            _bEpaulettes = BoxModel.Spawn(epMesh, epMats, _body, Vector3.zero, "Epaulettes");

            var (goldMesh, goldMats) = Models.MachineGun(true);
            _bGunL = SpawnHandGun(_armL, goldMesh, goldMats, out _bGunMuzzleL);
            _bGunR = SpawnHandGun(_armR, goldMesh, goldMats, out _bGunMuzzleR);

            // лучи света звездой из центра груди на время превращения (не на теле — не прячутся от своей камеры).
            // Мягкие: прозрачный материал с наложением «сложением» (Resources/BerserkRay), к концу ступенчато тают.
            _rays = new GameObject("BerserkRays").transform;
            _rays.SetParent(transform, false);
            var template = Resources.Load<Material>("BerserkRay");
            _rayMat = template != null ? new Material(template) : new Material(RuntimeMaterials.Emissive(RayColor, 2f));
            _rayMat.SetTexture("_BaseMap", RayTexture());
            _rayBeams = new Transform[RayCount];
            _rayLength = new float[RayCount];
            var rng = new System.Random(77);
            for (int i = 0; i < RayCount; i++)
            {
                var beam = new GameObject($"Ray{i}");
                beam.transform.SetParent(_rays, false);
                beam.AddComponent<MeshFilter>().sharedMesh = RayMesh();
                var r = beam.AddComponent<MeshRenderer>();
                r.sharedMaterial = _rayMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                // направления — равномерно по сфере (спираль Фибоначчи), чуть «приподняты»: меньше лучей уходит в землю
                float y = 1f - 2f * (i + 0.5f) / RayCount;
                float rad = Mathf.Sqrt(1f - y * y), a = i * 2.39996f;
                var dir = new Vector3(Mathf.Cos(a) * rad, y * 0.8f + 0.2f, Mathf.Sin(a) * rad).normalized;
                beam.transform.localRotation = Quaternion.LookRotation(dir);
                _rayBeams[i] = beam.transform;
                _rayLength[i] = 2.2f + (float)rng.NextDouble() * 1.4f;
            }
            _rayLight = new GameObject("BerserkLight").AddComponent<Light>();
            _rayLight.transform.SetParent(_rays, false);
            _rayLight.transform.localPosition = Vector3.zero;
            _rayLight.type = LightType.Point;
            _rayLight.color = new Color(1f, 0.85f, 0.4f);
            _rayLight.range = 9f;
            _rayLight.shadows = LightShadows.None;
            _rays.gameObject.SetActive(false);

            SetBerserkGear(false);
        }

        /// <summary>Звезда лучей из центра груди: медленно вращается, длина каждого луча мерцает, прозрачность — по glow.</summary>
        void UpdateRays(float glow)
        {
            _rays.position = _body.position + _body.up * 1.25f;
            _rays.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.7f) * 10f, Time.time * 40f, 0f);
            float t = Time.time;
            for (int i = 0; i < _rayBeams.Length; i++)
            {
                float flicker = 0.75f + 0.25f * Mathf.Sin(t * 7f + i * 1.7f);
                float length = _rayLength[i] * flicker * Mathf.Lerp(0.3f, 1f, glow);
                float width = RayWidth * Mathf.Lerp(0.5f, 1f, glow);
                _rayBeams[i].localScale = new Vector3(width, width, length);
            }
            var c = RayColor;
            c.a = 0.75f * glow;
            _rayMat.SetColor("_BaseColor", c);
        }

        /// <summary>Луч: два скрещённых квада вдоль +Z (длина 1), v текстуры — от основания (0) к концу (1).</summary>
        static Mesh RayMesh()
        {
            if (_rayMesh != null)
                return _rayMesh;
            _rayMesh = new Mesh { name = "BerserkRay" };
            _rayMesh.SetVertices(new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 1f), new Vector3(0.5f, 0f, 1f), new Vector3(0.5f, 0f, 0f),
                new Vector3(0f, -0.5f, 0f), new Vector3(0f, -0.5f, 1f), new Vector3(0f, 0.5f, 1f), new Vector3(0f, 0.5f, 0f),
            });
            _rayMesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            });
            _rayMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }, 0);
            _rayMesh.RecalculateNormals();
            _rayMesh.RecalculateBounds();
            return _rayMesh;
        }

        /// <summary>Пиксельная текстура луча 4×12: яркое основание, ступенчато тает к концу, края мягче середины.</summary>
        static Texture2D RayTexture()
        {
            if (_rayTexture != null)
                return _rayTexture;
            const int w = 4, h = 12;
            _rayTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "BerserkRay"
            };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float along = 1f - y / (h - 1f);
                float across = x == 0 || x == w - 1 ? 0.45f : 1f;
                _rayTexture.SetPixel(x, y, new Color(1f, 1f, 1f, along * along * across));
            }
            _rayTexture.Apply(false, true);
            return _rayTexture;
        }

        static GameObject SpawnHandGun(Transform arm, Mesh mesh, Material[] mats, out Transform muzzle)
        {
            var gun = BoxModel.Spawn(mesh, mats, arm, new Vector3(0f, -0.64f, 0.02f), "GoldMachineGun").transform;
            gun.localRotation = Quaternion.Euler(90f, 0f, 0f);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = new Vector3(0f, 0.065f, Models.MachineGunMuzzle);
            return gun.gameObject;
        }

        void SetBerserkGear(bool on)
        {
            _berserkGear = on;
            _bChest.SetActive(on);
            _bCap.SetActive(on);
            _bGlasses.SetActive(on);
            _bCigar.SetActive(on);
            _bEpaulettes.SetActive(on);
            _bGunL.SetActive(on);
            _bGunR.SetActive(on);
            _helmet.gameObject.SetActive(!on && !_health.IsDowned);
            if (on)
            {
                _pistolModel.SetActive(false);
                _gunModel.SetActive(false);
            }
        }

        /// <summary>Каждый кадр (и лёжа — обратное превращение должно доиграть): фазы превращения и эффекты.</summary>
        void UpdateBerserk(float dt)
        {
            if (_net == null)
                return;
            Vector3 center = _body.position + Vector3.up * 1.2f;

            if (_net.InBerserkTransform)
            {
                float t = _net.BerserkTransform01 * BerserkSpec.TransformTime;
                if (!_powerUpPlayed)
                {
                    _powerUpPlayed = true;
                    _swapFlashDone = false;
                    _revertPops = 0;
                    Sfx.PlayAt(Sfx.PowerUp, center, 1f, 80f);
                }
                // лучи: разгораются, вращаются, гаснут к концу
                _rays.gameObject.SetActive(true);
                float glow = Mathf.Sin(Mathf.Clamp01(t / BerserkSpec.TransformTime) * Mathf.PI);
                UpdateRays(glow);
                _rayLight.intensity = 5f * glow;
                if (Random.value < 0.6f)
                    VoxelParticles.Instance.Emit(center + Random.insideUnitSphere * 0.8f, Vector3.up * Random.Range(1.5f, 3f),
                        Random.Range(0.5f, 0.9f), 0.08f, 0.05f, VoxelParticles.Fire1, growEnd: 0.1f, shrinkStart: 0.5f);

                if (t >= BerserkSwapAt && !_swapFlashDone)
                {
                    _swapFlashDone = true;
                    SetBerserkGear(true);
                    // вспышка золотых искр в момент превращения
                    for (int i = 0; i < 40; i++)
                        VoxelParticles.Instance.Emit(center, Random.onUnitSphere * Random.Range(3f, 7f), Random.Range(0.4f, 0.8f),
                            0.12f, 0.06f, i % 2 == 0 ? VoxelParticles.Fire1 : VoxelParticles.Fire0, drag: 2f, growEnd: 0.05f, shrinkStart: 0.4f);
                }
                _morph = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BerserkSwapAt - 0.2f, BerserkSwapAt + 0.6f, t));
            }
            else if (_net.IsBerserk)
            {
                _rays.gameObject.SetActive(false);
                if (!_berserkGear)
                    SetBerserkGear(true); // подключились посреди режима
                _morph = 1f;
                _powerUpPlayed = true;
                // сигара дымит (свою — от первого лица — не показываем: дым лез бы прямо в камеру)
                bool ownFirstPerson = _controller != null && _controller.IsLocal;
                if (!ownFirstPerson && Time.time >= _nextCigarPuff)
                {
                    _nextCigarPuff = Time.time + Random.Range(0.25f, 0.5f);
                    // тонкая струйка с кончика сигары: вверх и чуть вбок, мелкими клубками, чтобы не закрывать лицо
                    VoxelParticles.Instance.Emit(_body.TransformPoint(Models.CigarTip), _body.TransformDirection(new Vector3(0.25f, 1f, 0.15f)) * Random.Range(0.5f, 0.8f),
                        Random.Range(0.8f, 1.2f), 0.03f, Random.Range(0.07f, 0.11f), VoxelParticles.RandomSmoke(), drag: 1f, growEnd: 0.4f, shrinkStart: 0.6f);
                }
            }
            else
            {
                _rays.gameObject.SetActive(false);
                _powerUpPlayed = false;
                float since = _net.SinceBerserkEnd;
                if (since < BerserkSpec.RevertTime)
                {
                    // обратное превращение: три «хлопка» облачков, на первом всё возвращается
                    int pops = Mathf.Min(3, 1 + Mathf.FloorToInt(since / 0.33f));
                    while (_revertPops < pops)
                    {
                        _revertPops++;
                        PuffBurst(center);
                        if (_berserkGear)
                            SetBerserkGear(false);
                    }
                    _morph = Mathf.MoveTowards(_morph, 0f, dt * 3f);
                }
                else
                {
                    if (_berserkGear)
                        SetBerserkGear(false);
                    _morph = 0f;
                    _revertPops = 0;
                }
            }

            ApplyMorph();
        }

        /// <summary>Облачка: белые клубы вокруг тела разлетаются и тают, с хлопком.</summary>
        void PuffBurst(Vector3 center)
        {
            for (int i = 0; i < 14; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                VoxelParticles.Instance.Emit(center + dir * 0.4f, dir * Random.Range(1.5f, 3f), Random.Range(0.45f, 0.7f),
                    0.25f, Random.Range(0.6f, 0.9f), VoxelParticles.RandomSmoke(), drag: 3f, growEnd: 0.3f, shrinkStart: 0.5f);
            }
            Sfx.PlayAt(Sfx.Pop, center, 0.8f, 40f, Random.Range(0.9f, 1.2f));
        }

        /// <summary>Накачанность: руки толще и шире в плечах, грудь «надувается» в момент вспышки.</summary>
        void ApplyMorph()
        {
            float thick = 1f + BerserkArmThick * _morph;
            var armScale = _armL.localScale;
            _armL.localScale = new Vector3(thick, armScale.y, thick);
            _armR.localScale = new Vector3(thick, _armR.localScale.y, thick);
            float spread = BerserkArmSpread * _morph;
            _armL.localPosition = ShoulderL + Vector3.left * spread;
            _armR.localPosition = ShoulderR + Vector3.right * spread;
            if (_bChest.activeSelf)
                _bChest.transform.localScale = new Vector3(Mathf.Lerp(0.7f, 1f, _morph), 1f, Mathf.Lerp(0.7f, 1f, _morph));
        }

        /// <summary>Поза берсерка: обе руки с пулемётами вперёд (после обычной шаговой анимации).</summary>
        void ApplyBerserkPose(float swing)
        {
            if (!_berserkGear)
                return;
            float bob = Mathf.Sin(_phase) * 0.15f * swing;
            _armL.localRotation = AimRotation * Quaternion.Euler(bob, 0f, -4f);
            _armR.localRotation = AimRotation * Quaternion.Euler(-bob, 0f, 4f);
        }

        /// <summary>Чужой выстрел берсерка: вспышка у дула попеременно левого и правого пулемёта.</summary>
        bool TryBerserkMuzzle(out Vector3 pos)
        {
            pos = default;
            if (!_berserkGear)
                return false;
            _gunSide = !_gunSide;
            pos = (_gunSide ? _bGunMuzzleL : _bGunMuzzleR).position;
            return true;
        }
    }
}
