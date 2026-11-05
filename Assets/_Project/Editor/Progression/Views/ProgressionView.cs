using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Progression
{
    /// <summary>Une catégorie de l'onglet Progression (ou une partie de catégorie), construite dans la fiche de la progression éditée.</summary>
    internal abstract class ProgressionView
    {
        protected ProgressionView(ProgressionContext context) => Context = context;

        protected ProgressionContext Context { get; }
        protected PlayerProgression Draft => Context.Draft;
        protected SerializedObject Serialized => Context.Serialized;
        protected ForgeDesignLimits Limits => Context.Limits;
        protected ProgressionState State => Context.State;

        public abstract void Build(VisualElement parent, PlayerProgression draft);

        /// <summary>Appelé après chaque modification, avant les mises à jour légères.</summary>
        public virtual void Refresh()
        {
        }

        protected void Rebuild() => Context.Rebuild();
        protected void ShowView(ProgressionCategory category) => Context.ShowView(category);
        protected void AddRefresher(Action refresher) => Context.AddRefresher(refresher);

        /// <summary>Liste déroulante en français pour un enum sérialisé (les valeurs de l'enum se suivent à partir de 0).</summary>
        protected DropdownField EnumDropdown<TEnum>(string label, SerializedProperty property, Func<TEnum, string> labeler, string tooltip,
            Action<TEnum> changed = null) where TEnum : Enum
        {
            var values = Enum.GetValues(typeof(TEnum)).Cast<TEnum>().ToList();
            var choices = values.Select(labeler).ToList();
            var field = new DropdownField(label, choices, Mathf.Clamp(property.enumValueIndex, 0, choices.Count - 1)) { tooltip = tooltip };
            var path = property.propertyPath;
            field.RegisterValueChangedCallback(e =>
            {
                var index = choices.IndexOf(e.newValue);
                if (index < 0)
                    return;
                Serialized.Update();
                Serialized.FindProperty(path).enumValueIndex = index;
                Serialized.ApplyModifiedProperties();
                changed?.Invoke(values[index]);
            });
            return field;
        }

        /// <summary>Choix d'une stat ou d'une compétence par son nom ; on enregistre son identifiant (stable même si on la renomme).</summary>
        /// <param name="emptyLabel">Choix « aucun » (identifiant vide), ou null s'il faut forcément choisir.</param>
        protected DropdownField IdDropdown(string label, SerializedProperty property, Func<List<(string id, string name)>> options, string tooltip,
            string emptyLabel = null)
        {
            var path = property.propertyPath;
            var field = new DropdownField(label) { tooltip = tooltip };
            List<(string id, string name)> current = null;
            void Sync()
            {
                current = options();
                if (emptyLabel != null)
                    current.Insert(0, (string.Empty, emptyLabel));
                field.choices = current.Select(o => o.name).ToList();
                var selected = current.FindIndex(o => o.id == Serialized.FindProperty(path)?.stringValue);
                field.SetValueWithoutNotify(selected >= 0 ? current[selected].name : "(à choisir)");
            }
            Sync();
            AddRefresher(Sync);
            field.RegisterValueChangedCallback(e =>
            {
                var index = field.choices.IndexOf(e.newValue);
                if (index < 0 || index >= current.Count)
                    return;
                Serialized.Update();
                Serialized.FindProperty(path).stringValue = current[index].id;
                Serialized.ApplyModifiedProperties();
            });
            return field;
        }
    }
}
