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
        const string MissingRarity = "⚠ Rareté supprimée";
        const string AllTypes = "Tous les types";

        DropdownField rarityFilter;
        DropdownField typeFilter;
        VisualElement itemsView;
        RarityEditor rarityEditor;
        Button itemsButton;
        Button raritiesButton;
        RangeMeter priceMeter;
        VisualElement statBars;
        Label effectsSummary;

        public ItemsTab(IToolHost window) : base(window)
        {
            // Raretés modifiées (vue « Raretés ») : filtres, couleurs de la liste et fiche suivent.
            Action onRaritiesSaved = () =>
            {
                if (rarityFilter == null)
                    return;
                FillRarityFilter();
                Reload();
                RebuildDetails();
            };
            RarityEditor.Saved += onRaritiesSaved;
            window.Closing += () => RarityEditor.Saved -= onRaritiesSaved;
        }

        /// <summary>Deux vues : les objets (liste + fiche) et les raretés qu'ils utilisent.</summary>
        public override VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexGrow = 1;
            var switcher = new VisualElement();
            switcher.AddToClassList("forge-view-switch");
            // Sans cela, la colonne comprime le sélecteur et ses boutons débordent de leur capsule.
            switcher.style.flexShrink = 0;
            itemsButton = ForgeUI.Button("Objets", "Liste des objets et fiche de l'objet sélectionné.", ShowItems, "forge-view-switch__button");
            raritiesButton = ForgeUI.Button("Raretés", "Créer, renommer, réordonner ou supprimer les raretés.", ShowRarities, "forge-view-switch__button");
            switcher.Add(itemsButton);
            switcher.Add(raritiesButton);
            root.Add(switcher);

            itemsView = base.Build();
            itemsView.style.flexGrow = 1;
            root.Add(itemsView);
            rarityEditor = new RarityEditor(Window);
            rarityEditor.ShowRequested += ShowRarities;
            root.Add(rarityEditor);
            ShowItems();
            return root;
        }

        void ShowItems() => SetView(false);

        void ShowRarities()
        {
            rarityEditor.RefreshUsage();
            SetView(true);
        }

        void SetView(bool rarities)
        {
            itemsView.style.display = rarities ? DisplayStyle.None : DisplayStyle.Flex;
            rarityEditor.style.display = rarities ? DisplayStyle.Flex : DisplayStyle.None;
            itemsButton.EnableInClassList("forge-view-switch__button--active", !rarities);
            raritiesButton.EnableInClassList("forge-view-switch__button--active", rarities);
        }

        public override void ShowUnsaved() => ShowItems();

        public override void Reveal(UnityEngine.Object asset)
        {
            ShowItems();
            base.Reveal(asset);
        }

        protected override string NewAssetName => "Nouvel objet";
        protected override string Noun => "objet";

        protected override Sprite GetPreview(ItemData item) => item.Icon;
        protected override Color GetColor(ItemData item) => Limits.GetRarity(item.RarityId).color;

        protected override IEnumerable<VisualElement> GetBadges(ItemData item)
        {
            var rarity = Limits.GetRarity(item.RarityId);
            yield return ForgeUI.Badge(rarity.DisplayName, rarity.color, Limits.HasRarity(item.RarityId)
                ? "Rareté de l'objet."
                : "Cette rareté a été supprimée : choisissez-en une autre dans « Classement ».");
            yield return ForgeUI.Badge(item.Type.ToString(), new Color(0.7f, 0.7f, 0.7f), "Type d'objet.");
            yield return ForgeUI.Badge($"{item.Price} or", new Color(0.95f, 0.8f, 0.3f), "Prix d'achat.");
            var passives = item.PassiveEffects.Count();
            if (passives > 0)
                yield return ForgeUI.Badge($"{passives} passif(s)", new Color(0.65f, 0.59f, 0.75f), "Passifs de l'arme (voir « Effets »).");
        }

        protected override VisualElement BuildFilters()
        {
            rarityFilter = new DropdownField { tooltip = "N'affiche que les objets de cette rareté." };
            FillRarityFilter();
            typeFilter = new DropdownField(new List<string> { AllTypes }.Concat(Enum.GetNames(typeof(ItemType))).ToList(), 0)
                { tooltip = "N'affiche que les objets de ce type." };
            rarityFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            typeFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            // L'un sous l'autre : côte à côte, ils ne tiennent pas dans la largeur de la liste.
            var filters = new VisualElement();
            filters.Add(rarityFilter);
            filters.Add(typeFilter);
            return filters;
        }

        /// <summary>Choix du filtre : les raretés actuelles, plus « Rareté supprimée » quand des objets en ont une.</summary>
        void FillRarityFilter()
        {
            var choices = new List<string> { AllRarities };
            choices.AddRange(Limits.rarities.Select(rule => rule.DisplayName));
            if (AssetFactory.LoadAll<ItemData>().Any(item => !Limits.HasRarity(item.RarityId)))
                choices.Add(MissingRarity);
            var previous = rarityFilter.value;
            rarityFilter.choices = choices;
            rarityFilter.SetValueWithoutNotify(choices.Contains(previous) ? previous : AllRarities);
        }

        protected override bool PassesFilters(ItemData item)
        {
            var rarity = rarityFilter.value;
            var rarityOk = rarity == AllRarities ||
                           (rarity == MissingRarity ? !Limits.HasRarity(item.RarityId) : Limits.HasRarity(item.RarityId) && Limits.GetRarity(item.RarityId).DisplayName == rarity);
            return rarityOk && (typeFilter.value == AllTypes || typeFilter.value == item.Type.ToString());
        }

        protected override void InitializeNew(SerializedObject draft)
        {
            var rarity = Limits.FirstRarity;
            draft.FindProperty("rarity").intValue = rarity.id;
            draft.FindProperty("price").intValue = rarity.price.min;
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, ItemData item)
        {
            var rule = Limits.GetRarity(item.RarityId);

            var identity = ForgeUI.Section("Identité", container);
            identity.Add(ForgeUI.Text("Nom", "displayName", "Nom affiché au joueur. Le fichier sera nommé ITM_<Nom> automatiquement."));
            identity.Add(new SpriteField("Icône", serializedObject.FindProperty("icon"), "Items", "Icône affichée dans l'inventaire. Cliquez pour ouvrir la galerie."));
            identity.Add(ForgeUI.Text("Description", "description", "Texte de l'infobulle en jeu.", multiline: true));

            var classification = ForgeUI.Section("Classement", container);
            var rarityField = new RarityField("Rareté", serializedObject.FindProperty("rarity"),
                "Détermine la couleur, le prix autorisé, le bonus maximum et le nombre de passifs d'une arme.");
            // Prix, bonus et passifs autorisés dépendent de la rareté : la fiche est reconstruite avec les nouvelles bornes.
            rarityField.Changed += _ => container.schedule.Execute(RebuildDetails);
            classification.Add(rarityField);
            // Dans l'en-tête, comme « + Ajouter » : le champ reste aligné avec les autres.
            classification.Q(className: "forge-section__header").Add(
                ForgeUI.Button("Gérer les raretés", "Crée, renomme, réordonne ou supprime les raretés (vue « Raretés »).", ShowRarities));
            classification.Add(ForgeUI.Enum<ItemType>("Type", "type", "Catégorie de l'objet. Seules les armes ont des passifs."));
            // Les effets proposés dépendent du type.
            classification.TrackPropertyValue(serializedObject.FindProperty("type"), _ => container.schedule.Execute(RebuildDetails));
            classification.Add(ForgeUI.Slider("Prix", "price", rule.price, "Prix d'achat en pièces d'or."));
            priceMeter = new RangeMeter("Position du prix");
            classification.Add(priceMeter);

            var statRange = new IntRange(-rule.maxStatValue, rule.maxStatValue);
            var stats = ForgeLists.Array(serializedObject, "stats", "Bonus de statistiques", "Bonus appliqués quand l'objet est équipé.",
                Limits.maxStatsPerItem,
                (element, _) =>
                {
                    var stat = new EnumField(StatType.Health) { tooltip = "Statistique modifiée. Chaque statistique ne doit apparaître qu'une fois." };
                    stat.BindProperty(element.FindPropertyRelative("stat"));
                    stat.style.width = 110;
                    stat.style.marginRight = 12;
                    var value = new SliderInt(statRange.min, statRange.max) { showInputField = true, tooltip = $"Valeur du bonus, bornée selon la rareté ({statRange})." };
                    value.BindProperty(element.FindPropertyRelative("value"));
                    value.style.flexGrow = 1;
                    return ForgeLists.Row(stat, value);
                },
                element => InitializeStat(serializedObject, element));
            // La puissance de chaque bonus, en couleur, dans le même bloc que les bonus.
            statBars = new VisualElement { tooltip = "Puissance de chaque bonus par rapport au maximum de la rareté." };
            statBars.style.marginTop = 12;
            stats.Add(statBars);
            container.Add(stats);

            container.Add(BuildEffects(serializedObject, item, rule));

            // Arme : attaque (touche de tir) et compétence, faites dans l'outil Attaques.
            if (item.Type == ItemType.Weapon)
            {
                var attacks = ForgeUI.Section("Attaques de l'arme", container,
                    "Faites dans l'outil Attaques (coups, combos, attaques chargées, compétences). Sans attaque, l'arme tire le projectile de base.");
                attacks.Add(new AssetPicker<AttackData>("Attaque", serializedObject.FindProperty("attack"),
                    "Jouée par la touche de tir (mécanique « projectile visé »).", a => a.User == AttackUser.Weapon));
                attacks.Add(new AssetPicker<AttackData>("Compétence", serializedObject.FindProperty("skill"),
                    "Jouée par la mécanique « Compétence de l'arme » (outil Progression > Mécaniques).", a => a.User == AttackUser.Skill));
            }
        }

        VisualElement BuildEffects(SerializedObject serializedObject, ItemData item, RarityRule rule)
        {
            var itemType = item.Type;
            var passiveSlots = EffectInfo.CanHavePassives(itemType) ? rule.passiveSlots : 0;
            var effects = ForgeLists.Array(serializedObject, "effects", "Effets",
                "Actif : à l'impact (arme) ou à l'utilisation (consommable). Passif : permanent tant que l'arme est équipée.",
                Limits.maxEffectsPerItem + passiveSlots,
                (element, _) => EffectRow(element, itemType),
                element => InitializeEffect(serializedObject, element, itemType, passiveSlots));
            effectsSummary = ForgeUI.Label(string.Empty, "forge-hint");
            effects.Insert(1, effectsSummary);
            return effects;
        }

        /// <summary>Une ligne d'effet : déclenchement (armes), effet proposé selon le type d'objet, puis valeur et durée si elles servent.</summary>
        VisualElement EffectRow(SerializedProperty element, ItemType itemType)
        {
            var row = new VisualElement();
            var serializedObject = element.serializedObject;
            var path = element.propertyPath;

            void Fill()
            {
                row.Clear();
                var property = serializedObject.FindProperty(path);
                if (property == null)
                    return;
                var triggerProperty = property.FindPropertyRelative("trigger");
                var typeProperty = property.FindPropertyRelative("type");
                var trigger = (EffectTrigger)triggerProperty.enumValueIndex;
                var type = (EffectType)typeProperty.enumValueIndex;

                var head = new VisualElement();
                head.AddToClassList("forge-row");
                if (EffectInfo.CanHavePassives(itemType) || trigger == EffectTrigger.Passive)
                {
                    var triggerField = new DropdownField(new List<string> { "Actif", "Passif" }, (int)trigger)
                    {
                        tooltip = "Actif : à l'impact. Passif : permanent tant que l'arme est équipée (nombre limité par la rareté)."
                    };
                    triggerField.AddToClassList("forge-effect-trigger");
                    triggerField.style.width = 96;
                    triggerField.style.flexGrow = 0;
                    triggerField.EnableInClassList("forge-effect-trigger--passive", trigger == EffectTrigger.Passive);
                    triggerField.RegisterValueChangedCallback(evt =>
                    {
                        var next = evt.newValue == "Passif" ? EffectTrigger.Passive : EffectTrigger.Active;
                        var p = serializedObject.FindProperty(path);
                        p.FindPropertyRelative("trigger").enumValueIndex = (int)next;
                        // L'effet choisi doit exister pour ce déclenchement.
                        var allowedNow = EffectInfo.Allowed(itemType, next);
                        if (allowedNow.Count > 0 && !allowedNow.Contains((EffectType)p.FindPropertyRelative("type").enumValueIndex))
                            p.FindPropertyRelative("type").enumValueIndex = (int)allowedNow[0];
                        serializedObject.ApplyModifiedProperties();
                    });
                    head.Add(triggerField);
                }

                var allowed = EffectInfo.Allowed(itemType, trigger).ToList();
                var options = allowed.ToList();
                if (!options.Contains(type))
                    options.Add(type);
                var labels = options.Select(t => allowed.Contains(t) ? EffectInfo.Label(t) : $"⚠ {EffectInfo.Label(t)} (sans effet ici)").ToList();
                var typeField = new DropdownField(labels, options.IndexOf(type)) { tooltip = EffectInfo.Description(type, trigger) };
                typeField.style.flexGrow = 1;
                typeField.RegisterValueChangedCallback(evt =>
                {
                    var index = labels.IndexOf(evt.newValue);
                    if (index < 0)
                        return;
                    serializedObject.FindProperty(path).FindPropertyRelative("type").enumValueIndex = (int)options[index];
                    serializedObject.ApplyModifiedProperties();
                });
                head.Add(typeField);
                row.Add(head);
                row.Add(ForgeUI.Label(EffectInfo.Description(type, trigger), "forge-hint"));

                if (EffectInfo.UsesValue(type))
                {
                    var value = ForgeUI.Slider($"Valeur ({EffectInfo.Unit(type, trigger)})", null, Limits.effectValue, EffectInfo.Description(type, trigger));
                    value.BindProperty(property.FindPropertyRelative("value"));
                    row.Add(value);
                }
                if (EffectInfo.UsesDuration(type, trigger))
                {
                    var duration = ForgeUI.Slider("Durée (s)", null, Limits.effectDuration, "0 = instantané.");
                    duration.BindProperty(property.FindPropertyRelative("duration"));
                    row.Add(duration);
                }
            }

            Fill();
            // Changer le déclenchement ou l'effet change les champs utiles : la ligne se redessine.
            row.TrackPropertyValue(element.FindPropertyRelative("trigger"), _ => row.schedule.Execute(Fill));
            row.TrackPropertyValue(element.FindPropertyRelative("type"), _ => row.schedule.Execute(Fill));
            return row;
        }

        /// <summary>Un nouvel effet est actif s'il reste de la place, sinon passif (armes) ; il prend le premier effet proposé.</summary>
        bool InitializeEffect(SerializedObject serializedObject, SerializedProperty element, ItemType itemType, int passiveSlots)
        {
            var effects = serializedObject.FindProperty("effects");
            int active = 0, passive = 0;
            for (var i = 0; i < effects.arraySize - 1; i++)
            {
                if (effects.GetArrayElementAtIndex(i).FindPropertyRelative("trigger").enumValueIndex == (int)EffectTrigger.Passive)
                    passive++;
                else
                    active++;
            }
            EffectTrigger trigger;
            if (active < Limits.maxEffectsPerItem)
                trigger = EffectTrigger.Active;
            else if (passive < passiveSlots)
                trigger = EffectTrigger.Passive;
            else
            {
                ForgeLists.Notify(passiveSlots > 0 ? "Effets actifs et passifs au maximum." : "Effets au maximum.");
                return false;
            }
            var allowed = EffectInfo.Allowed(itemType, trigger);
            element.FindPropertyRelative("trigger").enumValueIndex = (int)trigger;
            element.FindPropertyRelative("type").enumValueIndex = (int)(allowed.Count > 0 ? allowed[0] : EffectType.Heal);
            element.FindPropertyRelative("value").floatValue = 10f;
            element.FindPropertyRelative("duration").floatValue = trigger == EffectTrigger.Active ? 3f : 0f;
            return true;
        }

        protected override void OnRefresh(ItemData item)
        {
            var rule = Limits.GetRarity(item.RarityId);
            var scaleMax = Limits.rarities.Count > 0 ? Limits.rarities.Max(r => r.price.max) : rule.price.max;
            priceMeter.SetValue(item.Price, rule.price.min, rule.price.max, scaleMax, "or");

            BarList.Fill(statBars, item.Stats.Select(stat => new BarList.Bar(
                    stat.stat.ToString(), Mathf.Abs(stat.value), $"{stat.value:+0;-0} / {rule.maxStatValue}",
                    Mathf.Abs(stat.value) > rule.maxStatValue ? ChartColors.Critical : ChartColors.Series((int)stat.stat),
                    tooltip: $"{stat.stat} : {stat.value:+0;-0} (maximum {rule.maxStatValue} pour un objet {rule.DisplayName})")),
                "Aucun bonus de statistique.", Mathf.Max(1, rule.maxStatValue));

            var active = item.ActiveEffects.Count();
            var passives = item.PassiveEffects.Count();
            var text = $"Actifs : {active}/{Limits.maxEffectsPerItem}";
            if (EffectInfo.CanHavePassives(item.Type))
                text += $"  ·  Passifs : {passives}/{rule.passiveSlots} (arme {rule.DisplayName})";
            else if (passives > 0)
                text += $"  ·  {passives} passif(s) sans effet : seules les armes en ont";
            effectsSummary.text = text;
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
