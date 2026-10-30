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
        readonly VisualElement hud;
        readonly VisualElement box;
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
            statePanel = new VisualElement();
            ForgeLabels.Track(statePanel);
            right.Add(statePanel);
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

        /// <summary>Contrôles de l'état simulé : niveau, or, étage, marqueurs et inventaire.</summary>
        void RefreshState()
        {
            statePanel.Clear();
            var limits = ForgeDesignLimits.Instance;
            // Le niveau ne dépasse pas le niveau maximum de la progression utilisée en jeu.
            var progression = TheDungeon.Editor.Play.GameLauncher.ActiveProgression;
            var maxLevel = progression != null ? Mathf.Clamp(progression.MaxLevel, 1, limits.maxLevelCap) : limits.maxLevelCap;
            state.PlayerLevel = Mathf.Clamp(state.PlayerLevel, 1, maxLevel);
            var level = new SliderInt("Niveau", 1, maxLevel)
            {
                value = state.PlayerLevel,
                showInputField = true,
                tooltip = progression != null
                    ? $"Niveau du joueur, pour tester les conditions de niveau (1 à {maxLevel}, progression '{progression.DisplayName}')."
                    : "Niveau du joueur, pour tester les conditions de niveau."
            };
            level.RegisterValueChangedCallback(evt => { state.PlayerLevel = Mathf.Clamp(evt.newValue, 1, maxLevel); Restart(); });
            statePanel.Add(level);
            var gold = new IntegerField("Or") { value = state.Gold, tooltip = "Or du joueur." };
            gold.RegisterValueChangedCallback(evt => { state.Gold = Mathf.Max(0, evt.newValue); Restart(); });
            statePanel.Add(gold);
            var floor = new SliderInt("Étage", 1, limits.floorCount) { value = state.Floor, showInputField = true, tooltip = "Étage où a lieu la rencontre." };
            floor.RegisterValueChangedCallback(evt => { state.Floor = evt.newValue; Restart(); });
            statePanel.Add(floor);

            // Mécaniques citées par les conditions : leur niveau (0 = pas encore débloquée).
            var mechanicIds = npc == null ? new List<string>() : npc.Entries.SelectMany(e => e.conditions)
                .Concat(npc.Nodes.SelectMany(n => n.choices).SelectMany(c => c.conditions))
                .Where(c => c.type == ConditionType.MechanicAtLeast && !string.IsNullOrEmpty(c.flag)).Select(c => c.flag).Distinct().ToList();
            if (mechanicIds.Count > 0)
            {
                statePanel.Add(ForgeUI.Label("Mécaniques", "forge-palette__title"));
                foreach (var id in mechanicIds)
                {
                    var mechanic = DialogueText.FindMechanic(id);
                    var slider = new SliderInt(mechanic != null ? mechanic.displayName : ForgeText.Redacted, 0, Mathf.Max(1, mechanic?.MaxRank ?? 1))
                        { value = state.MechanicLevel(id), showInputField = true, tooltip = "Niveau de la mécanique (0 = pas encore débloquée)." };
                    slider.RegisterValueChangedCallback(evt => { state.SetMechanicLevel(id, evt.newValue); Restart(); });
                    statePanel.Add(slider);
                }
            }

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
            readonly Dictionary<string, int> mechanics = new();

            public event Action Changed;
            public int PlayerLevel { get; set; } = 1;
            public int Gold { get; set; }
            public int Floor { get; set; } = 1;
            public int Xp { get; private set; }
            public int Healed { get; private set; }

            public bool HasFlag(string flag) => flags.Contains(flag);
            public bool HasItem(ItemData item) => items.Contains(item);
            public int MechanicLevel(string mechanicId) => mechanics.TryGetValue(mechanicId ?? string.Empty, out var level) ? level : 0;

            public void SetMechanicLevel(string mechanicId, int level)
            {
                mechanics[mechanicId] = Mathf.Max(0, level);
                Changed?.Invoke();
            }

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
                mechanics.Clear();
                PlayerLevel = 1;
                Gold = 0;
                Xp = 0;
                Healed = 0;
                Changed?.Invoke();
            }
        }
    }
}
