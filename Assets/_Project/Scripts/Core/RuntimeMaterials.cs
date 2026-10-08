using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>Кэш простых URP Lit материалов по цвету для плейсхолдер-графики прототипа.</summary>
    public static class RuntimeMaterials
    {
        static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();

        public static readonly Color TeamRed = new Color(0.86f, 0.18f, 0.16f);
        public static readonly Color TeamGreen = new Color(0.15f, 0.8f, 0.3f);

        public static Color TeamColor(int team) => team == 0 ? TeamRed : TeamGreen;

        /// <summary>«КРАСНЫЕ» / «ЗЕЛЁНЫЕ».</summary>
        public static string TeamName(int team) => team == 0 ? "КРАСНЫЕ" : "ЗЕЛЁНЫЕ";

        /// <summary>«КРАСНЫХ» / «ЗЕЛЁНЫХ» (шахта чьей команды).</summary>
        public static string TeamNameGenitive(int team) => team == 0 ? "КРАСНЫХ" : "ЗЕЛЁНЫХ";

        public static Material Lit(Color color)
        {
            if (Cache.TryGetValue(color, out var mat) && mat != null)
                return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"Lit {color}" };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.1f);
            Cache[color] = mat;
            return mat;
        }

        static readonly Dictionary<Color, Material> EmissiveCache = new Dictionary<Color, Material>();

        /// <summary>Светящийся материал (индикаторы): виден издалека и в тени.</summary>
        public static Material Emissive(Color color, float intensity = 1.5f)
        {
            if (EmissiveCache.TryGetValue(color, out var mat) && mat != null)
                return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"Emissive {color}" };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.3f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * intensity);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EmissiveCache[color] = mat;
            return mat;
        }

        /// <summary>Примитив без коллайдера (только визуал).</summary>
        public static GameObject VisualPrimitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = Lit(color);
            return go;
        }
    }
}
