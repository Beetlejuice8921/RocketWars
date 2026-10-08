using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Stats
{
    /// <summary>Список достижений; индекс в списке — номер бита в маске игрока.</summary>
    [CreateAssetMenu(menuName = "RocketWars/Achievement Library", fileName = "AchievementLibrary")]
    public class AchievementLibrary : ScriptableObject
    {
        public List<AchievementDefinition> achievements = new List<AchievementDefinition>();
    }
}
