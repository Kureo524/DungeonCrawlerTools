using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Dialogue;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Dialogues
{
    /// <summary>
    /// Joue la conversation comme en jeu : boîte de dialogue avec portrait, texte qui s'écrit, réponses cliquables.
    /// L'état du joueur est simulé et modifiable pour tester chaque condition ; les effets s'appliquent et sont journalisés.
    /// Utilise la même logique que le jeu (DialogueLogic).
    /// </summary>
    public sealed class DialoguePreview : VisualElement
    {
        const int CharactersPerTick = 2;

        readonly SimulatedDialogueState state = new();
        readonly VisualElement screen;
        readonly VisualElement hud;
        readonly VisualElement box;
        readonly Image portrait;
        readonly Label namePlate;
        readonly Label text;
        readonly VisualElement choices;
        readonly Label prompt;
        readonly SimulatedPlayerPanel playerPanel;
        readonly VisualElement journal;
        readonly Toggle showLocked;

        NpcData npc;
        DialogueNode current;
        IVisualElementScheduledItem typing;
        IVisualElementScheduledItem pendingStateRefresh;
        string fullText = string.Empty;
        int shown;

        public DialoguePreview()
        {
            AddToClassList("forge-preview-game");

            var left = new VisualElement();
            left.AddToClassList("forge-preview-game__left");
            screen = new VisualElement();
            screen.AddToClassList("forge-preview-game__screen");
            hud = new VisualElement();
            hud.AddToClassList("forge-preview-game__hud");
            screen.Add(hud);

            box = new VisualElement();
            box.AddToClassList("forge-preview-game__box");
            portrait = new Image { scaleMode = ScaleMode.ScaleToFit };
            portrait.AddToClassList("forge-preview-game__portrait");
            box.Add(portrait);
            var content = new VisualElement();
            content.AddToClassList("forge-preview-game__content");
            namePlate = new Label();
            namePlate.AddToClassList("forge-preview-game__name");
            content.Add(namePlate);
            text = new Label();
            text.AddToClassList("forge-preview-game__text");
            content.Add(text);
            choices = new VisualElement();
            choices.AddToClassList("forge-preview-game__choices");
            content.Add(choices);
            prompt = new Label();
            prompt.AddToClassList("forge-preview-game__prompt");
            content.Add(prompt);
            box.Add(content);
            box.RegisterCallback<ClickEvent>(_ => OnBoxClicked());
            screen.Add(box);
            left.Add(screen);

            var controls = new VisualElement();
            controls.AddToClassList("forge-toolbar");
            controls.Add(ForgeUI.Button("↺ Rejouer", "Recommence la conversation avec l'état actuel du joueur.", Restart, "forge-button--primary"));
            controls.Add(ForgeUI.Button("Réinitialiser le joueur", "Niveau 1, 0 or, aucun marqueur ni objet.", () =>
            {
                state.Reset();
                RefreshState();
                Restart();
            }));
            showLocked = new Toggle("Montrer les réponses verrouillées") { tooltip = "Affiche aussi, grisées, les réponses dont les conditions ne sont pas remplies." };
            showLocked.RegisterValueChangedCallback(_ => ShowChoicesIfReady());
            controls.Add(showLocked);
            left.Add(controls);
            Add(left);

            var right = new VisualElement();
            right.AddToClassList("forge-preview-game__right");
            right.Add(ForgeUI.Label(ForgeText.WindowRich("Joueur simulé"), "forge-section-title"));
            playerPanel = new SimulatedPlayerPanel(state, () => npc, Restart);
            right.Add(playerPanel);
            var journalTitle = ForgeUI.Label(ForgeText.WindowRich("Journal"), "forge-section-title");
            journalTitle.AddToClassList("forge-preview-game__journal-title");
            right.Add(journalTitle);
            var journalScroll = new ScrollView();
            journalScroll.AddToClassList("forge-preview-game__journal");
            journal = new VisualElement();
            journalScroll.Add(journal);
            right.Add(journalScroll);
            Add(right);

            // Les effets de la conversation (marqueurs, objets…) se reflètent dans les contrôles, au tour suivant.
            state.Changed += () =>
            {
                RefreshHud();
                pendingStateRefresh?.Pause();
                pendingStateRefresh = schedule.Execute(RefreshState).StartingIn(50);
            };
        }

        public void SetNpc(NpcData data, Sprite background)
        {
            npc = data;
            screen.style.backgroundImage = background != null ? new StyleBackground(background) : StyleKeyword.None;
            state.Floor = Mathf.Max(1, data.Floor);
            RefreshState();
            Restart();
        }

        void Restart()
        {
            journal.Clear();
            Log($"Début de la conversation avec {ForgeText.Name(Name(DialogueSpeaker.Npc))}.", "forge-journal--system");
            Show(npc == null ? null : DialogueLogic.Start(npc, state));
        }

        void Show(DialogueNode node)
        {
            current = node;
            choices.Clear();
            prompt.text = string.Empty;
            if (node == null)
            {
                End();
                return;
            }

            DialogueLogic.ApplyAll(node.consequences, state);
            foreach (var consequence in node.consequences)
                Log(DialogueText.Describe(consequence), "forge-journal--effect");
            Log($"{Name(node.speaker)} : {ForgeText.Quote(node.text)}", "forge-journal--line");

            var isNarration = node.speaker == DialogueSpeaker.Narrator;
            portrait.style.display = isNarration ? DisplayStyle.None : DisplayStyle.Flex;
            portrait.sprite = node.speaker == DialogueSpeaker.Player ? PlayerSprite() : npc.Portrait;
            namePlate.style.display = isNarration ? DisplayStyle.None : DisplayStyle.Flex;
            namePlate.text = Name(node.speaker);
            var look = npc.Style;
            namePlate.style.backgroundColor = node.speaker == DialogueSpeaker.Npc ? npc.Color : look.playerPlate;
            namePlate.style.color = look.nameText;
            text.EnableInClassList("forge-preview-game__text--narration", isNarration);
            ApplyLook();

            fullText = node.text ?? string.Empty;
            shown = 0;
            text.text = string.Empty;
            typing?.Pause();
            typing = schedule.Execute(() =>
            {
                shown = Mathf.Min(fullText.Length, shown + CharactersPerTick);
                text.text = fullText.Substring(0, shown);
                if (shown >= fullText.Length)
                {
                    typing.Pause();
                    ShowChoicesIfReady();
                }
            }).Every(18);
        }

        bool IsTyping => current != null && shown < fullText.Length;

        void OnBoxClicked()
        {
            if (current == null)
                return;
            if (IsTyping)
            {
                // Un clic pendant l'écriture affiche tout le texte, comme en jeu.
                typing?.Pause();
                shown = fullText.Length;
                text.text = fullText;
                ShowChoicesIfReady();
                return;
            }
            if (current.choices.Count == 0)
                Follow(current.next);
        }

        void ShowChoicesIfReady()
        {
            choices.Clear();
            prompt.text = string.Empty;
            if (current == null || IsTyping)
                return;

            if (current.choices.Count == 0)
            {
                prompt.text = current.next < 0 ? "▼ Cliquer pour terminer" : "▼ Cliquer pour continuer";
                return;
            }

            var available = DialogueLogic.AvailableChoices(current, state).ToList();
            var index = 1;
            foreach (var choice in current.choices)
            {
                var unlocked = available.Contains(choice);
                if (!unlocked && !showLocked.value)
                    continue;
                var button = new Button(() => Choose(choice)) { text = $"{index++}. {choice.text}" };
                button.AddToClassList("forge-preview-game__choice");
                StyleChoice(button, choice);
                if (!unlocked)
                {
                    button.SetEnabled(false);
                    button.tooltip = "Verrouillée : " + string.Join(", ", choice.conditions.Where(c => !DialogueLogic.IsMet(c, state)).Select(DialogueText.Describe));
                    button.text += "  (verrouillée)";
                }
                choices.Add(button);
            }
            if (available.Count == 0)
                prompt.text = ForgeText.System("Aucune réponse disponible dans cet état : la conversation est bloquée.");
        }

        void Choose(DialogueChoice choice)
        {
            Log($"Vous : {ForgeText.Quote(choice.text)}", "forge-journal--player");
            DialogueLogic.ApplyAll(choice.consequences, state);
            foreach (var consequence in choice.consequences)
                Log(DialogueText.Describe(consequence), "forge-journal--effect");
            Follow(choice.target);
        }

        void Follow(int id)
        {
            if (id < 0)
            {
                Show(null);
                return;
            }
            var node = npc.FindNode(id);
            if (node == null)
            {
                Log($"Réplique #{id} introuvable : lien cassé.", "forge-journal--error");
                Show(null);
                return;
            }
            Show(node);
        }

        void End()
        {
            typing?.Pause();
            portrait.style.display = DisplayStyle.None;
            namePlate.style.display = DisplayStyle.None;
            text.text = ForgeText.System(npc == null || npc.Nodes.Count == 0 ? "La conversation est vide." : "Fin de la conversation.");
            text.EnableInClassList("forge-preview-game__text--narration", true);
            if (npc != null)
                ApplyLook();
            prompt.text = "Cliquez sur « Rejouer » pour recommencer.";
            Log("Fin de la conversation.", "forge-journal--system");
        }

        void Log(string message, string className)
        {
            // Messages du système (début, effets, erreurs) écrits comme dans le roman : [entre crochets].
            if (className is "forge-journal--system" or "forge-journal--effect" or "forge-journal--error")
                message = ForgeText.System(message);
            var entry = new Label(message);
            entry.AddToClassList("forge-journal");
            entry.AddToClassList(className);
            journal.Add(entry);
            journal.schedule.Execute(() => (journal.parent?.parent as ScrollView)?.ScrollTo(entry));
        }

        string Name(DialogueSpeaker speaker) => speaker == DialogueSpeaker.Player ? "Vous" : DialogueText.Speaker(speaker, npc);

        static Sprite PlayerSprite() => AssetDatabase.LoadAssetAtPath<Sprite>($"{DungeonPaths.Art}/Common/SPR_Player.png");

        /// <summary>Couleurs du personnage (outil Dialogues > Apparence) sur la boîte, le portrait et le texte.</summary>
        void ApplyLook()
        {
            var look = npc.Style;
            box.style.backgroundColor = look.boxBackground;
            SetBorder(box, look.boxBorder);
            SetBorder(portrait, look.boxBorder);
            text.style.color = text.ClassListContains("forge-preview-game__text--narration")
                ? new Color(look.text.r, look.text.g, look.text.b, look.text.a * 0.8f)
                : look.text;
            prompt.style.color = look.boxBorder;
        }

        /// <summary>Réponse du joueur aux couleurs du personnage, plus marquée au survol.</summary>
        void StyleChoice(Button button, DialogueChoice choice)
        {
            var (background, border, color) = npc.Style.ChoiceLook(choice);
            var solid = new Color(border.r, border.g, border.b, 1f);
            void Normal()
            {
                button.style.backgroundColor = background;
                SetBorder(button, border);
            }
            Normal();
            button.style.color = color;
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                button.style.backgroundColor = Color.Lerp(background, solid, 0.3f);
                SetBorder(button, solid);
            });
            button.RegisterCallback<PointerLeaveEvent>(_ => Normal());
        }

        static void SetBorder(VisualElement element, Color color) =>
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;

        /// <summary>Barre d'état du joueur simulé : une pastille par valeur.</summary>
        void RefreshHud()
        {
            hud.Clear();
            foreach (var (label, value, className) in new[]
                     {
                         ("Niv.", state.PlayerLevel.ToString(), null),
                         ("Or", state.Gold.ToString(), "forge-preview-game__stat--gold"),
                         ("Étage", state.Floor.ToString(), null),
                         ("PV soignés", state.Healed.ToString(), null),
                         ("XP", $"+{state.Xp}", null)
                     })
            {
                var stat = new VisualElement();
                stat.AddToClassList("forge-preview-game__stat");
                if (className != null)
                    stat.AddToClassList(className);
                stat.Add(ForgeUI.Label(label, "forge-preview-game__stat-label"));
                stat.Add(ForgeUI.Label(value, "forge-preview-game__stat-value"));
                hud.Add(stat);
            }
        }

        /// <summary>Contrôles de l'état simulé, puis la barre d'état.</summary>
        void RefreshState()
        {
            playerPanel.Refresh();
            RefreshHud();
        }
    }
}
