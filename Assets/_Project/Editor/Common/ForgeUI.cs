using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Petits constructeurs d'éléments d'interface partagés par tous les outils.</summary>
    public static class ForgeUI
    {
        /// <summary>
        /// Feuille de style commune + skin « Encre ». Un seul skin, sombre, même si l'éditeur est en thème clair :
        /// la direction artistique « Verre d'encre » est pensée pour un fond de nuit.
        /// </summary>
        public static void ApplyStyle(VisualElement root)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(DungeonPaths.StyleSheet);
            if (sheet != null)
                root.styleSheets.Add(sheet);
            root.AddToClassList("forge-theme");
            root.AddToClassList("forge-theme--dark");
            ForgeDropdowns.Attach(root);
            ForgeInputLimits.Attach(root);
        }



        public static Label Label(string text, string className = null)
        {
            var label = new Label(text) { enableRichText = true };
            if (className != null)
                label.AddToClassList(className);
            return label;
        }

        /// <summary>Carte de section : titre, description facultative, puis contenu.</summary>
        public static VisualElement Section(string title, VisualElement parent, string description = null)
        {
            var section = new VisualElement();
            section.AddToClassList("forge-section");
            var header = new VisualElement();
            // Titre écrit comme une fenêtre de statut du roman : [Personnage].
            header.Add(Label(ForgeText.WindowRich(title), "forge-section-title"));
            section.Add(header);
            Collapsible(section, header, title);
            ForgeLabels.Track(section);
            if (!string.IsNullOrEmpty(description))
                section.Add(Label(description, "forge-section-description"));
            parent.Add(section);
            return section;
        }

        /// <summary>
        /// Section repliable : un clic sur son en-tête la réduit à une ligne (ou la rouvre). L'état est gardé pendant la session,
        /// pour chaque titre de section. Les boutons de l'en-tête (+ Ajouter…) gardent leur rôle.
        /// </summary>
        public static void Collapsible(VisualElement section, VisualElement header, string key)
        {
            header.AddToClassList("forge-section__header");
            header.AddToClassList("forge-section__header--toggle");
            header.tooltip = "Cliquez pour replier ou déplier cette partie.";
            var chevron = new VisualElement { pickingMode = PickingMode.Ignore };
            chevron.AddToClassList("forge-section__chevron");
            chevron.generateVisualContent += context =>
            {
                var center = chevron.contentRect.center;
                var painter = context.painter2D;
                painter.strokeColor = chevron.resolvedStyle.color;
                painter.lineWidth = 1.6f;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                painter.BeginPath();
                if (section.ClassListContains("forge-section--collapsed"))
                {
                    // Replié : pointe vers la droite.
                    painter.MoveTo(center + new Vector2(-2f, -4f));
                    painter.LineTo(center + new Vector2(2f, 0f));
                    painter.LineTo(center + new Vector2(-2f, 4f));
                }
                else
                {
                    painter.MoveTo(center + new Vector2(-4f, -2f));
                    painter.LineTo(center + new Vector2(0f, 2f));
                    painter.LineTo(center + new Vector2(4f, -2f));
                }
                painter.Stroke();
            };
            header.Insert(0, chevron);

            var sessionKey = $"forge.section.collapsed.{key}";
            CollapseKeys.AddOrUpdate(section, sessionKey);
            SetCollapsed(section, SessionState.GetBool(sessionKey, false));
            header.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement target && (target is Button || target.GetFirstAncestorOfType<Button>() != null))
                    return;
                ToggleCollapsed(section);
            });
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, string> CollapseKeys = new();

        /// <summary>Replie ou déplie une section repliable, et retient ce choix pour la session.</summary>
        public static void ToggleCollapsed(VisualElement section)
        {
            var collapsed = !section.ClassListContains("forge-section--collapsed");
            if (CollapseKeys.TryGetValue(section, out var sessionKey))
                SessionState.SetBool(sessionKey, collapsed);
            SetCollapsed(section, collapsed);
        }

        static void SetCollapsed(VisualElement section, bool collapsed)
        {
            section.EnableInClassList("forge-section--collapsed", collapsed);
            section.Q(className: "forge-section__chevron")?.MarkDirtyRepaint();
        }

        public static Button Button(string text, string tooltip, Action onClick, string className = null)
        {
            var button = new Button(onClick) { text = text, tooltip = tooltip };
            button.AddToClassList("forge-button");
            if (className != null)
                button.AddToClassList(className);
            return button;
        }

        /// <summary>Bouton carré « retirer » : la croix est dessinée, donc toujours parfaitement centrée (un caractère ✕ ne l'est pas).</summary>
        public static Button CloseButton(string tooltip, Action onClick)
        {
            var button = Button(string.Empty, tooltip, onClick, "forge-button--icon");
            button.AddToClassList("forge-button--close");
            var cross = new VisualElement { pickingMode = PickingMode.Ignore };
            cross.AddToClassList("forge-button__cross");
            cross.generateVisualContent += context =>
            {
                var rect = cross.contentRect;
                var center = rect.center;
                const float half = 4f;
                var painter = context.painter2D;
                painter.strokeColor = cross.resolvedStyle.color;
                painter.lineWidth = 1.5f;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(center + new Vector2(-half, -half));
                painter.LineTo(center + new Vector2(half, half));
                painter.MoveTo(center + new Vector2(half, -half));
                painter.LineTo(center + new Vector2(-half, half));
                painter.Stroke();
            };
            button.Add(cross);
            return button;
        }

        /// <summary>Bouton carré avec une flèche dessinée vers le haut ou le bas : centrée au pixel près, contrairement à un caractère ↑ ↓.</summary>
        public static Button ArrowButton(bool up, string tooltip, Action onClick)
        {
            var button = Button(string.Empty, tooltip, onClick, "forge-button--icon");
            button.AddToClassList("forge-button--glyph");
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("forge-button__glyph");
            arrow.generateVisualContent += context =>
            {
                var center = arrow.contentRect.center;
                var direction = up ? -1f : 1f;
                var painter = context.painter2D;
                painter.strokeColor = arrow.resolvedStyle.color;
                painter.lineWidth = 1.5f;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                painter.BeginPath();
                // Hampe, puis pointe.
                painter.MoveTo(center + new Vector2(0f, -5f * direction));
                painter.LineTo(center + new Vector2(0f, 5f * direction));
                painter.MoveTo(center + new Vector2(-4f, 1f * direction));
                painter.LineTo(center + new Vector2(0f, 5f * direction));
                painter.LineTo(center + new Vector2(4f, 1f * direction));
                painter.Stroke();
            };
            button.Add(arrow);
            return button;
        }

        /// <summary>Barre de recherche : la loupe en image (pixelisée) est remplacée par un rond dessiné, net à toute taille.</summary>
        public static T SearchField<T>(T field) where T : VisualElement
        {
            var button = field.Q(className: "unity-search-field-base__search-button");
            if (button == null)
                return field;
            button.style.backgroundImage = StyleKeyword.None;
            button.AddToClassList("forge-search-icon");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.position = Position.Absolute;
            icon.style.left = icon.style.top = icon.style.right = icon.style.bottom = 0;
            icon.generateVisualContent += context =>
            {
                // Un simple rond, centré.
                var rect = icon.contentRect;
                var painter = context.painter2D;
                painter.strokeColor = icon.resolvedStyle.color;
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.Arc(rect.center, 3.5f, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Stroke();
            };
            button.Add(icon);
            return field;
        }

        public static TextField Text(string label, string bindingPath, string tooltip, bool multiline = false)
        {
            var field = new TextField(label) { bindingPath = bindingPath, tooltip = tooltip, multiline = multiline };
            if (multiline)
                field.AddToClassList("forge-multiline");
            return field;
        }

        public static SliderInt Slider(string label, string bindingPath, IntRange range, string tooltip)
        {
            return new SliderInt(label, range.min, range.max)
            {
                bindingPath = bindingPath,
                showInputField = true,
                tooltip = $"{tooltip}\nValeurs autorisées : {range}."
            };
        }

        public static Slider Slider(string label, string bindingPath, FloatRange range, string tooltip)
        {
            return new Slider(label, range.min, range.max)
            {
                bindingPath = bindingPath,
                showInputField = true,
                tooltip = $"{tooltip}\nValeurs autorisées : {range}."
            };
        }

        public static EnumField Enum<T>(string label, string bindingPath, string tooltip) where T : Enum
        {
            return new EnumField(label, default(T)) { bindingPath = bindingPath, tooltip = tooltip };
        }

        public static ObjectField Object<T>(string label, string bindingPath, string tooltip) where T : UnityEngine.Object
        {
            return new ObjectField(label) { objectType = typeof(T), bindingPath = bindingPath, allowSceneObjects = false, tooltip = tooltip };
        }

        public static Toggle Toggle(string label, string bindingPath, string tooltip)
        {
            return new Toggle(label) { bindingPath = bindingPath, tooltip = tooltip };
        }

        /// <summary>Pastille teintée (rareté, statut…) : fond très léger, point et texte de la couleur.</summary>
        public static VisualElement Badge(string text, Color color, string tooltip = null)
        {
            var badge = new VisualElement { tooltip = tooltip };
            badge.AddToClassList("forge-badge");
            badge.style.backgroundColor = new Color(color.r, color.g, color.b, 0.14f);
            badge.style.borderTopColor = badge.style.borderBottomColor = badge.style.borderLeftColor = badge.style.borderRightColor =
                new Color(color.r, color.g, color.b, 0.35f);
            var dot = new VisualElement();
            dot.AddToClassList("forge-badge__dot");
            dot.style.backgroundColor = color;
            badge.Add(dot);
            badge.Add(Label(text, "forge-badge__text"));
            return badge;
        }

        public static VisualElement EmptyState(string message)
        {
            var label = Label(ForgeText.System(message), "forge-empty");
            return label;
        }
    }
}
