using System.Collections.Generic;
using TheDungeon.Data;
using UnityEngine;

namespace TheDungeon.Game
{
    /// <summary>Celui qui lance une attaque de l'outil Attaques : un ennemi, ou le joueur avec son arme.</summary>
    public interface IAttackActor
    {
        DungeonGame Game { get; }
        Faction Faction { get; }
        Transform Transform { get; }
        /// <summary>Dégâts de base, avant les pourcentages de l'attaque et de l'étape.</summary>
        int BaseDamage { get; }
        int MaxHp { get; }
        /// <summary>Direction visée : vers le joueur pour un ennemi, la visée du joueur pour son arme.</summary>
        Vector2 Aim { get; }
        /// <summary>Point visé : le joueur pour un ennemi ; pour le joueur, l'ennemi le plus proche dans la visée, sinon un point devant lui.</summary>
        Vector2 TargetPoint { get; }
        IEnemySituation Situation { get; }
        RoomInstance Room { get; }
        /// <summary>Le joueur maintient la touche (attaque chargée) ; toujours faux pour un ennemi.</summary>
        bool Holding { get; }
        void SetVelocity(Vector2 velocity);
        void Flash(Color color);
        void Heal(int amount);
        void SelfDamage(int amount);
        void SetPhase(int phase);
        /// <summary>Effets de l'arme transmis aux cibles (joueur) ; null pour un ennemi.</summary>
        IReadOnlyList<ItemEffect> OnHitEffects { get; }
    }
}
