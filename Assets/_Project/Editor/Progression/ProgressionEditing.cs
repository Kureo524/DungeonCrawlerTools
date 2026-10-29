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
    /// <summary>Modifications d'une progression via SerializedObject (annulables avec Ctrl+Z), partagées par l'outil et les tests.</summary>
    public static class ProgressionEditing
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

            var health = Role(StatRole.MaxHealth, limits, "PV max", ChartColors.Hex("#dc4048"), StatVisibility.Hud, StatFormat.Bar);
            var damage = Role(StatRole.Damage, limits, "ATQ", ChartColors.Hex("#f2994a"), StatVisibility.Hud, StatFormat.Number);
            var defense = Role(StatRole.Defense, limits, "DÉF", ChartColors.Hex("#8c9bab"), StatVisibility.Hud, StatFormat.Number);
            var xp = Role(StatRole.XpToNext, limits, "XP", ChartColors.Hex("#4d99ff"), StatVisibility.Hud, StatFormat.Bar);
            xp.growth = StatGrowth.AlwaysIncreases;
            AddStat(serializedObject, health, "Exponentielle", 50, 430);
            AddStat(serializedObject, damage, "Exponentielle", 5, 30);
            AddStat(serializedObject, defense, "Linéaire", 0, 10);
            AddStat(serializedObject, xp, "Exponentielle", 100, 7000);
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
            AddStat(serializedObject, energy, "Linéaire", 100, 200);

            // Attributs à la Elden Ring : un point par niveau, gains réduits au-delà de 5 points.
            AddAttribute(serializedObject, Attribute("Vigueur", "Plus de points de vie.", health.color, (health.id, 15f, 5)));
            AddAttribute(serializedObject, Attribute("Force", "Des tirs plus puissants.", damage.color, (damage.id, 2f, 5)));
            AddAttribute(serializedObject, Attribute("Endurance", "Encaisse mieux les coups.", defense.color, (defense.id, 1f, 0)));
            SetAttributePoints(serializedObject, 1, 2, 1);

            var lynx = Skill("Œil de lynx", SkillEffect.CriticalChance, ChartColors.Hex("#e8c547"), 5f, 10f, 15f);
            var multi = Skill("Tir multiple", SkillEffect.ExtraProjectiles, ChartColors.Hex("#b07cf0"), 1f, 2f);
            var breath = Skill("Second souffle", SkillEffect.Regeneration, ChartColors.Hex("#4fc98a"), 1f, 2f);
            foreach (var skill in new[] { lynx, multi, breath })
                AddSkill(serializedObject, skill);
            AddReward(serializedObject, new LevelReward { level = 3, type = RewardType.UnlockSkill, skillId = lynx.id });
            AddReward(serializedObject, new LevelReward { level = 5, type = RewardType.UnlockSkill, skillId = multi.id });
            AddReward(serializedObject, new LevelReward { level = 8, type = RewardType.UnlockSkill, skillId = breath.id });
            AddReward(serializedObject, new LevelReward { level = 10, type = RewardType.UpgradeSkill, skillId = lynx.id });
            AddReward(serializedObject, new LevelReward { level = 15, type = RewardType.UpgradeSkill, skillId = multi.id });
            AddReward(serializedObject, new LevelReward { level = 16, type = RewardType.UpgradeSkill, skillId = lynx.id });
            AddReward(serializedObject, new LevelReward { level = 18, type = RewardType.UpgradeSkill, skillId = breath.id });

            // Mécaniques : tir et esquive dès le début, onde de choc et bouclier débloqués en route.
            var shot = Mechanic("Tir", MechanicAction.Projectile, MechanicInput.Primary, ChartColors.Hex("#f2c14e"));
            var dodge = Mechanic("Esquive", MechanicAction.Dash, MechanicInput.Dash, ChartColors.Hex("#8c9bab"), energy.id, 25f);
            dodge.upgrades.Add(new MechanicUpgrade { cost = -5f, cooldown = -0.1f });
            var nova = Mechanic("Onde de choc", MechanicAction.Nova, MechanicInput.Secondary, ChartColors.Hex("#e86a4a"), energy.id, 50f);
            nova.unlockedAtStart = false;
            nova.upgrades.Add(new MechanicUpgrade { power = 20f, count = 4 });
            var shield = Mechanic("Bouclier", MechanicAction.Shield, MechanicInput.Ability1, ChartColors.Hex("#7fb7ff"), energy.id, 60f);
            shield.unlockedAtStart = false;
            foreach (var mechanic in new[] { shot, dodge, nova, shield })
                AddMechanic(serializedObject, mechanic);
            AddReward(serializedObject, new LevelReward { level = 4, type = RewardType.UnlockMechanic, mechanicId = nova.id });
            AddReward(serializedObject, new LevelReward { level = 6, type = RewardType.UnlockMechanic, mechanicId = shield.id });
            AddReward(serializedObject, new LevelReward { level = 7, type = RewardType.UpgradeMechanic, mechanicId = dodge.id });
            AddReward(serializedObject, new LevelReward { level = 12, type = RewardType.UpgradeMechanic, mechanicId = nova.id });
            SortRewards(serializedObject);
        }


        public static ProgressionAttribute Attribute(string name, string description, Color color, params (string statId, float perPoint, int softCap)[] bonuses) => new()
        {
            displayName = name,
            description = description,
            color = color,
            maxPoints = 10,
            bonuses = bonuses.Select(b => new AttributeBonus { statId = b.statId, perPoint = b.perPoint, softCap = b.softCap }).ToList()
        };

        /// <summary>Mécanique avec les réglages par défaut de son action.</summary>
        public static CharacterMechanic Mechanic(string name, MechanicAction action, MechanicInput input, Color color, string costStatId = "", float cost = 0f)
        {
            var mechanic = new CharacterMechanic { displayName = name, input = input, color = color, costStatId = costStatId, cost = cost };
            ApplyActionDefaults(mechanic, action);
            return mechanic;
        }

        /// <summary>Réglages de départ d'une action : un tir, une ruée de 3 cases, un soin de 20 PV…</summary>
        public static void ApplyActionDefaults(CharacterMechanic mechanic, MechanicAction action)
        {
            mechanic.action = action;
            (mechanic.power, mechanic.count, mechanic.duration, mechanic.cooldown) = action switch
            {
                MechanicAction.Projectile => (100f, 1, 0f, 0.22f),
                MechanicAction.Nova => (60f, 10, 0f, 2.5f),
                MechanicAction.Dash => (3f, 1, 0.18f, 0.6f),
                MechanicAction.Heal => (20f, 1, 0f, 6f),
                MechanicAction.Shield => (0f, 1, 1.5f, 8f),
                _ => (50f, 1, 3f, 8f)
            };
        }

        public static ProgressionSkill Skill(string name, SkillEffect effect, Color color, params float[] ranks) => new()
        {
            displayName = name,
            effect = effect,
            color = color,
            ranks = ranks.ToList()
        };

        /// <summary>Stat jouant un rôle pour le jeu, bornée par les limites de l'équipe technique.</summary>
        public static ProgressionStat Role(StatRole role, ForgeDesignLimits limits, string name, Color color, StatVisibility visibility, StatFormat format)
        {
            var (min, max) = Bounds(role, limits) ?? (0f, 100f);
            return new ProgressionStat
            {
                displayName = name,
                role = role,
                color = color,
                visibility = visibility,
                format = format,
                decimals = role == StatRole.Speed ? 1 : 0,
                min = min,
                max = max,
                growth = StatGrowth.NeverDecreases
            };
        }

        /// <summary>Bornes imposées par les limites (dev) pour un rôle, ou null pour une stat libre.</summary>
        public static (float min, float max)? Bounds(StatRole role, ForgeDesignLimits limits) => role switch
        {
            StatRole.MaxHealth => (limits.playerHealth.min, limits.playerHealth.max),
            StatRole.Damage => (limits.playerDamage.min, limits.playerDamage.max),
            StatRole.Defense => (limits.playerDefense.min, limits.playerDefense.max),
            StatRole.Speed => (limits.playerSpeed.min, limits.playerSpeed.max),
            StatRole.XpToNext => (limits.xpRequired.min, limits.xpRequired.max),
            _ => null
        };

        /// <summary>Ajoute une stat et remplit sa courbe avec un preset. Renvoie son index.</summary>
        public static int AddStat(SerializedObject serializedObject, ProgressionStat stat, string preset = "Linéaire", float start = 0f, float end = 0f)
        {
            serializedObject.Update();
            var levels = serializedObject.FindProperty("levelCount").intValue;
            var formula = Formula.Parse(FormulaPresets.BuiltIn.First(p => p.name == preset).expression);
            stat.values = Generate(formula, levels, start, end, stat, out _).ToList();

            var stats = serializedObject.FindProperty("stats");
            stats.arraySize++;
            stats.GetArrayElementAtIndex(stats.arraySize - 1).boxedValue = stat;
            serializedObject.ApplyModifiedProperties();
            return stats.arraySize - 1;
        }

        /// <summary>Supprime une stat et les bonus d'attributs qui l'amélioraient.</summary>
        public static void RemoveStat(SerializedObject serializedObject, int index)
        {
            serializedObject.Update();
            var stats = serializedObject.FindProperty("stats");
            var id = stats.GetArrayElementAtIndex(index).FindPropertyRelative("id").stringValue;
            stats.DeleteArrayElementAtIndex(index);
            var mechanics = serializedObject.FindProperty("mechanics");
            for (var m = 0; m < mechanics.arraySize; m++)
            {
                var cost = mechanics.GetArrayElementAtIndex(m).FindPropertyRelative("costStatId");
                if (cost.stringValue == id)
                    cost.stringValue = string.Empty;
            }
            var attributes = serializedObject.FindProperty("attributes");
            for (var a = 0; a < attributes.arraySize; a++)
            {
                var bonuses = attributes.GetArrayElementAtIndex(a).FindPropertyRelative("bonuses");
                for (var b = bonuses.arraySize - 1; b >= 0; b--)
                    if (bonuses.GetArrayElementAtIndex(b).FindPropertyRelative("statId").stringValue == id)
                        bonuses.DeleteArrayElementAtIndex(b);
            }
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Ajoute un élément à une liste sérialisée (attributs, compétences, récompenses, bonus…). Renvoie son index.</summary>
        public static int Append(SerializedObject serializedObject, string path, object value)
        {
            serializedObject.Update();
            var list = serializedObject.FindProperty(path);
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).boxedValue = value;
            serializedObject.ApplyModifiedProperties();
            return list.arraySize - 1;
        }

        public static void RemoveAt(SerializedObject serializedObject, string path, int index)
        {
            serializedObject.Update();
            serializedObject.FindProperty(path).DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
        }

        public static int AddAttribute(SerializedObject serializedObject, ProgressionAttribute attribute) => Append(serializedObject, "attributes", attribute);
        public static int AddSkill(SerializedObject serializedObject, ProgressionSkill skill) => Append(serializedObject, "skills", skill);
        public static int AddReward(SerializedObject serializedObject, LevelReward reward) => Append(serializedObject, "rewards", reward);
        public static int AddMechanic(SerializedObject serializedObject, CharacterMechanic mechanic) => Append(serializedObject, "mechanics", mechanic);

        /// <summary>Supprime une mécanique et les récompenses qui la débloquaient ou l'amélioraient.</summary>
        public static void RemoveMechanic(SerializedObject serializedObject, int index)
        {
            serializedObject.Update();
            var mechanics = serializedObject.FindProperty("mechanics");
            var id = mechanics.GetArrayElementAtIndex(index).FindPropertyRelative("id").stringValue;
            mechanics.DeleteArrayElementAtIndex(index);
            var rewards = serializedObject.FindProperty("rewards");
            for (var r = rewards.arraySize - 1; r >= 0; r--)
            {
                var reward = rewards.GetArrayElementAtIndex(r);
                var type = (RewardType)reward.FindPropertyRelative("type").enumValueIndex;
                if (type is RewardType.UnlockMechanic or RewardType.UpgradeMechanic && reward.FindPropertyRelative("mechanicId").stringValue == id)
                    rewards.DeleteArrayElementAtIndex(r);
            }
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Supprime une compétence et les récompenses qui la débloquaient ou l'amélioraient.</summary>
        public static void RemoveSkill(SerializedObject serializedObject, int index)
        {
            serializedObject.Update();
            var skills = serializedObject.FindProperty("skills");
            var id = skills.GetArrayElementAtIndex(index).FindPropertyRelative("id").stringValue;
            skills.DeleteArrayElementAtIndex(index);
            var rewards = serializedObject.FindProperty("rewards");
            for (var r = rewards.arraySize - 1; r >= 0; r--)
            {
                var reward = rewards.GetArrayElementAtIndex(r);
                var type = (RewardType)reward.FindPropertyRelative("type").enumValueIndex;
                if (type is RewardType.UnlockSkill or RewardType.UpgradeSkill && reward.FindPropertyRelative("skillId").stringValue == id)
                    rewards.DeleteArrayElementAtIndex(r);

            }
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Remplace les points d'attributs par <paramref name="amount"/> point(s) tous les <paramref name="every"/> niveau(x) à partir de <paramref name="from"/>.</summary>
        public static void SetAttributePoints(SerializedObject serializedObject, int amount, int from, int every)
        {
            serializedObject.Update();
            var rewards = serializedObject.FindProperty("rewards");
            for (var r = rewards.arraySize - 1; r >= 0; r--)
                if (rewards.GetArrayElementAtIndex(r).FindPropertyRelative("type").enumValueIndex == (int)RewardType.AttributePoints)
                    rewards.DeleteArrayElementAtIndex(r);
            var levels = serializedObject.FindProperty("levelCount").intValue;
            for (var level = Mathf.Max(1, from); amount > 0 && level <= levels; level += Mathf.Max(1, every))
            {
                rewards.arraySize++;
                rewards.GetArrayElementAtIndex(rewards.arraySize - 1).boxedValue = new LevelReward { level = level, type = RewardType.AttributePoints, amount = amount };
            }
            serializedObject.ApplyModifiedProperties();
            SortRewards(serializedObject);
        }

        /// <summary>Trie les récompenses par niveau (l'ordre d'un même niveau est conservé).</summary>
        public static void SortRewards(SerializedObject serializedObject)
        {
            serializedObject.Update();
            var rewards = serializedObject.FindProperty("rewards");
            var sorted = Enumerable.Range(0, rewards.arraySize).Select(i => (LevelReward)rewards.GetArrayElementAtIndex(i).boxedValue)
                .OrderBy(r => r.level).ToList();
            for (var i = 0; i < sorted.Count; i++)
                rewards.GetArrayElementAtIndex(i).boxedValue = sorted[i];
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Points d'attributs gagnés du niveau 1 au dernier niveau.</summary>
        public static int TotalAttributePoints(PlayerProgression progression) =>
            progression.Rewards.Where(r => r.type == RewardType.AttributePoints && r.level >= 1 && r.level <= progression.MaxLevel).Sum(r => r.amount);

        /// <summary>Points que les attributs peuvent recevoir au total (somme des maximums).</summary>
        public static int AttributeCapacity(PlayerProgression progression) => progression.Attributes.Sum(a => Mathf.Max(0, a.maxPoints));

        /// <summary>Bonus maximal que les attributs peuvent ajouter à une stat (tous les attributs à fond).</summary>
        public static float MaxAttributeBonus(PlayerProgression progression, ProgressionStat stat) =>
            progression.Attributes.Sum(a => a.BonusFor(stat.id, a.maxPoints));

        /// <summary>Résumé des récompenses d'un niveau : « +1 point · Tir multiple · Œil de lynx rang 2 ».</summary>
        public static string RewardSummary(PlayerProgression progression, int level)
        {
            var parts = new List<string>();
            var points = progression.RewardsAt(level).Where(r => r.type == RewardType.AttributePoints).Sum(r => r.amount);
            if (points > 0)
                parts.Add(points > 1 ? $"+{points} points" : "+1 point");
            foreach (var reward in progression.RewardsAt(level).Where(r => r.type != RewardType.AttributePoints))
            {
                var skill = progression.FindSkill(reward.skillId);
                var mechanic = progression.FindMechanic(reward.mechanicId);
                parts.Add(reward.type switch
                {
                    RewardType.UnlockSkill => skill != null ? skill.displayName : "(compétence manquante)",
                    RewardType.UpgradeSkill => skill != null ? $"{skill.displayName} rang {RankAt(progression, skill, level)}" : "(compétence manquante)",
                    RewardType.UnlockMechanic => mechanic != null ? $"Action « {mechanic.displayName} »" : "(mécanique manquante)",
                    RewardType.UpgradeMechanic => mechanic != null ? $"{mechanic.displayName} rang {RankAt(progression, mechanic, level)}" : "(mécanique manquante)",
                    _ => reward.note
                });
            }
            return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        /// <summary>Rang d'une compétence une fois les récompenses de ce niveau obtenues (mêmes règles que le jeu).</summary>
        public static int RankAt(PlayerProgression progression, ProgressionSkill skill, int level) =>
            RankAt(progression.Rewards.Where(r => r.skillId == skill.id), RewardType.UnlockSkill, RewardType.UpgradeSkill, skill.MaxRank, 0, level);

        /// <summary>Rang d'une mécanique à ce niveau : 1 dès le début si elle est débloquée d'office, puis selon les récompenses.</summary>
        public static int RankAt(PlayerProgression progression, CharacterMechanic mechanic, int level) =>
            RankAt(progression.Rewards.Where(r => r.mechanicId == mechanic.id), RewardType.UnlockMechanic, RewardType.UpgradeMechanic,
                mechanic.MaxRank, mechanic.unlockedAtStart ? 1 : 0, level);

        static int RankAt(IEnumerable<LevelReward> rewards, RewardType unlock, RewardType upgrade, int maxRank, int startRank, int level)
        {
            var rank = startRank;
            foreach (var reward in rewards.Where(r => r.level <= level).OrderBy(r => r.level))
            {
                if ((reward.type == unlock && rank == 0) || (reward.type == upgrade && rank > 0))
                    rank = Mathf.Min(maxRank, rank + 1);
            }
            return rank;
        }

        /// <summary>Ce que fait la mécanique à ce rang, en une phrase : « tire 3 projectiles à 100 % de l'ATQ ».</summary>
        public static string Describe(PlayerProgression progression, CharacterMechanic mechanic, int rank)
        {
            var v = mechanic.At(rank);
            var action = mechanic.action switch
            {
                MechanicAction.Projectile => $"tire {v.Count} projectile(s) à {v.Power:0.#} % de l'attaque",
                MechanicAction.Nova => $"{v.Count} projectiles tout autour à {v.Power:0.#} % de l'attaque",
                MechanicAction.Dash => $"ruée de {v.Power:0.#} cases en {v.Duration:0.##} s (invincible)",
                MechanicAction.Heal => $"rend {v.Power:0.#} PV",
                MechanicAction.Shield => $"invincible pendant {v.Duration:0.##} s",
                _ => $"+{v.Power:0.#} % de vitesse pendant {v.Duration:0.##} s"
            };
            var stat = progression.FindStat(mechanic.costStatId);
            var cost = stat != null && v.Cost > 0f ? $"coûte {v.Cost:0.#} {stat.displayName}" : "gratuite";
            return $"{action} · {cost} · recharge {v.Cooldown:0.##} s";
        }

        /// <summary>Réglages utiles pour une action : libellés de « puissance », « nombre » et « durée » (null = réglage inutilisé).</summary>
        public static (string power, string count, string duration) ActionFields(MechanicAction action) => action switch
        {
            MechanicAction.Projectile => ("Dégâts (% de l'ATQ)", "Projectiles par tir", null),
            MechanicAction.Nova => ("Dégâts (% de l'ATQ)", "Projectiles autour", null),
            MechanicAction.Dash => ("Distance (cases)", null, "Durée (s)"),
            MechanicAction.Heal => ("PV rendus", null, null),
            MechanicAction.Shield => (null, null, "Invincibilité (s)"),
            _ => ("Vitesse en plus (%)", null, "Durée (s)")
        };

        public static string ActionLabel(MechanicAction action) => action switch
        {
            MechanicAction.Projectile => "Attaque : projectile visé",
            MechanicAction.Nova => "Attaque : onde tout autour",
            MechanicAction.Dash => "Mouvement : ruée",
            MechanicAction.Heal => "Soin",
            MechanicAction.Shield => "Défense : bouclier",
            _ => "Mouvement : sprint"
        };

        public static string InputLabel(MechanicInput input) => input switch
        {
            MechanicInput.Primary => "Clic gauche (gâchette droite)",
            MechanicInput.Secondary => "Clic droit (gâchette gauche)",
            MechanicInput.Dash => "Espace / Maj (B)",
            MechanicInput.Ability1 => "Touche 1 (LB)",
            MechanicInput.Ability2 => "Touche 2 (RB)",
            _ => "Touche 3 (X)"
        };

        public static void MoveStat(SerializedObject serializedObject, int index, int offset)
        {
            serializedObject.Update();
            var stats = serializedObject.FindProperty("stats");
            var target = index + offset;
            if (target < 0 || target >= stats.arraySize)
                return;
            stats.MoveArrayElement(index, target);
            serializedObject.ApplyModifiedProperties();
        }

        public static void SetValues(SerializedObject serializedObject, int statIndex, IReadOnlyList<float> values)
        {
            serializedObject.Update();
            var property = serializedObject.FindProperty("stats").GetArrayElementAtIndex(statIndex).FindPropertyRelative("values");
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).floatValue = values[i];
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Change le nombre de niveaux : les courbes sont prolongées (dernière valeur répétée) ou coupées, les récompenses des niveaux retirés supprimées.</summary>
        public static void SetLevelCount(SerializedObject serializedObject, int count)
        {
            serializedObject.Update();
            serializedObject.FindProperty("levelCount").intValue = count;
            var stats = serializedObject.FindProperty("stats");
            for (var s = 0; s < stats.arraySize; s++)
            {
                var values = stats.GetArrayElementAtIndex(s).FindPropertyRelative("values");
                var last = values.arraySize > 0 ? values.GetArrayElementAtIndex(values.arraySize - 1).floatValue : 0f;
                var previous = values.arraySize;
                values.arraySize = count;
                for (var i = previous; i < count; i++)
                    values.GetArrayElementAtIndex(i).floatValue = last;
            }
            // Les récompenses des niveaux retirés disparaissent avec eux.
            var rewards = serializedObject.FindProperty("rewards");
            for (var r = rewards.arraySize - 1; r >= 0; r--)
                if (rewards.GetArrayElementAtIndex(r).FindPropertyRelative("level").intValue > count)
                    rewards.DeleteArrayElementAtIndex(r);
            serializedObject.ApplyModifiedProperties();
        }


        /// <summary>
        /// Valeurs d'une formule pour chaque niveau, arrondies aux décimales de la stat et ramenées dans ses bornes.
        /// <paramref name="clamped"/> compte les niveaux ramenés dans les bornes. Lève <see cref="FormulaException"/> si un résultat n'est pas un nombre.
        /// </summary>
        public static float[] Generate(Formula formula, int levels, float start, float end, ProgressionStat stat, out int clamped)
        {
            var raw = formula.EvaluateLevels(levels, start, end);
            var values = new float[levels];
            clamped = 0;
            for (var i = 0; i < levels; i++)
            {
                if (double.IsNaN(raw[i]) || double.IsInfinity(raw[i]))
                    throw new FormulaException($"La formule ne donne pas de nombre au niveau {i + 1} (division par zéro, racine d'un négatif…).", 0);
                var value = Round((float)raw[i], stat.decimals);
                var bounded = Mathf.Clamp(value, stat.min, stat.max);
                if (!Mathf.Approximately(bounded, value))
                    clamped++;
                values[i] = bounded;
            }
            return values;
        }

        public static float Round(float value, int decimals)
        {
            var factor = Mathf.Pow(10f, Mathf.Clamp(decimals, 0, 3));
            return Mathf.Round(value * factor) / factor;
        }

        /// <summary>Niveaux (index) où la courbe ne respecte pas l'évolution demandée.</summary>
        public static IEnumerable<int> GrowthViolations(ProgressionStat stat) => GrowthViolations(stat.values, stat.growth);

        public static IEnumerable<int> GrowthViolations(IReadOnlyList<float> values, StatGrowth growth) =>
            growth == StatGrowth.Free
                ? Enumerable.Empty<int>()
                : Enumerable.Range(1, Math.Max(0, values.Count - 1))
                    .Where(i => growth == StatGrowth.AlwaysIncreases ? values[i] <= values[i - 1] : values[i] < values[i - 1]);

        public static string RoleLabel(StatRole role) => role switch
        {
            StatRole.MaxHealth => "PV max",
            StatRole.Damage => "Dégâts",
            StatRole.Defense => "Défense",
            StatRole.Speed => "Vitesse",
            StatRole.XpToNext => "XP pour le niveau suivant",
            _ => "Aucun (stat libre)"
        };

        public static string VisibilityLabel(StatVisibility visibility) => visibility switch
        {
            StatVisibility.Hud => "À l'écran (HUD)",
            StatVisibility.CharacterSheet => "Fiche du personnage",
            _ => "Masquée"
        };

        public static string FormatLabel(StatFormat format) => format switch
        {
            StatFormat.Bar => "Barre",
            StatFormat.Percent => "Pourcentage",
            _ => "Nombre"
        };

        public static string SkillEffectLabel(SkillEffect effect) => effect switch
        {
            SkillEffect.ExtraProjectiles => "Projectiles en plus",
            SkillEffect.CriticalChance => "Coups critiques (+ %)",
            SkillEffect.FireRate => "Cadence de tir (+ %)",
            SkillEffect.DashCooldown => "Recharge de l'esquive (- %)",
            SkillEffect.Regeneration => "Régénération (PV/s)",
            _ => "Aucun (description seulement)"
        };

        public static string RewardTypeLabel(RewardType type) => type switch
        {
            RewardType.UnlockSkill => "Débloquer une compétence",
            RewardType.UpgradeSkill => "Améliorer une compétence",
            RewardType.Note => "Note (texte libre)",
            RewardType.UnlockMechanic => "Débloquer une mécanique",
            RewardType.UpgradeMechanic => "Améliorer une mécanique",

            _ => "Points d'attributs"
        };

        public static string GrowthLabel(StatGrowth growth) => growth switch

        {
            StatGrowth.NeverDecreases => "Ne baisse jamais",
            StatGrowth.AlwaysIncreases => "Augmente à chaque niveau",
            _ => "Libre"
        };
    }
}
