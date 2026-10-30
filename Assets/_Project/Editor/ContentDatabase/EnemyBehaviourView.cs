using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Dialogues;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Vue « Comportement » d'un ennemi, en deux listes :
    /// <list type="bullet">
    /// <item>Déplacement : règles « Quand [conditions] → se déplace ainsi », testées de haut en bas, puis « Sinon » (comportement de base).</item>
    /// <item>Attaques : chacune avec son type, sa portée, ses conditions et son rythme (préparation, récupération, recharge).</item>
    /// </list>
    /// À droite, l'élément choisi et l'arène pour tester. Les listes se réordonnent par glisser-déposer.
    /// </summary>
    public sealed class EnemyBehaviourView : VisualElement
    {
        enum Kind { Rule, Otherwise, Attack, Phase }

        /// <summary>Élément affiché dans l'éditeur, gardé d'une reconstruction à l'autre.</summary>
        static Kind selectedKind = Kind.Otherwise;
        static int selected;

        readonly SerializedObject serializedObject;
        readonly Func<EnemyData> enemy;
        readonly VisualElement ruleList;
        readonly VisualElement otherwiseSlot;
        readonly VisualElement attackList;
        readonly VisualElement phaseList;
        readonly VisualElement editor;
        readonly BehaviourArena arena;
        readonly ForgeReorder ruleReorder;
        readonly ForgeReorder attackReorder;
        int activeRule = -1;
        float[] chances = Array.Empty<float>();

        static ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        SerializedProperty Patterns => serializedObject.FindProperty("patterns");
        SerializedProperty Attacks => serializedObject.FindProperty("attacks");
        SerializedProperty Phases => serializedObject.FindProperty("phases");

        readonly Action<UnityEngine.Object> reveal;

        /// <param name="reveal">Ouvre un élément dans son outil (l'attaque d'origine).</param>
        public EnemyBehaviourView(SerializedObject serializedObject, Func<EnemyData> enemy, Action<UnityEngine.Object> reveal)
        {
            this.serializedObject = serializedObject;
            this.enemy = enemy;
            this.reveal = reveal;
            AddToClassList("forge-behaviour");

            var lists = new VisualElement();
            lists.AddToClassList("forge-behaviour__rules");
            Add(lists);

            var moves = new VisualElement();
            moves.AddToClassList("forge-section");
            moves.Add(ForgeUI.Label(ForgeText.WindowRich("Déplacement"), "forge-section-title"));
            moves.Add(ForgeUI.Label("Règles testées de haut en bas : la première dont les conditions sont remplies est suivie.", "forge-hint"));
            ruleList = new VisualElement();
            moves.Add(ruleList);
            // « Sinon » reste en dernier, hors de la liste réordonnable.
            otherwiseSlot = new VisualElement();
            moves.Add(otherwiseSlot);
            var addRule = ForgeUI.Button("+ Règle", "Ajoute une règle de déplacement, à compléter.", () => AddRule(RulePresets.Empty), "forge-button--primary");
            var rulePresets = ForgeUI.Button("Modèles ▾", "Règles toutes prêtes : rage, recul, tireur, traque, encerclement, curée.", null);
            rulePresets.clicked += () => ShowRulePresets(rulePresets);
            moves.Add(ForgeLists.Row(addRule, rulePresets));
            lists.Add(moves);

            var attacks = new VisualElement();
            attacks.AddToClassList("forge-section");
            attacks.Add(ForgeUI.Label(ForgeText.WindowRich("Attaques"), "forge-section-title"));
            attacks.Add(ForgeUI.Label("Parmi les attaques rechargées, à portée et dont les conditions sont remplies, l'ennemi en tire une au hasard selon leur poids. " +
                "Le pourcentage : sa chance dans la situation de l'arène.", "forge-hint"));
            attackList = new VisualElement();
            attacks.Add(attackList);
            var addAttack = ForgeUI.Button("+ Attaque ▾", "Donne à cet ennemi une attaque de l'outil Attaques, ou en crée une depuis un modèle.", null, "forge-button--primary");
            addAttack.clicked += () => ShowAttackMenu(addAttack);
            attacks.Add(addAttack);
            addAttack.style.alignSelf = Align.FlexStart;
            lists.Add(attacks);

            var phases = new VisualElement();
            phases.AddToClassList("forge-section");
            phases.Add(ForgeUI.Label(ForgeText.WindowRich("Phases"), "forge-section-title"));
            phases.Add(ForgeUI.Label("On y entre par une étape d'attaque « Changer de phase ». Règles et attaques peuvent dépendre de la phase (condition « Phase de l'ennemi »).",
                "forge-hint"));
            phaseList = new VisualElement();
            phases.Add(phaseList);
            var addPhase = ForgeUI.Button("+ Phase", "Ajoute une phase : stats, aura et renforts.", AddPhase, "forge-button--primary");
            addPhase.style.alignSelf = Align.FlexStart;
            phases.Add(addPhase);
            lists.Add(phases);

            var main = new VisualElement();
            main.AddToClassList("forge-behaviour__main");
            editor = new VisualElement();
            editor.AddToClassList("forge-section");
            editor.AddToClassList("forge-rule-editor");
            main.Add(editor);
            var test = new VisualElement();
            test.AddToClassList("forge-section");
            test.Add(ForgeUI.Label(ForgeText.WindowRich("Arène de test"), "forge-section-title"));
            test.Add(ForgeUI.Label("Glissez le joueur et le mur : le déplacement et l'attaque choisis sont dessinés et s'allument dans les listes.", "forge-hint"));
            arena = new BehaviourArena(enemy, OnActiveChanged);
            test.Add(arena);
            main.Add(test);
            Add(main);

            ruleReorder = new ForgeReorder(ruleList, this, null, (from, to) => Move(Patterns, Kind.Rule, from, to));
            attackReorder = new ForgeReorder(attackList, this, null, (from, to) => Move(Attacks, Kind.Attack, from, to));

            // Toute modification (pastille, tuile, nom…) met à jour les résumés et l'arène.
            this.TrackPropertyValue(Patterns, _ => schedule.Execute(Refresh));
            this.TrackPropertyValue(Attacks, _ => schedule.Execute(Refresh));
            this.TrackPropertyValue(Phases, _ => schedule.Execute(Refresh));
            this.TrackPropertyValue(serializedObject.FindProperty("behaviour"), _ => schedule.Execute(Refresh));

            serializedObject.Update();
            ClampSelection();
            RebuildLists();
            RebuildEditor();
            SyncArena();
            schedule.Execute(arena.Refresh);
        }

        void ClampSelection()
        {
            var count = selectedKind switch
            {
                Kind.Rule => Patterns.arraySize,
                Kind.Attack => Attacks.arraySize,
                Kind.Phase => Phases.arraySize,
                _ => 1
            };
            if (selectedKind != Kind.Otherwise && selected >= count)
            {
                selectedKind = Kind.Otherwise;
                selected = 0;
            }
        }

        /// <summary>Après une modification : résumés et arène (l'éditeur n'est pas reconstruit, la saisie continue).</summary>
        public void Refresh()
        {
            if (ruleReorder.IsDragging || attackReorder.IsDragging)
                return;
            serializedObject.Update();
            ClampSelection();
            RebuildLists();
            arena.Refresh();
        }

        void OnActiveChanged(int rule, float[] attackChances)
        {
            activeRule = rule;
            chances = attackChances;
            foreach (var card in ruleList.Children())
                card.EnableInClassList("forge-rule-card--active", card.userData is int i && i == activeRule);
            foreach (var card in otherwiseSlot.Children())
                card.EnableInClassList("forge-rule-card--active", activeRule < 0);
            foreach (var card in attackList.Children())
                if (card.userData is int i)
                    ShowChance(card, i);
        }

        /// <summary>Pastille et pourcentage d'une attaque : sa chance d'être lancée dans la situation de l'arène.</summary>
        void ShowChance(VisualElement card, int index)
        {
            var chance = index < chances.Length ? chances[index] : 0f;
            card.EnableInClassList("forge-rule-card--active", chance > 0f);
            var label = card.Q<Label>(className: "forge-rule-card__chance");
            if (label != null)
                label.text = chance > 0f ? $"{chance:P0}" : "—";
        }

        void Select(Kind kind, int index)
        {
            selectedKind = kind;
            selected = index;
            RebuildLists();
            RebuildEditor();
            SyncArena();
            arena.Refresh();
        }

        void SyncArena()
        {
            arena.SelectedRule = selectedKind == Kind.Rule ? selected : -1;
            arena.SelectedAttack = selectedKind == Kind.Attack ? selected : -1;
        }

        #region Listes

        void RebuildLists()
        {
            var data = enemy();
            ruleList.Clear();
            for (var i = 0; i < data.Patterns.Count; i++)
                ruleList.Add(RuleCard(data.Patterns[i], i));
            otherwiseSlot.Clear();
            otherwiseSlot.Add(OtherwiseCard(data));
            attackList.Clear();
            for (var i = 0; i < data.Attacks.Count; i++)
                attackList.Add(AttackCard(data.Attacks[i], i));
            if (data.Attacks.Count == 0)
                attackList.Add(ForgeUI.Label($"Aucune attaque : l'ennemi attaque selon son comportement de base ({EnemyPatternText.Label(data.Behaviour).ToLowerInvariant()}).", "forge-hint"));
            phaseList.Clear();
            for (var i = 0; i < data.Phases.Count; i++)
                phaseList.Add(PhaseCard(data, i));
        }

        VisualElement Card(Kind kind, int index, string name, ForgeReorder reorder, string tooltip)
        {
            var card = new VisualElement { userData = index };
            card.AddToClassList("forge-rule-card");
            card.EnableInClassList("forge-rule-card--selected", selectedKind == kind && index == selected);
            card.EnableInClassList("forge-rule-card--active", kind == Kind.Rule && index == activeRule);
            card.RegisterCallback<ClickEvent>(_ => Select(kind, index));
            var handle = reorder.Handle(card, () => ForgeUI.Label($"{index + 1}  {name}", "forge-reorder-ghost__name"), tooltip);
            var number = ForgeUI.Label((index + 1).ToString(), "forge-rule-card__number");
            var title = ForgeUI.Label(name, "forge-rule-card__name");
            var dot = new VisualElement { tooltip = "S'allume quand l'arène de test l'utilise." };
            dot.AddToClassList("forge-rule-card__dot");
            card.Add(ForgeLists.Row(handle, number, title, dot));
            return card;
        }

        VisualElement RuleCard(EnemyPattern pattern, int index)
        {
            var card = Card(Kind.Rule, index, Name(pattern.name, "Règle", index), ruleReorder,
                "Maintenez et glissez pour changer l'ordre : une règle plus haute passe avant les autres.");
            card.Add(ForgeUI.Label($"<color=#a597c0>Quand</color> {When(pattern.conditions)}", "forge-rule-card__line"));
            card.Add(ForgeUI.Label($"<color=#a597c0>→</color> {EnemyPatternText.Action(pattern)}", "forge-rule-card__line"));
            return card;
        }

        VisualElement AttackCard(EnemyAttack attack, int index)
        {
            var card = Card(Kind.Attack, index, attack.Name ?? "(attaque manquante)", attackReorder,
                "Maintenez et glissez pour ranger les attaques (l'ordre ne compte qu'en cas de poids égaux dans l'aperçu).");
            // Chance d'être lancée dans la situation de l'arène, à côté de la pastille.
            var chance = ForgeUI.Label("—", "forge-rule-card__chance");
            chance.tooltip = "Chance d'être lancée dans la situation de l'arène de test (poids des attaques possibles à ce moment).";
            var dot = card.Q(className: "forge-rule-card__dot");
            dot.parent.Insert(dot.parent.IndexOf(dot), chance);
            ShowChance(card, index);
            var stats = attack.Resolve();
            card.Add(ForgeUI.Label(stats == null ? "<color=#e0606a>Attaque supprimée : choisissez-en une autre.</color>"
                : $"<color=#a597c0>⚔</color> {EnemyPatternText.Summary(stats)} · poids {attack.weight}{(attack.overridden != AttackField.None ? "  <color=#a597c0>· ajustée</color>" : string.Empty)}",
                "forge-rule-card__line"));
            if (attack.conditions.Count > 0)
                card.Add(ForgeUI.Label($"<color=#a597c0>Si</color> {When(attack.conditions)}", "forge-rule-card__line"));
            return card;
        }

        VisualElement PhaseCard(EnemyData data, int index)
        {
            var phase = data.Phases[index];
            var card = new VisualElement();
            card.AddToClassList("forge-rule-card");
            card.EnableInClassList("forge-rule-card--selected", selectedKind == Kind.Phase && index == selected);
            card.RegisterCallback<ClickEvent>(_ => Select(Kind.Phase, index));
            card.Add(ForgeLists.Row(ForgeUI.Label((index + 1).ToString(), "forge-rule-card__number"),
                ForgeUI.Label(string.IsNullOrWhiteSpace(phase.name) ? $"Phase {index + 1}" : phase.name, "forge-rule-card__name")));
            var parts = new List<string>();
            if (phase.damagePercent != 100) parts.Add($"dégâts {phase.damagePercent} %");
            if (phase.speedPercent != 100) parts.Add($"vitesse {phase.speedPercent} %");
            if (phase.damageTakenPercent != 100) parts.Add($"dégâts reçus {phase.damageTakenPercent} %");
            if (phase.auraRadius > 0f) parts.Add($"aura {EnemyPatternText.Label(phase.auraStatus).ToLowerInvariant()} {phase.auraRadius:0.#} cases");
            if (phase.spawn != null) parts.Add($"renforts {ForgeText.Name(phase.spawn.DisplayName)}");
            var entered = data.Attacks.Where(a => a.attack != null && a.attack.Steps.Any(st => st.action == StepAction.Phase && st.phase == index + 1)).Select(a => a.Name).ToList();
            card.Add(ForgeUI.Label(parts.Count == 0 ? "aucun changement" : string.Join(" · ", parts), "forge-rule-card__line"));
            card.Add(ForgeUI.Label(entered.Count == 0
                ? "<color=#e7b52f>Aucune attaque n'y fait entrer</color>"
                : $"<color=#a597c0>Entrée par</color> {string.Join(", ", entered.Select(ForgeText.Name))}", "forge-rule-card__line"));
            return card;
        }

        void AddPhase()
        {
            serializedObject.Update();
            var phases = Phases;
            if (phases.arraySize >= 9)
            {
                ForgeLists.Notify("Maximum atteint : 9 phases.");
                return;
            }
            phases.arraySize++;
            var phase = phases.GetArrayElementAtIndex(phases.arraySize - 1);
            phase.FindPropertyRelative("name").stringValue = $"Phase {phases.arraySize}";
            phase.FindPropertyRelative("damagePercent").intValue = 120;
            phase.FindPropertyRelative("speedPercent").intValue = 110;
            phase.FindPropertyRelative("damageTakenPercent").intValue = 100;
            phase.FindPropertyRelative("auraRadius").floatValue = 0f;
            phase.FindPropertyRelative("auraStatus").enumValueIndex = (int)StatusEffect.Rot;
            phase.FindPropertyRelative("auraPower").floatValue = 3f;
            phase.FindPropertyRelative("spawn").objectReferenceValue = null;
            phase.FindPropertyRelative("spawnInterval").floatValue = 6f;
            phase.FindPropertyRelative("spawnMax").intValue = 3;
            serializedObject.ApplyModifiedProperties();
            Select(Kind.Phase, phases.arraySize - 1);
        }

        VisualElement OtherwiseCard(EnemyData data)
        {
            var card = new VisualElement { tooltip = "Ce que fait l'ennemi quand aucune règle de déplacement ne s'applique." };
            card.AddToClassList("forge-rule-card");
            card.AddToClassList("forge-rule-card--otherwise");
            card.EnableInClassList("forge-rule-card--selected", selectedKind == Kind.Otherwise);
            card.EnableInClassList("forge-rule-card--active", activeRule < 0);
            card.RegisterCallback<ClickEvent>(_ => Select(Kind.Otherwise, 0));
            var dot = new VisualElement();
            dot.AddToClassList("forge-rule-card__dot");
            card.Add(ForgeLists.Row(ForgeUI.Label("Sinon", "forge-rule-card__name"), dot));
            card.Add(ForgeUI.Label($"<color=#a597c0>→</color> {EnemyPatternText.Label(data.Behaviour).ToLowerInvariant()}", "forge-rule-card__line"));
            return card;
        }

        static string Name(string name, string fallback, int index) => string.IsNullOrWhiteSpace(name) ? $"{fallback} {index + 1}" : name;

        static string When(IReadOnlyCollection<EnemyCondition> conditions) =>
            conditions.Count == 0 ? "toujours" : string.Join(" et ", conditions.Select(c => EnemyPatternText.Describe(c, MechanicName)));

        static string MechanicName(string id) =>
            TheDungeon.Editor.Play.GameLauncher.ActiveProgression?.Mechanics.FirstOrDefault(m => m.id == id)?.displayName ?? id;

        void Move(SerializedProperty list, Kind kind, int from, int to)
        {
            serializedObject.Update();
            list.MoveArrayElement(from, to);
            serializedObject.ApplyModifiedProperties();
            // L'élément déplacé reste sélectionné.
            Select(selectedKind, selectedKind == kind && selected == from ? to : selected);
        }

        void AddRule(RulePresets.Preset preset)
        {
            serializedObject.Update();
            var patterns = Patterns;
            if (patterns.arraySize >= Limits.maxEnemyPatterns)
            {
                ForgeLists.Notify($"Maximum atteint : {Limits.maxEnemyPatterns} règles.");
                return;
            }
            patterns.arraySize++;
            RulePresets.Apply(preset, patterns.GetArrayElementAtIndex(patterns.arraySize - 1));
            serializedObject.ApplyModifiedProperties();
            Select(Kind.Rule, patterns.arraySize - 1);
        }

        /// <summary>Donne une attaque existante à cet ennemi.</summary>
        void AddAttack(AttackData attackData)
        {
            serializedObject.Update();
            var attacks = Attacks;
            if (attacks.arraySize >= Limits.maxEnemyAttacks)
            {
                ForgeLists.Notify($"Maximum atteint : {Limits.maxEnemyAttacks} attaques.");
                return;
            }
            attacks.arraySize++;
            var slot = attacks.GetArrayElementAtIndex(attacks.arraySize - 1);
            slot.FindPropertyRelative("attack").objectReferenceValue = attackData;
            slot.FindPropertyRelative("conditions").arraySize = 0;
            slot.FindPropertyRelative("overridden").intValue = 0;
            slot.FindPropertyRelative("weight").intValue = 100;
            serializedObject.ApplyModifiedProperties();
            Select(Kind.Attack, attacks.arraySize - 1);
        }

        /// <summary>Crée l'attaque d'un modèle (fichier ATK_), puis la donne à cet ennemi.</summary>
        void CreateFromPreset(RulePresets.AttackPreset preset)
        {
            var existing = AssetFactory.LoadAll<AttackData>().FirstOrDefault(a => a.DisplayName == preset.Name);
            if (existing != null)
            {
                AddAttack(existing);
                ForgeLists.Notify($"L'attaque « {preset.Name} » existe déjà : elle est réutilisée.");
                return;
            }
            var created = ScriptableObject.CreateInstance<AttackData>();
            var serialized = new SerializedObject(created);
            RulePresets.Apply(preset, serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            created.name = NamingConvention.GetExpectedName(created);
            var saved = AssetFactory.SaveNew(created);
            AddAttack(saved);
            ForgeLists.Notify($"Attaque « {preset.Name} » créée (outil Attaques) et donnée à cet ennemi.");
        }

        void ShowAttackMenu(VisualElement anchor)
        {
            var menu = ForgeDropdowns.Menu();
            var existing = AssetFactory.LoadAll<AttackData>().Where(a => a.User == AttackUser.Enemy).OrderBy(a => a.DisplayName).ToList();
            menu.AddDisabledItem("Attaques d'ennemi existantes", false);
            if (existing.Count == 0)
                menu.AddDisabledItem("   (aucune pour l'instant)", false);
            foreach (var attack in existing)
            {
                var chosen = attack;
                menu.AddItem($"   {ForgeText.NameOrRedacted(attack.DisplayName)} — {EnemyPatternText.Summary(new ResolvedAttack { Settings = attack.Settings, Steps = attack.Steps })}",
                    false, () => AddAttack(chosen));
            }
            menu.AddSeparator(string.Empty);
            menu.AddDisabledItem("Créer depuis un modèle", false);
            foreach (var preset in RulePresets.Attacks.Where(p => p.User == AttackUser.Enemy))
            {
                var chosen = preset;
                menu.AddItem($"   {preset.Name} — {preset.Description}", false, () => CreateFromPreset(chosen));
            }
            menu.DropDown(anchor.worldBound, anchor, false);
        }

        void ShowRulePresets(VisualElement anchor)
        {
            var menu = ForgeDropdowns.Menu();
            foreach (var preset in RulePresets.All)
                menu.AddItem($"{preset.Name} — {preset.Description}", false, () => AddRule(preset));
            menu.DropDown(anchor.worldBound, anchor, false);
        }

        #endregion

        #region Éditeurs

        void RebuildEditor()
        {
            editor.Clear();
            serializedObject.Update();
            switch (selectedKind)
            {
                case Kind.Rule:
                    BuildRuleEditor(Patterns.GetArrayElementAtIndex(selected).propertyPath);
                    break;
                case Kind.Attack:
                    BuildAttackEditor(Attacks.GetArrayElementAtIndex(selected).propertyPath);
                    break;
                case Kind.Phase:
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
                Select(remaining == 0 ? Kind.Otherwise : selectedKind, Mathf.Min(selected, remaining - 1));
            }, "forge-button--danger");
            editor.Add(ForgeLists.Row(ForgeUI.Label(label, "forge-rule-card__number"), name, delete));
        }

        void Conditions(string path, string heading, string emptyText)
        {
            editor.Add(ForgeUI.Label(heading, "forge-rule-editor__heading"));
            editor.Add(new ConditionChips(serializedObject, RebuildEditor).Build(path, emptyText));
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

            // En-tête : l'attaque utilisée (modifiable), l'ouvrir dans l'outil Attaques, la retirer de cet ennemi.
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
                RebuildEditor();
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
                Select(remaining == 0 ? Kind.Otherwise : Kind.Attack, Mathf.Min(index, remaining - 1));
            }, "forge-button--danger");
            editor.Add(ForgeLists.Row(ForgeUI.Label($"Attaque {index + 1}", "forge-rule-card__number"), picker, open, remove));
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
                new AttackEditor.Override { Mask = slot.FindPropertyRelative("overridden"), Base = attackData.Settings }, RebuildEditor));

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

            // Enchaînement en lecture : on le modifie dans l'outil Attaques (il est partagé).
            editor.Add(ForgeUI.Label("Enchaînement", "forge-rule-editor__heading"));
            editor.Add(new AttackTimeline(() => enemy().Attacks.ElementAtOrDefault(index)?.Resolve()));
            for (var i = 0; i < attackData.Steps.Count; i++)
            {
                var step = attackData.Steps[i];
                var when = EnemyPatternText.When(step, i == 0, MechanicName);
                var line = $"<b>{i + 1}. {ForgeText.NameOrRedacted(step.name)}</b>  {EnemyPatternText.Summary(step)}";
                if (when != null)
                    line += $"\n<color=#a597c0>   si</color> {when}{(step.fallback == StepFallback.EndAttack ? " <color=#8a8a9c>(sinon l'attaque s'arrête)</color>" : " <color=#8a8a9c>(sinon sautée)</color>")}";
                editor.Add(ForgeUI.Label(line, "forge-step-line"));
            }
            editor.Add(ForgeUI.Label("Pour changer les étapes, ouvrez l'attaque : la modification vaut pour tous les ennemis qui l'utilisent.", "forge-hint"));

            Conditions(path, "Conditions pour cet ennemi", "aucune (seulement la portée et la recharge)");
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
                Select(Phases.arraySize == 0 ? Kind.Otherwise : Kind.Phase, Mathf.Min(index, Phases.arraySize - 1));
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
            var pill = new VisualElement { tooltip = tooltip };
            pill.AddToClassList("forge-chip");
            pill.AddToClassList("forge-chip--setting");
            pill.Add(ForgeUI.Label(before, "forge-chip__text"));
            pill.Add(field);
            if (!string.IsNullOrEmpty(after))
                pill.Add(ForgeUI.Label(after, "forge-chip__text"));
            return pill;
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
                        RebuildEditor();
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
            RebuildEditor();
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

        static VisualElement FloatPill(string before, string after, SerializedProperty property, FloatRange range, string tooltip)
        {
            var field = new FloatField { tooltip = $"{tooltip}\nValeurs autorisées : {range}." };
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

        #endregion
    }
}
