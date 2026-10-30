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
        NodeInspector treeInspector;
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
            yield return ForgeUI.Badge(FloorSet.Describe(asset.Floors), new Color(0.6f, 0.75f, 0.95f), "Étages où on le rencontre.");
            yield return ForgeUI.Badge($"{asset.LineCount} répliques", new Color(0.7f, 0.7f, 0.7f), "Répliques + réponses du joueur (compte du GDD).");
        }

        protected override VisualElement BuildFilters()
        {
            floorFilter = new DropdownField(new List<string> { AllFloors }.Concat(Enumerable.Range(1, Limits.floorCount).Select(f => $"Étage {f}")).ToList(), 0)
                { tooltip = "N'affiche que les personnages qu'on peut rencontrer à cet étage." };
            floorFilter.AddToClassList("forge-list-filter");
            floorFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            return floorFilter;
        }

        protected override bool PassesFilters(NpcData asset) =>
            floorFilter.value == AllFloors || Enumerable.Range(1, Limits.floorCount).Any(f => floorFilter.value == $"Étage {f}" && asset.AppearsOn(f));

        protected override void InitializeNew(SerializedObject serialized)
        {
            serialized.FindProperty("floor").intValue = 1;
            serialized.FindProperty("floors").intValue = FloorSet.Of(1);
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
            identity.Add(ForgeUI.Text("Nom", "displayName", "Nom affiché dans la boîte de dialogue. Le fichier sera nommé NPC_<Nom>."));
            identity.Add(ForgeUI.Text("Rôle", "role", "Rôle ou titre : forgeron, marchand, prisonnier…"));
            identity.Add(new SpriteField("Portrait", serializedObject.FindProperty("portrait"), "Npcs", "Portrait affiché à côté des répliques."));
            identity.Add(new FloorMaskField("Étages", serializedObject.FindProperty("floors"), Limits.floorCount,
                "Étages où l'on peut rencontrer ce personnage, un ou plusieurs. Il peut aussi être posé dans une salle précise (outil Salles).",
                fallback: asset.Floors));

            BuildAppearance(container, serializedObject);

            // Toute la discussion (compteurs, actions, conversation / arbre / aperçu) tient dans un bloc repliable.
            var discussion = ForgeUI.Section("Discussion", container);
            discussion.AddToClassList("forge-dialogue-discussion");
            stats = new VisualElement();
            stats.AddToClassList("forge-card-grid");
            discussion.Add(stats);
            var actions = new VisualElement();
            actions.AddToClassList("forge-toolbar");
            actions.AddToClassList("forge-dialogue-actions");
            actions.Add(ForgeUI.Button("Copier le script", "Copie la conversation au format texte (Markdown) pour la relire ou la faire relire hors d'Unity.", () =>
            {
                EditorGUIUtility.systemCopyBuffer = DialogueScript.Build(draft);
                ForgeLists.Notify("Script copié dans le presse-papiers.");
            }));
            actions.Add(ForgeUI.Button("Exporter la discussion", "Enregistre toute la discussion (personnage, apparence, répliques, conditions, effets) dans un fichier JSON.", ExportDiscussion));
            actions.Add(ForgeUI.Button("Importer une discussion", "Ouvre une discussion exportée : elle est préparée comme un nouveau personnage, à valider.", ImportDiscussion));
            discussion.Add(actions);

            var switcher = new VisualElement();
            switcher.AddToClassList("forge-view-switch");
            discussion.Add(switcher);
            var host = new VisualElement();
            discussion.Add(host);

            AddView(switcher, host, View.Conversation, "Conversation", "Écrire et modifier la conversation, comme une messagerie.", BuildConversation());
            AddView(switcher, host, View.Tree, "Arbre", "Tous les embranchements : zoomez, rangez les répliques et reliez-les à la souris.", BuildTree());
            AddView(switcher, host, View.Preview, "Aperçu en jeu", "Jouer la conversation comme en jeu, avec un joueur simulé.", preview = new DialoguePreview());
            ShowView(view);
            inspector.Show(selectedId);
            treeInspector.Show(selectedId);

            var entries = ForgeLists.Array(serializedObject, "entries", "Débuts alternatifs",
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
                });
            entries.AddToClassList("forge-dialogue-entries");
            container.Add(entries);
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

        VisualElement BuildTree()
        {
            var layout = new VisualElement();
            layout.AddToClassList("forge-dialogue-layout");
            graph = new DialogueGraphView(npc, draft, Edit, Select);
            graph.AddToClassList("forge-dialogue-layout__graph");
            layout.Add(graph);
            var side = new ScrollView();
            side.AddToClassList("forge-dialogue-layout__side");
            treeInspector = new NodeInspector(npc, draft, Edit, Select, KnownFlags);
            side.Add(treeInspector);
            layout.Add(side);
            return layout;
        }

        /// <summary>Couleurs de la boîte de dialogue, avec un aperçu qui suit chaque réglage.</summary>
        void BuildAppearance(VisualElement container, SerializedObject serializedObject)
        {
            var section = ForgeUI.Section("Apparence de la boîte de dialogue", container);
            section.Add(ForgeUI.Label("Couleurs utilisées en jeu et dans l'aperçu pour ce personnage.", "forge-hint"));
            var row = new VisualElement();
            row.AddToClassList("forge-dialogue-look");
            section.Add(row);

            var fields = new VisualElement();
            fields.AddToClassList("forge-dialogue-look__fields");
            row.Add(fields);
            void Group(string title, params (string label, string path, string tooltip)[] entries)
            {
                fields.Add(ForgeUI.Label(title, "forge-palette__title"));
                foreach (var (label, path, tooltip) in entries)
                    fields.Add(new ColorField(label) { showAlpha = false, bindingPath = path, tooltip = tooltip });
            }
            Group("Personnage",
                ("Plaque de nom", "color", "Plaque de nom du personnage, contour de son portrait dans la conversation."),
                ("Texte du nom", "style.nameText", "Texte de la plaque de nom."),
                ("Plaque du joueur", "style.playerPlate", "Plaque de nom quand c'est le joueur qui parle."));
            Group("Boîte de dialogue",
                ("Fond", "style.boxBackground", "Fond de la boîte de dialogue."),
                ("Contour", "style.boxBorder", "Contour de la boîte et du portrait."),
                ("Texte", "style.text", "Texte des répliques."));
            Group("Réponses du joueur",
                ("Fond", "style.choiceBackground", "Fond des réponses proposées au joueur."),
                ("Contour", "style.choiceBorder", "Contour des réponses proposées au joueur."),
                ("Texte", "style.choiceText", "Texte des réponses proposées au joueur."));
            var reset = ForgeUI.Button("Couleurs par défaut", "Remet les couleurs de la boîte de dialogue d'origine (la plaque de nom est conservée).", () =>
            {
                serializedObject.Update();
                var defaults = new DialogueStyle();
                foreach (var field in typeof(DialogueStyle).GetFields())
                    serializedObject.FindProperty($"style.{field.Name}").colorValue = (Color)field.GetValue(defaults);
                serializedObject.ApplyModifiedProperties();
            });
            reset.style.alignSelf = Align.FlexStart;
            reset.style.marginTop = 10;
            fields.Add(reset);

            var sample = new DialogueBoxSample(draft);
            row.Add(sample);
            sample.TrackSerializedObjectValue(serializedObject, _ => sample.Refresh());
        }

        void ExportDiscussion()
        {
            var path = EditorUtility.SaveFilePanel("Exporter la discussion", string.Empty, $"{draft.name}.{DialogueTransfer.Extension}", DialogueTransfer.Extension);
            if (string.IsNullOrEmpty(path))
                return;
            DialogueTransfer.Export(draft, path);
            ForgeLists.Notify($"Discussion exportée : {System.IO.Path.GetFileName(path)}.");
        }

        void ImportDiscussion()
        {
            var path = EditorUtility.OpenFilePanel("Importer une discussion", string.Empty, DialogueTransfer.Extension);
            if (string.IsNullOrEmpty(path))
                return;
            var imported = DialogueTransfer.Import(path);
            if (imported == null)
            {
                ForgeDialogs.Inform("Import impossible", "Ce fichier n'est pas une discussion exportée par Forge Studio.");
                return;
            }
            OpenDraft(() => DraftSession<NpcData>.CreateFrom(imported));
            UnityEngine.Object.DestroyImmediate(imported);
            ForgeLists.Notify("Discussion importée : vérifiez-la puis validez pour créer le personnage.");
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
            if (id == View.Tree)
                graph.schedule.Execute(graph.FrameAll);
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
            treeInspector.Show(id);
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
                graph.Show(selectedId);
            RefreshStats();
        }

        /// <summary>Après Ctrl+Z : la conversation, l'arbre et le détail de la réplique reprennent l'état annulé (la vue de l'arbre est gardée).</summary>
        protected override void AfterUndoRedo() => KeepingScroll(() =>
        {
            npc.Update();
            if (draft.FindNode(selectedId) == null)
                selectedId = draft.StartNode;
            inspector.Show(selectedId);
            treeInspector.Show(selectedId);
            RefreshViews();
            RefreshDetails();
        });

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
