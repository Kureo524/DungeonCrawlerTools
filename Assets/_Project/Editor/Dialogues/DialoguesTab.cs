using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// PNJ et arbres de dialogue (GDD : 25 PNJ, ~150 répliques, conditions et conséquences).
    /// Trois vues : conversation façon messagerie, arbre complet, aperçu jouable avec un joueur simulé.
    /// </summary>
    public sealed class DialoguesTab : AssetListTab<NpcData>
    {
        enum View
        {
            Conversation,
            Tree,
            Preview
        }

        const string AllFloors = "Tous les étages";

        View view = View.Conversation;
        int selectedId = -1;
        SerializedObject npc;
        NpcData draft;
        DropdownField floorFilter;
        VisualElement stats;
        ChatView chat;
        NodeInspector inspector;
        DialogueGraphView graph;
        DialoguePreview preview;
        readonly Dictionary<View, (Button button, VisualElement panel)> views = new();
        TextField composer;
        DialogueSpeaker composerSpeaker = DialogueSpeaker.Npc;
        readonly Dictionary<DialogueSpeaker, Button> speakerButtons = new();

        public DialoguesTab(IToolHost window) : base(window)
        {
        }

        protected override string NewAssetName => "Nouveau personnage";
        protected override string Noun => "personnage";

        protected override Sprite GetPreview(NpcData asset) => asset.Portrait;
        protected override Color GetColor(NpcData asset) => asset.Color;

        protected override IEnumerable<VisualElement> GetBadges(NpcData asset)
        {
            if (!string.IsNullOrWhiteSpace(asset.Role))
                yield return ForgeUI.Badge(asset.Role, asset.Color, "Rôle du personnage.");
            yield return ForgeUI.Badge($"Étage {asset.Floor}", new Color(0.6f, 0.75f, 0.95f), "Étage où on le rencontre.");
            yield return ForgeUI.Badge($"{asset.LineCount} répliques", new Color(0.7f, 0.7f, 0.7f), "Répliques + réponses du joueur (compte du GDD).");
        }

        protected override VisualElement BuildFilters()
        {
            floorFilter = new DropdownField(new List<string> { AllFloors }.Concat(Enumerable.Range(1, Limits.floorCount).Select(f => $"Étage {f}")).ToList(), 0)
                { tooltip = "N'affiche que les personnages de cet étage." };
            floorFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            return floorFilter;
        }

        protected override bool PassesFilters(NpcData asset) => floorFilter.value == AllFloors || floorFilter.value == $"Étage {asset.Floor}";

        protected override void InitializeNew(SerializedObject serialized)
        {
            serialized.FindProperty("floor").intValue = 1;
            DialogueEditing.AddNode(serialized, DialogueSpeaker.Npc, "Bonjour, voyageur.");
        }

        IEnumerable<string> KnownFlags()
        {
            var others = AssetFactory.LoadAll<NpcData>().Where(n => n != Original);
            return DialogueRulesEditor.FlagsUsedIn(others.Append(draft));
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, NpcData asset)
        {
            npc = serializedObject;
            draft = asset;
            selectedId = asset.StartNode;
            views.Clear();
            speakerButtons.Clear();

            var identity = ForgeUI.Section("Personnage", container);
            var identityRow = new VisualElement();
            identityRow.AddToClassList("forge-chart-row");
            var left = new VisualElement();
            left.AddToClassList("forge-chart-row__grow");
            left.Add(ForgeUI.Text("Nom", "displayName", "Nom affiché dans la boîte de dialogue. Le fichier sera nommé NPC_<Nom>."));
            left.Add(ForgeUI.Text("Rôle", "role", "Rôle ou titre : forgeron, marchand, prisonnier…"));
            left.Add(ForgeUI.Slider("Étage", "floor", new IntRange(1, Limits.floorCount), "Étage où l'on rencontre ce personnage."));
            left.Add(new ColorField("Couleur") { showAlpha = false, bindingPath = "color", tooltip = "Couleur des bulles et de la plaque de nom." });
            identityRow.Add(left);
            var right = new VisualElement();
            right.AddToClassList("forge-chart-row__grow");
            right.Add(new SpriteField("Portrait", serializedObject.FindProperty("portrait"), "Npcs", "Portrait affiché à côté des répliques."));
            identityRow.Add(right);
            identity.Add(identityRow);

            stats = new VisualElement();
            stats.AddToClassList("forge-card-grid");
            container.Add(stats);
            var export = ForgeUI.Button("Copier le script", "Copie la conversation au format texte (Markdown) pour la relire ou la faire relire hors d'Unity.", () =>
            {
                EditorGUIUtility.systemCopyBuffer = DialogueScript.Build(draft);
                ForgeLists.Notify("Script copié dans le presse-papiers.");
            });
            export.style.alignSelf = Align.FlexStart;
            container.Add(export);

            var switcher = new VisualElement();
            switcher.AddToClassList("forge-view-switch");
            container.Add(switcher);
            var host = new VisualElement();
            container.Add(host);

            AddView(switcher, host, View.Conversation, "Conversation", "Écrire et modifier la conversation, comme une messagerie.", BuildConversation());
            AddView(switcher, host, View.Tree, "Arbre", "Vue d'ensemble de tous les embranchements.", graph = new DialogueGraphView(id =>
            {
                ShowView(View.Conversation);
                Select(id);
            }));
            AddView(switcher, host, View.Preview, "Aperçu en jeu", "Jouer la conversation comme en jeu, avec un joueur simulé.", preview = new DialoguePreview());
            ShowView(view);
            inspector.Show(selectedId);

            container.Add(ForgeLists.Array(serializedObject, "entries", "Débuts alternatifs",
                "Testés dans l'ordre avant le début normal : par exemple, un autre accueil si la quête est déjà acceptée.", 4,
                (entry, index) =>
                {
                    var row = new VisualElement();
                    row.Add(EntryTarget(entry.FindPropertyRelative("node")));
                    row.Add(DialogueRulesEditor.Conditions(npc, $"entries.Array.data[{index}].conditions", "Si",
                        "Ce début est utilisé si toutes ces conditions sont remplies.", KnownFlags));
                    return row;
                },
                entry =>
                {
                    entry.FindPropertyRelative("node").intValue = draft.StartNode;
                    entry.FindPropertyRelative("conditions").arraySize = 0;
                    return true;
                }));
        }

        VisualElement BuildConversation()
        {
            var layout = new VisualElement();
            layout.AddToClassList("forge-dialogue-layout");

            var column = new VisualElement();
            column.AddToClassList("forge-dialogue-layout__chat");
            chat = new ChatView(Select);
            column.Add(chat);
            column.Add(BuildComposer());
            layout.Add(column);

            var side = new ScrollView();
            side.AddToClassList("forge-dialogue-layout__side");
            inspector = new NodeInspector(npc, draft, Edit, Select, KnownFlags);
            side.Add(inspector);
            layout.Add(side);
            return layout;
        }

        /// <summary>Barre de saisie : on écrit la conversation comme dans une messagerie, Entrée pour envoyer.</summary>
        VisualElement BuildComposer()
        {
            var bar = new VisualElement();
            bar.AddToClassList("forge-composer");

            var speakers = new VisualElement();
            speakers.AddToClassList("forge-composer__speakers");
            foreach (var (speaker, label, tooltip) in new[]
                     {
                         (DialogueSpeaker.Npc, "PNJ", "La réplique est dite par le personnage et suit la réplique sélectionnée."),
                         (DialogueSpeaker.Player, "Réponse du joueur", "Ajoute une réponse au choix du joueur sous la réplique sélectionnée."),
                         (DialogueSpeaker.Narrator, "Narration", "Texte de narration (sans personnage).")
                     })
            {
                var button = ForgeUI.Button(label, tooltip, () => SetComposerSpeaker(speaker), "forge-composer__speaker");
                speakerButtons[speaker] = button;
                speakers.Add(button);
            }
            bar.Add(speakers);

            var input = new VisualElement();
            input.AddToClassList("forge-row");
            composer = new TextField
            {
                tooltip = "Tapez la réplique puis Entrée (Maj+Entrée : retour à la ligne). Elle est ajoutée à la suite de la réplique sélectionnée.",
                multiline = true
            };
            composer.AddToClassList("forge-composer__input");
            composer.AddToClassList("forge-wrap");
            composer.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter) || evt.shiftKey)
                    return;
                Send();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            input.Add(composer);
            input.Add(ForgeUI.Button("Envoyer", "Ajoute la réplique (raccourci : Entrée).", Send, "forge-button--primary"));
            bar.Add(input);
            bar.Add(ForgeUI.Label("Entrée : envoyer · Changez qui parle avec les boutons ci-dessus · Cliquez une bulle pour écrire la suite à partir d'elle.", "forge-hint"));
            SetComposerSpeaker(composerSpeaker);
            return bar;
        }

        void SetComposerSpeaker(DialogueSpeaker speaker)
        {
            composerSpeaker = speaker;
            foreach (var (candidate, button) in speakerButtons)
                button.EnableInClassList("forge-composer__speaker--active", candidate == speaker);
        }

        void Send()
        {
            var text = composer.value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                ForgeLists.Notify("Écrivez d'abord le texte de la réplique.");
                return;
            }

            if (composerSpeaker == DialogueSpeaker.Player)
            {
                if (draft.FindNode(selectedId) == null)
                {
                    ForgeLists.Notify("Sélectionnez la réplique à laquelle le joueur répond.");
                    return;
                }
                var added = -1;
                Edit(() => added = DialogueEditing.AddChoice(npc, selectedId, text, DialogueEditing.End, Limits.maxChoicesPerNode));
                if (added < 0)
                {
                    ForgeLists.Notify($"Maximum atteint : {Limits.maxChoicesPerNode} réponses par réplique.");
                    return;
                }
                // Le PNJ répondra à cette réponse : on repasse en mode PNJ pour enchaîner naturellement.
                SetComposerSpeaker(DialogueSpeaker.Npc);
            }
            else
            {
                if (draft.Nodes.Count >= Limits.maxDialogueNodes)
                {
                    ForgeLists.Notify($"Maximum atteint : {Limits.maxDialogueNodes} répliques par personnage.");
                    return;
                }
                var newId = -1;
                var linked = false;
                var from = selectedId;
                Edit(() =>
                {
                    newId = DialogueEditing.AddNode(npc, composerSpeaker, text);
                    linked = DialogueEditing.LinkAfter(npc, from, newId) || npc.FindProperty("startNode").intValue == newId;
                });
                if (!linked)
                    ForgeLists.Notify("Réplique ajoutée mais reliée à rien : choisissez sa place (« Suite » ou une réponse).");
                Select(newId);
                chat.ScrollTo(newId);
            }
            composer.SetValueWithoutNotify(string.Empty);
            composer.schedule.Execute(() => composer.Focus());
        }

        VisualElement EntryTarget(SerializedProperty node)
        {
            var (labels, ids) = DialogueEditing.TargetChoices(draft, int.MinValue);
            labels.RemoveAt(0);
            ids.RemoveAt(0);
            if (labels.Count == 0)
                return ForgeUI.Label("Ajoutez d'abord des répliques.", "forge-hint");
            var field = new DropdownField("Commencer par", labels, Mathf.Max(0, ids.IndexOf(node.intValue)))
                { tooltip = "Première réplique de ce début alternatif." };
            var path = node.propertyPath;
            field.RegisterValueChangedCallback(evt =>
            {
                var index = labels.IndexOf(evt.newValue);
                if (index >= 0)
                    Edit(() => npc.FindProperty(path).intValue = ids[index]);
            });
            return field;
        }

        void AddView(VisualElement switcher, VisualElement host, View id, string label, string tooltip, VisualElement panel)
        {
            var button = ForgeUI.Button(label, tooltip, () => ShowView(id), "forge-view-switch__button");
            switcher.Add(button);
            host.Add(panel);
            views[id] = (button, panel);
        }

        void ShowView(View id)
        {
            view = id;
            foreach (var (candidate, (button, panel)) in views)
            {
                button.EnableInClassList("forge-view-switch__button--active", candidate == id);
                panel.style.display = candidate == id ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (id == View.Preview)
                preview.SetNpc(draft, Background());
            RefreshViews();
        }

        /// <summary>Sol d'un biome de l'étage du personnage, en fond de l'aperçu.</summary>
        Sprite Background()
        {
            var biome = AssetFactory.LoadAll<BiomeData>().FirstOrDefault(b => draft.Floor >= b.MinFloor && draft.Floor <= b.MaxFloor);
            return biome != null ? biome.FloorSprites.FirstOrDefault(s => s != null) : null;
        }

        void Select(int id)
        {
            selectedId = id;
            inspector.Show(id);
            RefreshViews();
        }

        void Edit(Action operation)
        {
            npc.Update();
            operation();
            npc.ApplyModifiedProperties();
            RefreshViews();
        }

        void RefreshViews()
        {
            if (draft == null)
                return;
            if (view == View.Conversation)
                chat.Show(draft, selectedId);
            else if (view == View.Tree)
                graph.Show(draft, selectedId);
            RefreshStats();
        }

        protected override void OnRefresh(NpcData asset)
        {
            if (inspector != null && asset.FindNode(selectedId) == null)
                Select(asset.StartNode);
            else
                RefreshViews();
        }

        void RefreshStats()
        {
            stats.Clear();
            var nodes = draft.Nodes;
            var reachable = TheDungeon.Dialogue.DialogueLogic.ReachableNodes(draft);
            var ends = nodes.Count(n => n.choices.Count == 0 && n.next < 0);
            var conditional = nodes.Sum(n => n.choices.Count(c => c.conditions.Count > 0));
            Stat("Répliques", draft.LineCount.ToString(), $"{nodes.Count} réplique(s) + {draft.LineCount - nodes.Count} réponse(s) du joueur. Objectif du GDD : ~{Limits.targetDialogueLines / Mathf.Max(1, Limits.targetNpcs)} par PNJ.", null);
            Stat("Fins", ends.ToString(), "Répliques qui terminent la conversation.", ends == 0 && nodes.Count > 0 ? "forge-stat--warning" : null);
            Stat("Réponses conditionnelles", conditional.ToString(), "Réponses soumises à une condition (niveau, objet, marqueur…).", null);
            var orphans = nodes.Count - reachable.Count;
            Stat("Inaccessibles", orphans.ToString(), "Répliques que rien ne permet d'atteindre depuis un début.", orphans > 0 ? "forge-stat--warning" : "forge-stat--good");
        }

        void Stat(string title, string value, string tooltip, string className)
        {
            var tile = new VisualElement { tooltip = tooltip };
            tile.AddToClassList("forge-card");
            tile.AddToClassList("forge-stat");
            tile.AddToClassList("forge-stat--compact");
            if (className != null)
                tile.AddToClassList(className);
            tile.Add(ForgeUI.Label(title, "forge-stat__title"));
            tile.Add(ForgeUI.Label(value, "forge-stat__value"));
            stats.Add(tile);
        }
    }
}
