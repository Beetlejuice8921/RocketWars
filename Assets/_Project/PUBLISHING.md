# Публикация: Steam, Яндекс Игры, VK, Telegram

Один проект — четыре сборки. Меню **RocketWars → Build** (или `BuildAll.CommandLine` из командной строки),
результат — `Build/<версия>/Steam | Yandex | VK | Telegram` и zip веб-версий.

## Перед каждой сборкой
- Менялись сетевые данные (NetworkVariable, RPC, сетевые структуры, префабы, генерация мира)? → поднять `NetProtocol.Version`
  (Scripts/Net/NetProtocol.cs). Иначе разные версии попадут в одну комнату.
- Версию игры поднимает диалог сборки (1.0.0 → 1.0.1).

## Что где
| Что | Файл |
|---|---|
| Отличия площадок | Scripts/Core/Platform/GamePlatform.cs (+ SteamPlatform, WebPlatform) |
| Вызовы SDK Яндекса, VK, Telegram | Plugins/WebGL/RWPlatform.jslib |
| Страницы (подключают SDK) | Assets/WebGLTemplates/RocketWars-Yandex, RocketWars-VK, RocketWars-Telegram |
| Сборка | Editor/BuildAll.cs |

## Яндекс Игры
- Загрузить `Yandex.zip` в консоли разработчика (index.html — в корне архива).
- Платформы: пока **только десктоп** (управления с телефона нет).
- **Хосты (CSP)** — сервисы Unity, к которым ходит игра (замерено на локальной веб-сборке 1.0.0, 2026-10-13):
  - `player-auth.services.api.unity.com` — анонимный вход
  - `lobby.services.api.unity.com` — комнаты (создать, найти, войти)
  - `relay-allocations.services.api.unity.com` — выделение Relay
  - `*.relay.cloud.unity3d.com` — само соединение с Relay (wss)
  - `config.uca.cloud.unity3d.com`, `cdp.cloud.unity3d.com` — конфиг и аналитика Unity
  Обоснование: «сетевой мультиплеер: Unity Relay/Lobby». После загрузки черновика сверить с ошибками `[Report Only]`
  в консоли браузера — если появятся новые адреса, добавить.
- Реклама: межстраничная показывается только после итогов матча и при выходе в меню; за награду пока не используется.
- Язык берётся из SDK, пока игрок не выбрал свой. Хинди в веб-версии скрыт (нет шрифта).

## VK
- Материалы для карточки (иконки, сниппет, скриншоты, тексты, чек-лист модерации) — `Marketing_VK/` в корне репозитория.
- **Хостинг VK** (бесплатно, CDN): `Deploy/VK/deploy.ps1` берёт последнюю `Build/<версия>/VK` и выкладывает через
  vk-miniapps-deploy (Node.js). Один раз вписать `app_id` в `Deploy/VK/vk-hosting-config.json`. Адреса в «Размещении»
  библиотека обновит сама. Лимиты: 24 выкладки в сутки, архив до 300 МБ. Можно и свой HTTPS-хостинг без X-Frame-Options.
  Запуск: `powershell -ExecutionPolicy Bypass -File Deploy/VK/deploy.ps1` (или `-Version 1.0.3`). При первом запуске
  утилита попросит войти в VK; токен положить в переменную окружения `MINI_APPS_ACCESS_TOKEN`, не в репозиторий.
- Адрес игры на хостинге VK меняется с каждой выкладкой → PlayerPrefs браузера теряются. Поэтому язык, громкость,
  чувствительность, клавиши и имя хранятся ещё и в VKWebAppStorage (ключ `rw_prefs`, WebPlatform.CloudPrefs);
  новый ключ PlayerPrefs, который должен переживать обновления, — добавить туда.
- VKWebAppInit шлёт сам шаблон сразу при открытии (VK ждёт не дольше 30 с, а сборка грузится дольше).
- Язык — параметр `vk_language`, имя — из профиля (VKWebAppGetUserInfo).
- Сворачивание приложения VK (VKWebAppViewHide/Restore) глушит звук, как и скрытая вкладка.
- Реклама — VKWebAppShowNativeAds (interstitial), подгружается заранее через VKWebAppCheckNativeAds.
  До модерации реклама тестовая; деньги — после регистрации в кабинете выплат.
- **Связь с поддержкой обязательна для каталога**: `WebPlatform.VkSupportUrl` → кнопка «Поддержка» в меню.
- В заявке выбирать только Web (десктоп): управления с телефона нет.

## Telegram Mini App
- Пошагово (хостинг, BotFather, Adsgram) — `Deploy/Telegram/README.md` в корне репозитория; выкладка — `Deploy/Telegram/deploy.ps1`
  (Cloudflare Pages).
- SDK: telegram-web-app.js (шаблон вызывает ready/expand, на телефоне — fullscreen и запрет свайпа), реклама — Adsgram
  (`WebPlatform.AdsgramInterstitialBlock` / `AdsgramRewardedBlock`), поддержка — `WebPlatform.TelegramSupportUrl`.
- Настройки — в Telegram CloudStorage (тот же ключ `rw_prefs` и список WebPlatform.CloudPrefs, что у VK).
- Язык и имя — из initDataUnsafe.user; во время матча включено подтверждение закрытия.
- На телефонах шаблон предупреждает, что нужны мышь и клавиатура (сенсорного управления нет).

## Отличия веб-версии от ПК
- Нет LAN, входа по IP и кнопки «Выйти из игры»; только интернет-комнаты, и только с другими веб-игроками.
- Присесть по умолчанию — **C** (Ctrl+W в браузере закрывает вкладку); Ctrl/Cmd назначить нельзя.
- Esc в браузере снимает захват мыши → открывается пауза; «Продолжить» захватывает мышь снова.
- Звук глушится, когда вкладка скрыта или идёт реклама. Сама игра не останавливается — она сетевая.
- Хост в фоновой вкладке тормозит матч для всех (браузер замедляет фоновые вкладки).
