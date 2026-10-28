using UnityEngine;

namespace TheDungeon.Data
{
    [CreateAssetMenu(menuName = "The Dungeon/Enemy", fileName = "ENM_NewEnemy")]
    public class EnemyData : ScriptableObject
    {
        [SerializeField, Tooltip("Nom affiché au joueur.")]
        string displayName;
        [SerializeField, Tooltip("Visuel de l'ennemi.")]
        Sprite sprite;
        [SerializeField, Tooltip("Un boss garde la salle finale d'un étage. Chaque étage doit en contenir exactement un.")]
        bool isBoss;
        [SerializeField, Tooltip("Comportement de combat.")]
        EnemyBehaviour behaviour;
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
