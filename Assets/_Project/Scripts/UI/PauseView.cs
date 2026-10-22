using System;
using RocketWars.Core;
using RocketWars.Net;
using RocketWars.Player;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>Пауза по Esc: продолжить, (хост) начать матч, сменить команду (лобби / после матча), опции, выйти из комнаты.</summary>
    public class PauseView
    {
        readonly VisualElement _screen;
        readonly Button _start;
        readonly Button _switchTeam;
        readonly Label _switchBlocker;
        readonly Label _room;
        readonly Button _invite;
        readonly Button _streamPanel;

        public PauseView(VisualElement root, Action resume)
        {
            _screen = Ui.El(root, "layer", "overlay");
            var panel = Ui.El(_screen, "panel");
            panel.style.width = 520;
            Ui.Text(panel, Loc.T("ПАУЗА"), "title", "shadow");
            _room = Ui.Text(panel, "", "label-dim");
            // Telegram: ссылка-приглашение в чат — друг откроет игру сразу в этой комнате
            _invite = Ui.Btn(panel, Loc.T("Позвать друга"), () => GamePlatform.Current.Invite(SessionService.Current?.Code));

            Ui.Btn(panel, Loc.T("Продолжить"), resume, "btn-primary");
            // стрим-комната: очередь, места, кик и бан зрителей
            _streamPanel = Ui.Btn(panel, Loc.T("Управление стримом"), () => UiRoot.Instance?.OpenStreamPanel());
            _start = Ui.Btn(panel, Loc.T("Начать матч"), () =>
            {
                MatchManager.Instance?.RequestStartMatch();
                resume();
            });

            _switchTeam = Ui.Btn(panel, "", () => NetworkPlayer.LocalPlayer?.RequestSwitchTeam());
            _switchBlocker = Ui.Text(panel, "", "label-dim");
            // «Перейти в команду КРАСНЫЕ после матча» длиннее ширины панели — переносим на вторую строку
            _switchTeam.style.whiteSpace = WhiteSpace.Normal;
            _switchTeam.style.height = StyleKeyword.Auto;
            _switchTeam.style.minHeight = 44;
            _switchTeam.style.paddingTop = _switchTeam.style.paddingBottom = 8;
            _switchBlocker.style.whiteSpace = WhiteSpace.Normal;

            Ui.Btn(panel, Loc.T("Опции"), () => UiRoot.Instance?.OpenOptions());

            Ui.Btn(panel, Loc.T("Выйти из комнаты"), () => NetworkBootstrap.Instance?.ReturnToMenu(), "btn-danger");
            Ui.Show(_screen, false);
        }

        public void SetVisible(bool visible) => Ui.Show(_screen, visible);

        public void Update()
        {
            var m = MatchManager.Instance;
            bool host = m != null && m.IsSpawned && m.IsServer && m.CanStartMatch;
            Ui.Show(_start, host);
            if (host)
            {
                string blocker = m.StartBlocker();
                _start.SetEnabled(blocker == null);
                _start.text = blocker ?? Loc.T(m.Phase == MatchPhase.MatchEnd ? "Новый матч" : "Начать матч");
            }

            UpdateSwitchTeam();

            var session = SessionService.Current;
            _room.text = session != null ? Loc.F("Код комнаты: {0}", session.Code) : Loc.T("Комната в локальной сети");
            Ui.Show(_invite, session != null && GamePlatform.Current.CanInvite);
            var net = MatchNet.Instance;
            Ui.Show(_streamPanel, net != null && net.IsSpawned && net.IsServer && net.IsStream);
        }

        /// <summary>
        /// «Перейти в команду ЗЕЛЁНЫЕ»: в лобби и после матча — сразу (серая с причиной, если нельзя; то же правило
        /// проверяет сервер), во время матча — запрос на переход после матча, который можно отменить.
        /// </summary>
        void UpdateSwitchTeam()
        {
            var me = NetworkPlayer.LocalPlayer;
            var health = me != null && me.IsSpawned ? me.Health : null;
            var match = MatchManager.Instance;
            Ui.Show(_switchTeam, health != null && match != null);
            Ui.Show(_switchBlocker, false);
            if (health == null || match == null)
                return;
            string other = RuntimeMaterials.TeamName(1 - health.Team);
            string note = null;
            if (match.CanStartMatch)
            {
                _switchTeam.text = Loc.F("Перейти в команду {0}", other);
                note = MatchNet.TeamSwitchBlocker(health);
                _switchTeam.SetEnabled(note == null);
            }
            else
            {
                _switchTeam.SetEnabled(true);
                if (me.TeamSwitchQueued)
                {
                    _switchTeam.text = Loc.T("Отменить переход");
                    note = Loc.F("Вы перейдёте в команду {0} после матча", other);
                }
                else
                {
                    _switchTeam.text = Loc.F("Перейти в команду {0} после матча", other);
                }
            }
            if (note != null)
            {
                _switchBlocker.text = note;
                Ui.Show(_switchBlocker, true);
            }
        }
    }
}
