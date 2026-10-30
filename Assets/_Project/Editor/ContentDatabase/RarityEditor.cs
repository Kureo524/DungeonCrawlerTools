using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Vue « Raretés » de l'outil Objets : créer, renommer, réordonner, régler ou supprimer une rareté. Comme les fiches,
    /// les modifications portent sur une copie de travail et ne sont enregistrées qu'à « Valider ». Les objets gardent
    /// l'identifiant de leur rareté : après une suppression, ceux qui l'utilisaient sont en erreur jusqu'à ce qu'on leur en choisisse une autre.
    /// </summary>
    public sealed class RarityEditor : VisualElement, IUnsavedSource
    {
        /// <summary>L'indicateur des modifications demande à afficher la vue « Raretés ».</summary>
        public event Action ShowRequested;

        public string UnsavedLabel => "Raretés des objets";

        public void ShowUnsaved() => ShowRequested?.Invoke();

        /// <summary>Raretés enregistrées : les outils rechargent leurs listes.</summary>
        public static event Action Saved;

        readonly IToolHost host;
        List<RarityRule> working;
        Dictionary<int, int> usage = new();
        readonly VisualElement cards;
        readonly ScrollView scroll;
        readonly ForgeReorder reorder;
        readonly IssuesPanel issuesPanel;
        readonly ValidationFooter footer;
        readonly Label orphanBanner;

        static ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        public RarityEditor(IToolHost host)
        {
            this.host = host;
            working = new List<RarityRule>(Limits.rarities);
            AddToClassList("forge-details-pane");
            style.flexGrow = 1;

            scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("forge-details");
            scroll.style.flexGrow = 1;
            Add(scroll);

            var intro = ForgeUI.Section("Raretés", scroll,
                "De la plus commune (en haut) à la plus rare (en bas) : l'ordre sert au jeu pour comparer deux objets. " +
                "Chaque rareté fixe la couleur, le prix autorisé, le bonus de stat maximum et le nombre de passifs d'une arme.");
            orphanBanner = ForgeUI.Label(string.Empty, "forge-rarity-orphans");
            intro.Add(orphanBanner);
            var add = ForgeUI.Button("+ Nouvelle rareté", $"Ajoute une rareté en bas de la liste (maximum {Limits.maxRarities}).", AddRarity,
                "forge-button--primary");
            add.style.alignSelf = Align.FlexStart;
            intro.Add(add);

            cards = new VisualElement();
            scroll.Add(cards);
            reorder = new ForgeReorder(cards, this, scroll, (from, to) =>
            {
                var moved = working[from];
                working.RemoveAt(from);
                working.Insert(to, moved);
                RebuildCards();
            });

            var issues = ForgeUI.Section("Problèmes détectés", scroll);
            issuesPanel = new IssuesPanel();
            issues.Add(issuesPanel);

            footer = new ValidationFooter(Revert, () => Commit());
            Add(footer);
            RebuildCards();
        }

        /// <summary>À l'affichage de la vue : des objets ont pu changer de rareté entre-temps, les compteurs sont recalculés.</summary>
        public void RefreshUsage() => RebuildCards();

        void RebuildCards()
        {
            cards.Clear();
            usage = Usage();
            for (var i = 0; i < working.Count; i++)
                cards.Add(Card(i, usage.TryGetValue(working[i].id, out var count) ? count : 0));
            Refresh();
        }

        VisualElement Card(int index, int itemCount)
        {
            var rule = working[index];
            var card = new VisualElement();
            card.AddToClassList("forge-section");
            card.AddToClassList("forge-rarity-card");

            var color = new ColorField { value = rule.color, showAlpha = false, tooltip = "Couleur de la rareté dans tous les outils et en jeu." };
            color.AddToClassList("forge-rarity-card__color");
            // Pastille de couleur, pas une barre : la règle « .forge-row > champ » étirerait le champ.
            color.style.width = 56;
            color.style.flexGrow = 0;
            color.style.marginRight = 10;
            color.RegisterValueChangedCallback(evt => Edit(index, (ref RarityRule r) => r.color = evt.newValue));
            var name = new TextField { value = rule.name, tooltip = "Nom affiché dans les outils. Renommer une rareté ne modifie pas les objets." };
            name.style.flexGrow = 1;
            name.RegisterValueChangedCallback(evt => Edit(index, (ref RarityRule r) => r.name = evt.newValue));
            var used = ForgeUI.Badge(itemCount == 0 ? "aucun objet" : $"{itemCount} objet(s)", rule.color, "Objets qui utilisent cette rareté.");
            var up = ForgeUI.ArrowButton(true, "Rend la rareté plus commune (monte d'un cran).", () => Move(index, -1));
            up.SetEnabled(index > 0);
            var down = ForgeUI.ArrowButton(false, "Rend la rareté plus rare (descend d'un cran).", () => Move(index, 1));
            down.SetEnabled(index < working.Count - 1);
            var delete = ForgeUI.Button("Supprimer", "Supprime la rareté (avec confirmation si des objets l'utilisent).", () => Delete(index, itemCount),
                "forge-button--danger");
            var handle = reorder.Handle(card, () => GhostContent(working[index]),
                "Maintenez et glissez pour placer la rareté où vous voulez (en haut : la plus commune).");
            card.Add(ForgeLists.Row(handle, color, name, used, up, down, delete));

            var priceMin = new IntegerField("Prix minimum") { value = rule.price.min, tooltip = "Prix d'achat le plus bas autorisé, en or." };
            priceMin.RegisterValueChangedCallback(evt => Edit(index, (ref RarityRule r) => r.price.min = evt.newValue));
            var priceMax = new IntegerField("Prix maximum") { value = rule.price.max, tooltip = "Prix d'achat le plus haut autorisé, en or." };
            priceMax.RegisterValueChangedCallback(evt => Edit(index, (ref RarityRule r) => r.price.max = evt.newValue));
            card.Add(Pair(priceMin, priceMax));

            var maxStat = new IntegerField("Bonus maximum") { value = rule.maxStatValue, tooltip = "Valeur la plus haute d'un bonus de stat (et la plus basse d'un malus, en négatif)." };
            maxStat.RegisterValueChangedCallback(evt => Edit(index, (ref RarityRule r) => r.maxStatValue = evt.newValue));
            var weight = new IntegerField("Poids de butin") { value = rule.defaultLootWeight, tooltip = "Poids proposé quand un objet de cette rareté est ajouté à une table de butin." };
            weight.RegisterValueChangedCallback(evt => Edit(index, (ref RarityRule r) => r.defaultLootWeight = evt.newValue));
            card.Add(Pair(maxStat, weight));

            var passives = new SliderInt("Passifs d'arme", 0, Limits.maxPassiveSlots)
            {
                value = rule.passiveSlots, showInputField = true,
                tooltip = $"Nombre de passifs qu'une arme de cette rareté peut avoir (0 à {Limits.maxPassiveSlots})."
            };
            passives.RegisterValueChangedCallback(evt => Edit(index, (ref RarityRule r) => r.passiveSlots = evt.newValue));
            passives.labelElement.style.width = passives.labelElement.style.minWidth = LabelWidth;
            card.Add(passives);
            return card;
        }

        const float LabelWidth = 120f;

        /// <summary>Deux champs côte à côte, libellés de même largeur et espacés : les colonnes restent alignées d'une ligne à l'autre.</summary>
        static VisualElement Pair(BaseField<int> left, BaseField<int> right)
        {
            foreach (var field in new[] { left, right })
            {
                field.labelElement.style.width = field.labelElement.style.minWidth = LabelWidth;
                field.style.flexBasis = 0;
            }
            left.style.marginRight = 24;
            return ForgeLists.Row(left, right);
        }

        /// <summary>Contenu de la copie qui suit le pointeur pendant un glisser-déposer : couleur et nom.</summary>
        static VisualElement GhostContent(RarityRule rule)
        {
            var swatch = new VisualElement();
            swatch.AddToClassList("forge-reorder-ghost__swatch");
            swatch.style.backgroundColor = rule.color;
            return ForgeLists.Row(swatch, ForgeUI.Label(rule.DisplayName, "forge-reorder-ghost__name"));
        }

        delegate void RuleEdit(ref RarityRule rule);

        void Edit(int index, RuleEdit edit)
        {
            var rule = working[index];
            edit(ref rule);
            working[index] = rule;
            Refresh();
        }

        void Move(int index, int offset)
        {
            var target = index + offset;
            (working[index], working[target]) = (working[target], working[index]);
            RebuildCards();
        }

        void AddRarity()
        {
            if (working.Count >= Limits.maxRarities)
            {
                ForgeLists.Notify($"Maximum atteint : {Limits.maxRarities} raretés.");
                return;
            }
            var last = working.Count > 0 ? working[^1] : Limits.GetRarity(0);
            // Un identifiant jamais vu : une rareté supprimée encore utilisée ne doit pas être « ressuscitée ».
            var id = ForgeDesignLimits.NextRarityId(working.Select(r => r.id).Concat(Limits.rarities.Select(r => r.id)).Concat(usage.Keys));
            working.Add(new RarityRule
            {
                id = id, name = "Nouvelle rareté", color = new Color(0.9f, 0.85f, 0.95f),
                maxStatValue = Mathf.Max(1, last.maxStatValue), price = last.price, defaultLootWeight = Mathf.Max(1, last.defaultLootWeight / 2),
                passiveSlots = Mathf.Min(Limits.maxPassiveSlots, last.passiveSlots)
            });
            RebuildCards();
            // La nouvelle carte est en bas de la liste : on y descend, on la met en avant et le nom est prêt à être remplacé.
            var created = cards.Children().Last();
            created.AddToClassList("forge-rarity-card--new");
            schedule.Execute(() =>
            {
                scroll.ScrollTo(created);
                var name = created.Q<TextField>();
                name?.Focus();
                name?.SelectAll();
            }).StartingIn(50);
            schedule.Execute(() => created.RemoveFromClassList("forge-rarity-card--new")).StartingIn(2500);
            ForgeLists.Notify("Rareté ajoutée en bas de la liste : donnez-lui un nom.");
        }

        void Delete(int index, int itemCount)
        {
            var rule = working[index];
            if (itemCount > 0 && !ForgeDialogs.Confirm("Supprimer une rareté utilisée",
                    $"{itemCount} objet(s) utilisent « {rule.DisplayName} ». Après validation, ils seront marqués en erreur " +
                    "jusqu'à ce que vous leur choisissiez une autre rareté.", "Supprimer", "Annuler"))
                return;
            working.RemoveAt(index);
            RebuildCards();
        }

        /// <summary>Nombre d'objets par identifiant de rareté (raretés supprimées comprises).</summary>
        static Dictionary<int, int> Usage() =>
            AssetFactory.LoadAll<ItemData>().GroupBy(item => item.RarityId).ToDictionary(group => group.Key, group => group.Count());

        List<ValidationIssue> Validate()
        {
            var issues = new List<ValidationIssue>();
            void Error(string message) => issues.Add(new ValidationIssue(Severity.Error, message, null) { RuleName = "Raretés" });

            if (working.Count == 0)
                Error("Il faut au moins une rareté.");
            foreach (var rule in working)
            {
                if (string.IsNullOrWhiteSpace(rule.name))
                    Error("Une rareté n'a pas de nom.");
                if (rule.price.min < 0 || rule.price.max < rule.price.min)
                    Error($"« {rule.DisplayName} » : le prix minimum doit être positif et inférieur au maximum.");
                if (rule.maxStatValue < 0)
                    Error($"« {rule.DisplayName} » : le bonus maximum ne peut pas être négatif.");
                if (!Limits.lootWeight.Contains(rule.defaultLootWeight))
                    Error($"« {rule.DisplayName} » : poids de butin hors limites ({Limits.lootWeight}).");
            }
            foreach (var duplicate in working.Where(r => !string.IsNullOrWhiteSpace(r.name))
                         .GroupBy(r => r.name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                Error($"Deux raretés s'appellent « {duplicate.Key} ».");
            return issues;
        }

        void Refresh()
        {
            var issues = Validate();
            var dirty = IsDirty();
            issuesPanel.Show(issues);
            footer.Show(issues, false, dirty);
            // À la fermeture de Forge Studio (ou d'Unity), les raretés non validées sont proposées à l'enregistrement.
            host.SetUnsaved(this, dirty, Commit, issues.Count > 0);

            // Objets qui resteront sans rareté une fois la liste validée.
            var orphans = usage.Where(pair => working.All(r => r.id != pair.Key)).ToList();
            orphanBanner.text = orphans.Count == 0
                ? string.Empty
                : ForgeText.System($"{orphans.Sum(pair => pair.Value)} objet(s) utilisent une rareté supprimée : ils sont en erreur. " +
                                   "Choisissez-leur une autre rareté dans leur fiche (filtre « Rareté supprimée » de la liste).");
            orphanBanner.style.display = orphans.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        bool IsDirty() => !working.SequenceEqual(Limits.rarities);

        bool Commit()
        {
            var errors = Validate();
            if (errors.Count > 0)
            {
                ForgeLists.Notify($"Non enregistré : {errors.Count} erreur(s) à corriger.");
                return false;
            }
            var limits = Limits;
            Undo.RecordObject(limits, "Modifier les raretés");
            limits.rarities = working.Select(rule => { rule.name = rule.name.Trim(); return rule; }).ToList();
            working = new List<RarityRule>(limits.rarities);
            EditorUtility.SetDirty(limits);
            AssetDatabase.SaveAssetIfDirty(limits);
            ValidationCache.Invalidate();
            ForgeLists.Notify("Raretés enregistrées.");
            RebuildCards();
            Saved?.Invoke();
            return true;
        }

        void Revert()
        {
            working = new List<RarityRule>(Limits.rarities);
            RebuildCards();
        }
    }
}
