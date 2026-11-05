using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static TheDungeon.Editor.Progression.ProgressionFields;

namespace TheDungeon.Editor.Progression
{
    /// <summary>Attributs (points répartis par le joueur, avec seuil de rendement) et compétences (une valeur par rang).</summary>
    internal sealed class ProgressionAttributesView : ProgressionView
    {
        public ProgressionAttributesView(ProgressionContext context) : base(context)
        {
        }

        public override void Build(VisualElement parent, PlayerProgression draft)
        {
            BuildAttributes(parent, draft);
            BuildSkills(parent, draft);
        }

        #region Attributs

        void BuildAttributes(VisualElement parent, PlayerProgression draft)
        {
            var section = ForgeUI.Section("Attributs (points à répartir)", parent,
                "Le joueur gagne des points avec les récompenses de niveau et les répartit lui-même en se reposant à un feu de camp, comme dans Elden Ring. " +
                "Chaque point améliore une ou plusieurs stats ; au-delà du seuil, un point rapporte moins.");
            var summary = ForgeUI.Label(string.Empty, "forge-hint");
            AddRefresher(() =>
            {
                var earned = ProgressionEditing.TotalAttributePoints(Draft);
                var capacity = ProgressionEditing.AttributeCapacity(Draft);
                summary.text = $"{earned} point(s) gagné(s) sur toute la partie · {capacity} point(s) dépensable(s) au maximum (somme des maximums)." +
                               (earned > capacity ? "  <color=#e0606a>Le joueur gagnera des points qu'il ne pourra pas dépenser.</color>" : string.Empty);
            });
            section.Add(summary);

            var attributes = Serialized.FindProperty("attributes");
            for (var i = 0; i < attributes.arraySize; i++)
                section.Add(AttributeCard(attributes.GetArrayElementAtIndex(i), i));

            var add = ForgeUI.Button("+ Ajouter un attribut", $"Maximum {Limits.maxAttributes} (la fiche du jeu n'en affiche pas plus).", () =>
            {
                var attribute = new ProgressionAttribute { displayName = UniqueName(Draft.Attributes.Select(a => a.displayName), "Nouvel attribut"), color = NextColor(Draft.Attributes.Count + 3) };
                if (Draft.Stats.Count > 0)
                    attribute.bonuses.Add(new AttributeBonus { statId = Draft.Stats[0].id, perPoint = 1f });
                ProgressionEditing.AddAttribute(Serialized, attribute);
                Rebuild();
            });
            add.SetEnabled(draft.Attributes.Count < Limits.maxAttributes);
            add.style.alignSelf = Align.FlexStart;
            section.Add(add);
        }

        VisualElement AttributeCard(SerializedProperty property, int index)
        {
            var card = Card(property, "Nom affiché au joueur dans sa fiche (Vigueur, Force…).", out var header);
            var maxPoints = new IntegerField("Points max") { tooltip = "Nombre maximum de points investis dans cet attribut." };
            maxPoints.BindProperty(property.FindPropertyRelative("maxPoints"));
            maxPoints.RegisterValueChangedCallback(e =>
            {
                if (e.newValue < 1)
                    maxPoints.value = 1;
            });
            header.Add(maxPoints);
            header.Add(SmallButton("Supprimer", "Supprime l'attribut (Ctrl+Z pour annuler).", () =>
            {
                ProgressionEditing.RemoveAt(Serialized, "attributes", index);
                Rebuild();
            }, true, "forge-button--danger"));
            card.Add(Description(property, "Explication montrée au joueur quand il répartit ses points."));

            var bonuses = property.FindPropertyRelative("bonuses");
            for (var b = 0; b < bonuses.arraySize; b++)
                card.Add(BonusRow(bonuses, b));
            card.Add(AttributeFooter(bonuses, index));
            return card;
        }

        /// <summary>Stat améliorée par chaque point de l'attribut, gain par point et rendement après le seuil.</summary>
        VisualElement BonusRow(SerializedProperty bonuses, int bonusIndex)
        {
            var bonus = bonuses.GetArrayElementAtIndex(bonusIndex);
            var row = Row();
            row.AddToClassList("forge-stat-card__sub");
            row.Add(IdDropdown("Stat", bonus.FindPropertyRelative("statId"), () => Draft.Stats.Select(s => (s.id, s.displayName)).ToList(),
                "Stat améliorée par chaque point."));
            row.Add(Bound(new FloatField("Par point") { tooltip = "Gain par point investi, jusqu'au seuil." }, bonus.FindPropertyRelative("perPoint")));
            row.Add(Bound(new IntegerField("Seuil") { tooltip = "À partir de ce nombre de points, le gain diminue (0 = jamais)." }, bonus.FindPropertyRelative("softCap")));
            row.Add(Bound(new Slider("Après le seuil", 0f, 1f) { showInputField = true, tooltip = "Part du gain conservée au-delà du seuil (0,5 = moitié)." },
                bonus.FindPropertyRelative("afterSoftCap")));
            row.Add(SmallButton("×", "Retirer ce bonus.", () =>
            {
                Serialized.Update();
                Serialized.FindProperty(bonuses.propertyPath).DeleteArrayElementAtIndex(bonusIndex);
                Serialized.ApplyModifiedProperties();
                Rebuild();
            }, true));
            return row;
        }

        /// <summary>Ajout d'un bonus et aperçu des gains à mi-parcours et au maximum.</summary>
        VisualElement AttributeFooter(SerializedProperty bonuses, int index)
        {
            var footer = Row();
            footer.Add(SmallButton("+ Bonus sur une stat", "Chaque point de l'attribut améliorera aussi cette stat.", () =>
            {
                ProgressionEditing.Append(Serialized, bonuses.propertyPath,
                    new AttributeBonus { statId = Draft.Stats.Count > 0 ? Draft.Stats[0].id : string.Empty, perPoint = 1f });
                Rebuild();
            }, Draft.Stats.Count > 0));
            var preview = ForgeUI.Label(string.Empty, "forge-hint");
            preview.style.flexGrow = 1;
            footer.Add(preview);
            AddRefresher(() =>
            {
                if (index >= Draft.Attributes.Count)
                    return;
                var current = Draft.Attributes[index];
                var half = Mathf.Max(1, current.maxPoints / 2);
                preview.text = $"À {half} pts : {Gains(current, half)}   ·   À {current.maxPoints} pts (max) : {Gains(current, current.maxPoints)}";
            });
            return footer;
        }

        string Gains(ProgressionAttribute attribute, int points)
        {
            var gains = attribute.bonuses.Select(b => (b, stat: Draft.FindStat(b.statId))).Where(p => p.stat != null)
                .Select(p => $"{p.stat.displayName} +{p.stat.Format(p.b.Total(points))}").ToList();
            return gains.Count > 0 ? string.Join(", ", gains) : "aucun effet";
        }

        #endregion

        #region Compétences

        void BuildSkills(VisualElement parent, PlayerProgression draft)
        {
            var section = ForgeUI.Section("Compétences", parent,
                "Débloquées puis améliorées par les récompenses de niveau. Une valeur par rang : le rang 1 est obtenu au déblocage, " +
                "chaque amélioration fait passer au rang suivant.");
            var skills = Serialized.FindProperty("skills");
            for (var i = 0; i < skills.arraySize; i++)
                section.Add(SkillCard(skills.GetArrayElementAtIndex(i), i));

            var add = ForgeUI.Button("+ Ajouter une compétence", $"Maximum {Limits.maxSkills}.", () =>
            {
                ProgressionEditing.AddSkill(Serialized, ProgressionEditing.Skill(UniqueName(Draft.Skills.Select(s => s.displayName), "Nouvelle compétence"),
                    SkillEffect.None, NextColor(Draft.Skills.Count + 5), 1f));
                Rebuild();
            });
            add.SetEnabled(draft.Skills.Count < Limits.maxSkills);
            add.style.alignSelf = Align.FlexStart;
            section.Add(add);
        }

        VisualElement SkillCard(SerializedProperty property, int index)
        {
            var card = Card(property, "Nom affiché au joueur.", out var header);
            header.Add(EnumDropdown<SkillEffect>("Effet", property.FindPropertyRelative("effect"), ProgressionLabels.SkillEffectLabel,
                "Ce que fait la compétence en jeu. « Aucun » : la description seule est montrée au joueur."));
            header.Add(SmallButton("Supprimer", "Supprime la compétence et ses récompenses (Ctrl+Z pour annuler).", () =>
            {
                var skill = Draft.Skills[index];
                var used = Draft.Rewards.Count(r => r.skillId == skill.id && r.type is RewardType.UnlockSkill or RewardType.UpgradeSkill);
                if (used > 0 && !ForgeDialogs.Confirm("Supprimer une compétence", $"« {skill.displayName} » est utilisée par {used} récompense(s) de niveau, " +
                        "qui seront supprimées aussi.", "Supprimer", "Annuler"))
                    return;
                ProgressionEditing.RemoveSkill(Serialized, index);
                Rebuild();
            }, true, "forge-button--danger"));
            card.Add(Description(property, "Explication montrée au joueur."));
            card.Add(RankFields(property.FindPropertyRelative("ranks")));

            var preview = ForgeUI.Label(string.Empty, "forge-hint");
            card.Add(preview);
            AddRefresher(() =>
            {
                if (index >= Draft.Skills.Count)
                    return;
                var skill = Draft.Skills[index];
                var obtained = Enumerable.Range(1, skill.MaxRank)
                    .Select(rank => (rank, level: Enumerable.Range(1, Draft.MaxLevel).FirstOrDefault(l => ProgressionEditing.RankAt(Draft, skill, l) >= rank)))
                    .Select(p => p.level > 0 ? $"rang {p.rank} au niveau {p.level} ({skill.EffectText(p.rank)})" : $"rang {p.rank} jamais obtenu");
                preview.text = string.Join("  ·  ", obtained);
            });
            return card;
        }

        /// <summary>Une valeur par rang, et les boutons pour ajouter ou retirer le dernier rang.</summary>
        VisualElement RankFields(SerializedProperty ranks)
        {
            var row = Row();
            for (var r = 0; r < ranks.arraySize; r++)
            {
                var field = Bound(new FloatField($"Rang {r + 1}") { tooltip = r == 0 ? "Valeur au déblocage." : $"Valeur après {r} amélioration(s)." },
                    ranks.GetArrayElementAtIndex(r));
                field.AddToClassList("forge-stat-card__rank");
                row.Add(field);
            }
            row.Add(SmallButton("+ rang", "Ajoute un rang (une amélioration possible de plus).", () =>
            {
                Serialized.Update();
                var list = Serialized.FindProperty(ranks.propertyPath);
                var last = list.arraySize > 0 ? list.GetArrayElementAtIndex(list.arraySize - 1).floatValue : 1f;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).floatValue = last;
                Serialized.ApplyModifiedProperties();
                Rebuild();
            }, ranks.arraySize < 10));
            row.Add(SmallButton("- rang", "Retire le dernier rang.", () =>
            {
                Serialized.Update();
                Serialized.FindProperty(ranks.propertyPath).arraySize--;
                Serialized.ApplyModifiedProperties();
                Rebuild();
            }, ranks.arraySize > 1));
            return row;
        }

        #endregion
    }
}
