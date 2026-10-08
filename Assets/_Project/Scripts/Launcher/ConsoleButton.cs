using UnityEngine;
using UnityEngine.UI;

namespace RocketWars.Launcher
{
    public enum ConsoleButtonKind
    {
        X,
        Y,
        Launch
    }

    /// <summary>Кнопка на экране пульта: триггер-коллайдер, в который целится игрок (ЛКМ — нажать).</summary>
    public class ConsoleButton : MonoBehaviour
    {
        public AimConsole Console { get; private set; }
        public ConsoleButtonKind Kind { get; private set; }
        public Image Background { get; private set; }

        public bool IsInteractable => Console.IsInteractable(Kind);

        public static ConsoleButton Attach(GameObject go, AimConsole console, ConsoleButtonKind kind, Image background)
        {
            var button = go.AddComponent<ConsoleButton>();
            button.Console = console;
            button.Kind = kind;
            button.Background = background;

            var rt = (RectTransform)go.transform;
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(rt.sizeDelta.x, rt.sizeDelta.y, 10f);
            col.center = new Vector3(rt.sizeDelta.x * (0.5f - rt.pivot.x), rt.sizeDelta.y * (0.5f - rt.pivot.y), 0f);
            return button;
        }
    }
}
