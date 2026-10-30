using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Liste déroulante des raretés (Objets > Raretés). Elle enregistre l'identifiant de la rareté, pas son nom :
    /// renommer une rareté ne casse donc rien. Une rareté supprimée reste affichée, marquée, tant qu'on n'en choisit pas une autre.
    /// </summary>
    public sealed class RarityField : DropdownField
    {
        readonly List<int> ids = new();
        Func<int> read;
        Action<int> write;

        /// <summary>Champ libre : la valeur se lit et s'écrit avec <see cref="Id"/>.</summary>
        public RarityField(string label, int initialId, string tooltip) : base(label)
        {
            this.tooltip = tooltip;
            var current = initialId;
            read = () => current;
            write = id => current = id;
            Rebuild();
            this.RegisterValueChangedCallback(_ => OnPicked());
        }

        /// <summary>Champ lié à une propriété entière (l'identifiant de rareté d'un objet, d'un bonus de biome…).</summary>
        public RarityField(string label, SerializedProperty property, string tooltip) : base(label)
        {
            this.tooltip = tooltip;
            var serializedObject = property.serializedObject;
            var path = property.propertyPath;
            read = () => serializedObject.FindProperty(path).intValue;
            write = id =>
            {
                serializedObject.FindProperty(path).intValue = id;
                serializedObject.ApplyModifiedProperties();
            };
            Rebuild();
            this.RegisterValueChangedCallback(_ => OnPicked());
            // Annulation, correction automatique… : la liste suit la valeur enregistrée.
            this.TrackPropertyValue(property, _ => Rebuild());
        }

        public int Id => read();

        /// <summary>Appelé quand l'utilisateur choisit une autre rareté.</summary>
        public event Action<int> Changed;

        void OnPicked()
        {
            var index = choices.IndexOf(value);
            if (index < 0 || index >= ids.Count || ids[index] == read())
                return;
            write(ids[index]);
            Changed?.Invoke(ids[index]);
            Rebuild();
        }

        /// <summary>Recharge les raretés (après une modification de la liste).</summary>
        public void Rebuild()
        {
            var limits = ForgeDesignLimits.Instance;
            var current = read();
            ids.Clear();
            ids.AddRange(limits.rarities.Select(rule => rule.id));
            var names = limits.rarities.Select(rule => rule.DisplayName).ToList();
            if (!ids.Contains(current))
            {
                ids.Add(current);
                names.Add($"⚠ Rareté supprimée (n°{current})");
            }
            choices = names;
            SetValueWithoutNotify(names[ids.IndexOf(current)]);
            EnableInClassList("forge-rarity-field--missing", !limits.HasRarity(current));
        }
    }
}
