using System;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static TheDungeon.Editor.Progression.ProgressionFields;

namespace TheDungeon.Editor.Progression
{
    /// <summary>Mécaniques du personnage : une ligne dépliable par action (touche, coût, recharge, réglages et améliorations par rang).</summary>
    internal sealed class ProgressionMechanicsView : ProgressionView
    {
        public ProgressionMechanicsView(ProgressionContext context) : base(context)
        {
        }

        public override void Build(VisualElement parent, PlayerProgression draft)
        {
            var section = ForgeUI.Section("Mécaniques du personnage", parent,
                "Les actions du joueur : attaques, mouvements, soin, bouclier. Chacune a sa touche, peut consommer une stat (une jauge comme l'énergie, ou les PV) " +
                "et a un temps de recharge. Cliquez sur une ligne pour afficher tous ses réglages.");
            if (draft.Mechanics.Count == 0)
                section.Add(new HelpBox("Aucune mécanique : le joueur garde le tir (clic gauche) et l'esquive (Espace) de base.", HelpBoxMessageType.Info));
            var mechanics = Serialized.FindProperty("mechanics");
            for (var i = 0; i < mechanics.arraySize; i++)
                section.Add(MechanicRow(draft, mechanics.GetArrayElementAtIndex(i), i));

            var add = ForgeUI.Button("+ Ajouter une mécanique", "Une mécanique par touche (6 au maximum).", () =>
            {
                var free = Enum.GetValues(typeof(MechanicInput)).Cast<MechanicInput>().FirstOrDefault(input => Draft.Mechanics.All(m => m.input != input));
                State.OpenMechanic = ProgressionEditing.AddMechanic(Serialized, ProgressionEditing.Mechanic(UniqueName(Draft.Mechanics.Select(m => m.displayName), "Nouvelle mécanique"),
                    MechanicAction.Projectile, free, NextColor(Draft.Mechanics.Count + 7)));
                Rebuild();
            });
            add.SetEnabled(draft.Mechanics.Count < Enum.GetValues(typeof(MechanicInput)).Length);
            add.style.alignSelf = Align.FlexStart;
            section.Add(add);
        }

        /// <summary>Ligne repliée (nom, touche, résumé) ; dépliée, elle montre la carte complète de la mécanique.</summary>
        VisualElement MechanicRow(PlayerProgression draft, SerializedProperty property, int index)
        {
            var open = index == State.OpenMechanic;
            var acc = Accordion(open, draft.Mechanics[index].color, draft.Mechanics[index].displayName, () =>
            {
                State.OpenMechanic = open ? -1 : index;
                Rebuild();
            }, out var name, out var meta, out var dot);
            var key = ForgeUI.Label(ProgressionEditing.InputLabel(draft.Mechanics[index].input), "forge-key");
            acc.Q(className: "forge-acc__head").Insert(3, key);
            AddRefresher(() =>
            {
                if (index >= Draft.Mechanics.Count)
                    return;
                var mechanic = Draft.Mechanics[index];
                name.text = mechanic.displayName;
                dot.style.backgroundColor = mechanic.color;
                key.text = ProgressionEditing.InputLabel(mechanic.input);
                var unlock = Enumerable.Range(1, Draft.MaxLevel).FirstOrDefault(l => ProgressionEditing.RankAt(Draft, mechanic, l) > 0);
                meta.text = ProgressionEditing.Describe(Draft, mechanic, 1) + (unlock == 0 ? " · jamais débloquée" : unlock == 1 ? string.Empty : $" · niveau {unlock}");
            });
            if (open)
                acc.Q(className: "forge-acc__body").Add(MechanicCard(property, index));
            return acc;
        }

        /// <summary>Ligne dépliable : pastille, nom, résumé, chevron ; le contenu détaillé va dans « forge-acc__body ».</summary>
        static VisualElement Accordion(bool open, Color color, string title, Action toggle, out Label name, out Label meta, out VisualElement dot)
        {
            var acc = new VisualElement();
            acc.AddToClassList("forge-acc");
            acc.EnableInClassList("forge-acc--open", open);
            var head = new VisualElement { tooltip = open ? "Replier." : "Afficher tous les réglages." };
            head.AddToClassList("forge-acc__head");
            head.RegisterCallback<ClickEvent>(_ => toggle());
            dot = Dot(color);
            head.Add(dot);
            name = ForgeUI.Label(title, "forge-acc__name");
            head.Add(name);
            meta = ForgeUI.Label(string.Empty, "forge-acc__meta");
            head.Add(meta);
            var chevron = ForgeUI.Label(open ? "▼" : "›", "forge-acc__chevron");
            head.Add(chevron);
            acc.Add(head);
            var body = new VisualElement();
            body.AddToClassList("forge-acc__body");
            body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            acc.Add(body);
            return acc;
        }

        VisualElement MechanicCard(SerializedProperty property, int index)
        {
            var mechanic = Draft.Mechanics[index];
            var card = Card(property, "Nom affiché au joueur dans la barre d'actions.", out var header);
            AddMechanicHeader(header, property, index);
            card.Add(Description(property, "Explication montrée au joueur."));

            var cost = Row();
            cost.Add(IdDropdown("Consomme", property.FindPropertyRelative("costStatId"),
                () => Draft.Stats.Select(s => (s.id, s.displayName)).ToList(), "Stat dépensée à chaque utilisation : une jauge (énergie, mana…) ou les PV.", "(gratuite)"));
            cost.Add(Bound(new FloatField("Par utilisation") { tooltip = "Quantité consommée à chaque utilisation." }, property.FindPropertyRelative("cost")));
            cost.Add(Bound(new FloatField("Recharge (s)") { tooltip = "Secondes avant de pouvoir la réutiliser." }, property.FindPropertyRelative("cooldown")));
            card.Add(cost);

            var labels = ProgressionEditing.ActionFields(mechanic.action);
            var settings = Row();
            if (labels.power != null)
                settings.Add(Bound(new FloatField(labels.power), property.FindPropertyRelative("power")));
            if (labels.count != null)
                settings.Add(Bound(new IntegerField(labels.count), property.FindPropertyRelative("count")));
            if (labels.duration != null)
                settings.Add(Bound(new FloatField(labels.duration), property.FindPropertyRelative("duration")));
            card.Add(settings);

            var unlock = Row();
            unlock.Add(Bound(new Toggle("Débloquée dès le début")
            {
                tooltip = "Sinon, ajoutez une récompense « Débloquer une mécanique » au niveau voulu (catégorie Récompenses)."
            }, property.FindPropertyRelative("unlockedAtStart")));
            card.Add(unlock);

            AddUpgrades(card, property, labels);
            card.Add(RankPreview(index));
            return card;
        }

        /// <summary>Action (qui remet les réglages par défaut), touche et suppression.</summary>
        void AddMechanicHeader(VisualElement header, SerializedProperty property, int index)
        {
            header.Add(EnumDropdown<MechanicAction>("Action", property.FindPropertyRelative("action"), ProgressionEditing.ActionLabel,
                "Ce que fait la mécanique. Changer d'action remet ses réglages par défaut.", action =>
                {
                    Serialized.Update();
                    var copy = (CharacterMechanic)Serialized.FindProperty(property.propertyPath).boxedValue;
                    ProgressionEditing.ApplyActionDefaults(copy, action);
                    Serialized.FindProperty(property.propertyPath).boxedValue = copy;
                    Serialized.ApplyModifiedProperties();
                    Rebuild();
                }));
            header.Add(EnumDropdown<MechanicInput>("Touche", property.FindPropertyRelative("input"), ProgressionEditing.InputLabel,
                "Touche qui la déclenche. Les clics peuvent être maintenus (tir continu)."));
            header.Add(SmallButton("Supprimer", "Supprime la mécanique et ses récompenses (Ctrl+Z pour annuler).", () =>
            {
                var used = Draft.Rewards.Count(r => r.mechanicId == Draft.Mechanics[index].id && r.type is RewardType.UnlockMechanic or RewardType.UpgradeMechanic);
                if (used > 0 && !ForgeDialogs.Confirm("Supprimer une mécanique", $"« {Draft.Mechanics[index].displayName} » est utilisée par {used} récompense(s) de niveau, " +
                        "qui seront supprimées aussi.", "Supprimer", "Annuler"))
                    return;
                ProgressionEditing.RemoveMechanic(Serialized, index);
                State.OpenMechanic = -1;
                Rebuild();
            }, true, "forge-button--danger"));
        }

        /// <summary>Améliorations : ce que chaque rang ajoute (ou retire) aux réglages, puis le bouton pour en ajouter.</summary>
        void AddUpgrades(VisualElement card, SerializedProperty property, (string power, string count, string duration) labels)
        {
            var upgrades = property.FindPropertyRelative("upgrades");
            for (var u = 0; u < upgrades.arraySize; u++)
            {
                var upgradeIndex = u;
                var upgrade = upgrades.GetArrayElementAtIndex(u);
                var row = Row();
                row.AddToClassList("forge-stat-card__sub");
                row.Add(ForgeUI.Label($"Rang {u + 2}", "forge-stat-card__rank-title"));
                if (labels.power != null)
                    row.Add(Bound(new FloatField($"{labels.power} +") { tooltip = "Ajouté à la valeur du rang précédent (négatif : retiré)." }, upgrade.FindPropertyRelative("power")));
                if (labels.count != null)
                    row.Add(Bound(new IntegerField($"{labels.count} +"), upgrade.FindPropertyRelative("count")));
                if (labels.duration != null)
                    row.Add(Bound(new FloatField($"{labels.duration} +"), upgrade.FindPropertyRelative("duration")));
                row.Add(Bound(new FloatField("Coût +") { tooltip = "Négatif pour la rendre moins chère." }, upgrade.FindPropertyRelative("cost")));
                row.Add(Bound(new FloatField("Recharge +") { tooltip = "Négatif pour la recharger plus vite." }, upgrade.FindPropertyRelative("cooldown")));
                row.Add(SmallButton("×", "Retirer cette amélioration (et les rangs suivants changent de numéro).", () =>
                {
                    ProgressionEditing.RemoveAt(Serialized, upgrades.propertyPath, upgradeIndex);
                    Rebuild();
                }, true));
                card.Add(row);
            }

            var footer = Row();
            footer.Add(SmallButton("+ Amélioration", "Ajoute un rang. Il s'obtient avec une récompense « Améliorer une mécanique ».", () =>
            {
                ProgressionEditing.Append(Serialized, upgrades.propertyPath, new MechanicUpgrade());
                Rebuild();
            }, upgrades.arraySize < 9));
            card.Add(footer);
        }

        /// <summary>Chiffres de chaque rang et niveau où il est obtenu.</summary>
        Label RankPreview(int index)
        {
            var preview = ForgeUI.Label(string.Empty, "forge-hint");
            preview.AddToClassList("forge-mono");
            AddRefresher(() =>
            {
                if (index >= Draft.Mechanics.Count)
                    return;
                var current = Draft.Mechanics[index];
                var lines = Enumerable.Range(1, current.MaxRank).Select(rank =>
                {
                    var level = Enumerable.Range(1, Draft.MaxLevel).FirstOrDefault(l => ProgressionEditing.RankAt(Draft, current, l) >= rank);
                    var when = level == 0 ? "jamais obtenu" : level == 1 && rank == 1 && current.unlockedAtStart ? "dès le début" : $"niveau {level}";
                    return $"[Rang {rank}, {when}] {ProgressionEditing.Describe(Draft, current, rank)}";
                });
                preview.text = string.Join("\n", lines);
            });
            return preview;
        }
    }
}
