using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    public sealed class ItemsTab : AssetListTab<ItemData>
    {
        const string AllRarities = "Toutes les raretés";
        const string AllTypes = "Tous les types";

        DropdownField rarityFilter;
        DropdownField typeFilter;

        protected override string NewAssetName => "Nouvel objet";
        protected override string Noun => "objet";

        protected override string GetLabel(ItemData item) => string.IsNullOrWhiteSpace(item.DisplayName) ? item.name : item.DisplayName;
        protected override Sprite GetPreview(ItemData item) => item.Icon;
        protected override Color GetColor(ItemData item) => Limits.GetRarity(item.Rarity).color;

        protected override IEnumerable<VisualElement> GetBadges(ItemData item)
        {
            yield return ForgeUI.Badge(item.Rarity.ToString(), GetColor(item), "Rareté de l'objet.");
            yield return ForgeUI.Badge(item.Type.ToString(), new Color(0.7f, 0.7f, 0.7f), "Type d'objet.");
            yield return ForgeUI.Badge($"{item.Price} or", new Color(0.95f, 0.8f, 0.3f), "Prix d'achat.");
        }

        protected override VisualElement BuildFilters()
        {
            rarityFilter = new DropdownField(new List<string> { AllRarities }.Concat(System.Enum.GetNames(typeof(Rarity))).ToList(), 0)
                { tooltip = "N'affiche que les objets de cette rareté." };
            typeFilter = new DropdownField(new List<string> { AllTypes }.Concat(System.Enum.GetNames(typeof(ItemType))).ToList(), 0)
                { tooltip = "N'affiche que les objets de ce type." };
            rarityFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            typeFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            return ForgeLists.Row(rarityFilter, typeFilter);
        }

        protected override bool PassesFilters(ItemData item) =>
            (rarityFilter.value == AllRarities || rarityFilter.value == item.Rarity.ToString()) &&
            (typeFilter.value == AllTypes || typeFilter.value == item.Type.ToString());

        protected override void OnCreated(ItemData item)
        {
            var serialized = new SerializedObject(item);
            serialized.FindProperty("price").intValue = Limits.GetRarity(Rarity.Common).price.min;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, ItemData item)
        {
            var identity = ForgeUI.Section("Identité", container);
            var nameField = ForgeUI.Text("Nom", "displayName", "Nom affiché au joueur. Le fichier est renommé automatiquement (ITM_…) quand vous validez.");
            nameField.isDelayed = true;
            nameField.RegisterValueChangedCallback(_ => ScheduleRename(item));
            identity.Add(nameField);
            identity.Add(ForgeUI.Object<Sprite>("Icône", "icon", "Glissez un sprite depuis la fenêtre Project."));
            identity.Add(ForgeUI.Text("Description", "description", "Texte de l'infobulle en jeu.", multiline: true));

            var classification = ForgeUI.Section("Classement", container);
            var rarityField = ForgeUI.Enum<Rarity>("Rareté", "rarity", "Détermine la couleur, le prix autorisé et le bonus maximum.");
            classification.Add(rarityField);
            classification.Add(ForgeUI.Enum<ItemType>("Type", "type", "Catégorie de l'objet."));
            var priceSlider = ForgeUI.Slider("Prix", "price", Limits.GetRarity(item.Rarity).price, "Prix d'achat en pièces d'or.");
            classification.Add(priceSlider);

            var statRange = new IntRange(-Limits.GetRarity(item.Rarity).maxStatValue, Limits.GetRarity(item.Rarity).maxStatValue);
            var stats = ForgeLists.Bound(serializedObject, "stats", "Bonus de statistiques", "Bonus appliqués quand l'objet est équipé.",
                Limits.maxStatsPerItem, () =>
                {
                    var slider = ForgeUI.Slider("", "value", statRange, "Valeur du bonus, bornée selon la rareté.");
                    slider.AddToClassList("forge-stat-slider");
                    slider.style.flexGrow = 1;
                    var stat = ForgeUI.Enum<StatType>("", "stat", "Statistique modifiée.");
                    stat.style.width = 110;
                    return ForgeLists.Row(stat, slider);
                },
                element => element.FindPropertyRelative("value").intValue = 1);
            container.Add(stats);

            var effects = ForgeLists.Bound(serializedObject, "effects", "Effets", "Effets spéciaux déclenchés par l'objet.",
                Limits.maxEffectsPerItem, () =>
                {
                    var type = ForgeUI.Enum<EffectType>("", "type", "Type d'effet.");
                    type.style.width = 110;
                    var value = ForgeUI.Slider("Valeur", "value", Limits.effectValue, "Puissance de l'effet.");
                    var duration = ForgeUI.Slider("Durée (s)", "duration", Limits.effectDuration, "0 = instantané.");
                    var row = new VisualElement();
                    row.Add(type);
                    row.Add(value);
                    row.Add(duration);
                    return row;
                });
            container.Add(effects);

            // Quand la rareté change, les bornes du prix et des bonus suivent.
            rarityField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is not Rarity rarity)
                    return;
                var rule = Limits.GetRarity(rarity);
                priceSlider.lowValue = rule.price.min;
                priceSlider.highValue = rule.price.max;
                priceSlider.tooltip = $"Prix d'achat en pièces d'or.\nValeurs autorisées : {rule.price}.";
                statRange = new IntRange(-rule.maxStatValue, rule.maxStatValue);
                container.Query<SliderInt>(className: "forge-stat-slider").ForEach(slider =>
                {
                    slider.lowValue = statRange.min;
                    slider.highValue = statRange.max;
                });
            });
        }
    }
}
