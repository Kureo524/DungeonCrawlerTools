using System;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Élément du comportement d'un ennemi affiché dans l'éditeur.</summary>
    internal enum BehaviourItem { Rule, Otherwise, Attack, Phase }

    /// <summary>
    /// Éditeur de l'élément choisi dans la vue Comportement : règle de déplacement, attaque de l'ennemi
    /// (avec ses valeurs propres), phase, ou comportement de base (« Sinon »).
    /// </summary>
    internal sealed class EnemyBehaviourEditor
    {
        readonly VisualElement editor;
        readonly SerializedObject serializedObject;
        readonly Func<EnemyData> enemy;
        readonly Action<UnityEngine.Object> reveal;
        readonly Action<BehaviourItem, int> select;
        BehaviourItem kind;
        int selected;

        /// <param name="select">Change l'élément choisi (après une suppression).</param>
        /// <param name="reveal">Ouvre un élément dans son outil (l'attaque d'origine).</param>
        public EnemyBehaviourEditor(VisualElement editor, SerializedObject serializedObject, Func<EnemyData> enemy,
            Action<BehaviourItem, int> select, Action<UnityEngine.Object> reveal)
        {
            this.editor = editor;
            this.serializedObject = serializedObject;
            this.enemy = enemy;
            this.select = select;
            this.reveal = reveal;
        }

        static ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        SerializedProperty Patterns => serializedObject.FindProperty("patterns");
        SerializedProperty Attacks => serializedObject.FindProperty("attacks");
        SerializedProperty Phases => serializedObject.FindProperty("phases");

        public void Show(BehaviourItem item, int index)
        {
            kind = item;
            selected = index;
            Rebuild();
        }

        void Rebuild()
        {
            editor.Clear();
            serializedObject.Update();
            switch (kind)
            {
                case BehaviourItem.Rule:
                    BuildRuleEditor(Patterns.GetArrayElementAtIndex(selected).propertyPath);
                    break;
                case BehaviourItem.Attack:
                    BuildAttackEditor(Attacks.GetArrayElementAtIndex(selected).propertyPath);
                    break;
                case BehaviourItem.Phase:
                    BuildPhaseEditor(Phases.GetArrayElementAtIndex(selected).propertyPath);
                    break;
                default:
                    BuildOtherwiseEditor();
                    break;
            }
        }

        /// <summary>En-tête commun : numéro, nom éditable et « Supprimer ».</summary>
        void Header(string label, string path, SerializedProperty list, string tooltip)
        {
            var name = new TextField { tooltip = tooltip };
            name.AddToClassList("forge-rule-editor__name");
            name.BindProperty(serializedObject.FindProperty(path).FindPropertyRelative("name"));
            name.style.flexGrow = 1;
            var listPath = list.propertyPath;
            var delete = ForgeUI.Button("Supprimer", "Supprime cet élément.", () =>
            {
                serializedObject.Update();
                serializedObject.FindProperty(listPath).DeleteArrayElementAtIndex(selected);
                serializedObject.ApplyModifiedProperties();
                var remaining = serializedObject.FindProperty(listPath).arraySize;
                select(remaining == 0 ? BehaviourItem.Otherwise : kind, Mathf.Min(selected, remaining - 1));
            }, "forge-button--danger");
            editor.Add(ForgeLists.Row(ForgeUI.Label(label, "forge-rule-card__number"), name, delete));
        }

        void Conditions(string path, string heading, string emptyText)
        {
            editor.Add(ForgeUI.Label(heading, "forge-rule-editor__heading"));
            editor.Add(new ConditionChips(serializedObject, Rebuild).Build(path, emptyText));
        }

        void BuildRuleEditor(string path)
        {
            Header($"Règle {selected + 1}", path, Patterns, "Nom de la règle (ex : « Rage »). Annoncé en jeu quand un boss change de règle.");
            Conditions(path, "Quand", "toujours (aucune condition)");
            var pattern = serializedObject.FindProperty(path);
            if (pattern.FindPropertyRelative("conditions").arraySize == 0 && selected < Patterns.arraySize - 1)
                editor.Add(ForgeUI.Label("Sans condition, cette règle s'applique toujours : les règles en dessous ne seront jamais utilisées.", "forge-rule-editor__warning"));

            editor.Add(ForgeUI.Label("Alors l'ennemi se déplace…", "forge-rule-editor__heading"));
            var movement = (PatternMovement)pattern.FindPropertyRelative("movement").enumValueIndex;
            var movements = new VisualElement();
            movements.AddToClassList("forge-tiles");
            foreach (var option in (PatternMovement[])Enum.GetValues(typeof(PatternMovement)))
                movements.Add(BehaviourGlyphs.Tile(EnemyPatternText.Short(option), EnemyPatternText.Label(option),
                    (p, c, s) => BehaviourGlyphs.Movement(p, c, s, option), option == movement,
                    () => SetEnum(path, "movement", (int)option)));
            editor.Add(Group("Façon", movements));
            if (movement != PatternMovement.Hold)
            {
                var settings = new VisualElement();
                settings.AddToClassList("forge-chips");
                settings.Add(IntPill("vitesse", "%", pattern.FindPropertyRelative("speedPercent"), Limits.patternSpeed, "Vitesse en pourcentage de la vitesse de l'ennemi."));
                editor.Add(Group("Réglage", settings));
            }
            if (enemy().Behaviour == EnemyBehaviour.Stationary && movement != PatternMovement.Hold)
                editor.Add(ForgeUI.Label("Cet ennemi est une tourelle (voir « Sinon ») : il ne se déplace jamais, même dans une règle.", "forge-rule-editor__warning"));
        }

        void BuildAttackEditor(string path)
        {
            var slot = serializedObject.FindProperty(path);
            var index = selected;
            var attackData = slot.FindPropertyRelative("attack").objectReferenceValue as AttackData;

            AttackHeader(path, index, attackData);
            if (attackData == null)
            {
                editor.Add(ForgeUI.Label("Cette attaque a été supprimée : choisissez-en une autre ou retirez-la.", "forge-rule-editor__warning"));
                return;
            }
            var users = AssetFactory.LoadAll<EnemyData>().Count(e => e.Attacks.Any(a => a.attack == attackData));
            editor.Add(ForgeUI.Label(users > 1 ? $"Attaque partagée par {users} ennemis." : "Attaque utilisée par cet ennemi seulement.", "forge-hint"));

            editor.Add(ForgeUI.Label("Déclenchement", "forge-rule-editor__heading"));
            editor.Add(ForgeUI.Label("Valeurs grisées : celles de l'attaque. Cliquez une valeur pour la changer pour cet ennemi seulement ; « ↺ » revient à celle de l'attaque.",
                "forge-hint"));
            editor.Add(AttackEditor.Settings(slot.FindPropertyRelative("overrides"),
                new AttackEditor.Override { Mask = slot.FindPropertyRelative("overridden"), Base = attackData.Settings }, Rebuild));

            AttackWeight(slot);
            AttackSteps(attackData, index);
            Conditions(path, "Conditions pour cet ennemi", "aucune (seulement la portée et la recharge)");
        }

        /// <summary>En-tête : l'attaque utilisée (modifiable), l'ouvrir dans l'outil Attaques, la retirer de cet ennemi.</summary>
        void AttackHeader(string path, int index, AttackData attackData)
        {
            var attacks = AssetFactory.LoadAll<AttackData>().OrderBy(a => a.DisplayName).ToList();
            var names = attacks.Select(a => ForgeText.NameOrRedacted(a.DisplayName)).ToList();
            var picker = new DropdownField(names, Mathf.Max(0, attacks.IndexOf(attackData))) { tooltip = "Attaque utilisée (outil Attaques)." };
            picker.AddToClassList("forge-rule-editor__name");
            picker.style.flexGrow = 1;
            if (attackData == null)
                picker.SetValueWithoutNotify("(attaque manquante)");
            picker.RegisterValueChangedCallback(evt =>
            {
                var chosen = names.IndexOf(evt.newValue);
                if (chosen < 0)
                    return;
                serializedObject.Update();
                serializedObject.FindProperty(path).FindPropertyRelative("attack").objectReferenceValue = attacks[chosen];
                serializedObject.ApplyModifiedProperties();
                Rebuild();
            });
            var open = ForgeUI.Button("Ouvrir l'attaque", "Modifie l'attaque elle-même : le changement vaut pour tous les ennemis qui l'utilisent.",
                () => { if (attackData != null) reveal(attackData); });
            open.SetEnabled(attackData != null);
            var remove = ForgeUI.Button("Retirer", "Retire cette attaque de l'ennemi (l'attaque reste dans l'outil Attaques).", () =>
            {
                serializedObject.Update();
                Attacks.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                var remaining = Attacks.arraySize;
                select(remaining == 0 ? BehaviourItem.Otherwise : BehaviourItem.Attack, Mathf.Min(index, remaining - 1));
            }, "forge-button--danger");
            editor.Add(ForgeLists.Row(ForgeUI.Label($"Attaque {index + 1}", "forge-rule-card__number"), picker, open, remove));
        }

        void AttackWeight(SerializedProperty slot)
        {
            editor.Add(ForgeUI.Label("Poids", "forge-rule-editor__heading"));
            var weight = new VisualElement();
            weight.AddToClassList("forge-chips");
            var weightPill = new VisualElement { tooltip = "Quand plusieurs attaques sont possibles en même temps, l'ennemi en tire une au hasard, proportionnellement à leur poids. 0 = jamais." };
            weightPill.AddToClassList("forge-chip");
            weightPill.AddToClassList("forge-chip--setting");
            weightPill.Add(ForgeUI.Label("poids", "forge-chip__text"));
            weightPill.Add(AttackEditor.IntField(slot.FindPropertyRelative("weight"), 0, 1000));
            weight.Add(weightPill);
            weight.Add(ForgeUI.Label("Les chances de chaque attaque, dans la situation de l'arène, s'affichent sur les cartes et sous l'arène.", "forge-hint"));
            editor.Add(weight);
        }

        /// <summary>Enchaînement en lecture : on le modifie dans l'outil Attaques (il est partagé).</summary>
        void AttackSteps(AttackData attackData, int index)
        {
            editor.Add(ForgeUI.Label("Enchaînement", "forge-rule-editor__heading"));
            editor.Add(new AttackTimeline(() => enemy().Attacks.ElementAtOrDefault(index)?.Resolve()));
            for (var i = 0; i < attackData.Steps.Count; i++)
            {
                var step = attackData.Steps[i];
                var when = EnemyPatternText.When(step, i == 0, EnemyBehaviourView.MechanicName);
                var line = $"<b>{i + 1}. {ForgeText.NameOrRedacted(step.name)}</b>  {EnemyPatternText.Summary(step)}";
                if (when != null)
                    line += $"\n<color=#a597c0>   si</color> {when}{(step.fallback == StepFallback.EndAttack ? " <color=#8a8a9c>(sinon l'attaque s'arrête)</color>" : " <color=#8a8a9c>(sinon sautée)</color>")}";
                editor.Add(ForgeUI.Label(line, "forge-step-line"));
            }
            editor.Add(ForgeUI.Label("Pour changer les étapes, ouvrez l'attaque : la modification vaut pour tous les ennemis qui l'utilisent.", "forge-hint"));
        }

        void BuildPhaseEditor(string path)
        {
            var phase = serializedObject.FindProperty(path);
            var index = selected;
            var name = new TextField { tooltip = "Nom de la phase, annoncé en jeu (ex : « Putréfaction »)." };
            name.AddToClassList("forge-rule-editor__name");
            name.BindProperty(phase.FindPropertyRelative("name"));
            name.style.flexGrow = 1;
            var delete = ForgeUI.Button("Supprimer", "Supprime cette phase (les phases suivantes changent de numéro).", () =>
            {
                serializedObject.Update();
                Phases.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                select(Phases.arraySize == 0 ? BehaviourItem.Otherwise : BehaviourItem.Phase, Mathf.Min(index, Phases.arraySize - 1));
            }, "forge-button--danger");
            editor.Add(ForgeLists.Row(ForgeUI.Label($"Phase {index + 1}", "forge-rule-card__number"), name, delete));
            editor.Add(ForgeUI.Label($"Pour y entrer : une étape « Changer de phase » → phase {index + 1}. Pour qu'une règle ou une attaque ne serve que dans cette phase : " +
                                     $"condition « Phase de l'ennemi = {index + 1} ».", "forge-hint"));

            editor.Add(ForgeUI.Label("Stats", "forge-rule-editor__heading"));
            var stats = new VisualElement();
            stats.AddToClassList("forge-chips");
            stats.Add(PhasePill(phase, "damagePercent", "dégâts", "%", "Dégâts de l'ennemi pendant cette phase.", true));
            stats.Add(PhasePill(phase, "speedPercent", "vitesse", "%", "Vitesse de l'ennemi pendant cette phase.", true));
            stats.Add(PhasePill(phase, "damageTakenPercent", "dégâts reçus", "%", "Moins de 100 : plus résistant ; plus de 100 : plus fragile.", true));
            editor.Add(stats);

            editor.Add(ForgeUI.Label("Aura", "forge-rule-editor__heading"));
            var aura = new VisualElement();
            aura.AddToClassList("forge-chips");
            aura.Add(PhasePill(phase, "auraRadius", "rayon", "cases (0 = aucune)", "Le joueur dans ce rayon subit l'effet en continu (putréfaction qui s'accumule…).", false));
            var statuses = ((StatusEffect[])Enum.GetValues(typeof(StatusEffect))).Skip(1).ToList();
            var status = new DropdownField(statuses.Select(EnemyPatternText.Label).ToList(), Mathf.Max(0, phase.FindPropertyRelative("auraStatus").enumValueIndex - 1));
            status.AddToClassList("forge-chip__field");
            status.AddToClassList("forge-chip__field--wide");
            status.RegisterValueChangedCallback(_ =>
            {
                serializedObject.Update();
                serializedObject.FindProperty(path).FindPropertyRelative("auraStatus").enumValueIndex = status.index + 1;
                serializedObject.ApplyModifiedProperties();
            });
            aura.Add(status);
            aura.Add(PhasePill(phase, "auraPower", "puissance", null, "Dégâts par seconde, % de lenteur…", false));
            editor.Add(aura);

            editor.Add(ForgeUI.Label("Renforts", "forge-rule-editor__heading"));
            editor.Add(new AssetPicker<EnemyData>("Ennemi", phase.FindPropertyRelative("spawn"), "Ennemi qui apparaît pendant la phase (vide : aucun)."));
            var spawn = new VisualElement();
            spawn.AddToClassList("forge-chips");
            spawn.Add(PhasePill(phase, "spawnInterval", "toutes les", "s", "Secondes entre deux renforts.", false));
            spawn.Add(PhasePill(phase, "spawnMax", "au plus", "en vie", "Renforts en vie en même temps.", true));
            editor.Add(spawn);
            editor.Add(ForgeUI.Label("Astuce : une étape « Absorption » dans une attaque de cette phase fait disparaître les renforts proches pour renforcer l'ennemi.", "forge-hint"));
        }

        static VisualElement PhasePill(SerializedProperty phase, string property, string before, string after, string tooltip, bool isInt)
        {
            var field = isInt
                ? (VisualElement)AttackEditor.IntField(phase.FindPropertyRelative(property), 0, 1000)
                : AttackEditor.FloatField(phase.FindPropertyRelative(property), 0f, 100f);
            return Pill(before, field, after, tooltip);
        }

        void BuildOtherwiseEditor()
        {
            editor.Add(ForgeLists.Row(ForgeUI.Label("Sinon", "forge-rule-card__number"),
                ForgeUI.Label("Quand aucune règle de déplacement ne s'applique, l'ennemi a son comportement de base.", "forge-hint")));
            var behaviour = (EnemyBehaviour)serializedObject.FindProperty("behaviour").enumValueIndex;
            var tiles = new VisualElement();
            tiles.AddToClassList("forge-tiles");
            foreach (var option in (EnemyBehaviour[])Enum.GetValues(typeof(EnemyBehaviour)))
                tiles.Add(BehaviourGlyphs.Tile(EnemyPatternText.Label(option), EnemyPatternText.Describe(option),
                    (p, c, s) => BehaviourGlyphs.Behaviour(p, c, s, option), option == behaviour,
                    () =>
                    {
                        serializedObject.Update();
                        serializedObject.FindProperty("behaviour").enumValueIndex = (int)option;
                        serializedObject.ApplyModifiedProperties();
                        Rebuild();
                    }));
            editor.Add(Group("Comportement", tiles));
            editor.Add(ForgeUI.Label(EnemyPatternText.Describe(behaviour), "forge-hint"));
            if (enemy().Attacks.Count > 0)
                editor.Add(ForgeUI.Label("Cet ennemi a ses propres attaques : le comportement de base ne sert qu'à le déplacer.", "forge-hint"));
        }

        static VisualElement Group(string title, VisualElement content)
        {
            var group = new VisualElement();
            group.AddToClassList("forge-rule-editor__group");
            group.Add(ForgeUI.Label(title, "forge-rule-editor__label"));
            content.style.flexGrow = 1;
            group.Add(content);
            return group;
        }

        void SetEnum(string path, string field, int value)
        {
            serializedObject.Update();
            serializedObject.FindProperty(path).FindPropertyRelative(field).enumValueIndex = value;
            serializedObject.ApplyModifiedProperties();
            Rebuild();
        }

        static VisualElement IntPill(string before, string after, SerializedProperty property, IntRange range, string tooltip)
        {
            var field = new IntegerField { tooltip = $"{tooltip}\nValeurs autorisées : {range}." };
            field.AddToClassList("forge-chip__field");
            field.BindProperty(property);
            field.RegisterValueChangedCallback(evt =>
            {
                if (!range.Contains(evt.newValue))
                    field.value = range.Clamp(evt.newValue);
            });
            return Pill(before, field, after, tooltip);
        }

        static VisualElement Pill(string before, VisualElement field, string after, string tooltip)
        {
            var pill = new VisualElement { tooltip = tooltip };
            pill.AddToClassList("forge-chip");
            pill.AddToClassList("forge-chip--setting");
            pill.Add(ForgeUI.Label(before, "forge-chip__text"));
            pill.Add(field);
            if (!string.IsNullOrEmpty(after))
                pill.Add(ForgeUI.Label(after, "forge-chip__text"));
            return pill;
        }
    }
}
