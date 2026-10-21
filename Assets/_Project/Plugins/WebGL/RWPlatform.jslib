// Мост к SDK площадок (Яндекс Игры — YaGames, VK — vkBridge, Telegram — Telegram.WebApp + реклама Adsgram). Скрипты SDK подключает веб-шаблон
// (Assets/WebGLTemplates/RocketWars-*). Ответы уходят в C# через SendMessage на объект RWPlatformBridge
// (Core/Platform/WebPlatform.cs). Нет SDK (локальный запуск) — всё работает, просто без площадки.
mergeInto(LibraryManager.library, {

  RW_Init: function (platformPtr, configPtr) {
    var platform = UTF8ToString(platformPtr);
    var config = {};
    try { config = JSON.parse(UTF8ToString(configPtr)) || {}; } catch (e) { }
    var send = function (method, arg) {
      SendMessage('RWPlatformBridge', method, arg === undefined ? '' : String(arg));
    };
    var rw = window.RWPlatform = { platform: platform, config: config, send: send, ysdk: null, tg: null, ads: {}, ready: false };

    document.addEventListener('visibilitychange', function () {
      send(document.hidden ? 'OnPause' : 'OnResume', 'tab');
    });

    // start — параметр запуска площадки (Telegram: startapp из ссылки-приглашения);
    // auth — подписанные данные запуска для сервера рейтинга (Telegram: initData)
    var done = function (lang, name, prefs, start, auth) {
      if (rw.ready) return;
      rw.ready = true;
      // winToggle — площадка даёт переключать «во весь экран / в окне» (Telegram на компьютере)
      send('OnInit', JSON.stringify({ lang: lang || '', name: name || '', prefs: prefs || '', start: start || '', auth: auth || '',
        winToggle: !!rw.winToggle, windowed: !!(rw.winToggle && !rw.tg.isFullscreen) }));
    };

    if (platform === 'yandex' && typeof YaGames !== 'undefined') {
      YaGames.init().then(function (ysdk) {
        rw.ysdk = ysdk;
        ysdk.on('game_api_pause', function () { send('OnPause', 'sdk'); });
        ysdk.on('game_api_resume', function () { send('OnResume', 'sdk'); });
        var lang = ysdk.environment && ysdk.environment.i18n ? ysdk.environment.i18n.lang : '';
        ysdk.getPlayer({ scopes: false })
          .then(function (player) { done(lang, player.getName ? player.getName() : ''); })
          .catch(function () { done(lang, ''); });
      }).catch(function (e) {
        console.warn('[RW] YaGames.init failed', e);
        done('', '');
      });
    } else if (platform === 'vk' && typeof vkBridge !== 'undefined') {
      var vkLang = new URLSearchParams(window.location.search).get('vk_language') || '';
      // свёрнуто мобильное приложение VK (WebView) — то же, что скрытая вкладка
      vkBridge.subscribe(function (e) {
        var type = e && e.detail && e.detail.type;
        if (type === 'VKWebAppViewHide') send('OnPause', 'sdk');
        else if (type === 'VKWebAppViewRestore') send('OnResume', 'sdk');
      });
      // VKWebAppInit шаблон отправил сразу при открытии страницы (VK ждёт его не дольше 30 с, а сборка грузится дольше)
      (window.RWVkInit || vkBridge.send('VKWebAppInit'))
        .then(function () {
          // реклама между экранами — подгрузить заранее, чтобы показ не ждал сети
          vkBridge.send('VKWebAppCheckNativeAds', { ad_format: 'interstitial' }).catch(function () { });
          var user = vkBridge.send('VKWebAppGetUserInfo')
            .then(function (u) { return u && u.first_name; })
            .catch(function () { return ''; });
          // настройки игрока — в хранилище VK: хостинг VK меняет адрес игры при каждой выкладке, и PlayerPrefs браузера теряются
          var prefs = vkBridge.send('VKWebAppStorageGet', { keys: ['rw_prefs'] })
            .then(function (d) { return d && d.keys && d.keys.length ? d.keys[0].value : ''; })
            .catch(function () { return ''; });
          return Promise.all([user, prefs]);
        })
        .then(function (r) { done(vkLang, r[0], r[1]); })
        .catch(function (e) {
          console.warn('[RW] VK Bridge init failed', e);
          done(vkLang, '');
        });
    } else if (platform === 'telegram' && window.Telegram && Telegram.WebApp && Telegram.WebApp.initData) {
      // initData пуст, если страницу открыли не из Telegram — тогда работаем как обычный сайт
      var tg = rw.tg = Telegram.WebApp;
      var tgUser = (tg.initDataUnsafe && tg.initDataUnsafe.user) || {};
      var tgName = tgUser.first_name || tgUser.username || '';
      // https://t.me/<бот>/<app>?startapp=<параметр> — Telegram передаёт его сюда
      var tgStart = (tg.initDataUnsafe && tg.initDataUnsafe.start_param) || '';
      // Telegram свёрнут / переключились на другой чат (Bot API 8.0)
      if (tg.isVersionAtLeast('8.0')) {
        tg.onEvent('deactivated', function () { send('OnPause', 'sdk'); });
        tg.onEvent('activated', function () { send('OnResume', 'sdk'); });
        // на компьютере (Telegram Desktop, web.telegram.org) — переключатель «Оконный режим» в опциях;
        // на телефоне всегда во весь экран: так игра горизонтальная и без шапки Telegram
        var tgMobile = tg.platform === 'android' || tg.platform === 'android_x' || tg.platform === 'ios';
        if (!tgMobile) {
          rw.winToggle = true;
          var syncWindowed = function () { send('OnWindowed', tg.isFullscreen ? '0' : '1'); };
          tg.onEvent('fullscreenChanged', syncWindowed);
          tg.onEvent('fullscreenFailed', syncWindowed); // web-версия без жеста игрока — остаёмся в окне
        }
      }
      if (tg.isVersionAtLeast('6.9')) {
        // облако Telegram отвечает быстро, но без ответа за 5 с не держим игру
        setTimeout(function () { done(tgUser.language_code, tgName, '', tgStart, tg.initData); }, 5000);
        tg.CloudStorage.getItem('rw_prefs', function (err, value) {
          done(tgUser.language_code, tgName, err ? '' : value, tgStart, tg.initData);
        });
      } else {
        done(tgUser.language_code, tgName, '', tgStart, tg.initData);
      }
    } else {
      done(navigator.language || '', '');
    }
  },

  // ссылка (поддержка): в Telegram — его средствами (t.me — внутри приложения), иначе — новая вкладка
  RW_OpenLink: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var rw = window.RWPlatform;
    var tg = rw && rw.tg;
    if (tg && /^https:\/\/t\.me\//.test(url)) tg.openTelegramLink(url);
    else if (tg) tg.openLink(url);
    else window.open(url, '_blank');
  },

  // Telegram на компьютере: во весь экран / в окне. Выбор помнит шаблон (localStorage rw_windowed) — при следующем запуске
  // он не разворачивает игру сам
  RW_SetFullscreen: function (on) {
    var rw = window.RWPlatform;
    if (!rw || !rw.winToggle) return;
    try { localStorage.setItem('rw_windowed', on ? '0' : '1'); } catch (e) { }
    if (on) rw.tg.requestFullscreen();
    else rw.tg.exitFullscreen();
  },

  // приглашение: окно Telegram «Поделиться» (выбор чата) со ссылкой и текстом; вне Telegram — та же страница новой вкладкой
  RW_Share: function (urlPtr, textPtr) {
    var share = 'https://t.me/share/url?url=' + encodeURIComponent(UTF8ToString(urlPtr)) +
      '&text=' + encodeURIComponent(UTF8ToString(textPtr));
    var rw = window.RWPlatform;
    if (rw && rw.tg) rw.tg.openTelegramLink(share);
    else window.open(share, '_blank');
  },

  // настройки игрока (JSON) — в облако площадки: VK и Telegram (у Яндекса адрес игры постоянный, PlayerPrefs хватает)
  RW_SavePrefs: function (jsonPtr) {
    var rw = window.RWPlatform;
    if (!rw) return;
    var json = UTF8ToString(jsonPtr);
    if (rw.platform === 'vk' && typeof vkBridge !== 'undefined') {
      vkBridge.send('VKWebAppStorageSet', { key: 'rw_prefs', value: json })
        .catch(function (e) { console.warn('[RW] VKWebAppStorageSet failed', e); });
    } else if (rw.tg && rw.tg.isVersionAtLeast('6.9')) {
      // значение — до 4096 символов; настроек ~600
      rw.tg.CloudStorage.setItem('rw_prefs', json, function (err) {
        if (err) console.warn('[RW] CloudStorage.setItem failed', err);
      });
    }
  },

  RW_GameReady: function () {
    var rw = window.RWPlatform;
    if (rw && rw.ysdk && rw.ysdk.features && rw.ysdk.features.LoadingAPI)
      rw.ysdk.features.LoadingAPI.ready();
  },

  RW_Gameplay: function (started) {
    var rw = window.RWPlatform;
    if (!rw) return;
    if (rw.ysdk && rw.ysdk.features && rw.ysdk.features.GameplayAPI) {
      if (started) rw.ysdk.features.GameplayAPI.start();
      else rw.ysdk.features.GameplayAPI.stop();
    } else if (rw.tg && rw.tg.isVersionAtLeast('6.2')) {
      // в матче Telegram спросит, прежде чем закрыть игру (свайп вниз, крестик)
      if (started) rw.tg.enableClosingConfirmation();
      else rw.tg.disableClosingConfirmation();
    }
  },

  // rewarded = 0 — межстраничная реклама, 1 — за награду. Ответ всегда один: OnAdClosed("1") — награду выдать, иначе "0".
  RW_ShowAd: function (rewarded) {
    var rw = window.RWPlatform;
    if (!rw) return;
    var send = rw.send;
    var closed = false;
    var finish = function (reward) {
      if (closed) return;
      closed = true;
      send('OnResume', 'ad');
      send('OnAdClosed', reward ? '1' : '0');
    };

    if (rw.platform === 'yandex' && rw.ysdk) {
      var got = false;
      var callbacks = {
        onOpen: function () { send('OnPause', 'ad'); },
        onRewarded: function () { got = true; },
        onClose: function () { finish(got); },
        onError: function () { finish(false); },
        onOffline: function () { finish(false); }
      };
      if (rewarded) rw.ysdk.adv.showRewardedVideo({ callbacks: callbacks });
      else rw.ysdk.adv.showFullscreenAdv({ callbacks: callbacks });
    } else if (rw.platform === 'vk' && typeof vkBridge !== 'undefined') {
      send('OnPause', 'ad');
      vkBridge.send('VKWebAppShowNativeAds', { ad_format: rewarded ? 'reward' : 'interstitial' })
        .then(function (data) { finish(rewarded && data && data.result); })
        .catch(function () { finish(false); })
        .then(function () {
          // следующая порция рекламы — заранее (VK Bridge делает это и сам, но запрос мог не пройти)
          vkBridge.send('VKWebAppCheckNativeAds', { ad_format: 'interstitial' }).catch(function () { });
        });
    } else if (rw.platform === 'telegram' && rw.tg && window.Adsgram) {
      var blockId = rewarded ? rw.config.adRewarded : rw.config.adInterstitial;
      if (!blockId) { finish(false); return; }
      try {
        var controller = rw.ads[blockId] || (rw.ads[blockId] = window.Adsgram.init({ blockId: blockId }));
        send('OnPause', 'ad');
        // resolve — досмотрел (или закрыл межстраничную), reject — ошибка, нет рекламы или пропустил видео за награду
        controller.show()
          .then(function (result) { finish(rewarded && result && result.done); })
          .catch(function () { finish(false); });
      } catch (e) {
        console.warn('[RW] Adsgram failed', e);
        finish(false);
      }
    } else {
      finish(false);
    }
  }
});
