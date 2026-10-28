using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common.Charts
{
    /// <summary>Carte d'avancement : anneau valeur / objectif, animé, cliquable.</summary>
    public sealed class ProgressRing : VisualElement
    {
        const float Size = 58f;
        const float Thickness = 7f;

        readonly VisualElement ring;
        readonly Label value;
        readonly Label caption;
        readonly Label status;
        readonly ChartAnimation animation;
        float ratio;
        bool hasErrors;

        public ProgressRing(string title, System.Action onClick)
        {
            AddToClassList("forge-card");
            AddToClassList("forge-progress-card");
            if (onClick != null)
            {
                AddToClassList("forge-card--clickable");
                RegisterCallback<ClickEvent>(_ => onClick());
            }

            ring = new VisualElement();
            ring.AddToClassList("forge-progress-card__ring");
            ring.style.width = Size;
            ring.style.height = Size;
            ring.generateVisualContent += Draw;
            Add(ring);

            var texts = new VisualElement();
            texts.AddToClassList("forge-progress-card__texts");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("forge-progress-card__title");
            texts.Add(titleLabel);
            value = new Label { enableRichText = true };
            value.AddToClassList("forge-progress-card__value");
            texts.Add(value);
            caption = new Label();
            caption.AddToClassList("forge-progress-card__caption");
            texts.Add(caption);
            status = new Label();
            status.AddToClassList("forge-progress-card__status");
            texts.Add(status);
            Add(texts);

            animation = new ChartAnimation(this, ring.MarkDirtyRepaint);
        }

        /// <param name="errors">Nombre d'éléments en erreur dans cette catégorie.</param>
        public void SetValue(int current, int target, int errors)
        {
            ratio = target <= 0 ? 1f : Mathf.Clamp01((float)current / target);
            hasErrors = errors > 0;
            value.text = $"{current} <size=11><color=#888888>/ {target}</color></size>";
            caption.text = $"{ratio:P0} de l'objectif du GDD";
            status.text = errors > 0 ? $"✖ {errors} en erreur" : current >= target ? "✔ Objectif atteint" : string.Empty;
            status.EnableInClassList("forge-progress-card__status--error", errors > 0);
            status.EnableInClassList("forge-progress-card__status--good", errors == 0 && current >= target);
            tooltip = $"{current} sur {target} prévus par le GDD." + (errors > 0 ? $"\n{errors} élément(s) à corriger." : string.Empty) +
                      "\nCliquez pour ouvrir la section.";
            animation.PlayOnce();
        }

        void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            var center = new Vector2(Size / 2f, Size / 2f);
            var radius = Size / 2f - Thickness / 2f - 1f;
            painter.lineWidth = Thickness;
            painter.lineCap = LineCap.Butt;
            painter.strokeColor = ChartColors.Track;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();

            var sweep = 360f * ratio * animation.Progress;
            if (sweep <= 0.5f)
                return;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = hasErrors ? ChartColors.Critical : ratio >= 1f ? ChartColors.Good : ChartColors.Series(0);
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + sweep));
            painter.Stroke();
        }
    }
}
