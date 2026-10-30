using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Data
{
    /// <summary>Déplacement d'un pattern d'ennemi.</summary>
    public enum PatternMovement
    {
        Chase,
        KeepDistance,
        Circle,
        Flee,
        Hold,
        Charge
    }

    /// <summary>Ce que vérifie une condition de pattern. Nouvelles valeurs toujours ajoutées à la fin (les ennemis enregistrent le numéro).</summary>
    public enum EnemyConditionType
    {
        PlayerCloserThan,
        PlayerFartherThan,
        PlayerInSight,
        PlayerHidden,
        PlayerHealthBelow,
        OwnHealthBelow,
        PlayerLevelAtLeast,
        MechanicAtLeast,
        FlagSet,
        FloorIn,
        BiomeIs,
        RoomTypeIs,
        AlliesAtMost,
        FightLongerThan,
        PhaseIs
    }

    /// <summary>Condition d'un pattern : environnement, position ou état du joueur, état de l'ennemi.</summary>
    [Serializable]
    public class EnemyCondition
    {
        [Tooltip("Ce qui est vérifié.")]
        public EnemyConditionType type;
        [Tooltip("Valeur comparée : cases, pourcentage, niveau, secondes, nombre d'alliés ou étages cochés selon le type.")]
        public int value = 1;
        [Tooltip("Identifiant de mécanique (progression) ou marqueur d'histoire, selon le type.")]
        public string flag;
        [Tooltip("Biome concerné (condition « Biome »).")]
        public BiomeData biome;
        [Tooltip("Type de salle concerné (condition « Type de salle »).")]
        public RoomType roomType;
    }

    /// <summary>
    /// Phase d'un ennemi : entrée par une étape d'attaque « Changer de phase ». Modifie ses stats, peut ajouter une aura
    /// (effet continu autour de lui) et faire apparaître des renforts à intervalle régulier. Les règles et attaques
    /// peuvent dépendre de la phase (condition « Phase de l'ennemi »).
    /// </summary>
    [Serializable]
    public class EnemyPhase
    {
        [Tooltip("Nom de la phase (ex : « Putréfaction »). Annoncé en jeu à l'entrée dans la phase.")]
        public string name = "Phase";
        [Tooltip("Dégâts de l'ennemi pendant cette phase, en %.")]
        public int damagePercent = 100;
        [Tooltip("Vitesse de l'ennemi pendant cette phase, en %.")]
        public int speedPercent = 100;
        [Tooltip("Dégâts reçus pendant cette phase, en % (moins de 100 = plus résistant).")]
        public int damageTakenPercent = 100;
        [Tooltip("Aura : rayon autour de l'ennemi, en cases (0 = pas d'aura).")]
        public float auraRadius;
        [Tooltip("Effet de l'aura sur le joueur qui s'y trouve.")]
        public StatusEffect auraStatus = StatusEffect.Rot;
        [Tooltip("Puissance de l'aura (dégâts par seconde, % de ralentissement…).")]
        public float auraPower = 3f;
        [Tooltip("Renfort qui apparaît pendant la phase (vide : aucun).")]
        public EnemyData spawn;
        [Tooltip("Secondes entre deux renforts.")]
        public float spawnInterval = 6f;
        [Tooltip("Nombre maximum de renforts en vie en même temps.")]
        public int spawnMax = 3;
    }

    /// <summary>
    /// Règle de déplacement d'un ennemi, utilisée quand toutes ses conditions sont remplies.
    /// Les règles sont testées dans l'ordre : la première qui s'applique est jouée ; sinon, le comportement de base.
    /// </summary>
    [Serializable]
    public class EnemyPattern
    {
        [Tooltip("Nom de la règle dans les outils (ex : « Rage », « Recul »).")]
        public string name;
        [Tooltip("Toutes doivent être remplies pour que la règle s'applique. Aucune condition = toujours.")]
        public List<EnemyCondition> conditions = new();
        [Tooltip("Façon de se déplacer pendant cette règle.")]
        public PatternMovement movement;
        [Tooltip("Vitesse en pourcentage de la vitesse de l'ennemi.")]
        public int speedPercent = 100;
    }

    [CreateAssetMenu(menuName = "The Dungeon/Enemy", fileName = "ENM_NewEnemy")]
    public class EnemyData : ScriptableObject, IDisplayNamed
    {
        [SerializeField, Tooltip("Nom affiché au joueur.")]
        string displayName;
        [SerializeField, Tooltip("Visuel de l'ennemi.")]
        Sprite sprite;
        [SerializeField, Tooltip("Un boss garde la salle finale d'un étage. Chaque étage doit en contenir exactement un.")]
        bool isBoss;
        [SerializeField, Tooltip("Comportement de combat par défaut : utilisé quand aucun pattern ne s'applique.")]
        EnemyBehaviour behaviour;
        [SerializeField, Tooltip("Règles de déplacement, testées dans l'ordre : la première dont les conditions sont remplies est jouée.")]
        List<EnemyPattern> patterns = new();
        [SerializeField, Tooltip("Attaques, testées dans l'ordre : la première prête, à portée et dont les conditions sont remplies est utilisée.")]
        List<EnemyAttack> attacks = new();
        [SerializeField, Tooltip("Phases 1, 2… (la phase 0 est l'ennemi de départ). On y entre par une étape d'attaque « Changer de phase ».")]
        List<EnemyPhase> phases = new();
        [SerializeField, Tooltip("Points de vie à l'étage 1.")]
        int baseHealth = 10;
        [SerializeField, Tooltip("Dégâts par coup à l'étage 1.")]
        int baseDamage = 2;
        [SerializeField, Tooltip("Réduction des dégâts reçus à l'étage 1.")]
        int baseDefense;
        [SerializeField, Tooltip("Vitesse de déplacement en cases par seconde.")]
        float moveSpeed = 2f;
        [SerializeField, Tooltip("XP gagnée par le joueur à l'étage 1.")]
        int xpReward = 5;
        [SerializeField, Tooltip("Croissance des stats par étage. 0.25 = +25 % à chaque étage descendu.")]
        float floorScaling = 0.25f;
        [SerializeField, Tooltip("Table de butin tirée à la mort de l'ennemi.")]
        LootTable lootTable;

        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public bool IsBoss => isBoss;
        public EnemyBehaviour Behaviour => behaviour;
        public IReadOnlyList<EnemyPattern> Patterns => patterns;
        public IReadOnlyList<EnemyAttack> Attacks => attacks;
        public IReadOnlyList<EnemyPhase> Phases => phases;

        /// <summary>Phase n (1 = première phase ajoutée), ou null pour la phase de départ ou une phase inexistante.</summary>
        public EnemyPhase Phase(int number) => number >= 1 && number <= phases.Count ? phases[number - 1] : null;
        public float MoveSpeed => moveSpeed;
        public float FloorScaling => floorScaling;
        public LootTable LootTable => lootTable;

        public int GetHealth(int floor) => Scale(baseHealth, floor);
        public int GetDamage(int floor) => Scale(baseDamage, floor);
        public int GetDefense(int floor) => Scale(baseDefense, floor);
        public int GetXpReward(int floor) => Scale(xpReward, floor);

        int Scale(int baseValue, int floor)
        {
            var multiplier = 1f + floorScaling * Mathf.Max(0, floor - 1);
            return Mathf.RoundToInt(baseValue * multiplier);
        }
    }
}
