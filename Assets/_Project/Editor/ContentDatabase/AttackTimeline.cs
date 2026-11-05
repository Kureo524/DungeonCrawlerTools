using System;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Frise d'une attaque, à l'échelle : pour chaque étape, préparation (le joueur voit venir), action, récupération (fenêtre de riposte).
    /// Les étapes conditionnelles sont hachurées : elles peuvent ne pas être jouées.
    /// </summary>
    public sealed class AttackTimeline : VisualElement
    {
        readonly Func<ResolvedAttack> attack;
        readonly VisualElement bar;
        readonly Label total;
        string signature;

        public AttackTimeline(Func<ResolvedAttack> attack)
        {
            this.attack = attack;
            AddToClassList("forge-timeline");
            style.flexDirection = FlexDirection.Column;
            style.alignSelf = Align.Stretch;
            bar = new VisualElement();
            bar.AddToClassList("forge-timeline__bar");
            bar.style.alignSelf = Align.Stretch;
            Add(bar);
            total = ForgeUI.Label(string.Empty, "forge-hint");
            Add(total);
            Refresh();
            schedule.Execute(Refresh).Every(300);
        }

        void Refresh()
        {
            var current = attack();
            if (current == null)
                return;
            var tempo = current.Tempo;
            var key = string.Join("|", current.Steps.Select(s => $"{s.name}{s.action}{s.windup}{s.recovery}{s.trigger}{s.conditions.Count}{s.dashDistance}{s.dashSpeed}")) + tempo;
            if (key == signature)
                return;
            signature = key;
            bar.Clear();
            var longest = 0f;
            for (var i = 0; i < current.Steps.Count; i++)
            {
                var step = current.Steps[i];
                var conditional = i > 0 && (step.trigger != StepTrigger.Always || step.conditions.Count > 0) || i == 0 && step.conditions.Count > 0;
                var actionTime = step.action == StepAction.Dash ? step.dashDistance / Mathf.Max(1f, step.dashSpeed) : 0.08f;
                Segment(step.windup * tempo, "forge-timeline__windup", $"{i + 1}. {Name(step, i)} : préparation {step.windup * tempo:0.##} s", conditional, $"{i + 1}");
                Segment(actionTime, "forge-timeline__hit", $"{i + 1}. {Name(step, i)} : {EnemyPatternText.Label(step.action).ToLowerInvariant()}", conditional, null);
                Segment(step.recovery * tempo, "forge-timeline__recovery", $"{i + 1}. {Name(step, i)} : récupération {step.recovery * tempo:0.##} s", conditional, null);
                longest += (step.windup + step.recovery) * tempo + actionTime;
            }
            Segment(current.Settings.cooldown, "forge-timeline__cooldown", $"Recharge {current.Settings.cooldown:0.#} s", false, "recharge");
            var dodge = current.Steps.Count > 0 ? current.Steps.Min(s => s.windup) * tempo : 0f;
            total.text = $"Durée : {longest:0.#} s au plus (+ {current.Settings.cooldown:0.#} s de recharge). Plus courte préparation : {dodge:0.##} s. " +
                         "Hachuré : étape conditionnelle.";
        }

        static string Name(AttackStep step, int index) => string.IsNullOrWhiteSpace(step.name) ? $"Étape {index + 1}" : step.name;

        void Segment(float seconds, string className, string tooltip, bool conditional, string label)
        {
            if (seconds <= 0.001f)
                return;
            var segment = new VisualElement { tooltip = tooltip };
            segment.AddToClassList("forge-timeline__segment");
            segment.AddToClassList(className);
            segment.EnableInClassList("forge-timeline__segment--conditional", conditional);
            segment.style.flexGrow = seconds;
            if (label != null)
                segment.Add(ForgeUI.Label(label, "forge-timeline__label"));
            bar.Add(segment);
        }
    }
}
