using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Data
{
    /// <summary>Ce que fait une étape d'attaque. Nouvelles valeurs toujours ajoutées à la fin (les attaques enregistrent le numéro).</summary>
    public enum StepAction
    {
        Strike,
        Projectiles,
        Dash,
        Summon,
        Pause,
        Teleport,
        Buff,
        Absorb,
        Phase
    }

    /// <summary>Pour qui l'attaque est faite : un ennemi, l'attaque d'une arme, ou la compétence d'une arme.</summary>
    public enum AttackUser
    {
        Enemy,
        Weapon,
        Skill
    }

    /// <summary>Déplacement de l'attaquant pendant la préparation et l'action d'une étape (marcher en frappant…).</summary>
    public enum StepMove
    {
        None,
        TowardTarget,
        AwayFromTarget,
        Strafe,
        Forward
    }

    /// <summary>Effet infligé à la cible touchée, en plus des dégâts.</summary>
    public enum StatusEffect
    {
        None,
        Poison,
        Burn,
        Rot,
        Frost,
        Bleed,
        Stun,
        Knockback,
        Launch
    }

    /// <summary>Effet sur l'attaquant lui-même (étapes « Renforcement » et « Absorption »).</summary>
    public enum BuffType
    {
        Heal,
        Damage,
        Speed,
        Armor,
        Invulnerable
    }

    /// <summary>Destination d'une téléportation.</summary>
    public enum TeleportTarget
    {
        BehindTarget,
        NearTarget,
        AwayFromTarget,
        RandomNearSelf
    }

    /// <summary>Forme de la zone frappée par une étape « Frappe ».</summary>
    public enum ZoneShape
    {
        Circle,
        Ring,
        Cone,
        Line
    }

    /// <summary>
    /// Où apparaît la zone : sur l'attaquant, sur la cible, devant l'attaquant, là où tombe un projectile lancé,
    /// ou au hasard près de l'attaquant ou de la cible.
    /// </summary>
    public enum ZoneAnchor
    {
        Self,
        Target,
        Ahead,
        Thrown,
        RandomNearSelf,
        RandomNearTarget
    }

    public enum ProjectilePattern
    {
        Aimed,
        Spread,
        Ring
    }

    public enum DashDirection
    {
        TowardPlayer,
        AwayFromPlayer,
        Sideways
    }

    /// <summary>Quand une étape est jouée, selon ce qui s'est passé avant dans l'attaque.</summary>
    public enum StepTrigger
    {
        Always,
        PreviousHit,
        PreviousMissed,
        HitsAtLeast,
        WasHit
    }

    /// <summary>Si la condition d'une étape n'est pas remplie : on passe à la suivante, ou l'attaque s'arrête là.</summary>
    public enum StepFallback
    {
        Skip,
        EndAttack
    }

    /// <summary>
    /// Une étape d'attaque : préparation (annoncée), action, récupération. Sa condition est évaluée au moment où elle commence,
    /// ce qui permet d'adapter l'attaque au joueur (ruée seulement s'il est loin, suite seulement si le coup précédent a touché…).
    /// </summary>
    [Serializable]
    public class AttackStep
    {
        [Tooltip("Nom de l'étape (ex : « Ruée », « Retombée »).")]
        public string name;
        public StepAction action;

        [Tooltip("Quand l'étape est jouée, selon ce qui s'est passé avant.")]
        public StepTrigger trigger;
        [Tooltip("Nombre de coups ayant touché depuis le début de l'attaque (condition « Au moins N coups ont touché »).")]
        public int hitCount = 2;
        [Tooltip("Conditions sur la situation au moment où l'étape commence (distance, PV…).")]
        public List<EnemyCondition> conditions = new();
        [Tooltip("Si la condition n'est pas remplie : passer à l'étape suivante, ou terminer l'attaque.")]
        public StepFallback fallback;

        [Tooltip("Préparation : l'ennemi s'arrête et annonce l'étape (la zone s'affiche au sol). Temps laissé au joueur pour esquiver.")]
        public float windup = 0.4f;
        [Tooltip("Récupération : l'ennemi reste immobile après l'étape. Fenêtre où le joueur peut riposter.")]
        public float recovery = 0.2f;
        [Tooltip("Coché : l'ennemi suit le joueur des yeux pendant la préparation ; décoché : la visée est fixée au début de l'étape.")]
        public bool trackDuringWindup;
        [Tooltip("Dégâts en pourcentage des dégâts de l'ennemi (0 = aucun dégât, ex : une ruée de placement).")]
        public int damagePercent = 100;

        [Tooltip("Forme de la zone frappée.")]
        public ZoneShape shape;
        public ZoneAnchor anchor;
        [Tooltip("Rayon (cercle, anneau, cône) ou longueur (ligne), en cases.")]
        public float radius = 1.5f;
        [Tooltip("Rayon intérieur de l'anneau, en cases : le joueur collé à l'ennemi n'est pas touché.")]
        public float innerRadius = 1f;
        [Tooltip("Ouverture du cône, en degrés.")]
        public float angle = 90f;
        [Tooltip("Largeur de la ligne, en cases.")]
        public float width = 1f;

        public ProjectilePattern pattern;
        public int projectiles = 1;
        [Tooltip("Ouverture de l'éventail, en degrés.")]
        public float spreadAngle = 60f;
        [Tooltip("Vitesse des projectiles, en cases par seconde.")]
        public float projectileSpeed = 6.5f;

        public DashDirection dashDirection;
        [Tooltip("Distance parcourue par la ruée, en cases (elle s'arrête au contact du joueur si elle va vers lui).")]
        public float dashDistance = 4f;
        [Tooltip("Vitesse de la ruée, en cases par seconde.")]
        public float dashSpeed = 12f;

        [Tooltip("Nombre d'ennemis invoqués.")]
        public int summons = 1;

        [Tooltip("Nombre de fois que l'étape est jouée d'affilée (la visée est reprise à chaque fois : double charge, triple coup…).")]
        public int repeat = 1;

        [Tooltip("Déplacement pendant la préparation et l'action (marcher en frappant, reculer en tirant…).")]
        public StepMove moveDuring;
        [Tooltip("Vitesse de ce déplacement, en cases par seconde.")]
        public float moveSpeed = 2f;

        [Tooltip("Distance de la zone : devant l'attaquant, rayon de dispersion des zones au hasard, ou portée max d'un lancer.")]
        public float anchorDistance = 3f;
        [Tooltip("Nombre de zones (au hasard : dispersées ; devant : en chapelet le long de la visée).")]
        public int zoneCount = 1;
        [Tooltip("Durée de la zone en secondes (0 = instantanée). Une zone qui dure blesse à intervalles réguliers (flaque, mur de feu…).")]
        public float activeDuration;
        [Tooltip("Intervalle entre deux dégâts d'une zone qui dure, en secondes.")]
        public float tickInterval = 0.5f;
        [Tooltip("La zone avance dans la direction visée, en cases par seconde (onde, vague de glace…).")]
        public float travelSpeed;
        [Tooltip("La zone grandit, en cases par seconde (anneau qui s'étend…).")]
        public float growth;
        [Tooltip("La zone pivote de cet angle pendant sa durée, en degrés (balayage d'un rayon…).")]
        public float sweepAngle;
        [Tooltip("Coché : l'attaquant reste occupé tant que la zone dure (rayon canalisé). Décoché : la zone reste en place et l'attaque continue.")]
        public bool channel;

        [Tooltip("Rotation des projectiles vers la cible, en degrés par seconde (0 = trajectoire droite).")]
        public float homing;
        [Tooltip("Saut : invulnérable pendant la ruée, il passe par-dessus le joueur (bond sur une tour, saut écrasant…).")]
        public bool airborne;

        public TeleportTarget teleportTo;
        [Tooltip("Ennemi invoqué (vide : un ennemi ordinaire du biome).")]
        public EnemyData summon;

        public BuffType buff;
        [Tooltip("Puissance de l'effet : % de PV rendus, ou % de dégâts / vitesse / réduction des dégâts en plus.")]
        public float buffPower = 20f;
        [Tooltip("Durée de l'effet, en secondes.")]
        public float buffDuration = 8f;

        [Tooltip("Phase que prend l'ennemi (voir les phases de l'ennemi ; 0 = phase de départ).")]
        public int phase = 1;

        [Tooltip("Effet infligé à la cible touchée.")]
        public StatusEffect status;
        [Tooltip("Puissance de l'effet : dégâts par seconde, % de ralentissement, % de PV (saignement) ou force de projection.")]
        public float statusPower = 5f;
        [Tooltip("Durée de l'effet, en secondes.")]
        public float statusDuration = 3f;

        [Tooltip("Réduction des dégâts reçus pendant l'étape, en % (garde, posture).")]
        public float guardPercent;
        [Tooltip("Super-armure : ni étourdi ni repoussé pendant l'étape.")]
        public bool superArmor;
        [Tooltip("Coût en PV de l'attaquant, en % de ses PV max (techniques de sang…).")]
        public float healthCost;

        [Tooltip("Arme du joueur : maintenir la touche prolonge la préparation et augmente les dégâts (attaque chargée).")]
        public bool chargeable;
        [Tooltip("Durée maximale de charge, en secondes.")]
        public float chargeTime = 1f;
        [Tooltip("Dégâts en plus à pleine charge, en %.")]
        public float chargeBonus = 100f;

        /// <summary>L'étape fait des dégâts (elle peut « toucher »).</summary>
        public bool CanHit => damagePercent > 0 && action is StepAction.Strike or StepAction.Projectiles or StepAction.Dash;
    }

    /// <summary>Réglages d'une attaque qu'un ennemi peut remplacer : quand elle se déclenche, et à quelle force et vitesse.</summary>
    [Serializable]
    public class AttackSettings
    {
        [Tooltip("Distance minimale au joueur, en cases, pour lancer l'attaque.")]
        public float minRange;
        [Tooltip("Distance maximale au joueur, en cases, pour lancer l'attaque.")]
        public float maxRange = 2f;
        [Tooltip("Délai avant de pouvoir relancer l'attaque, compté à partir de sa fin (secondes).")]
        public float cooldown = 3f;
        [Tooltip("Multiplie les dégâts de toutes les étapes (%).")]
        public int damagePercent = 100;
        [Tooltip("Cadence : 100 % = durées normales, 150 % = préparations et récupérations 1,5 fois plus rapides.")]
        public int tempoPercent = 100;

        public AttackSettings Clone() => (AttackSettings)MemberwiseClone();
    }

    /// <summary>Réglages qu'un ennemi peut remplacer pour lui seul.</summary>
    [Flags]
    public enum AttackField
    {
        None = 0,
        MinRange = 1 << 0,
        MaxRange = 1 << 1,
        Cooldown = 1 << 4,
        Damage = 1 << 5,
        Tempo = 1 << 9
    }

    /// <summary>
    /// Attaque réutilisable (ATK_) : un enchaînement d'étapes, lancé quand le joueur est à portée.
    /// Une fois lancée, l'attaque va jusqu'au bout (elle peut rater) ; chaque étape décide au moment où elle commence si elle est jouée.
    /// </summary>
    [CreateAssetMenu(menuName = "The Dungeon/Attack", fileName = "ATK_NewAttack")]
    public class AttackData : ScriptableObject, IDisplayNamed
    {
        [SerializeField, Tooltip("Nom de l'attaque (ex : « Coup chargé »). Annoncé en jeu quand un boss l'utilise.")]
        string displayName;
        [SerializeField, TextArea(2, 4), Tooltip("Note pour l'équipe : à quoi sert cette attaque, sur quels ennemis.")]
        string description;
        [SerializeField, Tooltip("Ennemi, attaque d'arme ou compétence d'arme : change le vocabulaire de l'outil et où l'attaque peut être donnée.")]
        AttackUser user;
        [SerializeField]
        AttackSettings settings = new();
        [SerializeField, Tooltip("Étapes jouées dans l'ordre.")]
        List<AttackStep> steps = new();

        public string DisplayName => displayName;
        public string Description => description;
        public AttackUser User => user;
        public AttackSettings Settings => settings;
        public IReadOnlyList<AttackStep> Steps => steps;
    }

    /// <summary>Attaque prête à jouer : réglages effectifs (avec ceux de l'ennemi) et étapes.</summary>
    public sealed class ResolvedAttack
    {
        public AttackSettings Settings;
        public IReadOnlyList<AttackStep> Steps;

        public float Tempo => 100f / Mathf.Max(10, Settings.tempoPercent);
        public bool UsesProjectiles
        {
            get
            {
                foreach (var step in Steps)
                    if (step.action == StepAction.Projectiles)
                        return true;
                return false;
            }
        }
    }

    /// <summary>Attaque d'un ennemi : une attaque réutilisable, ses conditions pour cet ennemi et les réglages qu'il remplace.</summary>
    [Serializable]
    public class EnemyAttack
    {
        [Tooltip("Attaque utilisée (outil Attaques).")]
        public AttackData attack;
        [Tooltip("Conditions pour cet ennemi, toutes nécessaires (en plus de la portée et de la recharge).")]
        public List<EnemyCondition> conditions = new();
        [Tooltip("Poids : quand plusieurs attaques sont possibles en même temps, l'ennemi en tire une au hasard, proportionnellement à leur poids (0 = jamais).")]
        public int weight = 100;
        [Tooltip("Réglages remplacés pour cet ennemi.")]
        public AttackField overridden;
        [Tooltip("Réglages propres à cet ennemi (seuls ceux cochés dans « overridden » comptent).")]
        public AttackSettings overrides = new();

        public bool Overrides(AttackField field) => (overridden & field) != 0;

        /// <summary>Réglages effectifs : ceux de l'attaque, remplacés par ceux de l'ennemi quand il en a ; null sans attaque.</summary>
        public ResolvedAttack Resolve()
        {
            if (attack == null)
                return null;
            var settings = attack.Settings.Clone();
            if (Overrides(AttackField.MinRange)) settings.minRange = overrides.minRange;
            if (Overrides(AttackField.MaxRange)) settings.maxRange = overrides.maxRange;
            if (Overrides(AttackField.Cooldown)) settings.cooldown = overrides.cooldown;
            if (Overrides(AttackField.Damage)) settings.damagePercent = overrides.damagePercent;
            if (Overrides(AttackField.Tempo)) settings.tempoPercent = overrides.tempoPercent;
            return new ResolvedAttack { Settings = settings, Steps = attack.Steps };
        }

        public string Name => attack == null ? null : !string.IsNullOrWhiteSpace(attack.DisplayName) ? attack.DisplayName : attack.name;
    }
}
