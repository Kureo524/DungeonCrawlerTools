using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TheDungeon.Data
{
    [Serializable]
    public struct StatModifier
    {
        [Tooltip("Statistique modifiée quand l'objet est équipé.")]
        public StatType stat;
        [Tooltip("Valeur ajoutée à la statistique (peut être négative).")]
        public int value;
    }

    [Serializable]
    public struct ItemEffect
    {
        [Tooltip("Effet déclenché à l'utilisation ou à l'impact.")]
        public EffectType type;
        [Tooltip("Puissance de l'effet (PV soignés, dégâts par seconde...).")]
        public float value;
        [Tooltip("Durée en secondes. 0 = instantané.")]
        public float duration;
        [Tooltip("Actif : à l'impact ou à l'utilisation. Passif : permanent tant que l'arme est équipée.")]
        public EffectTrigger trigger;
    }

    [CreateAssetMenu(menuName = "The Dungeon/Item", fileName = "ITM_NewItem")]
    public class ItemData : ScriptableObject, IDisplayNamed
    {
        [SerializeField, Tooltip("Nom affiché au joueur.")]
        string displayName;
        [SerializeField, Tooltip("Icône affichée dans l'inventaire.")]
        Sprite icon;
        [SerializeField, Tooltip("Identifiant de la rareté (liste éditée dans l'outil Objets) : couleur, prix, bonus et passifs autorisés.")]
        int rarity;
        [SerializeField, Tooltip("Catégorie de l'objet.")]
        ItemType type;
        [SerializeField, Tooltip("Bonus de statistiques quand l'objet est équipé.")]
        List<StatModifier> stats = new();
        [SerializeField, Tooltip("Effets spéciaux de l'objet.")]
        List<ItemEffect> effects = new();
        [SerializeField, Tooltip("Prix d'achat chez un marchand, en pièces d'or.")]
        int price;
        [SerializeField, TextArea(2, 5), Tooltip("Description affichée dans l'infobulle en jeu.")]
        string description;

        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public int RarityId => rarity;
        public ItemType Type => type;
        public IReadOnlyList<StatModifier> Stats => stats;
        public IReadOnlyList<ItemEffect> Effects => effects;
        public IEnumerable<ItemEffect> ActiveEffects => effects.Where(e => e.trigger == EffectTrigger.Active);
        public IEnumerable<ItemEffect> PassiveEffects => effects.Where(e => e.trigger == EffectTrigger.Passive);

        /// <summary>Somme des passifs d'un type (0 s'il n'y en a pas).</summary>
        public float PassiveTotal(EffectType type) => PassiveEffects.Where(e => e.type == type).Sum(e => e.value);
        public int Price => price;
        public string Description => description;
    }
}
