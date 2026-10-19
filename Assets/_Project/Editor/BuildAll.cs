using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RocketWars.Net;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RocketWars.EditorTools
{
    /// <summary>
    /// Сборка всех версий из одного проекта: Steam (Windows), Яндекс Игры, VK и Telegram Mini App (WebGL).
    /// Площадку выбирает символ компиляции (GamePlatform), остальной код общий.
    /// Результат: &lt;папка проекта&gt;/../Build/&lt;версия&gt;/Steam | Yandex | VK | Telegram, веб-версии ещё и в zip для загрузки.
    ///
    /// Из командной строки (редактор с этим проектом должен быть закрыт):
    ///   Unity.exe -batchmode -quit -projectPath &lt;проект&gt; -executeMethod RocketWars.EditorTools.BuildAll.CommandLine
    ///     [-targets steam,yandex,vk,telegram] [-bumpVersion] -logFile build.log
    /// </summary>
    public static class BuildAll
    {
        enum Target { Steam, Yandex, VK, Telegram }

        static readonly Target[] AllTargets = { Target.Steam, Target.Yandex, Target.VK, Target.Telegram };

        [MenuItem("RocketWars/Build/All Platforms", priority = 100)]
        static void MenuAll() => RunInteractive(AllTargets);

        [MenuItem("RocketWars/Build/Steam (Windows)", priority = 111)]
        static void MenuSteam() => RunInteractive(new[] { Target.Steam });

        [MenuItem("RocketWars/Build/Yandex Games (Web)", priority = 112)]
        static void MenuYandex() => RunInteractive(new[] { Target.Yandex });

        [MenuItem("RocketWars/Build/VK (Web)", priority = 113)]
        static void MenuVk() => RunInteractive(new[] { Target.VK });

        [MenuItem("RocketWars/Build/Telegram Mini App (Web)", priority = 114)]
        static void MenuTelegram() => RunInteractive(new[] { Target.Telegram });

        [MenuItem("RocketWars/Build/Bump Version", priority = 130)]
        static void MenuBump()
        {
            string old = PlayerSettings.bundleVersion;
            BumpVersion();
            Debug.Log($"[Build] version {old} -> {PlayerSettings.bundleVersion} (network protocol {NetProtocol.Version})");
        }

        static void RunInteractive(Target[] targets)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Rocket Wars", "Сначала выйдите из Play mode.", "OK");
                return;
            }

            string version = PlayerSettings.bundleVersion;
            int choice = EditorUtility.DisplayDialogComplex("Rocket Wars — сборка",
                $"Версия {version}, сетевой протокол {NetProtocol.Version}.\n\n" +
                "Если менялись сетевые данные (NetworkVariable, RPC, префабы), сначала поднимите NetProtocol.Version.\n\n" +
                $"Поднять версию до {NextVersion(version)} перед сборкой?",
                "Поднять и собрать", "Отмена", "Собрать как есть");
            if (choice == 1)
                return;
            if (choice == 0)
                BumpVersion();

            Run(targets);
        }

        /// <summary>Точка входа для -executeMethod.</summary>
        public static void CommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var targets = AllTargets;
            int t = Array.IndexOf(args, "-targets");
            if (t >= 0 && t + 1 < args.Length)
                targets = args[t + 1].Split(',').Select(s => (Target)Enum.Parse(typeof(Target), s.Trim(), true)).ToArray();
            if (args.Contains("-bumpVersion"))
                BumpVersion();

            bool ok = Run(targets);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Run(Target[] targets)
        {
            var originalTarget = EditorUserBuildSettings.activeBuildTarget;
            var originalGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Build", PlayerSettings.bundleVersion));
            var results = new List<string>();
            bool allOk = true;

            // сначала цель, активная сейчас, — меньше переключений платформы (каждое — переимпорт ассетов)
            var ordered = targets.OrderBy(x => TargetOf(x) == originalTarget ? 0 : 1).ToArray();
            try
            {
                foreach (var target in ordered)
                {
                    bool ok = BuildOne(target, root, out string message);
                    allOk &= ok;
                    results.Add($"{target}: {message}");
                }
            }
            finally
            {
                if (EditorUserBuildSettings.activeBuildTarget != originalTarget)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalTarget);
            }

            string summary = $"Rocket Wars {PlayerSettings.bundleVersion} (protocol {NetProtocol.Version})\n" + string.Join("\n", results);
            if (allOk) Debug.Log("[Build] " + summary);
            else Debug.LogError("[Build] " + summary);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Rocket Wars — сборка", summary, "OK");
                if (allOk)
                    EditorUtility.RevealInFinder(root);
            }
            return allOk;
        }

        static bool BuildOne(Target target, string root, out string message)
        {
            var buildTarget = TargetOf(target);
            var group = BuildPipeline.GetBuildTargetGroup(buildTarget);
            if (!BuildPipeline.IsBuildTargetSupported(group, buildTarget))
            {
                message = $"не собрано — в Unity Hub не установлен модуль {(buildTarget == BuildTarget.WebGL ? "WebGL Build Support" : buildTarget.ToString())}";
                return false;
            }

            string folder = Path.Combine(root, target.ToString());
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);

            if (buildTarget == BuildTarget.WebGL)
                ApplyWebSettings(target);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                target = buildTarget,
                targetGroup = group,
                locationPathName = buildTarget == BuildTarget.WebGL ? folder : Path.Combine(folder, PlayerSettings.productName + ".exe"),
                extraScriptingDefines = new[] { DefineOf(target) },
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                message = $"ОШИБКА ({report.summary.result}, ошибок: {report.summary.totalErrors})";
                return false;
            }

            string size = $"{report.summary.totalSize / (1024f * 1024f):0.0} МБ";
            if (buildTarget == BuildTarget.WebGL)
            {
                // архив для загрузки на площадку: index.html — в корне архива
                string zip = folder + ".zip";
                if (File.Exists(zip))
                    File.Delete(zip);
                ZipFile.CreateFromDirectory(folder, zip, System.IO.Compression.CompressionLevel.Optimal, false);
                size += $", zip {new FileInfo(zip).Length / (1024f * 1024f):0.0} МБ";
            }
            message = $"OK, {size} → {folder}";
            if (target == Target.VK && string.IsNullOrEmpty(RocketWars.Core.WebPlatform.VkSupportUrl))
                message += "\n  ВНИМАНИЕ: не задан WebPlatform.VkSupportUrl — без связи с поддержкой VK не пустит игру в каталог";
            if (target == Target.Telegram && (string.IsNullOrEmpty(RocketWars.Core.WebPlatform.AdsgramInterstitialBlock) ||
                                              string.IsNullOrEmpty(RocketWars.Core.WebPlatform.AdsgramRewardedBlock)))
                message += "\n  ВНИМАНИЕ: не заданы блоки Adsgram (WebPlatform.Adsgram*Block) — реклама в Telegram не показывается";
            return true;
        }

        /// <summary>
        /// Настройки веб-сборки. Brotli + встроенная распаковка: работает на любом хостинге, даже без заголовков Content-Encoding.
        /// Шаблон страницы — свой у каждой площадки (Assets/WebGLTemplates): он подключает её SDK.
        /// Меняют только WebGL-раздел Player Settings, на Steam-сборку не влияют.
        /// </summary>
        static void ApplyWebSettings(Target target)
        {
            PlayerSettings.WebGL.template = "PROJECT:RocketWars-" + target; // Assets/WebGLTemplates/RocketWars-Yandex | VK | Telegram
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true; // новые имена файлов при обновлении — без старого кэша у игроков
            PlayerSettings.WebGL.dataCaching = true;
        }

        static BuildTarget TargetOf(Target target) =>
            target == Target.Steam ? BuildTarget.StandaloneWindows64 : BuildTarget.WebGL;

        static string DefineOf(Target target) => target switch
        {
            Target.Steam => "PLATFORM_STEAM",
            Target.Yandex => "PLATFORM_YANDEX",
            Target.VK => "PLATFORM_VK",
            _ => "PLATFORM_TELEGRAM",
        };

        static void BumpVersion()
        {
            PlayerSettings.bundleVersion = NextVersion(PlayerSettings.bundleVersion);
            AssetDatabase.SaveAssets();
        }

        /// <summary>1.0.0 → 1.0.1 (последнее число +1).</summary>
        static string NextVersion(string version)
        {
            var parts = (version ?? "1.0.0").Split('.').ToList();
            while (parts.Count < 3)
                parts.Add("0");
            parts[parts.Count - 1] = int.TryParse(parts[parts.Count - 1], out int last) ? (last + 1).ToString() : "1";
            return string.Join(".", parts);
        }
    }
}
