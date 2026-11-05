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
        /// <summary>Élément affiché dans l'éditeur, gardé d'une reconstruction à l'autre.</summary>
        static BehaviourItem selectedKind = BehaviourItem.Otherwise;
        static int selected;

        readonly SerializedObject serializedObject;
        readonly Func<EnemyData> enemy;
        readonly VisualElement ruleList;
        readonly VisualElement otherwiseSlot;
        readonly VisualElement attackList;
        readonly VisualElement phaseList;
        readonly EnemyBehaviourEditor editor;
        readonly BehaviourArena arena;
        readonly ForgeReorder ruleReorder;
        readonly ForgeReorder attackReorder;
        int activeRule = -1;
        float[] chances = Array.Empty<float>();

        const int MaxPhases = 9;

        static ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        SerializedProperty Patterns => serializedObject.FindProperty("patterns");
        SerializedProperty Attacks => serializedObject.FindProperty("attacks");
        SerializedProperty Phases => serializedObject.FindProperty("phases");

        /// <param name="reveal">Ouvre un élément dans son outil (l'attaque d'origine).</param>
        public EnemyBehaviourView(SerializedObject serializedObject, Func<EnemyData> enemy, Action<UnityEngine.Object> reveal)
        {
            this.serializedObject = serializedObject;
            this.enemy = enemy;
            AddToClassList("forge-behaviour");

            var lists = new VisualElement();
            lists.AddToClassList("forge-behaviour__rules");
            Add(lists);
            ruleList = new VisualElement();
            otherwiseSlot = new VisualElement();
            lists.Add(MovesSection(ruleList, otherwiseSlot));
            attackList = new VisualElement();
            lists.Add(AttacksSection(attackList));
            phaseList = new VisualElement();
            lists.Add(PhasesSection(phaseList));

            var main = new VisualElement();
            main.AddToClassList("forge-behaviour__main");
            var editorRoot = new VisualElement();
            editorRoot.AddToClassList("forge-section");
            editorRoot.AddToClassList("forge-rule-editor");
            main.Add(editorRoot);
            editor = new EnemyBehaviourEditor(editorRoot, serializedObject, enemy, Select, reveal);
            arena = new BehaviourArena(enemy, OnActiveChanged);
            main.Add(ArenaSection(arena));
            Add(main);

            ruleReorder = new ForgeReorder(ruleList, this, null, (from, to) => Move(Patterns, BehaviourItem.Rule, from, to));
            attackReorder = new ForgeReorder(attackList, this, null, (from, to) => Move(Attacks, BehaviourItem.Attack, from, to));

            // Toute modification (pastille, tuile, nom…) met à jour les résumés et l'arène.
            this.TrackPropertyValue(Patterns, _ => schedule.Execute(Refresh));
            this.TrackPropertyValue(Attacks, _ => schedule.Execute(Refresh));
            this.TrackPropertyValue(Phases, _ => schedule.Execute(Refresh));
            this.TrackPropertyValue(serializedObject.FindProperty("behaviour"), _ => schedule.Execute(Refresh));

            serializedObject.Update();
            ClampSelection();
            RebuildLists();
            editor.Show(selectedKind, selected);
            SyncArena();
            schedule.Execute(arena.Refresh);
        }

        /// <summary>Déplacement : règles réordonnables, puis « Sinon » en dernier, hors de la liste.</summary>
        VisualElement MovesSection(VisualElement rules, VisualElement otherwise)
        {
            var moves = new VisualElement();
            moves.AddToClassList("forge-section");
            moves.Add(ForgeUI.Label(ForgeText.WindowRich("Déplacement"), "forge-section-title"));
            moves.Add(ForgeUI.Label("Règles testées de haut en bas : la première dont les conditions sont remplies est suivie.", "forge-hint"));
            moves.Add(rules);
            moves.Add(otherwise);
            var addRule = ForgeUI.Button("+ Règle", "Ajoute une règle de déplacement, à compléter.", () => AddRule(RulePresets.Empty), "forge-button--primary");
            var rulePresets = ForgeUI.Button("Modèles ▾", "Règles toutes prêtes : rage, recul, tireur, traque, encerclement, curée.", null);
            rulePresets.clicked += () => ShowRulePresets(rulePresets);
            moves.Add(ForgeLists.Row(addRule, rulePresets));
            return moves;
        }

        VisualElement AttacksSection(VisualElement list)
        {
            var attacks = new VisualElement();
            attacks.AddToClassList("forge-section");
            attacks.Add(ForgeUI.Label(ForgeText.WindowRich("Attaques"), "forge-section-title"));
            attacks.Add(ForgeUI.Label("Parmi les attaques rechargées, à portée et dont les conditions sont remplies, l'ennemi en tire une au hasard selon leur poids. " +
                "Le pourcentage : sa chance dans la situation de l'arène.", "forge-hint"));
            attacks.Add(list);
            var addAttack = ForgeUI.Button("+ Attaque ▾", "Donne à cet ennemi une attaque de l'outil Attaques, ou en crée une depuis un modèle.", null, "forge-button--primary");
            addAttack.clicked += () => ShowAttackMenu(addAttack);
            attacks.Add(addAttack);
            addAttack.style.alignSelf = Align.FlexStart;
            return attacks;
        }

        VisualElement PhasesSection(VisualElement list)
        {
            var phases = new VisualElement();
            phases.AddToClassList("forge-section");
            phases.Add(ForgeUI.Label(ForgeText.WindowRich("Phases"), "forge-section-title"));
            phases.Add(ForgeUI.Label("On y entre par une étape d'attaque « Changer de phase ». Règles et attaques peuvent dépendre de la phase (condition « Phase de l'ennemi »).",
                "forge-hint"));
            phases.Add(list);
            var addPhase = ForgeUI.Button("+ Phase", "Ajoute une phase : stats, aura et renforts.", AddPhase, "forge-button--primary");
            addPhase.style.alignSelf = Align.FlexStart;
            phases.Add(addPhase);
            return phases;
        }

        static VisualElement ArenaSection(BehaviourArena arena)
        {
            var test = new VisualElement();
            test.AddToClassList("forge-section");
            test.Add(ForgeUI.Label(ForgeText.WindowRich("Arène de test"), "forge-section-title"));
            test.Add(ForgeUI.Label("Glissez le joueur et le mur : le déplacement et l'attaque choisis sont dessinés et s'allument dans les listes.", "forge-hint"));
            test.Add(arena);
            return test;
        }

        void ClampSelection()
        {
            var count = selectedKind switch
            {
                BehaviourItem.Rule => Patterns.arraySize,
                BehaviourItem.Attack => Attacks.arraySize,
                BehaviourItem.Phase => Phases.arraySize,
                _ => 1
            };
            if (selectedKind != BehaviourItem.Otherwise && selected >= count)
            {
                selectedKind = BehaviourItem.Otherwise;
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

        void Select(BehaviourItem kind, int index)
        {
            selectedKind = kind;
            selected = index;
            RebuildLists();
            editor.Show(selectedKind, selected);
            SyncArena();
            arena.Refresh();
        }

        void SyncArena()
        {
            arena.SelectedRule = selectedKind == BehaviourItem.Rule ? selected : -1;
            arena.SelectedAttack = selectedKind == BehaviourItem.Attack ? selected : -1;
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

        VisualElement Card(BehaviourItem kind, int index, string name, ForgeReorder reorder, string tooltip)
        {
            var card = new VisualElement { userData = index };
            card.AddToClassList("forge-rule-card");
            card.EnableInClassList("forge-rule-card--selected", selectedKind == kind && index == selected);
            card.EnableInClassList("forge-rule-card--active", kind == BehaviourItem.Rule && index == activeRule);
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
            var card = Card(BehaviourItem.Rule, index, Name(pattern.name, "Règle", index), ruleReorder,
                "Maintenez et glissez pour changer l'ordre : une règle plus haute passe avant les autres.");
            card.Add(ForgeUI.Label($"<color=#a597c0>Quand</color> {When(pattern.conditions)}", "forge-rule-card__line"));
            card.Add(ForgeUI.Label($"<color=#a597c0>→</color> {EnemyPatternText.Action(pattern)}", "forge-rule-card__line"));
            return card;
        }

        VisualElement AttackCard(EnemyAttack attack, int index)
        {
            var card = Card(BehaviourItem.Attack, index, attack.Name ?? "(attaque manquante)", attackReorder,
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
            card.EnableInClassList("forge-rule-card--selected", selectedKind == BehaviourItem.Phase && index == selected);
            card.RegisterCallback<ClickEvent>(_ => Select(BehaviourItem.Phase, index));
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
            var index = EnemyBehaviourEditing.AddPhase(serializedObject, MaxPhases);
            if (index < 0)
                ForgeLists.Notify($"Maximum atteint : {MaxPhases} phases.");
            else
                Select(BehaviourItem.Phase, index);
        }

        VisualElement OtherwiseCard(EnemyData data)
        {
            var card = new VisualElement { tooltip = "Ce que fait l'ennemi quand aucune règle de déplacement ne s'applique." };
            card.AddToClassList("forge-rule-card");
            card.AddToClassList("forge-rule-card--otherwise");
            card.EnableInClassList("forge-rule-card--selected", selectedKind == BehaviourItem.Otherwise);
            card.EnableInClassList("forge-rule-card--active", activeRule < 0);
            card.RegisterCallback<ClickEvent>(_ => Select(BehaviourItem.Otherwise, 0));
            var dot = new VisualElement();
            dot.AddToClassList("forge-rule-card__dot");
            card.Add(ForgeLists.Row(ForgeUI.Label("Sinon", "forge-rule-card__name"), dot));
            card.Add(ForgeUI.Label($"<color=#a597c0>→</color> {EnemyPatternText.Label(data.Behaviour).ToLowerInvariant()}", "forge-rule-card__line"));
            return card;
        }

        static string Name(string name, string fallback, int index) => string.IsNullOrWhiteSpace(name) ? $"{fallback} {index + 1}" : name;

        static string When(IReadOnlyCollection<EnemyCondition> conditions) =>
            conditions.Count == 0 ? "toujours" : string.Join(" et ", conditions.Select(c => EnemyPatternText.Describe(c, MechanicName)));

        internal static string MechanicName(string id) =>
            TheDungeon.Editor.Play.GameLauncher.ActiveProgression?.Mechanics.FirstOrDefault(m => m.id == id)?.displayName ?? id;

        void Move(SerializedProperty list, BehaviourItem kind, int from, int to)
        {
            serializedObject.Update();
            list.MoveArrayElement(from, to);
            serializedObject.ApplyModifiedProperties();
            // L'élément déplacé reste sélectionné.
            Select(selectedKind, selectedKind == kind && selected == from ? to : selected);
        }

        void AddRule(RulePresets.Preset preset)
        {
            var index = EnemyBehaviourEditing.AddRule(serializedObject, preset, Limits.maxEnemyPatterns);
            if (index < 0)
                ForgeLists.Notify($"Maximum atteint : {Limits.maxEnemyPatterns} règles.");
            else
                Select(BehaviourItem.Rule, index);
        }

        /// <summary>Donne une attaque existante à cet ennemi.</summary>
        void AddAttack(AttackData attackData)
        {
            var index = EnemyBehaviourEditing.AddAttack(serializedObject, attackData, Limits.maxEnemyAttacks);
            if (index < 0)
                ForgeLists.Notify($"Maximum atteint : {Limits.maxEnemyAttacks} attaques.");
            else
                Select(BehaviourItem.Attack, index);
        }

        /// <summary>Crée l'attaque d'un modèle (fichier ATK_), ou reprend celle qui porte déjà ce nom, puis la donne à cet ennemi.</summary>
        void CreateFromPreset(RulePresets.AttackPreset preset)
        {
            var attack = EnemyBehaviourEditing.AttackFromPreset(preset, out var reused);
            AddAttack(attack);
            ForgeLists.Notify(reused
                ? $"L'attaque « {preset.Name} » existe déjà : elle est réutilisée."
                : $"Attaque « {preset.Name} » créée (outil Attaques) et donnée à cet ennemi.");
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
            menu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Content);
        }

        void ShowRulePresets(VisualElement anchor)
        {
            var menu = ForgeDropdowns.Menu();
            foreach (var preset in RulePresets.All)
                menu.AddItem($"{preset.Name} — {preset.Description}", false, () => AddRule(preset));
            menu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Content);
        }

        #endregion
    }
}
