using UnityEngine;

namespace RocketWars.Stats
{
    public enum AchievementRule
    {
        /// <summary>Больше всех положил врагов.</summary>
        MostEnemiesDowned,
        /// <summary>Больше всех поднял союзников.</summary>
        MostRevives,
        /// <summary>Больше всех взорвал вражеских шахт.</summary>
        MostSilosDestroyed,
        /// <summary>Больше всех падал с острова.</summary>
        MostFalls,
        /// <summary>Положил хотя бы одного своего.</summary>
        AnyTeamkill,
        /// <summary>Ни разу не лёг за матч.</summary>
        NeverDowned
    }

    /// <summary>Достижение по итогам матча: название, описание для подсказки, иконка (цвет + короткая метка).</summary>
    [CreateAssetMenu(menuName = "RocketWars/Achievement", fileName = "Achievement")]
    public class AchievementDefinition : ScriptableObject
    {
        public string title;
        [TextArea] public string description;
        public AchievementRule rule;
        public Color color = Color.white;
        [Tooltip("1–2 символа на значке")]
        public string badge;
    }
}
