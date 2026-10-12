using RocketWars.Core;
using RocketWars.Launcher;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>Локальный игрок: наводится прицелом на кнопку пульта и жмёт ЛКМ. Лежащий пультом не пользуется.</summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerInteractor : MonoBehaviour
    {
        public static PlayerInteractor Local { get; private set; }

        public Camera cam;
        public float range = 3.2f;

        public ConsoleButton Hovered { get; private set; }

        PlayerHealth _health;
        bool _wasLocked;

        void Awake() => _health = GetComponent<PlayerHealth>();

        void OnEnable() => Local = this;

        void OnDisable()
        {
            if (Local == this)
                Local = null;
        }

        void Update()
        {
            // клик, который только что захватил курсор, кнопку не нажимает
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool justLocked = locked && !_wasLocked;
            _wasLocked = locked;

            Hovered = null;
            if (cam == null || _health.IsDowned || !locked)
                return;

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Physics.Raycast(ray, out var hit, range, ~0, QueryTriggerInteraction.Collide))
                Hovered = hit.collider.GetComponent<ConsoleButton>();

            if (Hovered != null && !justLocked && GameInput.WasPressed(GameAction.Fire) && Hovered.IsInteractable)
                Hovered.Console.RequestPress(Hovered.Kind);
        }
    }
}
