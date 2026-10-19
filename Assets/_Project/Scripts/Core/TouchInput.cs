using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Сенсорное управление (телефон, планшет): состояние, которое заполняет UI/TouchControls, а читают
    /// <see cref="GameInput"/> (кнопки действий, движение) и FpsController (обзор). Остальной код про пальцы не знает:
    /// для него кнопка «Действие» на экране — то же, что E на клавиатуре.
    /// </summary>
    public static class TouchInput
    {
        /// <summary>Мобильное устройство — экранные кнопки вместо мыши и клавиатуры.</summary>
        public static bool Enabled => Application.isMobilePlatform || ForceEnabled;

        /// <summary>Включить на ПК (проверки в редакторе).</summary>
        public static bool ForceEnabled;

        /// <summary>Джойстик: x — вправо, y — вперёд, длина до 1.</summary>
        public static Vector2 Move;

        /// <summary>Поворот камеры за этот кадр — в тех же единицах, что смещение мыши.</summary>
        public static Vector2 LookDelta;

        /// <summary>Кнопка «Счёт» зажата — таблица результатов, как Tab.</summary>
        public static bool ScoreHeld;

        static readonly bool[] Held = new bool[System.Enum.GetValues(typeof(GameAction)).Length];
        static readonly int[] DownFrame = new int[Held.Length];
        static Vector2 _tap;
        static int _tapFrame = -1;

        public static void SetHeld(GameAction action, bool held)
        {
            if (held && !Held[(int)action])
                DownFrame[(int)action] = Time.frameCount;
            Held[(int)action] = held;
        }

        public static bool IsHeld(GameAction action) => Enabled && Held[(int)action];

        public static bool WasDown(GameAction action) => Enabled && Held[(int)action] && DownFrame[(int)action] == Time.frameCount;

        /// <summary>Короткое касание экрана (не кнопки) — нажать кнопку пульта, в которую ткнули.</summary>
        public static void Tap(Vector2 screenPosition)
        {
            _tap = screenPosition;
            _tapFrame = Time.frameCount;
        }

        public static bool TryGetTap(out Vector2 screenPosition)
        {
            screenPosition = _tap;
            return Enabled && _tapFrame == Time.frameCount;
        }

        /// <summary>Все пальцы отпущены (интерфейс закрыл игру: пауза, итоги, меню).</summary>
        public static void Release()
        {
            for (int i = 0; i < Held.Length; i++)
                Held[i] = false;
            Move = Vector2.zero;
            LookDelta = Vector2.zero;
            ScoreHeld = false;
        }
    }
}
