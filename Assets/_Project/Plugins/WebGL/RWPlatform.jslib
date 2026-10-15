// Мост к SDK площадок (Яндекс Игры — YaGames, VK — vkBridge). Скрипты SDK подключает веб-шаблон
// (Assets/WebGLTemplates/RocketWars-*). Ответы уходят в C# через SendMessage на объект RWPlatformBridge
// (Core/Platform/WebPlatform.cs). Нет SDK (локальный запуск) — всё работает, просто без площадки.
mergeInto(LibraryManager.library, {

  RW_Init: function (platformPtr) {
    var platform = UTF8ToString(platformPtr);
    var send = function (method, arg) {
      SendMessage('RWPlatformBridge', method, arg === undefined ? '' : String(arg));
    };
    var rw = window.RWPlatform = { platform: platform, send: send, ysdk: null, ready: false };

    document.addEventListener('visibilitychange', function () {
      send(document.hidden ? 'OnPause' : 'OnResume', 'tab');
    });

    var done = function (lang, name, prefs) {
      rw.ready = true;
      send('OnInit', JSON.stringify({ lang: lang || '', name: name || '', prefs: prefs || '' }));
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
    } else {
      done(navigator.language || '', '');
    }
  },

  // настройки игрока (JSON) — в облако площадки. Пока только VK (у Яндекса адрес игры постоянный, PlayerPrefs хватает)
  RW_SavePrefs: function (jsonPtr) {
    var rw = window.RWPlatform;
    if (!rw || rw.platform !== 'vk' || typeof vkBridge === 'undefined') return;
    vkBridge.send('VKWebAppStorageSet', { key: 'rw_prefs', value: UTF8ToString(jsonPtr) })
      .catch(function (e) { console.warn('[RW] VKWebAppStorageSet failed', e); });
  },

  RW_GameReady: function () {
    var rw = window.RWPlatform;
    if (rw && rw.ysdk && rw.ysdk.features && rw.ysdk.features.LoadingAPI)
      rw.ysdk.features.LoadingAPI.ready();
  },

  RW_Gameplay: function (started) {
    var rw = window.RWPlatform;
    if (!rw || !rw.ysdk || !rw.ysdk.features || !rw.ysdk.features.GameplayAPI)
      return;
    if (started) rw.ysdk.features.GameplayAPI.start();
    else rw.ysdk.features.GameplayAPI.stop();
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
    } else {
      finish(false);
    }
  }
});
