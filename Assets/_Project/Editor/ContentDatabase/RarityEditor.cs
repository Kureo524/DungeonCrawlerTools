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
            // Rang d'origine de la carte : retrouvé après un glisser-déposer pour réordonner la liste.
            var card = new VisualElement { userData = index };
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
            card.Add(ForgeLists.Row(DragHandle(card), color, name, used, up, down, delete));

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

        /// <summary>Trois traits dans le coin haut gauche de la carte : maintenir et glisser pour la placer n'importe où dans la liste.</summary>
        VisualElement DragHandle(VisualElement card)
        {
            var handle = new VisualElement { tooltip = "Maintenez et glissez pour placer la rareté où vous voulez (en haut : la plus commune)." };
            handle.AddToClassList("forge-drag-handle");
            handle.generateVisualContent += context =>
            {
                var center = handle.contentRect.center;
                var painter = context.painter2D;
                painter.strokeColor = handle.resolvedStyle.color;
                painter.lineWidth = 2f;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                for (var line = -1; line <= 1; line++)
                {
                    painter.MoveTo(center + new Vector2(-8f, line * 5.5f));
                    painter.LineTo(center + new Vector2(8f, line * 5.5f));
                }
                painter.Stroke();
            };
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || drag != null)
                    return;
                handle.CapturePointer(evt.pointerId);
                StartDrag(card, evt.position);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (drag != null && drag.Card == card && handle.HasPointerCapture(evt.pointerId))
                    DragTo(evt.position);
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (handle.HasPointerCapture(evt.pointerId))
                    handle.ReleasePointer(evt.pointerId);
                EndDrag();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            return handle;
        }

        /// <summary>Glisser-déposer en cours : la carte reste dans la liste (sa place est marquée), une copie suit le pointeur.</summary>
        sealed class DragState
        {
            public VisualElement Card;
            public VisualElement Ghost;
            public int From;
            public int To;
            public float Step;
            public float GrabOffset;
            public float[] Centers;
        }

        DragState drag;

        void StartDrag(VisualElement card, Vector2 pointer)
        {
            var children = cards.Children().ToList();
            var from = children.IndexOf(card);
            // Écart entre deux cartes, marge comprise (elles ont toutes la même hauteur) : la place que la carte libère ou occupe.
            var step = children.Count > 1 ? children[1].layout.y - children[0].layout.y : card.layout.height;
            var local = this.WorldToLocal(pointer);
            var cardTop = this.WorldToLocal(card.worldBound.position);

            // Copie légère de la carte (poignée, couleur, nom), au-dessus de tout le reste.
            var rule = working[(int)card.userData];
            var ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            ghost.AddToClassList("forge-section");
            ghost.AddToClassList("forge-rarity-ghost");
            ghost.style.position = Position.Absolute;
            ghost.style.left = cardTop.x;
            ghost.style.width = card.worldBound.width;
            ghost.style.top = cardTop.y;
            // Opaque : la copie passe au-dessus des autres cartes sans se mélanger à leur texte.
            ghost.style.backgroundColor = new Color(0.10f, 0.11f, 0.16f, 1f);
            var swatch = new VisualElement();
            swatch.AddToClassList("forge-rarity-ghost__swatch");
            swatch.style.backgroundColor = rule.color;
            ghost.Add(ForgeLists.Row(swatch, ForgeUI.Label(rule.DisplayName, "forge-rarity-ghost__name")));
            Add(ghost);

            card.AddToClassList("forge-rarity-card--placeholder");
            drag = new DragState
            {
                Card = card, Ghost = ghost, From = from, To = from, Step = step, GrabOffset = local.y - cardTop.y,
                Centers = children.Select(c => c.layout.center.y).ToArray()
            };
        }

        void DragTo(Vector2 pointer)
        {
            // Défilement automatique près des bords de la liste.
            var viewport = scroll.contentViewport.worldBound;
            if (pointer.y < viewport.yMin + 40f)
                scroll.scrollOffset -= new Vector2(0f, 14f);
            else if (pointer.y > viewport.yMax - 40f)
                scroll.scrollOffset += new Vector2(0f, 14f);

            var local = this.WorldToLocal(pointer);
            drag.Ghost.style.top = local.y - drag.GrabOffset;

            // Place visée : nombre de cartes (hors celle déplacée) dont le milieu est au-dessus du pointeur.
            var pointerInList = cards.WorldToLocal(pointer).y;
            var to = 0;
            for (var i = 0; i < drag.Centers.Length; i++)
                if (i != drag.From && drag.Centers[i] < pointerInList)
                    to++;
            if (to == drag.To)
                return;
            drag.To = to;

            // Les cartes entre l'ancienne et la nouvelle place glissent d'un cran pour ouvrir la place.
            var children = cards.Children().ToList();
            for (var i = 0; i < children.Count; i++)
            {
                float offset = 0f;
                if (i == drag.From)
                    offset = (to - drag.From) * drag.Step;
                else if (drag.From < i && i <= to)
                    offset = -drag.Step;
                else if (to <= i && i < drag.From)
                    offset = drag.Step;
                children[i].style.translate = new Translate(0f, offset);
            }
        }

        /// <summary>Lâcher : la liste de travail prend le nouvel ordre.</summary>
        void EndDrag()
        {
            if (drag == null)
                return;
            var (from, to) = (drag.From, drag.To);
            drag.Ghost.RemoveFromHierarchy();
            foreach (var card in cards.Children())
                card.style.translate = StyleKeyword.Null;
            drag.Card.RemoveFromClassList("forge-rarity-card--placeholder");
            drag = null;
            if (from == to)
                return;
            var rule = working[from];
            working.RemoveAt(from);
            working.Insert(to, rule);
            RebuildCards();
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
