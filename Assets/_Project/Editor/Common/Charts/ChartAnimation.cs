using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        // Graphiques animés, retrouvés à partir de leur élément pour rejouer l'animation à l'ouverture d'un panneau.
        static readonly ConditionalWeakTable<VisualElement, ChartAnimation> Registry = new();
        const string AnimatedClass = "forge-animated";

        /// <summary>Barres qui grandissent par transition USS (largeur ou hauteur) : rejouées en repartant de zéro.</summary>
        static readonly (string className, bool vertical)[] GrowingBars =
        {
            ("forge-bar-row__fill", false), ("forge-meter__fill", false), ("forge-bar__fill", true)
        };

        public ChartAnimation(VisualElement owner, Action onFrame)
        {
            this.owner = owner;
            this.onFrame = onFrame;
            owner.AddToClassList(AnimatedClass);
            Registry.AddOrUpdate(owner, this);
        }

        /// <summary>
        /// Rejoue les animations de tous les graphiques et barres d'un panneau, comme à sa première ouverture.
        /// </summary>
        public static void ReplayAll(VisualElement root)
        {
            root.Query(className: AnimatedClass).ForEach(element =>
            {
                if (Registry.TryGetValue(element, out var animation) && animation.played)
                    animation.Play();
            });

            foreach (var (className, vertical) in GrowingBars)
            {
                var bars = root.Query(className: className).ToList();
                if (bars.Count == 0)
                    continue;
                // Sans transition, la barre revient à zéro ; à l'image suivante, la transition la ramène à sa valeur.
                var targets = new List<StyleLength>();
                foreach (var bar in bars)
                {
                    targets.Add(vertical ? bar.style.height : bar.style.width);
                    bar.style.transitionDuration = new List<TimeValue> { new(0f) };
                    if (vertical)
                        bar.style.height = 0f;
                    else
                        bar.style.width = 0f;
                }
                root.schedule.Execute(() =>
                {
                    for (var i = 0; i < bars.Count; i++)
                    {
                        bars[i].style.transitionDuration = StyleKeyword.Null;
                        if (vertical)
                            bars[i].style.height = targets[i];
                        else
                            bars[i].style.width = targets[i];
                    }
                }).StartingIn(30);
            }
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
