using System;
using System.Collections.Generic;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Liste de problèmes détectés, avec bouton de correction automatique quand il existe.</summary>
    public sealed class IssuesPanel : VisualElement
    {
        readonly bool showContext;

        /// <summary>Appelé après une correction automatique, pour rafraîchir l'affichage.</summary>
        public event Action Fixed;

        /// <summary>Appelé au clic sur un problème (pour sélectionner l'asset concerné).</summary>
        public event Action<ValidationIssue> Clicked;

        public IssuesPanel(bool showContext = false)
        {
            this.showContext = showContext;
        }

        public void Show(IReadOnlyCollection<ValidationIssue> issues)
        {
            Clear();
            if (issues.Count == 0)
            {
                var ok = Row(null, "Aucun problème détecté.", "forge-issue--ok");
                Add(ok);
                return;
            }

            foreach (var issue in issues)
            {
                var isError = issue.Severity == Severity.Error;
                var message = showContext && issue.Context != null ? $"<b>{issue.Context.name}</b> : {issue.Message}" : issue.Message;
                var row = Row(isError ? "console.erroricon.sml" : "console.warnicon.sml", message,
                    isError ? "forge-issue--error" : "forge-issue--warning");
                row.tooltip = $"Règle : {issue.RuleName}\n{(issue.Fix != null ? "Une correction automatique est disponible." : "À corriger à la main.")}";
                row.RegisterCallback<ClickEvent>(_ => Clicked?.Invoke(issue));

                if (issue.Fix != null)
                {
                    row.Add(ForgeUI.Button(issue.FixLabel ?? "Corriger", "Applique la correction automatique proposée.", () =>
                    {
                        issue.Fix();
                        Fixed?.Invoke();
                    }));
                }
                Add(row);
            }
        }

        static VisualElement Row(string icon, string message, string className)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-issue");
            row.AddToClassList(className);
            if (icon != null)
            {
                var image = new Image { image = EditorGUIUtility.IconContent(icon).image, scaleMode = ScaleMode.ScaleToFit };
                image.style.width = 16;
                image.style.height = 16;
                image.style.marginRight = 6;
                row.Add(image);
            }
            var label = ForgeUI.Label(message);
            label.style.flexGrow = 1;
            row.Add(label);
            return row;
        }
    }
}
