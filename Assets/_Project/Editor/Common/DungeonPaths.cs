using System;
using TheDungeon.Data;

namespace TheDungeon.Editor.Common
{
    /// <summary>Emplacements où les outils rangent automatiquement chaque type de donnée.</summary>
    public static class DungeonPaths
    {
        public const string Root = "Assets/_Project";
        public const string Data = Root + "/Data";
        public const string Items = Data + "/Items";
        public const string Enemies = Data + "/Enemies";
        public const string LootTables = Data + "/LootTables";
        public const string Rooms = Data + "/Rooms";
        public const string Floors = Data + "/Floors";
        public const string Progression = Data + "/Progression";
        public const string Settings = Root + "/Settings";
        public const string Help = Root + "/Editor/Help";
        public const string StyleSheet = Root + "/Editor/Styles/Forge.uss";

        public static string FolderFor(Type type)
        {
            if (type == typeof(ItemData)) return Items;
            if (type == typeof(EnemyData)) return Enemies;
            if (type == typeof(LootTable)) return LootTables;
            if (type == typeof(RoomData)) return Rooms;
            if (type == typeof(FloorData)) return Floors;
            if (type == typeof(PlayerProgression)) return Progression;
            throw new ArgumentException($"Aucun dossier défini pour {type.Name}", nameof(type));
        }
    }
}
