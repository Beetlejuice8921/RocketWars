using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>Короткие помощники для сборки интерфейса UI Toolkit из кода.</summary>
    public static class Ui
    {
        public static VisualElement El(VisualElement parent, params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes)
                e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        public static Label Text(VisualElement parent, string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes)
                l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        public static Button Btn(VisualElement parent, string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("btn");
            foreach (var c in classes)
                b.AddToClassList(c);
            parent?.Add(b);
            return b;
        }

        public static TextField Field(VisualElement parent, string value, int maxLength, string cls = null)
        {
            var f = new TextField { value = value, maxLength = maxLength };
            f.AddToClassList("field");
            if (cls != null)
                f.AddToClassList(cls);
            parent?.Add(f);
            return f;
        }

        public static void Show(VisualElement e, bool visible) => e.EnableInClassList("hidden", !visible);

        public static bool IsShown(VisualElement e) => !e.ClassListContains("hidden");

        /// <summary>Мировая точка → координаты панели (null — за спиной камеры).</summary>
        public static Vector2? WorldToPanel(IPanel panel, Camera cam, Vector3 world)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f)
                return null;
            return RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sp.x, Screen.height - sp.y));
        }

        public static void Place(VisualElement e, Vector2 panelPos)
        {
            e.style.left = panelPos.x;
            e.style.top = panelPos.y;
        }
    }
}
