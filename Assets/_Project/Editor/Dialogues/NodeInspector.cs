using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>Panneau de détail de la réplique sélectionnée.</summary>
    public sealed class NodeInspector : VisualElement
    {
        readonly SerializedObject npc;
        readonly NpcData draft;
        readonly Action<Action> edit;
        readonly Action<int> select;
        readonly Func<IEnumerable<string>> knownFlags;

        public NodeInspector(SerializedObject npc, NpcData draft, Action<Action> edit, Action<int> select, Func<IEnumerable<string>> knownFlags)
        {
            this.npc = npc;
            this.draft = draft;
            this.edit = edit;
            this.select = select;
            this.knownFlags = knownFlags;
            AddToClassList("forge-dialogue-inspector");
            ForgeLabels.Track(this);
        }

        public void Show(int nodeId)
        {
            Clear();
            npc.Update();
            var index = DialogueEditing.IndexOf(npc, nodeId);
            if (index < 0)
            {
                Add(ForgeUI.EmptyState("Sélectionnez une bulle pour la modifier,\nou écrivez une première réplique dans la barre du bas."));
                return;
            }
            var limits = ForgeDesignLimits.Instance;
            var path = $"nodes.Array.data[{index}]";
            var node = npc.FindProperty(path);

            var header = new VisualElement();
            header.AddToClassList("forge-row");
            var title = ForgeUI.Label(ForgeText.WindowRich($"Réplique #{nodeId}"), "forge-section-title");
            title.style.flexGrow = 1;
            header.Add(title);
            if (draft.StartNode == nodeId)
                header.Add(ForgeUI.Badge("DÉBUT", new Color(0.4f, 0.8f, 0.45f), "La conversation commence par cette réplique."));
            Add(header);

            var actions = new VisualElement();
            actions.AddToClassList("forge-toolbar");
            var startButton = ForgeUI.Button("Définir comme début", "La conversation commencera par cette réplique.",
                () => edit(() => DialogueEditing.SetStart(npc, nodeId)));
            startButton.SetEnabled(draft.StartNode != nodeId);
            actions.Add(startButton);
            actions.Add(ForgeUI.Button("Supprimer", "Supprime la réplique ; ce qui y menait mène à la fin de la conversation.", () =>
            {
                edit(() => DialogueEditing.DeleteNode(npc, nodeId));
                select(draft.StartNode);
            }, "forge-button--danger"));
            Add(actions);

            Add(SpeakerField(node));

            var text = new TextField("Texte") { multiline = true, tooltip = "Ce que dit le personnage. Survolez la bulle pour relire le texte complet." };
            text.AddToClassList("forge-multiline");
            text.AddToClassList("forge-dialogue-inspector__text");
            text.BindProperty(node.FindPropertyRelative("text"));
            var counter = ForgeUI.Label(string.Empty, "forge-dialogue-inspector__counter");
            void Count(string value)
            {
                var length = value?.Length ?? 0;
                counter.text = $"{length} / {limits.maxLineLength} caractères";
                counter.EnableInClassList("forge-dialogue-inspector__counter--over", length > limits.maxLineLength);
            }
            text.RegisterValueChangedCallback(evt => Count(evt.newValue));
            Count(node.FindPropertyRelative("text").stringValue);
            Add(text);
            Add(counter);

            // La suite directe n'a de sens que sans réponse du joueur : sinon, chaque réponse mène à sa propre suite.
            var choices = node.FindPropertyRelative("choices");
            if (choices.arraySize == 0)
                Add(TargetField("Suite", node.FindPropertyRelative("next"), nodeId, "Réplique qui suit quand il n'y a pas de réponse à choisir."));

            var replies = ForgeLists.Array(npc, $"{path}.choices", "Réponses du joueur",
                "Réponses proposées après cette réplique. Sans réponse, la conversation passe à la « Suite ».", limits.maxChoicesPerNode,
                (choice, choiceIndex) => ChoiceRow(choice, $"{path}.choices.Array.data[{choiceIndex}]", nodeId),
                choice =>
                {
                    choice.FindPropertyRelative("text").stringValue = "Nouvelle réponse";
                    choice.FindPropertyRelative("target").intValue = DialogueEditing.End;
                    choice.FindPropertyRelative("conditions").arraySize = 0;
                    choice.FindPropertyRelative("consequences").arraySize = 0;
                    DialogueEditing.ResetChoiceLook(choice);
                    node.FindPropertyRelative("next").intValue = DialogueEditing.End;
                    return true;
                });
            replies.AddToClassList("forge-dialogue-inspector__replies");
            // Ajouter la première réponse (ou retirer la dernière) fait apparaître ou disparaître la « Suite ».
            var count = choices.arraySize;
            replies.TrackPropertyValue(node.FindPropertyRelative("choices.Array.size"), property =>
            {
                if ((property.intValue == 0) != (count == 0))
                    schedule.Execute(() => Show(nodeId));
                count = property.intValue;
            });
            Add(replies);

            Add(DialogueRulesEditor.Consequences(npc, $"{path}.consequences", "Effets de la réplique",
                "Appliqués dès que la réplique s'affiche (ex : le PNJ donne une récompense).", knownFlags));
        }

        VisualElement SpeakerField(SerializedProperty node)
        {
            var speaker = node.FindPropertyRelative("speaker");
            var labels = Enum.GetValues(typeof(DialogueSpeaker)).Cast<DialogueSpeaker>().Select(s => DialogueText.Speaker(s, draft)).ToList();
            var field = new DropdownField("Qui parle", labels, Mathf.Clamp(speaker.enumValueIndex, 0, labels.Count - 1))
                { tooltip = "Le PNJ (bulle à gauche), le joueur (bulle à droite) ou une narration (texte centré)." };
            field.RegisterValueChangedCallback(evt =>
            {
                var index = labels.IndexOf(evt.newValue);
                if (index >= 0)
                    edit(() => speaker.enumValueIndex = index);
            });
            return field;
        }

        VisualElement ChoiceRow(SerializedProperty choice, string choicePath, int nodeId)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-dialogue-choice");
            var text = new TextField { tooltip = "Texte de la réponse du joueur.", multiline = true };
            text.AddToClassList("forge-wrap");
            text.BindProperty(choice.FindPropertyRelative("text"));
            row.Add(text);
            row.Add(TargetField("Mène à", choice.FindPropertyRelative("target"), nodeId, "Réplique déclenchée par cette réponse."));

            var details = new Foldout { text = RuleSummary(choice), value = false };
            details.AddToClassList("forge-dialogue-choice__rules");
            details.Add(DialogueRulesEditor.Conditions(npc, $"{choicePath}.conditions", "Conditions",
                "La réponse n'est proposée que si toutes les conditions sont remplies.", knownFlags));
            details.Add(DialogueRulesEditor.Consequences(npc, $"{choicePath}.consequences", "Effets",
                "Appliqués quand le joueur choisit cette réponse.", knownFlags));
            row.Add(details);
            row.Add(ChoiceLook(choice));
            return row;
        }

        /// <summary>Couleurs propres à une réponse, à la place de celles du personnage (Apparence de la boîte de dialogue).</summary>
        VisualElement ChoiceLook(SerializedProperty choice)
        {
            var custom = choice.FindPropertyRelative("customLook");
            var foldout = new Foldout { text = custom.boolValue ? "Apparence (couleurs propres)" : "Apparence", value = false };
            foldout.AddToClassList("forge-dialogue-choice__rules");
            var toggle = new Toggle("Couleurs propres")
                { tooltip = "Cochée : cette réponse a ses propres couleurs. Décochée : elle prend celles du personnage." };
            toggle.BindProperty(custom);
            foldout.Add(toggle);
            var colors = new VisualElement();
            foreach (var (label, path, tooltip) in new[]
                     {
                         ("Fond", "background", "Fond de cette réponse."),
                         ("Contour", "border", "Contour de cette réponse."),
                         ("Texte", "textColor", "Texte de cette réponse.")
                     })
            {
                var field = new ColorField(label) { showAlpha = false, tooltip = tooltip };
                field.BindProperty(choice.FindPropertyRelative(path));
                colors.Add(field);
            }
            colors.SetEnabled(custom.boolValue);
            foldout.Add(colors);
            var choicePath = choice.propertyPath;
            toggle.RegisterValueChangedCallback(evt =>
            {
                colors.SetEnabled(evt.newValue);
                foldout.text = evt.newValue ? "Apparence (couleurs propres)" : "Apparence";
                if (!evt.newValue || evt.previousValue == evt.newValue)
                    return;
                // Point de départ, si la réponse n'a jamais eu de couleurs : celles du personnage.
                var defaults = new DialogueChoice();
                npc.Update();
                if (npc.FindProperty($"{choicePath}.background").colorValue != defaults.background ||
                    npc.FindProperty($"{choicePath}.border").colorValue != defaults.border ||
                    npc.FindProperty($"{choicePath}.textColor").colorValue != defaults.textColor)
                    return;
                var look = draft.Style;
                npc.FindProperty($"{choicePath}.background").colorValue = look.choiceBackground;
                npc.FindProperty($"{choicePath}.border").colorValue = look.choiceBorder;
                npc.FindProperty($"{choicePath}.textColor").colorValue = look.choiceText;
                npc.ApplyModifiedProperties();
            });
            return foldout;
        }

        static string RuleSummary(SerializedProperty choice)
        {
            var conditions = choice.FindPropertyRelative("conditions").arraySize;
            var consequences = choice.FindPropertyRelative("consequences").arraySize;
            return conditions + consequences == 0
                ? "Conditions et effets"
                : $"Conditions et effets ({conditions} condition(s), {consequences} effet(s))";
        }

        /// <summary>Choix de la réplique suivante, avec la possibilité d'en créer une nouvelle directement.</summary>
        VisualElement TargetField(string label, SerializedProperty target, int ownerId, string tooltip)
        {
            const string create = "+ Nouvelle réplique du PNJ";
            var (labels, ids) = DialogueEditing.TargetChoices(draft, ownerId);
            labels.Add(create);
            var index = Mathf.Max(0, ids.IndexOf(target.intValue));
            var field = new DropdownField(label, labels, index) { tooltip = tooltip };
            field.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == create)
                {
                    var targetPath = target.propertyPath;
                    edit(() =>
                    {
                        var newId = DialogueEditing.AddNode(npc, DialogueSpeaker.Npc, "…");
                        npc.FindProperty(targetPath).intValue = newId;
                    });
                    select(ownerId);
                    return;
                }
                var chosen = labels.IndexOf(evt.newValue);
                if (chosen >= 0 && chosen < ids.Count)
                {
                    var targetPath = target.propertyPath;
                    edit(() => npc.FindProperty(targetPath).intValue = ids[chosen]);
                }
            });
            return field;
        }
    }
}
