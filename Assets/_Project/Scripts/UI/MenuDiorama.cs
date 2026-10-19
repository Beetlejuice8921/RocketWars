using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Fx;
using RocketWars.Loot;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.UI
{
    /// <summary>
    /// Сцена за главным меню: камера у самой травы смотрит на горизонт. Три плана:
    /// передний — справа на траве сидит солдат и читает книгу;
    /// второй — солдаты перебегают с одного края экрана на другой, стреляя на бегу;
    /// третий — вдалеке бой 10 на 10: перестрелка из пистолетов и ракеты из шахт;
    /// четвёртый — ещё дальше с неба опускаются ящики с припасами на парашютиках, как сброс в игре.
    /// Бой бесконечный: солдаты бессмертные (взрыв подбрасывает — полежал и встал), земля не разрушается.
    /// Пока сцена на экране, всё идёт в 5 раз медленнее (Time.timeScale), с началом игры скорость возвращается.
    /// Строится один раз при первом показе меню; сцена игры перезагружается при выходе в меню — строится заново.
    /// </summary>
    public class MenuDiorama : MonoBehaviour
    {
        public const float SlowMotion = 0.2f;
        const int TeamSize = 10;

        // камера: в метре над травой, чуть вниз — горизонт немного выше середины экрана (как на макете)
        static readonly Vector3 CameraPosition = new Vector3(0f, MenuGround.Top + 1.05f, 0f);
        const float CameraPitch = 1.2f, CameraFov = 55f;
        /// <summary>
        /// Мягкое пошатывание камеры «с рук»: амплитуды поворота (градусы: тангаж, рысканье, крен) и сдвига (м),
        /// скорость в реальном времени — замедление боя на него не влияет. Шум Перлина редко отходит от середины
        /// дальше половины размаха, так что на деле камера качается примерно вдвое слабее этих чисел.
        /// </summary>
        static readonly Vector3 SwayAngles = new Vector3(0.8f, 1.1f, 0.6f);
        const float SwayShift = 0.06f, SwaySpeed = 0.18f;
        /// <summary>Передний план: где на экране (доли) сидит читающий солдат — точка земли под ним.</summary>
        static readonly Vector2 ReaderViewport = new Vector2(0.78f, 0.27f);

        /// <summary>Второй план: бегущих солдат столько, на таком удалении от камеры (м), с такой скоростью.</summary>
        const int RunnerCount = 4;
        const float RunnerDepthMin = 11f, RunnerDepthMax = 16f;
        const float RunnerSpeed = 6f;
        /// <summary>Насколько за край экрана (м) бегун появляется и убегает.</summary>
        const float RunnerEdgeMargin = 2.5f;

        /// <summary>
        /// Третий план, поле боя: центр правее середины (левую часть экрана закрывает панель меню), команды разнесены
        /// по диагонали TeamAxis — красные ближе и левее, зелёные дальше и правее (не прячутся за читающим солдатом).
        /// </summary>
        static readonly Vector3 BattleCenter = new Vector3(4f, MenuGround.Top, 35f);
        static readonly Vector3 TeamAxis = new Vector3(0.857f, 0f, 0.514f);
        const float TeamOffset = 9f, ZoneHalfWidth = 4f, ZoneHalfDepth = 5f;
        const float SiloOffset = 19f;

        /// <summary>
        /// Четвёртый план: ящики на парашютиках (как LootCrate.CreateAirDrop) — на таком удалении от камеры (м),
        /// с такой высоты и скоростью; сколько в воздухе одновременно; сколько ящик стоит на земле, прежде чем исчезнуть.
        /// </summary>
        const float CrateDepthMin = 52f, CrateDepthMax = 75f;
        const float CrateDropHeight = 28f, CrateDropSpeed = LootCrate.DropSpeed;
        const int CratesInAir = 4;
        const float CrateGroundTime = 8f;
        const float ChuteScale = 0.45f, ChuteFoldTime = 0.5f;

        const float RocketIntervalMin = 2.2f, RocketIntervalMax = 3.8f;
        const float RocketArc = 9f;
        /// <summary>Дым за ракетой реже, чем в игре: в замедлении он висит впятеро дольше и затягивает всё небо.</summary>
        const float RocketSmokeInterval = 0.08f;
        const float KnockRadius = 7.5f;
        const float BulletSpeed = 150f;

        static MenuDiorama _instance;

        Transform _root;
        Camera _cam;
        MenuReader _reader;
        int _screenW, _screenH;
        readonly List<Fighter> _fighters = new List<Fighter>();
        readonly List<Runner> _runners = new List<Runner>();
        readonly List<Crate> _crates = new List<Crate>();
        float _crateTimer;
        readonly List<Vector3>[] _silos = { new List<Vector3>(), new List<Vector3>() };
        readonly List<MenuRocket> _rockets = new List<MenuRocket>();
        readonly List<Tracer> _tracers = new List<Tracer>();
        float _rocketTimer = 1f;
        int _nextRocketTeam;

        class Fighter
        {
            public MenuSoldier Soldier;
            public MenuSoldier Target;
            public float Timer;
            public int ShotsLeft;
        }

        /// <summary>Бегун второго плана: пересекает экран в направлении Dir (+1 — вправо), потом пауза и новый забег.</summary>
        class Runner
        {
            public MenuSoldier Soldier;
            public float Delay, ShotTimer;
            public int Dir;
            public bool Running;
        }

        /// <summary>Ящик четвёртого плана: опускается под куполом, приземлившись — купол складывается, потом ящик пропадает.</summary>
        class Crate
        {
            public Transform Transform, Chute;
            public float T, Yaw, Phase, Landed = -1f;
        }

        class MenuRocket
        {
            public Transform Transform;
            public Vector3 Start, Target;
            public float Duration, T, SmokeTimer;
        }

        class Tracer
        {
            public Transform Transform;
            public Vector3 Direction;
            public float Left;
        }

        /// <summary>Показать сцену меню в камеру cam (и включить замедление).</summary>
        public static void Show(Camera cam)
        {
            if (_instance == null)
                _instance = new GameObject("MenuDiorama").AddComponent<MenuDiorama>();
            _instance.Activate(cam);
        }

        /// <summary>Спрятать (начинается игра): нормальная скорость, эффекты боя убраны.</summary>
        public static void Hide()
        {
            if (_instance != null)
                _instance.Deactivate();
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                Time.timeScale = 1f;
            }
        }

        void Activate(Camera cam)
        {
            if (_root == null)
                Build();
            _root.gameObject.SetActive(true);
            enabled = true;
            _cam = cam;
            var shake = cam.GetComponent<CameraShake>();
            if (shake != null)
                Destroy(shake);
            cam.transform.SetParent(null, false);
            cam.transform.SetPositionAndRotation(CameraPosition, Quaternion.Euler(CameraPitch, 0f, 0f));
            cam.fieldOfView = CameraFov;
            cam.nearClipPlane = 0.1f;
            PlaceReader();
            Time.timeScale = SlowMotion;
        }

        void Deactivate()
        {
            if (_root != null)
                _root.gameObject.SetActive(false);
            enabled = false;
            Time.timeScale = 1f;
            // дым и комья боя не должны доживать до матча (острова стоят там же, где поле)
            if (VoxelParticles.Instance != null)
                VoxelParticles.Instance.Clear();
        }

        // ---------- постройка ----------

        void Build()
        {
            _root = new GameObject("Scene").transform;
            _root.SetParent(transform, false);
            Biomes.Current = Biome.Grass;
            MenuGround.Build(_root);

            for (int team = 0; team < 2; team++)
            {
                float side = team == 0 ? -1f : 1f;
                var (siloMesh, siloMats) = Models.Silo(team);
                foreach (float dz in new[] { -5f, 6f })
                {
                    Vector3 pos = BattleCenter + TeamAxis * (side * (SiloOffset + Random.Range(-1.5f, 1.5f))) + Across * dz;
                    var silo = BoxModel.Spawn(siloMesh, siloMats, _root, pos, $"Silo_T{team}").transform;
                    silo.localRotation = Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f);
                    _silos[team].Add(pos + Vector3.up * Models.SiloRoofTop);
                }

                for (int i = 0; i < TeamSize; i++)
                {
                    var soldier = new MenuSoldier(_root, team, RandomSpot(team), Quaternion.LookRotation(TeamAxis * -side).eulerAngles.y);
                    _fighters.Add(new Fighter { Soldier = soldier, Timer = Random.Range(0f, 1.5f) });
                }
            }

            // бегуны второго плана ждут за кадром; первые забеги — вразнобой
            for (int i = 0; i < RunnerCount; i++)
            {
                var soldier = new MenuSoldier(_root, i % 2, new Vector3(-200f, MenuGround.Top, RunnerDepthMin), 90f) { RunSpeed = RunnerSpeed };
                _runners.Add(new Runner { Soldier = soldier, Delay = i * 1.3f + Random.Range(0f, 0.8f) });
            }

            _reader = new MenuReader(_root);
        }

        /// <summary>Случайная точка в зоне команды (красные слева, зелёные справа).</summary>
        static Vector3 RandomSpot(int team)
        {
            float side = team == 0 ? -1f : 1f;
            return BattleCenter + TeamAxis * (side * TeamOffset + Random.Range(-ZoneHalfWidth, ZoneHalfWidth))
                                + Across * Random.Range(-ZoneHalfDepth, ZoneHalfDepth);
        }

        /// <summary>Поперёк линии фронта.</summary>
        static Vector3 Across => Vector3.Cross(Vector3.up, TeamAxis);

        /// <summary>Читающего солдата — в точку земли под ReaderViewport (зависит от пропорций экрана).</summary>
        void PlaceReader()
        {
            // по неподвижной камере: пошатывание не должно сдвигать солдата
            _cam.transform.SetPositionAndRotation(CameraPosition, Quaternion.Euler(CameraPitch, 0f, 0f));
            _screenW = Screen.width;
            _screenH = Screen.height;
            var ray = _cam.ViewportPointToRay(new Vector3(ReaderViewport.x, ReaderViewport.y, 0f));
            if (ray.direction.y >= -0.01f)
                return;
            float t = (MenuGround.Top - ray.origin.y) / ray.direction.y;
            Vector3 ground = ray.origin + ray.direction * t;
            _reader.Place(ground, _cam.transform.position);
        }

        // ---------- кадр ----------

        void Update()
        {
            if (_cam == null)
                return;
            if (Screen.width != _screenW || Screen.height != _screenH)
                PlaceReader();
            float dt = Time.deltaTime;
            _reader.Update(dt);
            UpdateRunners(dt);
            UpdateFighters(dt);
            UpdateCrates(dt);
            UpdateRockets(dt);
            UpdateTracers(dt);
            Sway();
        }

        /// <summary>Плавный шум Перлина по трём осям: камера чуть покачивается, как в руках оператора.</summary>
        void Sway()
        {
            float t = Time.unscaledTime * SwaySpeed;
            float Noise(float seed) => Mathf.PerlinNoise(t, seed) * 2f - 1f;
            var angles = new Vector3(CameraPitch + Noise(1.3f) * SwayAngles.x, Noise(7.1f) * SwayAngles.y, Noise(13.7f) * SwayAngles.z);
            var shift = new Vector3(Noise(21.9f), Noise(31.3f) * 0.6f, 0f) * SwayShift;
            _cam.transform.SetPositionAndRotation(CameraPosition + shift, Quaternion.Euler(angles));
        }

        void UpdateFighters(float dt)
        {
            foreach (var f in _fighters)
            {
                var s = f.Soldier;
                s.Update(dt);
                if (!s.CanAct)
                {
                    f.Target = null;
                    continue;
                }

                if (f.Target != null)
                {
                    Vector3 to = f.Target.Position - s.Position;
                    s.Yaw = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y;
                }

                f.Timer -= dt;
                if (f.Timer > 0f)
                    continue;

                if (f.ShotsLeft > 0 && f.Target != null && f.Target.CanAct && s.Current == MenuSoldier.State.Stand)
                {
                    Fire(f);
                    continue;
                }

                // новое решение: перебежка или очередь по случайному врагу
                if (s.Current == MenuSoldier.State.Stand && Random.value < 0.35f)
                {
                    s.Aiming = false;
                    f.Target = null;
                    s.Run(RandomSpot(s.Team));
                    f.Timer = Random.Range(1f, 2f);
                }
                else if (s.Current == MenuSoldier.State.Stand)
                {
                    f.Target = PickEnemy(s.Team);
                    s.Aiming = f.Target != null;
                    f.ShotsLeft = Random.Range(2, 6);
                    f.Timer = Random.Range(0.3f, 0.7f); // довернуться и прицелиться
                }
                else
                {
                    f.Timer = 0.3f;
                }
            }
        }

        MenuSoldier PickEnemy(int team)
        {
            MenuSoldier pick = null;
            int seen = 0;
            foreach (var f in _fighters)
                if (f.Soldier.Team != team && f.Soldier.CanAct && Random.Range(0, ++seen) == 0)
                    pick = f.Soldier;
            return pick;
        }

        void Fire(Fighter f)
        {
            var s = f.Soldier;
            s.Shoot();
            f.ShotsLeft--;
            f.Timer = Random.Range(0.35f, 0.9f);
            if (f.ShotsLeft == 0)
            {
                s.Aiming = false;
                f.Target = null;
                f.Timer = Random.Range(0.4f, 1.2f);
            }

            // бессмертные — пули проходят рядом: в грудь с разбросом
            ShotEffects(s, (f.Target ?? s).Chest + Random.insideUnitSphere * 1.2f);
        }

        /// <summary>Трассер от дула к точке aim (и дальше), вспышка, изредка звук.</summary>
        void ShotEffects(MenuSoldier s, Vector3 aim)
        {
            Vector3 muzzle = s.MuzzlePosition;
            Vector3 dir = (aim - muzzle).normalized;
            SpawnTracer(muzzle, dir, Vector3.Distance(muzzle, aim) + 25f);
            VoxelParticles.Instance.Emit(muzzle + dir * 0.1f, dir * 2f, 0.08f, 0.18f, 0.1f, VoxelParticles.Fire1);
            if (Random.value < 0.3f)
                Sfx.PlayAt(Sfx.Gunshot, muzzle, 0.25f, 150f, Random.Range(0.55f, 0.7f));
        }

        // ---------- второй план ----------

        void UpdateRunners(float dt)
        {
            foreach (var r in _runners)
            {
                var s = r.Soldier;
                s.Update(dt);
                if (!r.Running)
                {
                    r.Delay -= dt;
                    if (r.Delay <= 0f)
                        StartRun(r);
                    continue;
                }
                if (!s.CanAct)
                    continue;

                if (s.Current == MenuSoldier.State.Stand)
                {
                    if (Mathf.Abs(s.Position.x - s.RunTarget.x) < 0.5f)
                    {
                        // убежал за край — передышка за кадром и новый забег
                        r.Running = false;
                        r.Delay = Random.Range(0.5f, 2.5f);
                        continue;
                    }
                    s.Run(s.RunTarget); // встал после взрыва — бежит дальше
                }

                // на бегу стреляет вперёд, по ходу движения
                s.Aiming = true;
                s.Yaw = r.Dir > 0 ? 90f : -90f;
                r.ShotTimer -= dt;
                if (r.ShotTimer <= 0f)
                {
                    r.ShotTimer = Random.Range(0.35f, 0.8f);
                    s.Shoot();
                    ShotEffects(s, s.Chest + new Vector3(r.Dir * 30f, Random.Range(-1f, 1.5f), Random.Range(-3f, 3f)));
                }
            }
        }

        /// <summary>Забег через весь экран на случайной глубине второго плана: от края до края с запасом за кадр.</summary>
        void StartRun(Runner r)
        {
            var s = r.Soldier;
            float depth = Random.Range(RunnerDepthMin, RunnerDepthMax);
            float halfWidth = depth * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * _cam.aspect + RunnerEdgeMargin;
            // красные бегут вправо, к зелёным, зелёные — влево
            r.Dir = s.Team == 0 ? 1 : -1;
            float x0 = _cam.transform.position.x;
            s.Teleport(new Vector3(x0 - r.Dir * halfWidth, MenuGround.Top, depth), r.Dir > 0 ? 90f : -90f);
            s.Run(new Vector3(x0 + r.Dir * halfWidth, MenuGround.Top, depth + Random.Range(-2f, 2f)));
            r.ShotTimer = Random.Range(0.3f, 0.8f);
            r.Running = true;
        }

        // ---------- четвёртый план ----------

        void UpdateCrates(float dt)
        {
            float dropTime = CrateDropHeight / CrateDropSpeed;
            int inAir = 0;
            foreach (var c in _crates)
                if (c.Landed < 0f)
                    inAir++;
            _crateTimer -= dt;
            if (inAir < CratesInAir && _crateTimer <= 0f)
            {
                // самые первые — сразу на разной высоте, чтобы небо не было пустым, пока долетит первый
                bool prime = _crates.Count < CratesInAir;
                SpawnCrate(prime ? Random.Range(0f, dropTime * 0.85f) : 0f);
                _crateTimer = prime ? 0f : Random.Range(0.6f, 1.4f) * dropTime / CratesInAir;
            }

            for (int i = _crates.Count - 1; i >= 0; i--)
            {
                var c = _crates[i];
                c.T += dt;
                if (c.Landed < 0f)
                {
                    float h = CrateDropHeight - CrateDropSpeed * c.T;
                    var p = c.Transform.position;
                    if (h <= 0f)
                    {
                        c.Transform.SetPositionAndRotation(new Vector3(p.x, MenuGround.Top, p.z), Quaternion.Euler(0f, c.Yaw, 0f));
                        c.Landed = c.T;
                        continue;
                    }
                    c.Transform.position = new Vector3(p.x, MenuGround.Top + h, p.z);
                    // маятник под куполом, у земли затухает (как LootCrate.UpdateAirDrop)
                    float sway = Mathf.Clamp01(h / 6f);
                    c.Transform.rotation = Quaternion.Euler(Mathf.Sin(c.T * 1.9f + c.Phase) * 7f * sway,
                        c.Yaw + Mathf.Sin(c.T * 0.7f + c.Phase) * 10f, Mathf.Cos(c.T * 1.5f + c.Phase) * 6f * sway);
                    continue;
                }

                float since = c.T - c.Landed;
                if (c.Chute != null)
                {
                    // купол оседает набок и пропадает
                    float k = since / ChuteFoldTime;
                    if (k >= 1f)
                    {
                        Destroy(c.Chute.gameObject);
                        c.Chute = null;
                    }
                    else
                    {
                        c.Chute.localScale = new Vector3(ChuteScale * (1f + 0.3f * k), ChuteScale * (1f - k), ChuteScale * (1f - 0.6f * k));
                        c.Chute.localRotation = Quaternion.Euler(0f, 0f, 40f * k);
                    }
                }
                // постоял — ушёл в землю
                float sink = Mathf.Clamp01((since - CrateGroundTime) / 0.6f);
                if (sink > 0f)
                    c.Transform.localScale = Vector3.one * (1f - sink);
                if (sink >= 1f)
                {
                    Destroy(c.Transform.gameObject);
                    _crates.RemoveAt(i);
                }
            }
        }

        /// <summary>Новый ящик над случайной точкой четвёртого плана в правой, не закрытой панелью меню части экрана.</summary>
        void SpawnCrate(float startTime)
        {
            float depth = Random.Range(CrateDepthMin, CrateDepthMax);
            float halfWidth = depth * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * _cam.aspect;
            float x = _cam.transform.position.x + Mathf.Lerp(-halfWidth, halfWidth, Random.Range(0.42f, 0.95f));

            var go = new GameObject("Crate");
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(x, MenuGround.Top + CrateDropHeight, depth);
            var (bodyMesh, bodyMats) = Models.CrateBody();
            BoxModel.Spawn(bodyMesh, bodyMats, go.transform, Vector3.zero, "Body");
            var (lidMesh, lidMats) = Models.CrateLid();
            BoxModel.Spawn(lidMesh, lidMats, go.transform, new Vector3(0f, Models.CrateBodyHeight, -0.5f), "Lid");
            var (chuteMesh, chuteMats) = Models.Parachute();
            var chute = BoxModel.Spawn(chuteMesh, chuteMats, go.transform, new Vector3(0f, 0.3f, 0f), "Parachute").transform;
            chute.localScale = Vector3.one * ChuteScale;
            _crates.Add(new Crate
            {
                Transform = go.transform, Chute = chute, T = startTime, Yaw = Random.Range(0, 4) * 90f, Phase = Random.Range(0f, 10f)
            });
        }

        // ---------- трассеры ----------

        void SpawnTracer(Vector3 from, Vector3 dir, float distance)
        {
            Tracer tracer = _tracers.Find(t => t.Left <= 0f);
            if (tracer == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.name = "Tracer";
                go.transform.SetParent(_root, false);
                go.transform.localScale = new Vector3(0.05f, 0.05f, 0.9f);
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = RuntimeMaterials.Emissive(new Color(1f, 0.85f, 0.4f), 3f);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tracer = new Tracer { Transform = go.transform };
                _tracers.Add(tracer);
            }
            tracer.Transform.gameObject.SetActive(true);
            tracer.Transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
            tracer.Direction = dir;
            tracer.Left = distance;
        }

        void UpdateTracers(float dt)
        {
            foreach (var t in _tracers)
            {
                if (t.Left <= 0f)
                    continue;
                float step = BulletSpeed * dt;
                t.Transform.position += t.Direction * step;
                t.Left -= step;
                if (t.Left <= 0f || t.Transform.position.y < MenuGround.Top)
                {
                    t.Left = 0f;
                    t.Transform.gameObject.SetActive(false);
                }
            }
        }

        // ---------- ракеты ----------

        void UpdateRockets(float dt)
        {
            _rocketTimer -= dt;
            if (_rocketTimer <= 0f)
            {
                _rocketTimer = Random.Range(RocketIntervalMin, RocketIntervalMax);
                Launch(_nextRocketTeam);
                _nextRocketTeam = 1 - _nextRocketTeam;
            }

            for (int i = _rockets.Count - 1; i >= 0; i--)
            {
                var r = _rockets[i];
                r.T += dt;
                float u = Mathf.Clamp01(r.T / r.Duration);
                Vector3 pos = RocketPoint(r, u);
                Vector3 vel = (RocketPoint(r, Mathf.Min(1f, u + 0.01f)) - RocketPoint(r, Mathf.Max(0f, u - 0.01f))) / (0.02f * r.Duration);
                r.Transform.position = pos;
                if (vel.sqrMagnitude > 0.01f)
                    r.Transform.rotation = Quaternion.LookRotation(vel);

                Vector3 tail = pos - r.Transform.forward * Models.RocketTail;
                VoxelFx.RocketFlame(tail, -r.Transform.forward);
                r.SmokeTimer -= dt;
                while (r.SmokeTimer <= 0f)
                {
                    r.SmokeTimer += RocketSmokeInterval;
                    VoxelFx.RocketSmoke(tail, vel);
                }

                if (u >= 1f)
                {
                    Detonate(r.Target);
                    Destroy(r.Transform.gameObject);
                    _rockets.RemoveAt(i);
                }
            }
        }

        static Vector3 RocketPoint(MenuRocket r, float u)
        {
            Vector3 p = Vector3.Lerp(r.Start, r.Target, u);
            p.y += 4f * RocketArc * u * (1f - u);
            return p;
        }

        void Launch(int team)
        {
            var silos = _silos[team];
            Vector3 start = silos[Random.Range(0, silos.Count)];
            // в гущу врагов: рядом со случайным вражеским солдатом
            var enemy = PickEnemy(team);
            Vector3 target = enemy != null ? enemy.Position : RandomSpot(1 - team);
            target += new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
            target.y = MenuGround.Top;

            var go = new GameObject("Rocket");
            go.transform.SetParent(_root, false);
            go.transform.position = start;
            var (mesh, mats) = Models.Rocket(team);
            BoxModel.Spawn(mesh, mats, go.transform, Vector3.zero, "Model").transform.localScale = Vector3.one * Models.RocketScale;
            float distance = Vector3.Distance(start, target);
            _rockets.Add(new MenuRocket
            {
                Transform = go.transform, Start = start, Target = target, Duration = 2.2f + distance / 25f
            });
            Sfx.PlayAt(Sfx.Launch, start, 0.35f, 200f, 0.6f);
        }

        static readonly List<Vector3> Clods = new List<Vector3>();
        static readonly List<BlockKind> ClodKinds = new List<BlockKind>();

        /// <summary>Взрыв: гриб и комья земли, но без воронки; ближних подбрасывает.</summary>
        void Detonate(Vector3 pos)
        {
            VoxelFx.Mushroom(pos);
            Clods.Clear();
            ClodKinds.Clear();
            for (int i = 0; i < 24; i++)
            {
                Vector2 c = Random.insideUnitCircle * 3f;
                Clods.Add(pos + new Vector3(c.x, -0.5f, c.y));
                ClodKinds.Add(i % 3 == 0 ? BlockKind.Dirt : BlockKind.Grass);
            }
            VoxelFx.Debris(pos, Clods, ClodKinds);
            Sfx.PlayAt(Sfx.Explosion, pos, 0.6f, 300f, 0.55f);

            foreach (var f in _fighters)
            {
                var s = f.Soldier;
                Vector3 away = s.Position - pos;
                away.y = 0f;
                float d = away.magnitude;
                if (d > KnockRadius)
                    continue;
                float k = 1f - d / KnockRadius;
                Vector3 dir = d > 0.1f ? away / d : Random.insideUnitSphere;
                dir.y = 0f;
                s.Knock(dir.normalized * Mathf.Lerp(4f, 11f, k) + Vector3.up * Mathf.Lerp(6f, 13f, k), Random.Range(2f, 4f));
            }
        }
    }
}
