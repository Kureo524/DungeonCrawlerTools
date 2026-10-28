using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common.Charts
{
    /// <summary>Petite animation d'apparition (0 → 1, ralentie en fin de course) pour les graphiques.</summary>
    public sealed class ChartAnimation
    {
        const float Duration = 0.45f;

        readonly VisualElement owner;
        readonly Action onFrame;
        IVisualElementScheduledItem tick;
        double start;

        public float Progress { get; private set; } = 1f;

        public ChartAnimation(VisualElement owner, Action onFrame)
        {
            this.owner = owner;
            this.onFrame = onFrame;
        }

        bool played;

        /// <summary>Joue l'animation la première fois seulement ; ensuite, simple rafraîchissement.</summary>
        public void PlayOnce()
        {
            if (played)
            {
                onFrame();
                return;
            }
            played = true;
            Play();
        }

        public void Play()
        {
            start = EditorApplication.timeSinceStartup;
            Progress = 0f;
            tick?.Pause();
            tick = owner.schedule.Execute(() =>
            {
                var t = (float)((EditorApplication.timeSinceStartup - start) / Duration);
                Progress = t >= 1f ? 1f : 1f - (1f - t) * (1f - t) * (1f - t);
                onFrame();
                if (Progress >= 1f)
                    tick.Pause();
            }).Every(16);
        }
    }
}
