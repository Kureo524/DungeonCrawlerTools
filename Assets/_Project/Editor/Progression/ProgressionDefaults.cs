using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Common.Charts;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Progression
{
    /// <summary>Progression proposée à la création : stats, attributs, compétences et mécaniques d'exemple, prêtes à jouer.</summary>
    public static class ProgressionDefaults
    {
        /// <summary>Progression de départ : PV, dégâts, défense et XP, courbes générées avec les presets intégrés.</summary>
        public static void Reset(SerializedObject serializedObject, ForgeDesignLimits limits)
        {
            serializedObject.FindProperty("levelCount").intValue = limits.maxLevel;
            serializedObject.FindProperty("stats").arraySize = 0;
            serializedObject.FindProperty("attributes").arraySize = 0;
            serializedObject.FindProperty("skills").arraySize = 0;
            serializedObject.FindProperty("mechanics").arraySize = 0;
            serializedObject.FindProperty("rewards").arraySize = 0;
            serializedObject.ApplyModifiedProperties();

            var stats = AddStats(serializedObject, limits);
            AddAttributes(serializedObject, stats);
            AddSkills(serializedObject);
            AddMechanics(serializedObject, stats.energy);
            ProgressionEditing.SortRewards(serializedObject);
        }

        /// <summary>PV, ATQ, DÉF et XP (rôles du jeu), et une jauge d'énergie pour les mécaniques.</summary>
        static (ProgressionStat health, ProgressionStat damage, ProgressionStat defense, ProgressionStat energy) AddStats(SerializedObject serializedObject, ForgeDesignLimits limits)
        {
            var health = ProgressionEditing.Role(StatRole.MaxHealth, limits, "PV max", ChartColors.Hex("#dc4048"), StatVisibility.Hud, StatFormat.Bar);
            var damage = ProgressionEditing.Role(StatRole.Damage, limits, "ATQ", ChartColors.Hex("#f2994a"), StatVisibility.Hud, StatFormat.Number);
            var defense = ProgressionEditing.Role(StatRole.Defense, limits, "DÉF", ChartColors.Hex("#8c9bab"), StatVisibility.Hud, StatFormat.Number);
            var xp = ProgressionEditing.Role(StatRole.XpToNext, limits, "XP", ChartColors.Hex("#4d99ff"), StatVisibility.Hud, StatFormat.Bar);
            xp.growth = StatGrowth.AlwaysIncreases;
            ProgressionEditing.AddStat(serializedObject, health, "Exponentielle", 50, 430);
            ProgressionEditing.AddStat(serializedObject, damage, "Exponentielle", 5, 30);
            ProgressionEditing.AddStat(serializedObject, defense, "Linéaire", 0, 10);
            ProgressionEditing.AddStat(serializedObject, xp, "Exponentielle", 100, 7000);
            var energy = new ProgressionStat
            {
                displayName = "Énergie",
                color = ChartColors.Hex("#5fc8d8"),
                visibility = StatVisibility.Hud,
                format = StatFormat.Bar,
                min = 0f,
                max = 300f,
                resource = true,
                regenPerSecond = 20f
            };
            ProgressionEditing.AddStat(serializedObject, energy, "Linéaire", 100, 200);
            return (health, damage, defense, energy);
        }

        static void AddAttributes(SerializedObject serializedObject, (ProgressionStat health, ProgressionStat damage, ProgressionStat defense, ProgressionStat energy) stats)
        {
            // Attributs à la Elden Ring : un point par niveau, gains réduits au-delà de 5 points.
            ProgressionEditing.AddAttribute(serializedObject, ProgressionEditing.Attribute("Vigueur", "Plus de points de vie.", stats.health.color, (stats.health.id, 15f, 5)));
            ProgressionEditing.AddAttribute(serializedObject, ProgressionEditing.Attribute("Force", "Des tirs plus puissants.", stats.damage.color, (stats.damage.id, 2f, 5)));
            ProgressionEditing.AddAttribute(serializedObject, ProgressionEditing.Attribute("Endurance", "Encaisse mieux les coups.", stats.defense.color, (stats.defense.id, 1f, 0)));
            ProgressionEditing.SetAttributePoints(serializedObject, 1, 2, 1);
        }

        /// <summary>Trois compétences débloquées puis améliorées au fil des niveaux.</summary>
        static void AddSkills(SerializedObject serializedObject)
        {
            var lynx = ProgressionEditing.Skill("Œil de lynx", SkillEffect.CriticalChance, ChartColors.Hex("#e8c547"), 5f, 10f, 15f);
            var multi = ProgressionEditing.Skill("Tir multiple", SkillEffect.ExtraProjectiles, ChartColors.Hex("#b07cf0"), 1f, 2f);
            var breath = ProgressionEditing.Skill("Second souffle", SkillEffect.Regeneration, ChartColors.Hex("#4fc98a"), 1f, 2f);
            foreach (var skill in new[] { lynx, multi, breath })
                ProgressionEditing.AddSkill(serializedObject, skill);
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 3, type = RewardType.UnlockSkill, skillId = lynx.id });
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 5, type = RewardType.UnlockSkill, skillId = multi.id });
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 8, type = RewardType.UnlockSkill, skillId = breath.id });
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 10, type = RewardType.UpgradeSkill, skillId = lynx.id });
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 15, type = RewardType.UpgradeSkill, skillId = multi.id });
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 16, type = RewardType.UpgradeSkill, skillId = lynx.id });
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 18, type = RewardType.UpgradeSkill, skillId = breath.id });
        }

        static void AddMechanics(SerializedObject serializedObject, ProgressionStat energy)
        {
            // Mécaniques : tir et esquive dès le début, onde de choc et bouclier débloqués en route.
            var shot = ProgressionEditing.Mechanic("Tir", MechanicAction.Projectile, MechanicInput.Primary, ChartColors.Hex("#f2c14e"));
            var dodge = ProgressionEditing.Mechanic("Esquive", MechanicAction.Dash, MechanicInput.Dash, ChartColors.Hex("#8c9bab"), energy.id, 25f);
            dodge.upgrades.Add(new MechanicUpgrade { cost = -5f, cooldown = -0.1f });
            var nova = ProgressionEditing.Mechanic("Onde de choc", MechanicAction.Nova, MechanicInput.Secondary, ChartColors.Hex("#e86a4a"), energy.id, 50f);
            nova.unlockedAtStart = false;
            nova.upgrades.Add(new MechanicUpgrade { power = 20f, count = 4 });
            var shield = ProgressionEditing.Mechanic("Bouclier", MechanicAction.Shield, MechanicInput.Ability1, ChartColors.Hex("#7fb7ff"), energy.id, 60f);
            shield.unlockedAtStart = false;
            foreach (var mechanic in new[] { shot, dodge, nova, shield })
                ProgressionEditing.AddMechanic(serializedObject, mechanic);
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 4, type = RewardType.UnlockMechanic, mechanicId = nova.id });
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 6, type = RewardType.UnlockMechanic, mechanicId = shield.id });
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 7, type = RewardType.UpgradeMechanic, mechanicId = dodge.id });
            ProgressionEditing.AddReward(serializedObject, new LevelReward { level = 12, type = RewardType.UpgradeMechanic, mechanicId = nova.id });
        }
    }
}
