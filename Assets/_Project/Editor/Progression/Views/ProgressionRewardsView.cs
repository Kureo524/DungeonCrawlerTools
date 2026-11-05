using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static TheDungeon.Editor.Progression.ProgressionFields;

namespace TheDungeon.Editor.Progression
{
    /// <summary>
    /// Récompenses par niveau : frise des niveaux, récompenses du niveau choisi et points d'attributs à intervalle régulier.
    /// La frise, les pastilles et les lignes de récompense servent aussi à l'aperçu et à la simulation.
    /// </summary>
    internal sealed class ProgressionRewardsView : ProgressionView
    {
        public ProgressionRewardsView(ProgressionContext context) : base(context)
        {
        }

        public override void Build(VisualElement parent, PlayerProgression draft)
        {
            if (State.RewardLevel < 1)
                State.RewardLevel = draft.Rewards.Count > 0 ? draft.Rewards.Min(r => r.level) : 1;
            State.RewardLevel = Mathf.Clamp(State.RewardLevel, 1, draft.MaxLevel);

            var timeline = ForgeUI.Section("Récompenses par niveau", parent,
                "Choisissez un niveau sur la frise pour voir et modifier ce qu'il rapporte : points d'attributs, compétence ou mécanique débloquée ou améliorée, note.");
            timeline.Add(Timeline(draft, false));
            timeline.Add(Legend());

            var level = ForgeUI.Section($"Niveau {State.RewardLevel}", parent);
            AddLevelRewards(level, draft, State.RewardLevel);

            BuildRegularPoints(parent);
        }

        /// <summary>Bloc repliable comme les autres (même flèche, même en-tête) : des points d'attributs tous les N niveaux.</summary>
        void BuildRegularPoints(VisualElement parent)
        {
            var quick = ForgeUI.Section("Points d'attributs à intervalle régulier", parent,
                "Donne des points d'attributs tous les N niveaux. « Remplacer » efface les points déjà prévus, « Ajouter » les cumule avec eux.");
            var row = Row();
            var amount = new IntegerField("Points") { value = 1, tooltip = "Points d'attributs donnés à chaque fois." };
            var every = new IntegerField("Tous les … niveaux") { value = 1, tooltip = "Écart entre deux niveaux qui donnent des points." };
            var from = new IntegerField("À partir du niveau") { value = 2, tooltip = "Premier niveau qui donne des points." };
            row.Add(amount);
            row.Add(every);
            row.Add(from);
            row.Add(SmallButton("Ajouter", "Ajoute ces points à ceux déjà donnés (un niveau qui en donne déjà en reçoit davantage). Ctrl+Z pour annuler.", () =>
            {
                ProgressionEditing.AddAttributePoints(Serialized, Mathf.Max(0, amount.value), Mathf.Max(1, from.value), Mathf.Max(1, every.value));
                Rebuild();
            }, true, "forge-button--primary"));
            row.Add(SmallButton("Remplacer", "Remplace toutes les récompenses « Points d'attributs » par ce rythme (Ctrl+Z pour annuler).", () =>
            {
                ProgressionEditing.SetAttributePoints(Serialized, Mathf.Max(0, amount.value), Mathf.Max(1, from.value), Mathf.Max(1, every.value));
                Rebuild();
            }, true));
            quick.Add(row);
        }

        /// <summary>Récompenses d'un niveau, modifiables, et boutons pour en ajouter.</summary>
        public void AddLevelRewards(VisualElement parent, PlayerProgression draft, int level)
        {
            var rewards = Serialized.FindProperty("rewards");
            var any = false;
            for (var i = 0; i < draft.Rewards.Count; i++)
            {
                if (draft.Rewards[i].level != level)
                    continue;
                parent.Add(RewardRow(rewards.GetArrayElementAtIndex(i), i, draft.Rewards[i]));
                any = true;
            }
            if (!any)
                parent.Add(ForgeUI.Label("Rien à ce niveau.", "forge-hint"));

            var add = new VisualElement();
            add.AddToClassList("forge-row");
            void AddButton(string text, RewardType type, bool enabled = true)
            {
                var button = ForgeUI.Button(text, $"Ajoute une récompense au niveau {level}.", () =>
                {
                    ProgressionEditing.AddReward(Serialized, new LevelReward { level = level, type = type, amount = 1 });
                    ProgressionEditing.SortRewards(Serialized);
                    Rebuild();
                });
                button.SetEnabled(enabled);
                add.Add(button);
            }
            AddButton("+ Points d'attributs", RewardType.AttributePoints);
            AddButton("+ Compétence", RewardType.UnlockSkill, draft.Skills.Count > 0);
            AddButton("+ Rang de compétence", RewardType.UpgradeSkill, draft.Skills.Count > 0);
            AddButton("+ Mécanique", RewardType.UnlockMechanic, draft.Mechanics.Count > 0);
            AddButton("+ Rang de mécanique", RewardType.UpgradeMechanic, draft.Mechanics.Count > 0);
            AddButton("+ Note", RewardType.Note);
            parent.Add(add);
        }

        VisualElement RewardRow(SerializedProperty property, int index, LevelReward reward)
        {
            var row = Row();
            row.AddToClassList("forge-reward-row");
            row.Add(LevelField(property, reward));
            row.Add(EnumDropdown<RewardType>("Type", property.FindPropertyRelative("type"), ProgressionEditing.RewardTypeLabel,
                "Points d'attributs, compétence ou mécanique débloquée ou améliorée, ou note.", _ => Rebuild()));
            row.Add(RewardValueField(property, reward));
            row.Add(SmallButton("×", "Supprimer la récompense (Ctrl+Z pour annuler).", () =>
            {
                ProgressionEditing.RemoveAt(Serialized, "rewards", index);
                Rebuild();
            }, true));
            return row;
        }

        IntegerField LevelField(SerializedProperty property, LevelReward reward)
        {
            var level = Bound(new IntegerField("Niveau") { isDelayed = true, tooltip = $"Niveau auquel la récompense est obtenue (1 à {Draft.MaxLevel}). La changer la déplace sur la frise." },
                property.FindPropertyRelative("level"));
            level.AddToClassList("forge-reward-row__level");
            level.RegisterValueChangedCallback(e =>
            {
                // Le rattachement aux données envoie lui aussi la valeur actuelle : seul un vrai changement de niveau reconstruit la vue
                // (sinon la catégorie se reconstruirait sans fin et plus aucun clic n'aboutirait).
                if (e.newValue == reward.level)
                    return;
                var clamped = Mathf.Clamp(e.newValue, 1, Draft.MaxLevel);
                if (clamped != e.newValue)
                {
                    level.value = clamped;
                    return;
                }
                // La récompense change de niveau : on la suit (frise et simulation).
                State.RewardLevel = clamped;
                if (State.View == ProgressionCategory.Simulation)
                    State.OpenLevel = State.SimLevel = clamped;
                level.schedule.Execute(() =>
                {
                    ProgressionEditing.SortRewards(Serialized);
                    Rebuild();
                });
            });
            return level;
        }

        /// <summary>Champ propre au type de récompense : nombre de points, compétence, mécanique ou texte de la note.</summary>
        VisualElement RewardValueField(SerializedProperty property, LevelReward reward)
        {
            switch (reward.type)
            {
                case RewardType.AttributePoints:
                    return Bound(new IntegerField("Points") { tooltip = "Points d'attributs donnés." }, property.FindPropertyRelative("amount"));
                case RewardType.UnlockSkill or RewardType.UpgradeSkill:
                    return IdDropdown("Compétence", property.FindPropertyRelative("skillId"), () => Draft.Skills.Select(s => (s.id, s.displayName)).ToList(),
                        reward.type == RewardType.UnlockSkill ? "Compétence débloquée (rang 1)." : "Compétence qui passe au rang suivant (elle doit déjà être débloquée).");
                case RewardType.UnlockMechanic or RewardType.UpgradeMechanic:
                    return IdDropdown("Mécanique", property.FindPropertyRelative("mechanicId"), () => Draft.Mechanics.Select(m => (m.id, m.displayName)).ToList(),
                        reward.type == RewardType.UnlockMechanic ? "Mécanique débloquée (rang 1)." : "Mécanique qui passe au rang suivant (elle doit déjà être débloquée).");
                default:
                    return Bound(new TextField("Note") { tooltip = "Texte affiché au joueur en atteignant le niveau (contenu débloqué, événement…)." },
                        property.FindPropertyRelative("note"));
            }
        }

        /// <summary>Frise des niveaux : une case par niveau avec les pastilles de ses récompenses. Cliquer ouvre le niveau.</summary>
        public VisualElement Timeline(PlayerProgression draft, bool compact)
        {
            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            var row = new VisualElement();
            row.AddToClassList("forge-timeline");
            scroll.Add(row);
            for (var l = 1; l <= draft.MaxLevel; l++)
            {
                var level = l;
                var cell = new VisualElement { tooltip = ForgeText.System($"Niveau {l} : {ProgressionEditing.RewardSummary(draft, l)}") };
                cell.AddToClassList("forge-tl");
                cell.EnableInClassList("forge-tl--selected", !compact && l == State.RewardLevel);
                cell.Add(ForgeUI.Label(l.ToString(), "forge-tl__number"));
                var dots = new VisualElement { pickingMode = PickingMode.Ignore };
                dots.AddToClassList("forge-tl__dots");
                foreach (var reward in draft.Rewards.Where(r => r.level == l))
                    dots.Add(RewardPill(draft, reward));
                cell.Add(dots);
                cell.RegisterCallback<ClickEvent>(_ =>
                {
                    State.RewardLevel = level;
                    if (State.View == ProgressionCategory.Rewards)
                        Rebuild();
                    else
                        ShowView(ProgressionCategory.Rewards);
                });
                row.Add(cell);
            }
            return scroll;
        }

        public static VisualElement Pill(string modifier, string tooltip, Color? color = null)
        {
            var pill = new VisualElement { tooltip = tooltip };
            pill.AddToClassList("forge-pill");
            if (modifier != null)
                pill.AddToClassList(modifier);
            if (color.HasValue)
                pill.style.backgroundColor = color.Value;
            return pill;
        }

        public static VisualElement RewardPill(PlayerProgression draft, LevelReward reward) => reward.type switch
        {
            RewardType.AttributePoints => Pill("forge-pill--points", $"+{reward.amount} point(s) d'attribut"),
            RewardType.UnlockSkill or RewardType.UpgradeSkill => Pill("forge-pill--skill", ProgressionEditing.RewardTypeLabel(reward.type) + " : " + (draft.FindSkill(reward.skillId)?.displayName ?? "■■■")),
            RewardType.UnlockMechanic or RewardType.UpgradeMechanic => Pill("forge-pill--mechanic", ProgressionEditing.RewardTypeLabel(reward.type) + " : " + (draft.FindMechanic(reward.mechanicId)?.displayName ?? "■■■")),
            _ => Pill("forge-pill--note", string.IsNullOrWhiteSpace(reward.note) ? "Note vide" : reward.note)
        };

        public static VisualElement Legend(bool withStats = false)
        {
            var legend = new VisualElement();
            legend.AddToClassList("forge-legend");
            void Item(VisualElement pill, string text)
            {
                var item = new VisualElement();
                item.Add(pill);
                item.Add(new Label(text));
                legend.Add(item);
            }
            if (withStats)
            {
                Item(Pill(null, null, new Color(0.64f, 0.65f, 0.71f)), "Stat modifiée (sa couleur)");
                Item(Pill("forge-pill--bad", null, new Color(0.64f, 0.65f, 0.71f)), "Problème");
            }
            Item(Pill("forge-pill--points", null), "Points d'attributs");
            Item(Pill("forge-pill--skill", null), "Compétence");
            Item(Pill("forge-pill--mechanic", null), "Mécanique");
            Item(Pill("forge-pill--note", null), "Note");
            return legend;
        }
    }
}
