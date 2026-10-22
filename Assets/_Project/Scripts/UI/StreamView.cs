using RocketWars.Core;
using RocketWars.Net;
using RocketWars.Online;
using RocketWars.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Стрим-комната на экране. У хоста (стримера) — QR-код со ссылкой на комнату и код для зрителей:
    /// между матчами крупно, во время раунда — компактно. У зрителя в очереди — его место и обзорная камера,
    /// медленно облетающая острова (он смотрит матч, пока ждёт).
    /// </summary>
    public class StreamView
    {
        const float OrbitRadius = 175f;
        const float OrbitHeight = 75f;
        const float OrbitSpeed = 4f; // градусов в секунду

        readonly VisualElement _qrPanel, _qr;
        readonly Label _qrCode, _qrQueue;
        readonly VisualElement _waitPanel;
        readonly Label _waitPlace, _waitNote;
        Texture2D _qrTexture;
        string _qrFor;
        float _orbit;

        public StreamView(VisualElement root)
        {
            _qrPanel = Ui.El(root, "stream-qr");
            _qrPanel.pickingMode = PickingMode.Ignore;
            Ui.Text(_qrPanel, Loc.T("ЗАХОДИ ИГРАТЬ!"), "stream-qr-title");
            _qr = Ui.El(_qrPanel, "stream-qr-image");
            _qrCode = Ui.Text(_qrPanel, "", "stream-qr-code");
            _qrQueue = Ui.Text(_qrPanel, "", "stream-qr-queue");
            Ui.Show(_qrPanel, false);

            _waitPanel = Ui.El(root, "stream-wait");
            _waitPanel.pickingMode = PickingMode.Ignore;
            _waitPlace = Ui.Text(_waitPanel, "", "stream-wait-place", "shadow");
            _waitNote = Ui.Text(_waitPanel, "", "stream-wait-note", "shadow");
            Ui.Show(_waitPanel, false);
        }

        public void Dispose()
        {
            if (_qrTexture != null)
                Object.Destroy(_qrTexture);
        }

        /// <param name="results">открыты итоги матча — QR уходит в угол, чтобы не закрывать таблицу</param>
        public void Update(bool inGame, bool results)
        {
            var net = MatchNet.Instance;
            bool stream = inGame && net != null && net.IsSpawned && net.IsStream;
            if (stream)
                StreamBot.Tick(net, net.IsServer, SessionService.Current?.Code); // бот Telegram: стрим, анонсы, итоги
            UpdateQr(stream && net.IsServer, net, results);
            UpdateWaiting(stream && !net.IsServer, net);
        }

        void UpdateQr(bool host, MatchNet net, bool results)
        {
            string code = SessionService.Current?.Code;
            bool show = host && StreamHost.ShowQr && !string.IsNullOrEmpty(code);
            Ui.Show(_qrPanel, show);
            if (!show)
                return;

            if (code != _qrFor)
            {
                _qrFor = code;
                if (_qrTexture != null)
                    Object.Destroy(_qrTexture);
                var modules = QrCode.Encode(GamePlatform.InviteUrl(code));
                _qrTexture = modules != null ? QrCode.ToTexture(modules) : null;
                _qr.style.backgroundImage = _qrTexture != null ? new StyleBackground(_qrTexture) : new StyleBackground(StyleKeyword.None);
                _qrCode.text = Loc.F("КОД: {0}", code);
            }

            // идёт раунд — QR поменьше, чтобы не закрывать игру; между матчами — крупно
            var match = MatchManager.Instance;
            bool compact = results || match != null && match.IsSpawned && !match.CanStartMatch;
            _qrPanel.EnableInClassList("stream-qr--compact", compact);
            _qrPanel.EnableInClassList("stream-qr--corner", results);
            _qrQueue.text = Loc.F("В очереди: {0}", net.Queue.Count);
        }

        void UpdateWaiting(bool client, MatchNet net)
        {
            bool waiting = client && NetworkPlayer.LocalPlayer == null;
            Ui.Show(_waitPanel, waiting);
            if (!waiting)
                return;

            int place = net.QueuePosition(net.NetworkManager.LocalClientId);
            if (place > 0)
            {
                _waitPlace.text = Loc.F("ВЫ В ОЧЕРЕДИ: {0} ИЗ {1}", place, net.Queue.Count);
                _waitNote.text = Loc.T(net.Stream.Rotate
                    ? "Вы войдёте в игру, когда освободится место или после матча"
                    : "Вы войдёте в игру, когда освободится место");
            }
            else
            {
                _waitPlace.text = Loc.T("ВХОДИМ В ИГРУ…");
                _waitNote.text = "";
            }
            Orbit();
        }

        /// <summary>Обзорная камера ждущего зрителя: медленно облетает оба острова.</summary>
        void Orbit()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            if (cam.transform.parent != null)
                cam.transform.SetParent(null, false);
            _orbit += OrbitSpeed * Time.unscaledDeltaTime;
            float a = _orbit * Mathf.Deg2Rad;
            cam.transform.position = new Vector3(Mathf.Sin(a) * OrbitRadius, OrbitHeight, -Mathf.Cos(a) * OrbitRadius);
            cam.transform.LookAt(Vector3.zero);
            cam.fieldOfView = 60f;
        }
    }
}
