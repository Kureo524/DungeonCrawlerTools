using System;
using System.Collections.Generic;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// Barre de saisie de la conversation, comme dans une messagerie : on choisit qui parle, on tape, Entrée pour envoyer.
    /// Une réplique s'ajoute à la suite de la réplique sélectionnée ; une réponse du joueur, sous elle.
    /// </summary>
    internal sealed class DialogueComposer : VisualElement
    {
        readonly SerializedObject npc;
        readonly NpcData draft;
        readonly Func<int> selected;
        readonly Action<Action> edit;
        readonly Action<int> select;
        readonly Action<int> scrollTo;
        readonly Dictionary<DialogueSpeaker, Button> speakerButtons = new();
        readonly TextField input;
        DialogueSpeaker speaker;

        /// <param name="selected">Réplique sélectionnée, à la suite de laquelle on écrit.</param>
        /// <param name="edit">Applique une modification annulable.</param>
        public DialogueComposer(SerializedObject npc, NpcData draft, DialogueSpeaker speaker, Func<int> selected, Action<Action> edit,
            Action<int> select, Action<int> scrollTo)
        {
            this.npc = npc;
            this.draft = draft;
            this.speaker = speaker;
            this.selected = selected;
            this.edit = edit;
            this.select = select;
            this.scrollTo = scrollTo;
            AddToClassList("forge-composer");

            var speakers = new VisualElement();
            speakers.AddToClassList("forge-composer__speakers");
            foreach (var (option, label, tooltip) in new[]
                     {
                         (DialogueSpeaker.Npc, "PNJ", "La réplique est dite par le personnage et suit la réplique sélectionnée."),
                         (DialogueSpeaker.Player, "Réponse du joueur", "Sous une réplique du PNJ : ajoute un intitulé de réponse à choisir. " +
                                                                       "Sous une réplique du joueur : le joueur enchaîne avec une nouvelle réplique, dans son cadre."),
                         (DialogueSpeaker.Narrator, "Narration", "Texte de narration (sans personnage).")
                     })
            {
                var button = ForgeUI.Button(label, tooltip, () => SetSpeaker(option), "forge-composer__speaker");
                speakerButtons[option] = button;
                speakers.Add(button);
            }
            Add(speakers);

            var row = new VisualElement();
            row.AddToClassList("forge-row");
            input = new TextField
            {
                tooltip = "Tapez la réplique puis Entrée (Maj+Entrée : retour à la ligne). Elle est ajoutée à la suite de la réplique sélectionnée.",
                multiline = true
            };
            input.AddToClassList("forge-composer__input");
            input.AddToClassList("forge-wrap");
            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter) || evt.shiftKey)
                    return;
                Send();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            row.Add(input);
            row.Add(ForgeUI.Button("Envoyer", "Ajoute la réplique (raccourci : Entrée).", Send, "forge-button--primary"));
            Add(row);
            Add(ForgeUI.Label("Entrée : envoyer · Changez qui parle avec les boutons ci-dessus · Cliquez une bulle pour écrire la suite à partir d'elle.", "forge-hint"));
            SetSpeaker(speaker);
        }

        /// <summary>Qui parle a changé (retenu par l'onglet d'une fiche à l'autre).</summary>
        public event Action<DialogueSpeaker> SpeakerChanged;

        void SetSpeaker(DialogueSpeaker value)
        {
            speaker = value;
            foreach (var (candidate, button) in speakerButtons)
                button.EnableInClassList("forge-composer__speaker--active", candidate == value);
            SpeakerChanged?.Invoke(value);
        }

        void Send()
        {
            var text = input.value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                ForgeLists.Notify("Écrivez d'abord le texte de la réplique.");
                return;
            }

            var current = draft.FindNode(selected());
            if (speaker == DialogueSpeaker.Player && current != null && current.speaker == DialogueSpeaker.Player && current.choices.Count == 0)
            {
                // Le joueur enchaîne : nouvelle réplique du joueur juste après la sienne.
                if (draft.Nodes.Count >= ForgeDesignLimits.Instance.maxDialogueNodes)
                {
                    ForgeLists.Notify($"Maximum atteint : {ForgeDesignLimits.Instance.maxDialogueNodes} répliques par personnage.");
                    return;
                }
                var added = -1;
                edit(() => added = DialogueEditing.AddLineAfter(npc, current.id, DialogueSpeaker.Player, text));
                select(added);
                scrollTo(added);
            }
            else if (speaker == DialogueSpeaker.Player)
            {
                if (current == null)
                {
                    ForgeLists.Notify("Sélectionnez la réplique à laquelle le joueur répond.");
                    return;
                }
                var added = -1;
                edit(() => added = DialogueEditing.AddChoice(npc, selected(), text, DialogueEditing.End, ForgeDesignLimits.Instance.maxChoicesPerNode));
                if (added < 0)
                {
                    ForgeLists.Notify($"Maximum atteint : {ForgeDesignLimits.Instance.maxChoicesPerNode} réponses par réplique.");
                    return;
                }
                // Le PNJ répondra à cette réponse : on repasse en mode PNJ pour enchaîner naturellement.
                SetSpeaker(DialogueSpeaker.Npc);
            }
            else
            {
                if (draft.Nodes.Count >= ForgeDesignLimits.Instance.maxDialogueNodes)
                {
                    ForgeLists.Notify($"Maximum atteint : {ForgeDesignLimits.Instance.maxDialogueNodes} répliques par personnage.");
                    return;
                }
                var newId = -1;
                var linked = false;
                var from = selected();
                edit(() =>
                {
                    newId = DialogueEditing.AddNode(npc, speaker, text);
                    linked = DialogueEditing.LinkAfter(npc, from, newId) || npc.FindProperty("startNode").intValue == newId;
                });
                if (!linked)
                    ForgeLists.Notify("Réplique ajoutée mais reliée à rien : choisissez sa place (« Suite » ou une réponse).");
                select(newId);
                scrollTo(newId);
            }
            input.SetValueWithoutNotify(string.Empty);
            input.schedule.Execute(() => input.Focus());
        }
    }
}
