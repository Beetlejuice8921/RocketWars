using System.Collections.Generic;
using System.Text;
using RocketWars.Core;
using RocketWars.Net;
using RocketWars.Online;
using RocketWars.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Окно «Управление стримом» (хост стрим-комнаты, из паузы): места в игре, длина матча, вход открыт/закрыт,
    /// смена игроков после матча, QR на экране; анонс подписчикам бота Telegram (стримерам-партнёрам);
    /// списки «В игре» и «Очередь» с кнопками для каждого зрителя — пропустить вперёд, в очередь, убрать ник,
    /// исключить, заблокировать.
    /// </summary>
    public class StreamPanelView
    {
        const float RebuildInterval = 0.5f;

        readonly VisualElement _screen;
        readonly Label _slots, _wins, _code;
        readonly Button _locked, _rotate, _qr, _announceSoon, _announceNow;
        readonly VisualElement _announceRow;
        readonly Label _announceNote;
        readonly VisualElement _playing, _queue;
        readonly Label _playingTitle, _queueTitle;
        float _nextRebuild;
        string _signature;

        public bool IsOpen => Ui.IsShown(_screen);

        public StreamPanelView(VisualElement root)
        {
            _screen = Ui.El(root, "layer", "overlay");
            var panel = Ui.El(_screen, "panel", "stream-panel");
            Ui.Text(panel, Loc.T("УПРАВЛЕНИЕ СТРИМОМ"), "title", "shadow");
            _code = Ui.Text(panel, "", "label-dim", "stream-panel-code");

            var scroll = new ScrollView();
            scroll.AddToClassList("options-scroll");
            panel.Add(scroll);

            _slots = Stepper(scroll, Loc.T("Мест в игре"), () => StreamHost.SetSlots(StreamHost.Slots - 1), () => StreamHost.SetSlots(StreamHost.Slots + 1));
            _wins = Stepper(scroll, Loc.T("Раундов для победы"), () => StreamHost.SetWins(StreamHost.WinsNeeded - 1), () => StreamHost.SetWins(StreamHost.WinsNeeded + 1));

            var toggles = Ui.El(scroll, "row", "stream-toggles");
            _locked = Ui.Btn(toggles, "", () => StreamHost.Locked = !StreamHost.Locked, "btn-small", "grow");
            _rotate = Ui.Btn(toggles, "", () => StreamHost.Rotate = !StreamHost.Rotate, "btn-small", "grow");
            _qr = Ui.Btn(toggles, "", () => StreamHost.ShowQr = !StreamHost.ShowQr, "btn-small", "grow");

            // бот Telegram: анонс всем подписчикам — у стримеров-партнёров (Backend: streamers)
            Ui.Text(scroll, Loc.T("АНОНС В TELEGRAM"), "section");
            _announceRow = Ui.El(scroll, "row");
            _announceSoon = Ui.Btn(_announceRow, Loc.T("Анонс: через 15 минут"), () => StreamBot.Announce(true), "btn-small", "grow");
            _announceNow = Ui.Btn(_announceRow, Loc.T("Анонс: уже в эфире"), () => StreamBot.Announce(false), "btn-small", "btn-primary", "grow");
            _announceNote = Ui.Text(scroll, "", "label-dim", "stream-announce-note");

            _playingTitle = Ui.Text(scroll, "", "section");
            _playing = Ui.El(scroll, "stream-list");
            _queueTitle = Ui.Text(scroll, "", "section");
            _queue = Ui.El(scroll, "stream-list");

            Ui.Btn(panel, Loc.T("Закрыть"), Close, "btn-primary");
            Ui.Show(_screen, false);
        }

        static Label Stepper(VisualElement parent, string title, System.Action minus, System.Action plus)
        {
            var row = Ui.El(parent, "row", "stream-stepper");
            Ui.Text(row, title, "grow");
            Ui.Btn(row, "-", minus, "btn-small", "stream-step");
            var value = Ui.Text(row, "", "stream-step-value");
            Ui.Btn(row, "+", plus, "btn-small", "stream-step");
            return value;
        }

        public void Open()
        {
            Ui.Show(_screen, true);
            _signature = null;
            _nextRebuild = 0f;
        }

        public void Close() => Ui.Show(_screen, false);

        public void Update()
        {
            var net = MatchNet.Instance;
            if (net == null || !net.IsSpawned || !net.IsServer || !StreamHost.Enabled)
            {
                Close();
                return;
            }

            _code.text = Loc.F("Код комнаты: {0}", SessionService.Current?.Code ?? "-");
            _slots.text = StreamHost.Slots.ToString();
            _wins.text = StreamHost.WinsNeeded.ToString();
            _locked.text = Loc.T(StreamHost.Locked ? "Вход: закрыт" : "Вход: открыт");
            _locked.EnableInClassList("btn-danger", StreamHost.Locked);
            _rotate.text = Loc.T(StreamHost.Rotate ? "Смена игроков: вкл" : "Смена игроков: выкл");
            _rotate.EnableInClassList("btn-primary", StreamHost.Rotate);
            _qr.text = Loc.T(StreamHost.ShowQr ? "QR на экране: вкл" : "QR на экране: выкл");
            _qr.EnableInClassList("btn-primary", StreamHost.ShowQr);

            UpdateAnnounce();

            if (Time.unscaledTime < _nextRebuild)
                return;
            _nextRebuild = Time.unscaledTime + RebuildInterval;
            RebuildLists(net);
        }

        void UpdateAnnounce()
        {
            bool canAnnounce = StreamBot.Partner && StreamBot.AnnouncesLeft > 0;
            Ui.Show(_announceRow, StreamBot.Partner);
            int wait = StreamBot.AnnounceCooldown;
            bool ready = canAnnounce && wait == 0 && !StreamBot.Busy;
            _announceSoon.SetEnabled(ready);
            _announceNow.SetEnabled(ready);

            string note;
            if (RatingApi.SignedIn && !RatingApi.Verified)
                note = Loc.T("Анонсы и награды работают, когда стример играет из Telegram");
            else if (!StreamBot.Known)
                note = Loc.T("Проверяем партнёрство…");
            else if (!StreamBot.Partner)
                note = Loc.F("Анонсы в Telegram — для стримеров-партнёров игры. Напишите нам: {0}", "@RocketWarsChat");
            else if (StreamBot.Busy)
                note = Loc.T("Отправляем…");
            else if (StreamBot.AnnouncesLeft == 0)
                note = StreamBot.Status ?? Loc.T("Анонсы на этот стрим закончились");
            else if (wait > 0)
                note = (StreamBot.Status != null ? StreamBot.Status + "\n" : "") + Loc.F("Следующий анонс — через {0} мин", (wait + 59) / 60);
            else
                note = (StreamBot.Status != null ? StreamBot.Status + "\n" : "") + Loc.F("Осталось анонсов: {0}", StreamBot.AnnouncesLeft);
            _announceNote.text = note;
        }

        void RebuildLists(MatchNet net)
        {
            var players = new List<PlayerHealth>();
            foreach (var p in PlayerHealth.All)
                if (p.IsSpawned && !p.GetComponent<NetworkPlayer>().IsBot)
                    players.Add(p);
            players.Sort((a, b) => a.Team != b.Team ? a.Team.CompareTo(b.Team) : a.OwnerClientId.CompareTo(b.OwnerClientId));

            // кнопки пересоздаём, только когда списки изменились (иначе нажатие попадёт в исчезнувшую кнопку)
            var sig = new StringBuilder();
            foreach (var p in players)
                sig.Append(p.OwnerClientId).Append(p.Team).Append(p.DisplayName).Append('|');
            sig.Append('#');
            foreach (var e in net.Queue)
                sig.Append(e.ClientId).Append(e.Name).Append('|');
            if (sig.ToString() == _signature)
                return;
            _signature = sig.ToString();

            _playingTitle.text = Loc.F("В ИГРЕ: {0} / {1}", players.Count, StreamHost.Slots);
            _playing.Clear();
            foreach (var p in players)
            {
                ulong id = p.OwnerClientId;
                var row = Ui.El(_playing, "row", "stream-row");
                var name = Ui.Text(row, p.DisplayName, "grow", "stream-name");
                name.style.color = RuntimeMaterials.TeamColor(p.Team);
                if (id == net.NetworkManager.LocalClientId)
                {
                    Ui.Text(row, Loc.T("стример"), "label-dim");
                    continue;
                }
                PlayerButtons(row, net, id, true);
            }

            _queueTitle.text = Loc.F("ОЧЕРЕДЬ: {0}", net.Queue.Count);
            _queue.Clear();
            if (net.Queue.Count == 0)
                Ui.Text(_queue, Loc.T("Пока никого. Покажите QR-код зрителям"), "label-dim");
            for (int i = 0; i < net.Queue.Count; i++)
            {
                var e = net.Queue[i];
                var row = Ui.El(_queue, "row", "stream-row");
                Ui.Text(row, $"{i + 1}. {e.Name}", "grow", "stream-name");
                PlayerButtons(row, net, e.ClientId, false);
            }
        }

        static void PlayerButtons(VisualElement row, MatchNet net, ulong id, bool playing)
        {
            if (playing)
                Ui.Btn(row, Loc.T("В очередь"), () => net.ServerSendToQueue(id), "btn-small");
            else
                Ui.Btn(row, Loc.T("Без очереди"), () => net.ServerQueueToFront(id), "btn-small");
            Ui.Btn(row, Loc.T("Сбросить ник"), () => net.ServerResetName(id), "btn-small");
            Ui.Btn(row, Loc.T("Кик"), () => net.ServerKick(id, false), "btn-small");
            Ui.Btn(row, Loc.T("Бан"), () => net.ServerKick(id, true), "btn-small", "btn-danger");
        }
    }
}
