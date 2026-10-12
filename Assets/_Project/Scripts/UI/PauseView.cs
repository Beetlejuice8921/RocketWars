using System;
using RocketWars.Core;
using RocketWars.Net;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>Пауза по Esc: продолжить, (хост) начать матч, громкость, выйти из комнаты.</summary>
    public class PauseView
    {
        readonly VisualElement _screen;
        readonly Button _start;
        readonly Label _room;

        public PauseView(VisualElement root, Action resume)
        {
            _screen = Ui.El(root, "layer", "overlay");
            var panel = Ui.El(_screen, "panel");
            panel.style.width = 520;
            Ui.Text(panel, Loc.T("ПАУЗА"), "title", "shadow");
            _room = Ui.Text(panel, "", "label-dim");

            Ui.Btn(panel, Loc.T("Продолжить"), resume, "btn-primary");
            _start = Ui.Btn(panel, Loc.T("Начать матч"), () =>
            {
                MatchManager.Instance?.RequestStartMatch();
                resume();
            });

            Ui.Text(panel, Loc.T("ГРОМКОСТЬ"), "section");
            var volume = new Slider(0f, 1f) { value = UiRoot.Volume };
            volume.RegisterValueChangedCallback(e => UiRoot.Volume = e.newValue);
            panel.Add(volume);

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

            var session = SessionService.Current;
            _room.text = session != null ? Loc.F("Код комнаты: {0}", session.Code) : Loc.T("Комната в локальной сети");
        }
    }
}
