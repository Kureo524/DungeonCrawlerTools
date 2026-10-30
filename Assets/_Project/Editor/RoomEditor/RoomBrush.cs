using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Editor.RoomEditor
{
    public enum BrushKind
    {
        Floor,
        Wall,
        Pit,
        Enemy,
        Chest,
        Prop,
        PlayerSpawn,
        Eraser,
        Npc
    }

    /// <summary>Ce que l'on peint sur la grille : un type de case ou un élément tiré du biome.</summary>
    public sealed class RoomBrush
    {
        public BrushKind Kind { get; }
        public string Label { get; }
        public Sprite Icon { get; }
        public EnemyData Enemy { get; }
        public LootTable LootTable { get; }
        public Sprite PropSprite { get; }
        public NpcData Npc { get; }

        RoomBrush(BrushKind kind, string label, Sprite icon, EnemyData enemy = null, LootTable lootTable = null, Sprite propSprite = null, NpcData npc = null)
        {
            Npc = npc;
            Kind = kind;
            Label = label;
            Icon = icon;
            Enemy = enemy;
            LootTable = lootTable;
            PropSprite = propSprite;
        }

        public static RoomBrush Tile(BrushKind kind, string label, Sprite icon) => new(kind, label, icon);
        public static RoomBrush ForEnemy(EnemyData enemy) => new(BrushKind.Enemy, enemy.DisplayName, enemy.Sprite, enemy: enemy);
        public static RoomBrush ForChest(LootTable table, Sprite chestSprite) => new(BrushKind.Chest, table.DisplayName, chestSprite, lootTable: table);
        public static RoomBrush ForProp(BiomeProp prop) => new(BrushKind.Prop, prop.name, prop.sprite, propSprite: prop.sprite);
        public static RoomBrush Spawn(Sprite icon) => new(BrushKind.PlayerSpawn, "Apparition du joueur", icon);
        public static RoomBrush Eraser() => new(BrushKind.Eraser, "Gomme", null);
        public static RoomBrush ForNpc(NpcData npc) => new(BrushKind.Npc, npc.DisplayName, npc.Portrait, npc: npc);

        public bool SameAs(RoomBrush other) =>
            other != null && Kind == other.Kind && Enemy == other.Enemy && LootTable == other.LootTable && PropSprite == other.PropSprite && Npc == other.Npc;
    }
}
