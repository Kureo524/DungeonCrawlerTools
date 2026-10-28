using System;

namespace TheDungeon.Data
{
    public enum Rarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    public enum ItemType
    {
        Weapon,
        Armor,
        Accessory,
        Consumable,
        Key
    }

    public enum StatType
    {
        Health,
        Damage,
        Defense,
        Speed
    }

    public enum EffectType
    {
        Heal,
        Poison,
        Burn,
        Regeneration,
        LifeSteal,
        Stun
    }

    public enum EnemyBehaviour
    {
        Melee,
        Ranged,
        Charger,
        Summoner,
        Stationary
    }

    public enum TileType
    {
        Floor,
        Wall,
        Pit
    }

    public enum RoomType
    {
        Start,
        Combat,
        Treasure,
        Shop,
        Boss
    }

    [Flags]
    public enum Doors
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3
    }
}
