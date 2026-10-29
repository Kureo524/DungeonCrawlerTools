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

        readonly SimulatedState state = new();
        readonly VisualElement screen;
        readonly Label hud;
        readonly Image portrait;
        readonly Label namePlate;
        readonly Label text;
        readonly VisualElement choices;
        readonly Label prompt;
        readonly VisualElement statePanel;
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
            hud = new Label { enableRichText = true };
            hud.AddToClassList("forge-preview-game__hud");
            screen.Add(hud);

            var box = new VisualElement();
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
            right.Add(ForgeUI.Label("Joueur simulé", "forge-section-title"));
            statePanel = new VisualElement();
            right.Add(statePanel);
            right.Add(ForgeUI.Label("Journal", "forge-section-title"));
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
            Log($"Début de la conversation avec {Name(DialogueSpeaker.Npc)}", "forge-journal--system");
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
            Log($"{Name(node.speaker)} : {node.text}", "forge-journal--line");

            var isNarration = node.speaker == DialogueSpeaker.Narrator;
            portrait.style.display = isNarration ? DisplayStyle.None : DisplayStyle.Flex;
            portrait.sprite = node.speaker == DialogueSpeaker.Player ? PlayerSprite() : npc.Portrait;
            namePlate.style.display = isNarration ? DisplayStyle.None : DisplayStyle.Flex;
            namePlate.text = Name(node.speaker);
            namePlate.style.backgroundColor = node.speaker == DialogueSpeaker.Npc ? npc.Color : new Color(0.3f, 0.6f, 0.4f);
            text.EnableInClassList("forge-preview-game__text--narration", isNarration);

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
                if (!unlocked)
                {
                    button.SetEnabled(false);
                    button.tooltip = "Verrouillée : " + string.Join(", ", choice.conditions.Where(c => !DialogueLogic.IsMet(c, state)).Select(DialogueText.Describe));
                    button.text += "  (verrouillée)";
                }
                choices.Add(button);
            }
            if (available.Count == 0)
                prompt.text = "Aucune réponse disponible dans cet état : la conversation est bloquée.";
        }

        void Choose(DialogueChoice choice)
        {
            Log($"Vous : {choice.text}", "forge-journal--player");
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
            text.text = npc == null || npc.Nodes.Count == 0 ? "La conversation est vide." : "— Fin de la conversation —";
            text.EnableInClassList("forge-preview-game__text--narration", true);
            prompt.text = "Cliquez sur « Rejouer » pour recommencer.";
            Log("Fin de la conversation", "forge-journal--system");
        }

        void Log(string message, string className)
        {
            var entry = new Label(message);
            entry.AddToClassList("forge-journal");
            entry.AddToClassList(className);
            journal.Add(entry);
            journal.schedule.Execute(() => (journal.parent?.parent as ScrollView)?.ScrollTo(entry));
        }

        string Name(DialogueSpeaker speaker) => speaker == DialogueSpeaker.Player ? "Vous" : DialogueText.Speaker(speaker, npc);

        static Sprite PlayerSprite() => AssetDatabase.LoadAssetAtPath<Sprite>($"{DungeonPaths.Art}/Common/SPR_Player.png");

        void RefreshHud() =>
            hud.text = $"Niv. <b>{state.PlayerLevel}</b>   <color=#f2cc4d>{state.Gold} or</color>   Étage {state.Floor}   PV soignés {state.Healed}   XP +{state.Xp}";

        /// <summary>Contrôles de l'état simulé : niveau, or, étage, marqueurs et inventaire.</summary>
        void RefreshState()
        {
            statePanel.Clear();
            var limits = ForgeDesignLimits.Instance;
            var level = new SliderInt("Niveau", 1, limits.maxLevelCap) { value = state.PlayerLevel, showInputField = true, tooltip = "Niveau du joueur, pour tester les conditions de niveau." };
            level.RegisterValueChangedCallback(evt => { state.PlayerLevel = evt.newValue; Restart(); });
            statePanel.Add(level);
            var gold = new IntegerField("Or") { value = state.Gold, tooltip = "Or du joueur." };
            gold.RegisterValueChangedCallback(evt => { state.Gold = Mathf.Max(0, evt.newValue); Restart(); });
            statePanel.Add(gold);
            var floor = new SliderInt("Étage", 1, limits.floorCount) { value = state.Floor, showInputField = true, tooltip = "Étage où a lieu la rencontre." };
            floor.RegisterValueChangedCallback(evt => { state.Floor = evt.newValue; Restart(); });
            statePanel.Add(floor);

            statePanel.Add(ForgeUI.Label("Marqueurs d'histoire", "forge-palette__title"));
            var flags = npc == null ? new List<string>() : DialogueRulesEditor.FlagsUsedIn(new[] { npc }).Distinct().OrderBy(f => f).ToList();
            if (flags.Count == 0)
                statePanel.Add(ForgeUI.Label("Aucun marqueur dans cette conversation.", "forge-hint"));
            foreach (var flag in flags)
            {
                var toggle = new Toggle(flag) { value = state.HasFlag(flag), tooltip = "Cochez pour simuler un marqueur déjà activé (ex : quête acceptée plus tôt)." };
                toggle.RegisterValueChangedCallback(evt => { state.SetFlag(flag, evt.newValue); Restart(); });
                statePanel.Add(toggle);
            }

            statePanel.Add(ForgeUI.Label("Inventaire", "forge-palette__title"));
            var items = npc == null ? new List<ItemData>() : ItemsUsedIn(npc).ToList();
            if (items.Count == 0)
                statePanel.Add(ForgeUI.Label("Aucun objet cité dans cette conversation.", "forge-hint"));
            foreach (var item in items)
            {
                var toggle = new Toggle(item.DisplayName) { value = state.HasItem(item), tooltip = "Cochez si le joueur possède déjà cet objet." };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) state.AddItem(item); else state.RemoveItem(item);
                    Restart();
                });
                statePanel.Add(toggle);
            }
            RefreshHud();
        }

        static IEnumerable<ItemData> ItemsUsedIn(NpcData npc)
        {
            var conditions = npc.Entries.SelectMany(e => e.conditions)
                .Concat(npc.Nodes.SelectMany(n => n.choices).SelectMany(c => c.conditions));
            var consequences = npc.Nodes.SelectMany(n => n.consequences)
                .Concat(npc.Nodes.SelectMany(n => n.choices).SelectMany(c => c.consequences));
            return conditions.Select(c => c.item).Concat(consequences.Select(c => c.item)).Where(i => i != null).Distinct();
        }

        /// <summary>État de jeu simulé pour l'aperçu.</summary>
        sealed class SimulatedState : IDialogueState
        {
            readonly HashSet<string> flags = new();
            readonly List<ItemData> items = new();

            public event Action Changed;
            public int PlayerLevel { get; set; } = 1;
            public int Gold { get; set; }
            public int Floor { get; set; } = 1;
            public int Xp { get; private set; }
            public int Healed { get; private set; }

            public bool HasFlag(string flag) => flags.Contains(flag);
            public bool HasItem(ItemData item) => items.Contains(item);

            public void SetFlag(string flag, bool value)
            {
                if (string.IsNullOrEmpty(flag))
                    return;
                if (value) flags.Add(flag); else flags.Remove(flag);
                Changed?.Invoke();
            }

            public void AddGold(int amount)
            {
                Gold = Mathf.Max(0, Gold + amount);
                Changed?.Invoke();
            }

            public void AddItem(ItemData item)
            {
                if (item != null && !items.Contains(item))
                    items.Add(item);
                Changed?.Invoke();
            }

            public void RemoveItem(ItemData item)
            {
                items.Remove(item);
                Changed?.Invoke();
            }

            public void AddXp(int amount)
            {
                Xp += amount;
                Changed?.Invoke();
            }

            public void Heal(int amount)
            {
                Healed += amount;
                Changed?.Invoke();
            }

            public void Reset()
            {
                flags.Clear();
                items.Clear();
                PlayerLevel = 1;
                Gold = 0;
                Xp = 0;
                Healed = 0;
                Changed?.Invoke();
            }
        }
    }
}
