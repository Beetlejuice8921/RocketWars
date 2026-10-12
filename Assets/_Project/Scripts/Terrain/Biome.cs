namespace RocketWars.Terrain
{
    /// <summary>Тип земли раунда: меняет текстуры кубиков, пучки на поверхности и цвет обломков.</summary>
    public enum Biome : byte
    {
        Grass,
        Desert,
        Snow
    }

    public static class Biomes
    {
        public const int Count = 3;

        /// <summary>Земля текущего раунда (ставит MatchNet при постройке мира) — для эффектов, которым не передают остров.</summary>
        public static Biome Current { get; set; } = Biome.Grass;

        /// <summary>По номеру раунда по кругу: соседние раунды всегда на разной земле, у всех игроков одинаково.</summary>
        public static Biome ForRound(int roundId) => (Biome)(((roundId - 1) % Count + Count) % Count);
    }
}
