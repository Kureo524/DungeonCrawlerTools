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

namespace TheDungeon.Editor.ContentDatabase
{
    public sealed class ItemsTab : AssetListTab<ItemData>
    {
        const string AllRarities = "Toutes les raretés";
        const string AllTypes = "Tous les types";

        DropdownField rarityFilter;
        DropdownField typeFilter;
        RangeMeter priceMeter;
        VisualElement statBars;

        public ItemsTab(IToolHost window) : base(window)
        {
        }

        protected override string NewAssetName => "Nouvel objet";
        protected override string Noun => "objet";

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
            rarityFilter = new DropdownField(new List<string> { AllRarities }.Concat(Enum.GetNames(typeof(Rarity))).ToList(), 0)
                { tooltip = "N'affiche que les objets de cette rareté." };
            typeFilter = new DropdownField(new List<string> { AllTypes }.Concat(Enum.GetNames(typeof(ItemType))).ToList(), 0)
                { tooltip = "N'affiche que les objets de ce type." };
            rarityFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            typeFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            return ForgeLists.Row(rarityFilter, typeFilter);
        }

        protected override bool PassesFilters(ItemData item) =>
            (rarityFilter.value == AllRarities || rarityFilter.value == item.Rarity.ToString()) &&
            (typeFilter.value == AllTypes || typeFilter.value == item.Type.ToString());

        protected override void InitializeNew(SerializedObject draft)
        {
            draft.FindProperty("price").intValue = Limits.GetRarity(Rarity.Common).price.min;
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, ItemData item)
        {
            var identity = ForgeUI.Section("Identité", container);
            identity.Add(ForgeUI.Text("Nom", "displayName", "Nom affiché au joueur. Le fichier sera nommé ITM_<Nom> automatiquement."));
            identity.Add(new SpriteField("Icône", serializedObject.FindProperty("icon"), "Items", "Icône affichée dans l'inventaire. Cliquez pour ouvrir la galerie."));
            identity.Add(ForgeUI.Text("Description", "description", "Texte de l'infobulle en jeu.", multiline: true));

            var classification = ForgeUI.Section("Classement", container);
            var rarityField = ForgeUI.Enum<Rarity>("Rareté", "rarity", "Détermine la couleur, le prix autorisé et le bonus maximum.");
            classification.Add(rarityField);
            classification.Add(ForgeUI.Enum<ItemType>("Type", "type", "Catégorie de l'objet."));
            var priceSlider = ForgeUI.Slider("Prix", "price", Limits.GetRarity(item.Rarity).price, "Prix d'achat en pièces d'or.");
            classification.Add(priceSlider);
            priceMeter = new RangeMeter("Position du prix");
            classification.Add(priceMeter);

            var statRange = StatRange(item.Rarity);
            container.Add(ForgeLists.Array(serializedObject, "stats", "Bonus de statistiques", "Bonus appliqués quand l'objet est équipé.",
                Limits.maxStatsPerItem,
                (element, _) =>
                {
                    var stat = new EnumField(StatType.Health) { tooltip = "Statistique modifiée. Chaque statistique ne doit apparaître qu'une fois." };
                    stat.BindProperty(element.FindPropertyRelative("stat"));
                    stat.style.width = 110;
                    var value = new SliderInt(statRange.min, statRange.max) { showInputField = true, tooltip = $"Valeur du bonus, bornée selon la rareté ({statRange})." };
                    value.BindProperty(element.FindPropertyRelative("value"));
                    value.AddToClassList("forge-stat-slider");
                    value.style.flexGrow = 1;
                    return ForgeLists.Row(stat, value);
                },
                element => InitializeStat(serializedObject, element)));

            var budget = ForgeUI.Section("Puissance des bonus (par rapport au maximum de la rareté)", container);
            statBars = new VisualElement();
            budget.Add(statBars);

            container.Add(ForgeLists.Array(serializedObject, "effects", "Effets", "Effets spéciaux déclenchés par l'objet.",
                Limits.maxEffectsPerItem,
                (element, _) =>
                {
                    var row = new VisualElement();
                    var type = new EnumField("Effet", EffectType.Heal) { tooltip = "Type d'effet." };
                    type.BindProperty(element.FindPropertyRelative("type"));
                    var value = ForgeUI.Slider("Valeur", null, Limits.effectValue, "Puissance de l'effet.");
                    value.BindProperty(element.FindPropertyRelative("value"));
                    var duration = ForgeUI.Slider("Durée (s)", null, Limits.effectDuration, "0 = instantané.");
                    duration.BindProperty(element.FindPropertyRelative("duration"));
                    row.Add(type);
                    row.Add(value);
                    row.Add(duration);
                    return row;
                }));

            // Quand la rareté change, les bornes du prix et des bonus suivent.
            rarityField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is not Rarity rarity)
                    return;
                var rule = Limits.GetRarity(rarity);
                priceSlider.lowValue = rule.price.min;
                priceSlider.highValue = rule.price.max;
                priceSlider.tooltip = $"Prix d'achat en pièces d'or.\nValeurs autorisées : {rule.price}.";
                statRange = StatRange(rarity);
                container.Query<SliderInt>(className: "forge-stat-slider").ForEach(slider =>
                {
                    slider.lowValue = statRange.min;
                    slider.highValue = statRange.max;
                });
            });
        }

        protected override void OnRefresh(ItemData item)
        {
            var rule = Limits.GetRarity(item.Rarity);
            var legendaryMax = Limits.GetRarity(Rarity.Legendary).price.max;
            priceMeter.SetValue(item.Price, rule.price.min, rule.price.max, legendaryMax, "or");

            BarList.Fill(statBars, item.Stats.Select(stat => new BarList.Bar(
                    stat.stat.ToString(), Mathf.Abs(stat.value), $"{stat.value:+0;-0} / {rule.maxStatValue}",
                    Mathf.Abs(stat.value) > rule.maxStatValue ? ChartColors.Critical : ChartColors.Series((int)stat.stat),
                    tooltip: $"{stat.stat} : {stat.value:+0;-0} (maximum {rule.maxStatValue} pour un objet {item.Rarity})")),
                "Aucun bonus de statistique.", rule.maxStatValue);
        }

        IntRange StatRange(Rarity rarity)
        {
            var max = Limits.GetRarity(rarity).maxStatValue;
            return new IntRange(-max, max);
        }

        /// <summary>Une nouvelle ligne prend une statistique pas encore utilisée, pour éviter les doublons.</summary>
        static bool InitializeStat(SerializedObject serializedObject, SerializedProperty element)
        {
            var stats = serializedObject.FindProperty("stats");
            var used = new HashSet<int>();
            for (var i = 0; i < stats.arraySize - 1; i++)
                used.Add(stats.GetArrayElementAtIndex(i).FindPropertyRelative("stat").enumValueIndex);

            var free = Enumerable.Range(0, Enum.GetValues(typeof(StatType)).Length).Where(index => !used.Contains(index)).DefaultIfEmpty(-1).First();
            if (free < 0)
            {
                ForgeLists.Notify("Toutes les statistiques ont déjà un bonus.");
                return false;
            }
            element.FindPropertyRelative("stat").enumValueIndex = free;
            element.FindPropertyRelative("value").intValue = 1;
            return true;
        }
    }
}
