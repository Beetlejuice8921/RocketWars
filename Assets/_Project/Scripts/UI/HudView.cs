using System.Collections.Generic;
using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Launcher;
using RocketWars.Loot;
using RocketWars.Net;
using RocketWars.Player;
using RocketWars.Stats;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// HUD в игре: панель команд со счётом и фрагами (TeamBar), отсчёт и баннеры, полоса HP, лежит/падение, подъём союзника,
    /// подсказка пульта, метки HELP! над лежащими союзниками (видны сквозь стены), мини-карта, разминка,
    /// броня над полосой HP, патроны пулемёта, ящики с припасами и маяк.
    /// </summary>
    public class HudView
    {
        const float GoTime = 0.8f;

        readonly VisualElement _screen, _crosshair, _hp, _hpFill, _progress, _progressFill, _sosLayer;
        readonly VisualElement _minimap, _minimapMe, _helmetBrim, _binoculars, _scope;
        readonly Label _scopeDistance;
        readonly Label _protection, _binoDistance;
        // маркер попадания: четыре пиксельных штриха по диагоналям вокруг прицела
        readonly VisualElement _hitMarker;
        readonly VisualElement[] _hitBars = new VisualElement[4];
        static readonly Color HitColor = Color.white, KillColor = new Color(1f, 0.2f, 0.15f);
        readonly Image _minimapImage;
        readonly TeamBar _teamBar;
        readonly Label _banner, _subbanner, _countdown, _hpText;
        readonly Label _downed, _falling, _prompt, _info, _roomCode;
        readonly VisualElement _armor, _armorFill;
        readonly Label _armorText, _ammo, _loot, _beacon;
        const float LootToastTime = 3f;
        float _lootTime = -10f;
        NetworkPlayer _lootSource;
        readonly List<HelpMarker> _helpPool = new List<HelpMarker>();

        // ники над головами: только в прямой видимости, союзники — дальше, противники — ближе
        const float AllyNameRange = 60f, EnemyNameRange = 35f;
        const float NameHeight = 2.5f;
        readonly VisualElement _nameLayer;
        readonly List<Label> _namePool = new List<Label>();

        // лента событий (справа вверху, под мини-картой)
        const float FeedLifetime = 6f;
        const float FeedFade = 1f;
        const int FeedMax = 6;
        readonly VisualElement _feed;
        readonly List<(VisualElement row, float time)> _feedRows = new List<(VisualElement, float)>();

        /// <summary>
        /// Строку HUD собираем заново, только когда меняется её числовой ключ: иначе каждый кадр — десяток
        /// новых строк и периодические подвисания от сборщика мусора.
        /// </summary>
        sealed class TextKey
        {
            long _key = long.MinValue;

            public bool Changed(long key)
            {
                if (key == _key)
                    return false;
                _key = key;
                return true;
            }

            /// <summary>Текст стёрли в обход ключа — следующий Changed обязательно вернёт true.</summary>
            public void Reset() => _key = long.MinValue;
        }

        readonly TextKey _hpKey = new TextKey(), _armorKey = new TextKey(), _ammoKey = new TextKey(), _beaconKey = new TextKey();
        readonly TextKey _infoKey = new TextKey(), _fallKey = new TextKey(), _distKey = new TextKey(), _countdownKey = new TextKey();
        string _roomCodeSource, _countdownText = "";

        // компоненты локального игрока (раньше — по GetComponent на каждый кадр)
        NetworkPlayer _compsOf;
        FallRespawn _fall;
        ReviveInteraction _revive;
        RocketRider _rider;
        CrateInteraction _crates;
        FlagInteraction _flags;
        PistolView _pistol;
        Binoculars _bino;
        FpsController _fps;

        static readonly RaycastHit[] Hits = new RaycastHit[32];

        public HudView(VisualElement root)
        {
            _screen = Ui.El(root, "layer", "hud");
            _screen.pickingMode = PickingMode.Ignore;

            // ники над игроками — под метками HELP!
            _nameLayer = Ui.El(_screen, "layer");
            _nameLayer.pickingMode = PickingMode.Ignore;

            _sosLayer = Ui.El(_screen, "layer");
            _sosLayer.pickingMode = PickingMode.Ignore;

            // край каски сверху экрана в приседе (под счётом и прицелом)
            _helmetBrim = Ui.El(_screen, "helmet-brim");
            BuildHelmetArc(_helmetBrim);

            // вид в бинокль: маска двух окуляров, сетка, дальномер
            _binoculars = Ui.El(_screen, "layer", "binoculars");
            var mask = new Image { image = BuildBinocularMask(), scaleMode = ScaleMode.StretchToFill };
            mask.AddToClassList("layer");
            _binoculars.Add(mask);
            Ui.El(_binoculars, "bino-line-h");
            Ui.El(_binoculars, "bino-line-v");
            for (int i = -4; i <= 4; i++)
            {
                if (i == 0) continue;
                var tick = Ui.El(_binoculars, "bino-tick");
                tick.style.left = Length.Percent(50f + i * 3f);
            }
            Ui.Text(_binoculars, "×5", "bino-zoom", "shadow");
            _binoDistance = Ui.Text(_binoculars, "", "bino-distance", "shadow");

            // оптика снайперки: один круглый окуляр, тонкое перекрестие с толстыми «пеньками» по краям, метки
            _scope = Ui.El(_screen, "layer", "binoculars");
            var scopeMask = new Image { image = BuildScopeMask(), scaleMode = ScaleMode.StretchToFill };
            scopeMask.AddToClassList("layer");
            _scope.Add(scopeMask);
            Ui.El(_scope, "scope-line-h");
            Ui.El(_scope, "scope-line-v");
            Ui.El(_scope, "scope-post-l");
            Ui.El(_scope, "scope-post-r");
            Ui.El(_scope, "scope-post-b");
            for (int i = 1; i <= 4; i++)
            {
                var dot = Ui.El(_scope, "scope-dot");
                dot.style.top = Length.Percent(50f + i * 4f);
            }
            Ui.El(_scope, "scope-center");
            Ui.Text(_scope, "×4", "scope-zoom", "shadow");
            _scopeDistance = Ui.Text(_scope, "", "scope-distance", "shadow");

            _crosshair = Ui.El(_screen, "crosshair");
            Ui.El(_crosshair, "crosshair-h");
            Ui.El(_crosshair, "crosshair-v");
            _hitMarker = Ui.El(_screen, "hitmarker");
            for (int i = 0; i < 4; i++)
            {
                // штрих в 45° от центра в каждую четверть: (±1, ±1)
                float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? -1f : 1f;
                var bar = Ui.El(_hitMarker, "hm-bar");
                bar.style.left = sx * 15f - 3f;
                bar.style.top = sy * 15f - 9f;
                bar.style.rotate = new Rotate(sx * sy > 0 ? -45f : 45f);
                _hitBars[i] = bar;
            }

            // счёт, раунд и игроки обеих команд с фрагами (как в Counter-Strike)
            _teamBar = new TeamBar(_screen);

            _roomCode = Ui.Text(_screen, "", "room-code", "shadow");
            _info = Ui.Text(_screen, "", "info", "shadow");

            _minimap = Ui.El(_screen, "minimap");
            _minimapImage = new Image { scaleMode = ScaleMode.StretchToFill };
            _minimapImage.style.flexGrow = 1;
            _minimap.Add(_minimapImage);
            _minimapMe = Ui.El(_minimap, "minimap-me");

            _feed = Ui.El(_screen, "feed");
            KillFeed.Received += OnFeed;

            _banner = Ui.Text(_screen, "", "center-text", "banner", "shadow");
            _subbanner = Ui.Text(_screen, "", "center-text", "subbanner", "shadow");
            _countdown = Ui.Text(_screen, "", "center-text", "countdown", "shadow");
            _downed = Ui.Text(_screen, Loc.T("ВЫ ЛЕЖИТЕ — ждите союзника"), "center-text", "downed", "shadow");
            _falling = Ui.Text(_screen, "", "center-text", "falling", "shadow");
            _prompt = Ui.Text(_screen, "", "center-text", "prompt", "shadow");
            _protection = Ui.Text(_screen, Loc.T("ЗАЩИТА ПОВЫШЕНА · урон -33%"), "center-text", "protection", "shadow");

            _progress = Ui.El(_screen, "progress");
            _progressFill = Ui.El(_progress, "progress-fill");

            _hp = Ui.El(_screen, "hp");
            _hpFill = Ui.El(_hp, "hp-fill");
            _hpText = Ui.Text(_hp, "100", "hp-text", "shadow");

            _armor = Ui.El(_screen, "armor");
            _armorFill = Ui.El(_armor, "armor-fill");
            _armorText = Ui.Text(_armor, "", "armor-text", "shadow");
            _ammo = Ui.Text(_screen, "", "ammo", "shadow");
            _loot = Ui.Text(_screen, "", "center-text", "loot", "shadow");
            _beacon = Ui.Text(_screen, "", "center-text", "beacon", "shadow");

            foreach (var e in _screen.Query<VisualElement>().ToList())
                e.pickingMode = PickingMode.Ignore;
            Ui.Show(_screen, false);
        }

        public void SetVisible(bool visible) => Ui.Show(_screen, visible);

        public void Dispose()
        {
            KillFeed.Received -= OnFeed;
            WatchLoot(null);
        }

        /// <summary>Подписка на «что выпало из ящика» у текущего локального игрока.</summary>
        void WatchLoot(NetworkPlayer me)
        {
            if (_lootSource == me)
                return;
            if (_lootSource != null)
                _lootSource.LootReceived -= OnLoot;
            _lootSource = me;
            if (me != null)
                me.LootReceived += OnLoot;
        }

        static string LootName(LootKind kind) => kind switch
        {
            LootKind.MachineGun => Loc.T("ПУЛЕМЁТ"),
            LootKind.Sniper => Loc.T("СНАЙПЕРКА"),
            LootKind.Bazooka => Loc.T("БАЗУКА"),
            LootKind.Armor => Loc.T("БРОНЕЖИЛЕТ"),
            LootKind.Beacon => Loc.T("МАЯК"),
            _ => Loc.T("АПТЕЧКИ"),
        };

        void OnLoot(LootKind kind)
        {
            _loot.text = kind switch
            {
                LootKind.MachineGun => Loc.F("ПУЛЕМЁТ · {0} патронов", MachineGunSpec.Ammo),
                LootKind.Sniper => Loc.F("СНАЙПЕРКА · {0} патрона · {aim} - прицел ×4", SniperSpec.Ammo),
                LootKind.Bazooka => Loc.F("БАЗУКА · {0} заряда · осторожно, задевает своих", BazookaSpec.Ammo),
                LootKind.Armor => Loc.F("БРОНЕЖИЛЕТ · +{0} брони", PlayerHealth.MaxArmor.ToString("0")),
                LootKind.Beacon => Loc.T("МАЯК · вражеские шахты видны на пульте"),
                _ => Loc.F("АПТЕЧКИ ×{0} · {use} — вылечиться", LootCrate.MedkitCount),
            };
            _lootTime = Time.time;
        }

        /// <summary>Новая строка ленты: «кто [иконка] кого», цвета команд, свои события подсвечены рамкой.</summary>
        void OnFeed(FeedEntry e)
        {
            var row = Ui.El(_feed, "feed-row");
            row.pickingMode = PickingMode.Ignore;
            ulong me = NetworkPlayer.LocalPlayer != null ? NetworkPlayer.LocalPlayer.NetworkObjectId : ulong.MaxValue - 1;
            if (e.ActorId == me || e.TargetId == me)
                row.AddToClassList("feed-me");

            if (!string.IsNullOrEmpty(e.Actor))
                Ui.Text(row, e.Actor, "feed-name", e.ActorTeam == 0 ? "team0" : "team1");
            var icon = new Image { image = KillFeed.Icon(e.Kind), scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("feed-icon");
            row.Add(icon);
            Ui.Text(row, KillFeed.TargetText(e), "feed-name", e.TargetTeam == 0 ? "team0" : "team1");
            foreach (var child in row.Children())
                child.pickingMode = PickingMode.Ignore;

            _feedRows.Add((row, Time.unscaledTime));
            while (_feedRows.Count > FeedMax)
            {
                _feedRows[0].row.RemoveFromHierarchy();
                _feedRows.RemoveAt(0);
            }
        }

        void UpdateFeed()
        {
            float now = Time.unscaledTime;
            for (int i = _feedRows.Count - 1; i >= 0; i--)
            {
                float age = now - _feedRows[i].time;
                if (age > FeedLifetime)
                {
                    _feedRows[i].row.RemoveFromHierarchy();
                    _feedRows.RemoveAt(i);
                    continue;
                }
                _feedRows[i].row.style.opacity = Mathf.Clamp01((FeedLifetime - age) / FeedFade);
            }
            // лента под мини-картой, высота которой зависит от карты
            _feed.style.top = Ui.IsShown(_minimap) ? _minimap.layout.yMax + 12f : 20f;
        }

        /// <summary>Крупные надписи прячутся, пока открыта пауза или итоги.</summary>
        public void SetBannersHidden(bool hidden)
        {
            Ui.Show(_banner, !hidden);
            Ui.Show(_subbanner, !hidden);
        }

        public void Update()
        {
            var me = NetworkPlayer.LocalPlayer;
            var health = me != null ? me.Health : null;
            WatchLoot(me);
            CacheComponents(me);

            UpdateMatch(health);
            UpdatePlayer(me, health);
            UpdateNames(me, health);
            UpdateSos(health);
            UpdateMinimap(me, health);
            UpdateFeed();

            var session = SessionService.Current;
            string code = session != null ? session.Code : null;
            if (!ReferenceEquals(code, _roomCodeSource))
            {
                _roomCodeSource = code;
                _roomCode.text = code != null ? Loc.F("Код комнаты: {0}", code) : "";
            }
        }

        void CacheComponents(NetworkPlayer me)
        {
            // часть компонентов LocalPlayerRig добавляет позже спавна (FlagInteraction — последним): до тех пор ищем заново
            if (_compsOf == me && (me == null || _flags != null))
                return;
            _compsOf = me;
            _fall = me != null ? me.GetComponent<FallRespawn>() : null;
            _revive = me != null ? me.GetComponent<ReviveInteraction>() : null;
            _rider = me != null ? me.GetComponent<RocketRider>() : null;
            _crates = me != null ? me.GetComponent<CrateInteraction>() : null;
            _flags = me != null ? me.GetComponent<FlagInteraction>() : null;
            _pistol = me != null ? me.GetComponent<PistolView>() : null;
            _bino = me != null ? me.GetComponent<Binoculars>() : null;
            _fps = me != null ? me.GetComponent<FpsController>() : null;
        }

        void UpdateMatch(PlayerHealth health)
        {
            var match = MatchManager.Instance;
            bool hasMatch = match != null && match.IsSpawned;
            var s = hasMatch ? match.State : default;
            int myTeam = health != null ? health.Team : -1;

            _teamBar.Update(hasMatch && s.Phase != MatchPhase.Lobby, s);

            string banner = "", sub = "", countdown = "";
            Color bannerColor = Color.white;
            bool lobby = hasMatch && s.Phase == MatchPhase.Lobby, blink = false;
            if (hasMatch)
            {
                switch (s.Phase)
                {
                    case MatchPhase.Lobby:
                        banner = Loc.T("РАЗМИНКА");
                        sub = LobbyText(match, out blink);
                        break;
                    case MatchPhase.Countdown:
                        int left = Mathf.Max(1, Mathf.CeilToInt((float)(s.PhaseEnd - NetClock.Now)));
                        if (_countdownKey.Changed(left))
                            _countdownText = $"{left}...";
                        countdown = _countdownText;
                        break;
                    case MatchPhase.Round:
                        if (NetClock.Now - s.PhaseEnd < GoTime)
                            countdown = "GO!";
                        break;
                    case MatchPhase.RoundEnd:
                        banner = s.Winner < 0 ? Loc.T("НИЧЬЯ") : Loc.F("РАУНД ВЫИГРАЛИ {0}", RuntimeMaterials.TeamName(s.Winner));
                        sub = s.Winner < 0 ? Loc.T("раунд переигрывается") : "";
                        if (s.Winner >= 0) bannerColor = RuntimeMaterials.TeamColor(s.Winner);
                        break;
                    case MatchPhase.MatchEnd:
                        banner = myTeam < 0 ? Loc.F("ПОБЕДИЛИ {0}", RuntimeMaterials.TeamName(s.Winner)) : myTeam == s.Winner ? Loc.T("ПОБЕДА!") : Loc.T("ПОРАЖЕНИЕ");
                        bannerColor = RuntimeMaterials.TeamColor(s.Winner);
                        sub = TouchInput.Enabled
                            ? match.IsServer ? Loc.T("[СТАРТ] — новый матч") : ""
                            : match.IsServer ? Loc.T("Enter — новый матч, Tab — итоги") : Loc.T("Tab — итоги");
                        break;
                }
            }

            _banner.text = banner;
            _banner.style.color = bannerColor;
            _subbanner.text = sub;
            _subbanner.EnableInClassList("subbanner-lobby", lobby);
            // «Enter — начать матч» мигает: 0.5 с видно, 0.5 с почти погасло
            _subbanner.style.opacity = blink && Time.unscaledTime % 1f >= 0.5f ? 0.15f : 1f;
            _countdown.text = countdown;
        }

        /// <summary>Подсказка разминки; blink — это призыв к хосту начать матч (мигает).</summary>
        static string LobbyText(MatchManager match, out bool blink)
        {
            blink = false;
            if (!match.IsServer)
                return Loc.T("Матч начнёт хост");
            string blocker = match.StartBlocker();
            if (blocker != null)
                return blocker;
            blink = true;
            return Loc.T(TouchInput.Enabled ? "[СТАРТ] — начать матч" : "Enter — начать матч");
        }

        void UpdatePlayer(NetworkPlayer me, PlayerHealth health)
        {
            bool alive = health != null && !health.IsDowned;
            Ui.Show(_hp, health != null);
            Ui.Show(_crosshair, alive);
            Ui.Show(_downed, health != null && health.IsDowned);

            if (health != null)
            {
                float k = health.Hp / PlayerHealth.MaxHp;
                _hpFill.style.width = Length.Percent(k * 100f);
                int hp = Mathf.CeilToInt(health.Hp);
                if (_hpKey.Changed(hp))
                    _hpText.text = hp.ToString();
            }
            UpdateLoot(me, health);

            var fall = _fall;
            bool isFalling = fall != null && fall.IsFalling;
            if (_fallKey.Changed(isFalling ? Mathf.RoundToInt(fall.TimeLeft * 10f) : -1))
                _falling.text = isFalling ? Loc.F("Падение… {0}", fall.TimeLeft.ToString("0.0")) : "";

            // подъём союзника важнее подсказки пульта
            var revive = _revive;
            var interactor = PlayerInteractor.Local;
            string prompt = "";
            bool showProgress = false, useContext = false; // useContext — на телефоне показать кнопку «Действие»
            if (revive != null && revive.Target != null)
            {
                useContext = true;
                prompt = revive.IsReviving ? Loc.F("Поднимаем {0}…", revive.Target.DisplayName) : Loc.F("Удерживайте {use} — поднять {0}", revive.Target.DisplayName);
                showProgress = true;
                _progressFill.style.width = Length.Percent(revive.Progress01 * 100f);
            }
            else if (me != null && _rider is RocketRider rider && (rider.IsRiding || rider.HasGrabTarget))
            {
                useContext = rider.IsRiding || !rider.TargetFull;
                prompt = rider.InFlight ? Loc.T("Отпустите {use} — спрыгнуть · {trick} — трюк")
                    : rider.IsRiding ? Loc.T("Отпустите {use} — спрыгнуть")
                    : rider.TargetFull ? Loc.T("На ракете нет места (4 из 4)")
                    : Loc.T("Удерживайте {use} — сесть на ракету");
            }
            else if (me != null && me.ParachuteOpen)
            {
                prompt = Loc.T("{jump} — отстегнуть парашют");
            }
            else if (me != null && _crates is CrateInteraction crates && crates.Target != null)
            {
                var crate = crates.Target;
                if (crate.State == CrateState.Closed)
                {
                    prompt = crates.IsOpening ? Loc.T("Открываем ящик…") : Loc.T("Удерживайте {use} — открыть ящик");
                    useContext = true;
                    showProgress = true;
                    _progressFill.style.width = Length.Percent(crates.Progress01 * 100f);
                }
                else if (crate.HasItem)
                {
                    prompt = Loc.F("{use} - взять: {0}", LootName(crate.Kind));
                    useContext = true;
                }
                else
                {
                    useContext = crate.Medkits > 0 && (health == null || health.Hp < PlayerHealth.MaxHp);
                    prompt = health != null && health.Hp >= PlayerHealth.MaxHp
                        ? Loc.F("Аптечек в ящике: {0} · здоровье полное", crate.Medkits)
                        : Loc.F("{use} — аптечка +{0} HP (в ящике: {1})", LootCrate.MedkitHeal.ToString("0"), crate.Medkits);
                }
            }
            else if (me != null && _flags is FlagInteraction flags && flags.Target != null)
            {
                prompt = Loc.F("{use} - забрать флаг {0}", RuntimeMaterials.TeamNameGenitive(flags.Target.Team));
                useContext = true;
            }
            else if (interactor != null && interactor.Hovered != null)
            {
                var b = interactor.Hovered;
                string name = b.Kind == ConsoleButtonKind.Launch ? "LAUNCH" : b.Kind.ToString();
                prompt = b.IsInteractable ? Loc.F("{fire} — нажать {0}", name)
                    : b.Console.IsLockedFor(NetClock.LocalClientId) ? Loc.T("Пультом пользуется другой игрок")
                    : !MatchManager.ConsolesEnabled ? Loc.T("Пульты включатся в начале раунда")
                    : Loc.F("{0} недоступна", name);
            }
            _prompt.text = prompt;
            TouchInput.UseContext = useContext;
            Ui.Show(_progress, showProgress);
            UpdateHelmet(me, health);

            if (health == null)
            {
                _info.text = "";
                _infoKey.Reset();
                return;
            }

            int ready = 0, alivesilos = 0;
            foreach (var silo in RocketSilo.All)
            {
                if (silo.Team != health.Team || silo.State == SiloState.Destroyed)
                    continue;
                alivesilos++;
                if (silo.State == SiloState.Ready) ready++;
            }
            if (_infoKey.Changed((uint)health.Team | (long)alivesilos << 8 | (long)ready << 16))
            {
                _info.text = Loc.F("Команда: {0}", RuntimeMaterials.TeamName(health.Team)) + "\n" +
                             Loc.F("Шахт: {0} · готово: {1}", alivesilos, ready) +
                             (TouchInput.Enabled ? "" : "\n" + Loc.T("Tab — итоги") + "\n" + Loc.T("Esc — пауза")); // на телефоне — кнопки «II» и «СЧЁТ»
            }
        }

        /// <summary>Броня над полосой HP, патроны пулемёта, что выпало из ящика, таймер маяка.</summary>
        void UpdateLoot(NetworkPlayer me, PlayerHealth health)
        {
            float armor = health != null ? health.Armor : 0f;
            Ui.Show(_armor, armor > 0f);
            // у берсерка брони 200 — полоса считается от неё, а не от бронежилета (100)
            float armorMax = Mathf.Max(PlayerHealth.MaxArmor, me != null && me.IsBerserk ? BerserkSpec.Armor : 0f);
            _armorFill.style.width = Length.Percent(Mathf.Clamp01(armor / armorMax) * 100f);
            int armorValue = Mathf.CeilToInt(armor);
            if (armorValue > 0 && _armorKey.Changed(armorValue))
                _armorText.text = Loc.F("БРОНЯ {0}", armorValue);

            var weapon = _pistol;
            bool gold = weapon != null && weapon.HoldingGoldGuns && health != null && !health.IsDowned;
            bool gun = !gold && weapon != null && weapon.HoldingMachineGun && health != null && !health.IsDowned;
            bool sniper = !gold && weapon != null && weapon.HoldingSniper && health != null && !health.IsDowned;
            bool bazooka = !gold && weapon != null && weapon.HoldingBazooka && health != null && !health.IsDowned;
            Ui.Show(_ammo, gun || gold || sniper || bazooka);
            // ключ: оружие (1–4) и остаток патронов
            long ammoKey = gold ? 1 : sniper ? 2L << 32 | (uint)weapon.SniperAmmo : bazooka ? 3L << 32 | (uint)weapon.BazookaAmmo
                : gun ? 4L << 32 | (uint)weapon.MachineGunAmmo : 0;
            if (_ammoKey.Changed(ammoKey))
            {
                if (gold)
                    _ammo.text = Loc.T("ЗОЛОТЫЕ ПУЛЕМЁТЫ  БЕЗ ЛИМИТА");
                else if (sniper)
                    _ammo.text = $"{Loc.T("СНАЙПЕРКА")}  {weapon.SniperAmmo} / {SniperSpec.Ammo}";
                else if (bazooka)
                    _ammo.text = $"{Loc.T("БАЗУКА")}  {weapon.BazookaAmmo} / {BazookaSpec.Ammo}";
                else if (gun)
                    _ammo.text = $"{Loc.T("ПУЛЕМЁТ")}  {weapon.MachineGunAmmo} / {MachineGunSpec.Ammo}";
            }

            float since = Time.time - _lootTime;
            Ui.Show(_loot, since < LootToastTime);
            _loot.style.opacity = Mathf.Clamp01((LootToastTime - since) / 0.5f);

            float beacon = me != null ? me.BeaconTimeLeft : 0f;
            var carrying = _flags;
            bool flag = carrying != null && carrying.Carrying != null;
            bool berserk = me != null && me.IsBerserk;
            bool showBeacon = beacon > 0f || flag || berserk;
            Ui.Show(_beacon, showBeacon);
            // ключ: что показываем (флаг какой команды / превращение / берсерк / маяк) и секунды
            long beaconKey = !showBeacon ? -1
                : flag ? 1L << 32 | (uint)carrying.Carrying.Team
                : berserk ? (me.InBerserkTransform ? 2L << 32 : 3L << 32 | (uint)Mathf.CeilToInt(me.BerserkTimeLeft))
                : 4L << 32 | (uint)Mathf.CeilToInt(beacon);
            if (!_beaconKey.Changed(beaconKey) || !showBeacon)
                return;
            _beacon.text = flag
                ? Loc.F("ВЫ НЕСЁТЕ ФЛАГ {0} - доставьте его на свой остров", RuntimeMaterials.TeamNameGenitive(carrying.Carrying.Team))
                : berserk
                    ? (me.InBerserkTransform ? Loc.T("ПРЕВРАЩЕНИЕ В БЕРСЕРКА!") : Loc.F("РЕЖИМ БЕРСЕРКА - {0} с", Mathf.CeilToInt(me.BerserkTimeLeft)))
                    : Loc.F("МАЯК: вражеские шахты на пульте - {0} с", Mathf.CeilToInt(beacon));
        }

        /// <summary>Столбиков в дуге каски и их высота у краёв экрана (доля от высоты в центре).</summary>
        const int HelmetColumns = 16;
        const float HelmetEdge = 0.15f, HelmetStep = 0.1f;

        /// <summary>
        /// Край каски — дуга из узких столбиков разной высоты (ниже всего в центре), ступеньками по-пиксельному:
        /// скругление одного прямоугольника не может быть больше его высоты, и край выходил почти прямым.
        /// </summary>
        static void BuildHelmetArc(VisualElement brim)
        {
            for (int i = 0; i < HelmetColumns; i++)
            {
                float x = (i + 0.5f) / HelmetColumns * 2f - 1f;
                float h = Mathf.Lerp(HelmetEdge, 1f, Mathf.Sqrt(1f - x * x));
                h = Mathf.Round(h / HelmetStep) * HelmetStep;
                var col = Ui.El(brim, "helmet-col");
                col.style.left = Length.Percent(100f * i / HelmetColumns);
                col.style.width = Length.Percent(100f / HelmetColumns);
                col.style.height = Length.Percent(100f * h);
                Ui.El(col, "helmet-band");
                Ui.El(col, "helmet-lip");
            }
        }

        /// <summary>В приседе сверху опускается край каски и выводится, что защита повышена.</summary>
        void UpdateHelmet(NetworkPlayer me, PlayerHealth health)
        {
            // бинокль у глаз: маска окуляров вместо прицела и каски
            var bino = _bino;
            float b = bino != null ? bino.Amount : 0f;
            bool scope = bino != null && bino.IsScope;
            Ui.Show(_binoculars, b > 0.01f && !scope);
            _binoculars.style.opacity = b;
            Ui.Show(_scope, b > 0.01f && scope);
            _scope.style.opacity = b;
            bool hasDistance = bino != null && bino.Distance > 0f;
            if (_distKey.Changed(hasDistance ? Mathf.RoundToInt(bino.Distance) : -1))
            {
                string distance = hasDistance ? Loc.F("ДИСТ {0} м", bino.Distance.ToString("0")) : Loc.T("ДИСТ —");
                _binoDistance.text = distance;
                _scopeDistance.text = distance;
            }
            if (b > 0.5f)
                Ui.Show(_crosshair, false);

            // маркер попадания из пистолета
            var pistol = _pistol;
            float sinceHit = pistol != null ? Time.time - pistol.LastHitTime : 99f;
            bool hitShown = sinceHit < PistolView.HitMarkerTime;
            Ui.Show(_hitMarker, hitShown);
            if (hitShown)
            {
                // «выпрыгивает» крупнее и сжимается к прицелу, потом тает; положил цель — красный и больше
                bool kill = pistol.LastHitKnockedDown;
                float pop = Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(sinceHit / 0.08f)) * (kill ? 1.35f : 1f);
                _hitMarker.style.scale = new Scale(Vector3.one * pop);
                _hitMarker.style.opacity = Mathf.Clamp01((PistolView.HitMarkerTime - sinceHit) / 0.2f);
                foreach (var bar in _hitBars)
                    bar.style.backgroundColor = kill ? KillColor : HitColor;
            }

            var fps = _fps;
            float k = fps != null && health != null && !health.IsDowned && b < 0.5f ? fps.CrouchAmount : 0f;
            // плавный «въезд»: при k = 0 каска целиком за верхним краем экрана
            _helmetBrim.style.translate = new Translate(0, Length.Percent(-105f * (1f - k)));
            Ui.Show(_helmetBrim, k > 0.001f);
            Ui.Show(_protection, k > 0.6f);
            _protection.style.opacity = Mathf.InverseLerp(0.6f, 1f, k);
        }

        /// <summary>Чёрная маска с двумя круглыми окулярами (мягкий край), генерируется один раз.</summary>
        /// <summary>Маска оптики: один круг по центру, тёмное кольцо по краю линзы.</summary>
        static Texture2D BuildScopeMask()
        {
            const int w = 1024, h = 576;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ScopeMask" };
            var pixels = new Color32[w * h];
            float r = h * 0.46f, edge = 6f, ring = 14f;
            var c = new Vector2(w * 0.5f, h * 0.5f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) - r;
                float outside = Mathf.Clamp01(d / edge + 0.5f);
                float shade = Mathf.Clamp01((d + ring) / ring) * 0.55f; // затемнение у края линзы
                pixels[y * w + x] = new Color32(0, 0, 0, (byte)(Mathf.Max(outside, shade) * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D BuildBinocularMask()
        {
            const int w = 1024, h = 576;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BinocularMask" };
            var pixels = new Color32[w * h];
            float r = h * 0.47f, edge = 10f;
            var c1 = new Vector2(w * 0.5f - r * 0.62f, h * 0.5f);
            var c2 = new Vector2(w * 0.5f + r * 0.62f, h * 0.5f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(Vector2.Distance(p, c1), Vector2.Distance(p, c2)) - r;
                byte a = (byte)(Mathf.Clamp01(d / edge + 0.5f) * 255f);
                pixels[y * w + x] = new Color32(0, 0, 0, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// Ники над стоящими игроками: мелко, в цвет команды, без плашки. Только если голову видно напрямую
        /// (сквозь стены не выдаём, где прячется противник); с расстоянием мельчают и тают.
        /// </summary>
        void UpdateNames(NetworkPlayer me, PlayerHealth health)
        {
            var cam = Camera.main;
            int used = 0;
            if (health != null && cam != null && _screen.panel != null)
            {
                Vector3 eye = cam.transform.position;
                foreach (var p in PlayerHealth.All)
                {
                    if (p == health || !p.IsSpawned || p.IsDowned)
                        continue;
                    float range = p.Team == health.Team ? AllyNameRange : EnemyNameRange;
                    // тело может быть сдвинуто относительно объекта (висит на ракете) — берём его
                    var body = p.transform.Find("Body");
                    Vector3 head = (body != null ? body.position : p.transform.position) + Vector3.up * NameHeight;
                    float dist = Vector3.Distance(eye, head);
                    if (dist > range || !HeadVisible(eye, head, p, me))
                        continue;
                    var pos = Ui.WorldToPanel(_screen.panel, cam, head);
                    if (pos == null)
                        continue;

                    if (used == _namePool.Count)
                    {
                        var l = Ui.Text(_nameLayer, "", "nametag", "shadow");
                        l.pickingMode = PickingMode.Ignore;
                        _namePool.Add(l);
                    }
                    var label = _namePool[used++];
                    Ui.Show(label, true);
                    Ui.Place(label, pos.Value);
                    label.text = p.DisplayName;
                    label.EnableInClassList("team0", p.Team == 0);
                    label.EnableInClassList("team1", p.Team != 0);
                    float k = dist / range;
                    label.style.fontSize = Mathf.Lerp(12f, 9f, k);
                    label.style.opacity = 0.9f * Mathf.Clamp01((1f - k) / 0.3f);
                }
            }

            for (int i = used; i < _namePool.Count; i++)
                Ui.Show(_namePool[i], false);
        }

        /// <summary>Ничего не заслоняет голову игрока target (кроме него самого и нас).</summary>
        static bool HeadVisible(Vector3 eye, Vector3 head, PlayerHealth target, NetworkPlayer me)
        {
            Vector3 dir = head - eye;
            float length = dir.magnitude;
            int count = Physics.RaycastNonAlloc(eye, dir / length, Hits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var owner = Hits[i].collider.GetComponentInParent<PlayerHealth>();
                if (owner == target || (me != null && owner == me.Health))
                    continue;
                return false;
            }
            return true;
        }

        /// <summary>Метка над лежащим союзником: расстояние и вылетающие вверх надписи HELP!.</summary>
        sealed class HelpMarker
        {
            public VisualElement Root;
            public Label Distance;
            public Label[] Rising;
            public int Meters = -1;
        }

        const float HelpRisePeriod = 1.2f;
        const float HelpRiseHeight = 140f;
        /// <summary>Метка в полный размер ближе HelpNear метров и вчетверо меньше дальше HelpFar (союзник на другом острове).</summary>
        const float HelpNear = 15f, HelpFar = 45f, HelpFarScale = 0.25f;

        HelpMarker CreateHelpMarker()
        {
            var m = new HelpMarker { Root = Ui.El(_sosLayer, "help") };
            // две вылетающие надписи со сдвигом в полпериода: поток не прерывается
            m.Rising = new[] { Ui.Text(m.Root, "HELP!", "help-rise", "shadow"), Ui.Text(m.Root, "HELP!", "help-rise", "shadow") };
            m.Distance = Ui.Text(m.Root, "", "help-dist", "shadow");
            m.Root.pickingMode = PickingMode.Ignore;
            foreach (var child in m.Root.Children())
                child.pickingMode = PickingMode.Ignore;
            return m;
        }

        void UpdateSos(PlayerHealth health)
        {
            var cam = Camera.main;
            int used = 0;
            float now = Time.unscaledTime;
            if (health != null && cam != null && _screen.panel != null)
            {
                foreach (var p in PlayerHealth.All)
                {
                    if (p == health || !p.IsSpawned || !p.IsDowned || p.Team != health.Team)
                        continue;
                    // над лежащим телом, чтобы метка его не закрывала
                    var pos = Ui.WorldToPanel(_screen.panel, cam, p.transform.position + Vector3.up * 2.2f);
                    if (pos == null)
                        continue;
                    if (used == _helpPool.Count)
                        _helpPool.Add(CreateHelpMarker());
                    var m = _helpPool[used++];
                    Ui.Show(m.Root, true);
                    Ui.Place(m.Root, pos.Value);
                    int meters = Mathf.RoundToInt(Vector3.Distance(cam.transform.position, p.transform.position));
                    if (m.Meters != meters)
                    {
                        m.Meters = meters;
                        m.Distance.text = Loc.F("{0} м", meters);
                        // издалека метка мельчает (точка привязки 0×0 — масштаб вокруг неё), расстояние остаётся читаемым
                        float size = Mathf.Lerp(1f, HelpFarScale, Mathf.InverseLerp(HelpNear, HelpFar, meters));
                        m.Root.style.scale = new Scale(Vector3.one * size);
                        m.Distance.style.scale = new Scale(Vector3.one * Mathf.Max(1f, 0.6f / size));
                    }

                    // у каждого лежащего — своя фаза
                    float phase = p.NetworkObjectId * 0.37f;

                    // копии надписи улетают вверх, растут и тают
                    for (int i = 0; i < m.Rising.Length; i++)
                    {
                        float k = Mathf.Repeat((now + phase) / HelpRisePeriod + i / (float)m.Rising.Length, 1f);
                        var rise = m.Rising[i];
                        rise.style.translate = new Translate(0, -HelpRiseHeight * k);
                        rise.style.scale = new Scale(Vector3.one * Mathf.Lerp(0.9f, 1.35f, k));
                        rise.style.opacity = Mathf.Clamp01(1.2f * (1f - k)) * Mathf.Clamp01(k * 6f);
                    }
                }
            }

            for (int i = used; i < _helpPool.Count; i++)
                Ui.Show(_helpPool[i].Root, false);
        }

        void UpdateMinimap(NetworkPlayer me, PlayerHealth health)
        {
            var map = TacticalMap.Instance;
            var tex = map != null && health != null ? map.GetTexture(health.Team) : null;
            Ui.Show(_minimap, tex != null);
            if (tex == null)
                return;

            if (_minimapImage.image != tex)
            {
                _minimapImage.image = tex;
                _minimap.style.height = 300f * tex.height / tex.width;
            }

            var uv = map.WorldToScreen(health.Team, me.transform.position);
            _minimapMe.style.left = Length.Percent(Mathf.Clamp01(uv.x) * 100f);
            _minimapMe.style.top = Length.Percent((1f - Mathf.Clamp01(uv.y)) * 100f);
        }
    }
}
