using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Editor.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Barre fixée en bas des outils : état de la validation, « Annuler » et « Valider ».</summary>
    public sealed class ValidationFooter : VisualElement
    {
        readonly Label status;
        readonly Image statusIcon;
        readonly Button revertButton;
        readonly Button commitButton;

        public ValidationFooter(Action onRevert, Action onCommit)
        {
            AddToClassList("forge-footer");

            statusIcon = new Image { scaleMode = ScaleMode.ScaleToFit };
            statusIcon.AddToClassList("forge-footer__icon");
            Add(statusIcon);
            status = ForgeUI.Label(string.Empty, "forge-footer__status");
            Add(status);

            revertButton = ForgeUI.Button("Annuler les modifications", "Revient à la dernière version enregistrée.", onRevert);
            commitButton = ForgeUI.Button("Valider", "Vérifie les données et les enregistre si elles ne contiennent aucune erreur.", onCommit, "forge-button--primary");
            commitButton.AddToClassList("forge-footer__commit");
            Add(revertButton);
            Add(commitButton);
        }

        /// <summary>Met à jour l'état affiché.</summary>
        /// <param name="isNew">Asset pas encore créé : le bouton devient « Valider et créer ».</param>
        public void Show(IReadOnlyCollection<ValidationIssue> issues, bool isNew, bool isDirty)
        {
            var errors = issues.Count(issue => issue.Severity == Severity.Error);
            var warnings = issues.Count - errors;

            commitButton.text = isNew ? "Valider et créer" : "Valider et enregistrer";
            commitButton.SetEnabled(isNew || isDirty);
            commitButton.EnableInClassList("forge-button--blocked", errors > 0);
            commitButton.tooltip = errors > 0
                ? $"Impossible d'enregistrer : {errors} erreur(s) à corriger (voir « Problèmes détectés »)."
                : "Vérifie les données et les enregistre.";
            revertButton.text = isNew ? "Abandonner" : "Annuler les modifications";
            revertButton.SetEnabled(isNew || isDirty);

            string icon;
            if (errors > 0)
            {
                icon = "console.erroricon.sml";
                status.text = $"{errors} erreur(s) bloquante(s)" + (warnings > 0 ? $", {warnings} avertissement(s)" : string.Empty);
            }
            else if (warnings > 0)
            {
                icon = "console.warnicon.sml";
                status.text = $"Valide, avec {warnings} avertissement(s)";
            }
            else
            {
                icon = "TestPassed";
                status.text = "Valide";
            }
            if (isNew)
                status.text += " · pas encore créé";
            else if (isDirty)
                status.text += " · modifications non enregistrées";
            statusIcon.image = EditorGUIUtility.IconContent(icon).image;
        }
    }
}
