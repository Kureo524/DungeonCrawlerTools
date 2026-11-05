using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Générateur de courbe : remplit toute la courbe de la stat sélectionnée à partir d'une fonction (preset ou formule personnelle),
    /// avec un aperçu en pointillés avant d'appliquer. Les presets peuvent être enregistrés pour toute l'équipe.
    /// </summary>
    internal sealed class ProgressionGeneratorView : ProgressionView
    {
        const string CustomFunction = "Personnalisée";

        readonly Action previewChanged;

        Foldout generator;
        DropdownField presetField;
        TextField formulaField;
        FloatField startField;
        FloatField endField;
        Label generatorStatus;
        Button applyButton;
        Button deletePresetButton;
        int generatorStat = -1;

        /// <param name="previewChanged">Appelé quand l'aperçu change, pour redessiner la courbe.</param>
        public ProgressionGeneratorView(ProgressionContext context, Action previewChanged) : base(context) => this.previewChanged = previewChanged;

        /// <summary>Valeurs proposées par la fonction, ou null si elle ne peut pas être calculée.</summary>
        public float[] PreviewValues { get; private set; }

        /// <summary>L'aperçu n'est dessiné que lorsque le générateur est déplié.</summary>
        public bool IsOpen => generator != null && generator.value;

        public override void Build(VisualElement parent, PlayerProgression draft)
        {
            generator = new Foldout
            {
                text = "Générer la courbe avec une fonction",
                value = State.GeneratorOpen,
                tooltip = "Remplit toute la courbe de la stat sélectionnée à partir d'une fonction (preset ou formule personnelle). Un aperçu en pointillés s'affiche avant d'appliquer."
            };
            generator.AddToClassList("forge-section");
            generator.AddToClassList("forge-fold");
            generator.RegisterValueChangedCallback(e =>
            {
                if (e.target != generator)
                    return;
                State.GeneratorOpen = e.newValue;
                UpdatePreview();
            });
            parent.Add(generator);

            BuildFormulaFields();
            generatorStatus = ForgeUI.Label(string.Empty, "forge-hint");
            generatorStatus.AddToClassList("forge-mono");
            generator.Add(generatorStatus);
            generator.Add(ForgeUI.Label(Formula.Help, "forge-hint"));
            generator.Add(BuildActions());

            generatorStat = -1;
            presetField.value = "Exponentielle";
        }

        /// <summary>Fonction (preset), formule, et valeurs « start » et « end ».</summary>
        void BuildFormulaFields()
        {
            presetField = new DropdownField("Fonction", PresetChoices(), 0) { tooltip = "Presets intégrés, presets enregistrés par l'équipe, ou formule personnalisée." };
            presetField.RegisterValueChangedCallback(e =>
            {
                var preset = FormulaPresets.All.FirstOrDefault(p => p.name == e.newValue);
                if (preset != null)
                    formulaField.SetValueWithoutNotify(preset.expression);
                UpdatePreview();
            });
            generator.Add(presetField);

            formulaField = new TextField("Formule") { tooltip = Formula.Help };
            formulaField.AddToClassList("forge-formula");
            formulaField.AddToClassList("forge-mono");
            formulaField.RegisterValueChangedCallback(e =>
            {
                var match = FormulaPresets.All.FirstOrDefault(p => p.expression == e.newValue);
                presetField.SetValueWithoutNotify(match?.name ?? CustomFunction);
                UpdatePreview();
            });
            generator.Add(formulaField);

            startField = new FloatField("Valeur au niveau 1 (start)") { tooltip = "Variable « start » de la formule." };
            startField.RegisterValueChangedCallback(_ => UpdatePreview());
            endField = new FloatField($"Valeur au niveau {Draft.MaxLevel} (end)") { tooltip = "Variable « end » de la formule." };
            endField.RegisterValueChangedCallback(_ => UpdatePreview());
            generator.Add(startField);
            generator.Add(endField);
        }

        /// <summary>Appliquer l'aperçu, enregistrer la formule comme preset d'équipe ou supprimer un preset d'équipe.</summary>
        VisualElement BuildActions()
        {
            var actions = new VisualElement();
            actions.AddToClassList("forge-row");
            applyButton = ForgeUI.Button("Appliquer", "Remplace toutes les valeurs de la stat par l'aperçu (Ctrl+Z pour annuler).", ApplyGenerator, "forge-button--primary");
            actions.Add(applyButton);
            var presetName = new TextField { tooltip = "Nom du preset à enregistrer." };
            presetName.style.flexGrow = 1;
            presetName.textEdition.placeholder = "Nom du preset";
            actions.Add(presetName);
            actions.Add(ForgeUI.Button("Enregistrer comme preset", "Ajoute la formule à la liste des fonctions, pour toute l'équipe (Settings/ProgressionPresets.asset).",
                () => SavePreset(presetName)));
            deletePresetButton = ForgeUI.Button("Supprimer le preset", "Retire ce preset d'équipe de la liste (les presets intégrés ne peuvent pas être supprimés).", DeletePreset,
                "forge-button--danger");
            actions.Add(deletePresetButton);
            return actions;
        }

        static List<string> PresetChoices() => FormulaPresets.All.Select(p => p.name).Append(CustomFunction).ToList();

        /// <summary>Quand on change de stat, start et end reprennent ses valeurs actuelles au premier et au dernier niveau.</summary>
        public void SyncWithSelectedStat()
        {
            var selected = State.SelectedStat;
            if (generatorStat == selected || selected >= Draft.Stats.Count)
                return;
            generatorStat = selected;
            var values = Draft.Stats[selected].values;
            startField.SetValueWithoutNotify(values.Count > 0 ? values[0] : 0f);
            endField.SetValueWithoutNotify(values.Count > 0 ? values[^1] : 0f);
        }

        public void UpdatePreview()
        {
            PreviewValues = null;
            var selected = State.SelectedStat;
            var hasStat = selected < Draft.Stats.Count;
            applyButton.text = hasStat ? $"Appliquer à « {Draft.Stats[selected].displayName} »" : "Appliquer";
            deletePresetButton.style.display = presetField.value != CustomFunction && !FormulaPresets.IsBuiltIn(presetField.value) ? DisplayStyle.Flex : DisplayStyle.None;

            if (!hasStat)
                SetStatus("Ajoutez d'abord une stat.", true);
            else if (!Formula.TryParse(formulaField.value, out var formula, out var error))
                SetStatus(error, true);
            else
            {
                try
                {
                    var stat = Draft.Stats[selected];
                    PreviewValues = ProgressionEditing.Generate(formula, Draft.MaxLevel, startField.value, endField.value, stat, out var clamped);
                    SetStatus(clamped > 0
                        ? $"[Aperçu en pointillés. {clamped} niveau(x) ramené(s) dans les bornes de la stat ({stat.min} à {stat.max}).]"
                        : "[Aperçu en pointillés sur la courbe.]", false);
                }
                catch (FormulaException exception)
                {
                    SetStatus(exception.Message, true);
                }
            }
            applyButton.SetEnabled(PreviewValues != null);
            previewChanged();
        }

        void SetStatus(string message, bool isError)
        {
            generatorStatus.text = isError ? $"<color=#e0606a>[{message}]</color>" : message;
        }

        void ApplyGenerator()
        {
            if (PreviewValues == null)
                return;
            ProgressionEditing.SetValues(Serialized, State.SelectedStat, PreviewValues);
            ForgeLists.Notify($"Courbe « {Draft.Stats[State.SelectedStat].displayName} » générée.");
            Context.Refresh();
        }

        void SavePreset(TextField nameField)
        {
            var name = nameField.value?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ForgeLists.Notify("Donnez un nom au preset.");
                return;
            }
            if (FormulaPresets.IsBuiltIn(name) || name == CustomFunction)
            {
                ForgeLists.Notify("Ce nom est réservé à un preset intégré.");
                return;
            }
            if (!Formula.TryParse(formulaField.value, out _, out var error))
            {
                ForgeLists.Notify($"Formule invalide : {error}");
                return;
            }
            if (FormulaPresets.All.Any(p => p.name == name) &&
                !ForgeDialogs.Confirm("Remplacer le preset", $"Le preset « {name} » existe déjà. Le remplacer ?", "Remplacer", "Annuler"))
                return;
            FormulaPresets.Save(name, formulaField.value);
            presetField.choices = PresetChoices();
            presetField.SetValueWithoutNotify(name);
            nameField.SetValueWithoutNotify(string.Empty);
            ForgeLists.Notify($"Preset « {name} » enregistré.");
            UpdatePreview();
        }

        void DeletePreset()
        {
            var name = presetField.value;
            if (FormulaPresets.IsBuiltIn(name) || !ForgeDialogs.Confirm("Supprimer le preset", $"Supprimer le preset « {name} » pour toute l'équipe ?", "Supprimer", "Annuler"))
                return;
            FormulaPresets.Delete(name);
            presetField.choices = PresetChoices();
            presetField.SetValueWithoutNotify(CustomFunction);
            UpdatePreview();
        }
    }
}
