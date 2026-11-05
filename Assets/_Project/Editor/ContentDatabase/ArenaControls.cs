using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Réglages de l'arène pour les conditions autres que la distance et la ligne de vue (PV, niveau, étage, biome, mécaniques, marqueurs…) :
    /// seuls ceux que les règles et les attaques de l'ennemi utilisent sont proposés.
    /// </summary>
    internal sealed class ArenaControls
    {
        readonly ArenaSituation situation;
        readonly VisualElement root;
        readonly Action changed;
        string builtSignature;

        /// <param name="changed">Appelé après chaque réglage, pour réévaluer l'arène.</param>
        public ArenaControls(ArenaSituation situation, VisualElement root, Action changed)
        {
            this.situation = situation;
            this.root = root;
            this.changed = changed;
        }

        /// <summary>
        /// Réglages de test des conditions utilisées par les règles de l'ennemi (la distance et la ligne de vue se règlent dans l'arène).
        /// Reconstruits seulement quand l'ensemble des conditions utilisées change, pour ne pas perdre la saisie en cours.
        /// </summary>
        public void Rebuild(EnemyData data)
        {
            var conditions = data.Patterns.SelectMany(p => p.conditions).Concat(data.Attacks.SelectMany(a => a.conditions)).ToList();
            var types = conditions.Select(c => c.type).Distinct().OrderBy(t => t).ToList();
            var mechanics = conditions.Where(c => c.type == EnemyConditionType.MechanicAtLeast && !string.IsNullOrEmpty(c.flag)).Select(c => c.flag).Distinct().ToList();
            var flags = conditions.Where(c => c.type == EnemyConditionType.FlagSet && !string.IsNullOrEmpty(c.flag)).Select(c => c.flag).Distinct().ToList();
            var signature = string.Join(",", types) + "|" + string.Join(",", mechanics) + "|" + string.Join(",", flags);
            if (signature == builtSignature)
                return;
            builtSignature = signature;
            root.Clear();

            foreach (var type in types)
            {
                var control = ConditionControl(type, data);
                if (control != null)
                    root.Add(control);
            }
            AddMechanicControls(mechanics);
            AddFlagControls(flags);
            if (root.childCount == 0)
                root.Add(ForgeUI.Label("Les réglages des autres conditions (PV, niveau, biome, mécaniques…) apparaissent ici dès qu'une règle les utilise.",
                    "forge-hint"));
        }

        /// <summary>Réglage d'une condition (PV, niveau, étage, alliés, phase, durée, biome, type de salle), ou null si elle se règle dans l'arène.</summary>
        VisualElement ConditionControl(EnemyConditionType type, EnemyData data)
        {
            var limits = ForgeDesignLimits.Instance;
            switch (type)
            {
                case EnemyConditionType.PlayerHealthBelow:
                    return Slider("PV du joueur (%)", 0, 100, Mathf.RoundToInt(situation.PlayerHealth * 100f), v => situation.PlayerHealth = v / 100f);
                case EnemyConditionType.OwnHealthBelow:
                    return Slider("PV de l'ennemi (%)", 0, 100, Mathf.RoundToInt(situation.OwnHealth * 100f), v => situation.OwnHealth = v / 100f);
                case EnemyConditionType.PlayerLevelAtLeast:
                    return Slider("Niveau du joueur", 1, limits.maxLevelCap, situation.PlayerLevel, v => situation.PlayerLevel = v);
                case EnemyConditionType.FloorIn:
                    return Slider("Étage", 1, limits.floorCount, situation.Floor, v => situation.Floor = v);
                case EnemyConditionType.AlliesAtMost:
                    return Slider("Alliés restants", 0, limits.maxEnemiesPerRoom, situation.Allies, v => situation.Allies = v);
                case EnemyConditionType.PhaseIs:
                    return Slider("Phase de l'ennemi", 0, Mathf.Max(1, data.Phases.Count), situation.Phase, v => situation.Phase = v);
                case EnemyConditionType.FightLongerThan:
                    return Slider("Durée du combat (s)", 0, 300, Mathf.RoundToInt(situation.FightTime), v => situation.FightTime = v);
                case EnemyConditionType.BiomeIs:
                    return BiomeControl();
                case EnemyConditionType.RoomTypeIs:
                    return RoomTypeControl();
            }
            return null;
        }

        DropdownField BiomeControl()
        {
            var biomes = AssetFactory.LoadAll<BiomeData>();
            var names = new List<string> { "Autre" }.Concat(biomes.Select(b => ForgeText.NameOrRedacted(b.DisplayName))).ToList();
            var biome = new DropdownField("Biome", names, Mathf.Max(0, biomes.IndexOf(situation.Biome) + 1));
            biome.RegisterValueChangedCallback(evt =>
            {
                var index = names.IndexOf(evt.newValue);
                situation.Biome = index > 0 ? biomes[index - 1] : null;
                changed();
            });
            return biome;
        }

        EnumField RoomTypeControl()
        {
            var roomType = new EnumField("Type de salle", situation.RoomType);
            roomType.RegisterValueChangedCallback(evt =>
            {
                situation.RoomType = (RoomType)evt.newValue;
                changed();
            });
            return roomType;
        }

        /// <summary>Rang de chaque mécanique du joueur testée par une condition.</summary>
        void AddMechanicControls(IEnumerable<string> mechanics)
        {
            var progression = TheDungeon.Editor.Play.GameLauncher.ActiveProgression;
            foreach (var id in mechanics)
            {
                var mechanic = progression?.Mechanics.FirstOrDefault(m => m.id == id);
                if (mechanic == null)
                    continue;
                var key = id;
                root.Add(Slider($"{mechanic.displayName} (rang)", 0, mechanic.MaxRank, situation.MechanicLevel(id), v => situation.Mechanics[key] = v));
            }
        }

        /// <summary>Marqueurs testés par une condition, à cocher.</summary>
        void AddFlagControls(IEnumerable<string> flags)
        {
            foreach (var flag in flags)
            {
                var key = flag;
                var toggle = new Toggle($"Marqueur {ForgeText.Name(flag)}") { value = situation.HasFlag(flag) };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                        situation.Flags.Add(key);
                    else
                        situation.Flags.Remove(key);
                    changed();
                });
                root.Add(toggle);
            }
        }

        SliderInt Slider(string label, int min, int max, int value, Action<int> apply)
        {
            var slider = new SliderInt(label, min, max) { value = value, showInputField = true };
            slider.RegisterValueChangedCallback(evt =>
            {
                apply(evt.newValue);
                changed();
            });
            return slider;
        }
    }
}
