using System;

namespace TheDungeon.Data
{
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

    /// <summary>Nouvelles valeurs toujours ajoutées à la fin : les objets enregistrent le numéro de l'effet.</summary>
    public enum EffectType
    {
        Heal,
        Poison,
        Burn,
        Regeneration,
        LifeSteal,
        Stun,
        AttackBoost,
        CriticalChance,
        LastStand,
        KillHeal,
        DamageReduction,
        MoveSpeed
    }

    /// <summary>Actif : à l'impact (arme) ou à l'utilisation (consommable). Passif : permanent tant que l'arme est équipée.</summary>
    public enum EffectTrigger
    {
        Active,
        Passive
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
        Boss,
        Npc
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
