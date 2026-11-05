using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Progression;
using TheDungeon.Game;
using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Tests
{
    /// <summary>Progression : formules, presets, import / export SVG, ancien format, stats libres, attributs, compétences et récompenses en jeu.</summary>
    public class ProgressionTests
    {
        ForgeDesignLimits limits;

        [SetUp]
        public void SetUp() => limits = ScriptableObject.CreateInstance<ForgeDesignLimits>();

        [TearDown]
        public void TearDown() => TestData.Destroy(limits);

        PlayerProgression Default() => TestData.Create<PlayerProgression>("PRG_Test", so => ProgressionDefaults.Reset(so, limits));

        static double Evaluate(string text) => Formula.Parse(text).EvaluateLevels(1, 0, 0)[0];

        [Test]
        public void Formula_FollowsOperatorPrecedence()
        {
            Assert.AreEqual(14, Evaluate("2 + 3 * 4"));
            Assert.AreEqual(-4, Evaluate("-2^2"));
            Assert.AreEqual(512, Evaluate("2^3^2"));
            Assert.AreEqual(3, Evaluate("max(1, min(3, 5))"));
        }

        [Test]
        public void Formula_UsesLevelVariables()
        {
            var values = Formula.Parse("start + (end - start) * t").EvaluateLevels(3, 10, 30);
            CollectionAssert.AreEqual(new double[] { 10, 20, 30 }, values);
            CollectionAssert.AreEqual(new double[] { 1, 2, 3 }, Formula.Parse("n").EvaluateLevels(3, 0, 0));
        }

        [Test]
        public void Formula_ExplainsErrors()
        {
            Assert.IsFalse(Formula.TryParse("start * level", out _, out var unknown));
            StringAssert.Contains("level", unknown);
            Assert.IsFalse(Formula.TryParse("pow(2)", out _, out var arity));
            StringAssert.Contains("2 valeur", arity);
            Assert.IsFalse(Formula.TryParse("(1 + 2", out _, out var parenthesis));
            StringAssert.Contains(")", parenthesis);
        }

        [Test]
        public void BuiltInPresets_GoFromStartToEnd()
        {
            foreach (var preset in FormulaPresets.BuiltIn)
            {
                var values = Formula.Parse(preset.expression).EvaluateLevels(20, 10, 100);
                Assert.AreEqual(10, values[0], 0.001, preset.name);
                Assert.AreEqual(100, values[^1], 0.001, preset.name);
            }
        }

        [Test]
        public void Generate_RoundsAndClampsToStatBounds()
        {
            var stat = new ProgressionStat { min = 0, max = 25, decimals = 0 };
            var values = ProgressionEditing.Generate(Formula.Parse("start + (end - start) * t"), 3, 10.4f, 30, stat, out var clamped);
            CollectionAssert.AreEqual(new[] { 10f, 20f, 25f }, values);
            Assert.AreEqual(1, clamped);
        }

        [Test]
        public void Svg_RoundTripKeepsEverything()
        {
            var original = Default();
            var imported = ScriptableObject.CreateInstance<PlayerProgression>();

            var redrawn = ProgressionSvg.Import(ProgressionSvg.Export(original), imported);

            CollectionAssert.IsEmpty(redrawn);
            Assert.AreEqual(original.MaxLevel, imported.MaxLevel);
            CollectionAssert.AreEqual(original.Stats.Select(s => s.displayName), imported.Stats.Select(s => s.displayName));
            for (var i = 0; i < original.Stats.Count; i++)
                CollectionAssert.AreEqual(original.Stats[i].values, imported.Stats[i].values);
            TestData.Destroy(original, imported);
        }

        [Test]
        public void Svg_RedrawnCurveIsImported()
        {
            var original = Default();
            var document = XDocument.Parse(ProgressionSvg.Export(original));
            var group = document.Descendants().First(e => e.Name.LocalName == "g" && (string)e.Attribute("data-stat-index") == "0");
            var plot = ((string)group.Attribute("data-plot")).Split(' ').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var polyline = group.Descendants().First(e => e.Name.LocalName == "polyline");
            // Courbe redessinée comme dans Inkscape : un tracé horizontal à mi-hauteur.
            var middle = plot[1] + plot[3] / 2f;
            polyline.ReplaceWith(new XElement(polyline.Name.Namespace + "path", new XAttribute("class", "forge-curve"),
                new XAttribute("d", FormattableString.Invariant($"M {plot[0]} {middle} H {plot[0] + plot[2]}"))));
            var imported = ScriptableObject.CreateInstance<PlayerProgression>();

            var redrawn = ProgressionSvg.Import(document.ToString(), imported);

            Assert.AreEqual(1, redrawn.Count);
            var expected = Mathf.Round((float)group.Attribute("data-y-max") / 2f);
            Assert.IsTrue(imported.Stats[0].values.All(v => Mathf.Abs(v - expected) <= 1f), string.Join(", ", imported.Stats[0].values));
            CollectionAssert.AreEqual(original.Stats[1].values, imported.Stats[1].values);
            TestData.Destroy(original, imported);
        }

        [Test]
        public void Svg_ShapeFromAnyDrawingIsNormalized()
        {
            const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><g transform=\"translate(50, 20)\"><path d=\"M0 100 l50 -50 L100 0\"/></g></svg>";
            CollectionAssert.AreEqual(new[] { 0f, 0.5f, 1f }, ProgressionSvg.ImportShape(svg, 3));
        }

        [Test]
        public void Svg_WithoutForgeData_IsRejected()
        {
            var target = ScriptableObject.CreateInstance<PlayerProgression>();
            Assert.Throws<System.FormatException>(() => ProgressionSvg.Import("<svg xmlns=\"http://www.w3.org/2000/svg\"/>", target));
            TestData.Destroy(target);
        }

        [Test]
        public void OldProgression_IsConvertedToCurves()
        {
            var progression = TestData.Create<PlayerProgression>("PRG_Old", so =>
            {
                var levels = so.FindProperty("levels");
                levels.arraySize = 3;
                for (var i = 0; i < 3; i++)
                {
                    levels.GetArrayElementAtIndex(i).FindPropertyRelative("maxHealth").intValue = 100 + i * 10;
                    levels.GetArrayElementAtIndex(i).FindPropertyRelative("damage").intValue = 5 + i;
                    levels.GetArrayElementAtIndex(i).FindPropertyRelative("xpRequired").intValue = 50 + i;
                }
                levels.GetArrayElementAtIndex(1).FindPropertyRelative("unlock").stringValue = "Double saut";
            });

            Assert.AreEqual(3, progression.MaxLevel);
            CollectionAssert.AreEqual(new[] { 100f, 110f, 120f }, progression.Find(StatRole.MaxHealth).values);
            CollectionAssert.AreEqual(new[] { 50f, 51f, 52f }, progression.Find(StatRole.XpToNext).values);
            var note = progression.Rewards.Single();
            Assert.AreEqual((2, RewardType.Note, "Double saut"), (note.level, note.type, note.note));
            TestData.Destroy(progression);
        }

        [Test]
        public void AttributeBonus_IsReducedAfterSoftCap()
        {
            var bonus = new AttributeBonus { perPoint = 10f, softCap = 5, afterSoftCap = 0.5f };

            Assert.AreEqual(30f, bonus.Total(3));
            Assert.AreEqual(50f, bonus.Total(5));
            Assert.AreEqual(75f, bonus.Total(10));
        }

        [Test]
        public void AttributePoints_AreEarnedByLevelAndSpentByThePlayer()
        {
            var progression = Default();
            var state = new GameState(progression);
            var vigor = progression.Attributes.First(a => a.displayName == "Vigueur");
            Assert.AreEqual(0, state.AttributePoints);
            Assert.IsFalse(state.Invest(vigor));

            state.AddXp(state.XpToNext);
            var maxHp = state.MaxHp;

            Assert.AreEqual(1, state.AttributePoints);
            Assert.IsTrue(state.Invest(vigor));
            Assert.AreEqual(0, state.AttributePoints);
            Assert.AreEqual(1, state.Points(vigor));
            Assert.AreEqual(maxHp + 15, state.MaxHp);
            Assert.AreEqual(state.MaxHp, state.Hp);
            TestData.Destroy(progression);
        }

        [Test]
        public void Skills_AreUnlockedThenUpgradedByLevelRewards()
        {
            var progression = Default();
            var state = new GameState(progression);
            var lynx = progression.Skills.First(s => s.effect == SkillEffect.CriticalChance);
            Assert.AreEqual(0f, state.SkillValue(SkillEffect.CriticalChance));

            while (state.Level < 10)
                state.AddXp(state.XpToNext);

            Assert.AreEqual(2, state.SkillRank(lynx));
            Assert.AreEqual(10f, state.SkillValue(SkillEffect.CriticalChance));
            Assert.AreEqual(2, ProgressionEditing.RankAt(progression, lynx, 10));
            TestData.Destroy(progression);
        }

        [Test]
        public void Mechanic_SpendsItsGaugeWhichRegenerates()
        {
            var progression = Default();
            var state = new GameState(progression);
            var dodge = progression.Mechanics.First(m => m.action == MechanicAction.Dash);
            var energy = state.CostStat(dodge);
            Assert.AreEqual(100f, state.Current(energy));

            Assert.IsTrue(state.TrySpend(dodge));
            Assert.AreEqual(75f, state.Current(energy));
            state.Tick(1f);
            Assert.AreEqual(95f, state.Current(energy), 0.001f);
            for (var i = 0; i < 3; i++)
                state.TrySpend(dodge);
            Assert.IsFalse(state.TrySpend(dodge), "Il ne reste que 20 d'énergie pour un coût de 25.");
            TestData.Destroy(progression);
        }

        [Test]
        public void Mechanic_IsUnlockedThenUpgradedByLevelRewards()
        {
            var progression = Default();
            var state = new GameState(progression);
            var nova = progression.Mechanics.First(m => m.action == MechanicAction.Nova);
            Assert.IsFalse(state.IsUnlocked(nova));
            Assert.IsTrue(state.IsUnlocked(progression.Mechanics.First(m => m.action == MechanicAction.Projectile)));

            while (state.Level < 4)
                state.AddXp(state.XpToNext);
            Assert.AreEqual(1, state.MechanicRank(nova));
            Assert.AreEqual(10, state.ValuesOf(nova).Count);

            while (state.Level < 12)
                state.AddXp(state.XpToNext);
            Assert.AreEqual(2, state.MechanicRank(nova));
            Assert.AreEqual(14, state.ValuesOf(nova).Count);
            Assert.AreEqual(80f, state.ValuesOf(nova).Power);
            Assert.AreEqual(2, ProgressionEditing.RankAt(progression, nova, 12));
            TestData.Destroy(progression);
        }

        [Test]
        public void Simulation_ComputesEffortPointsAndActions()
        {
            var progression = Default();
            var xp = progression.Find(StatRole.XpToNext);
            var attack = progression.Find(StatRole.Damage);

            var early = ProgressionSimulation.At(progression, 3, 25f);
            var sim = ProgressionSimulation.At(progression, 10, 25f);

            Assert.AreEqual(9, sim.AttributePoints, "Un point par niveau à partir du niveau 2.");
            Assert.AreEqual(Mathf.RoundToInt(Enumerable.Range(1, 9).Sum(l => xp.GetValue(l))), sim.XpCumulative);
            Assert.AreEqual(Mathf.CeilToInt(xp.GetValue(10) / 25f), sim.EnemiesToNext);
            var nova = sim.Actions.First(a => a.Mechanic.action == MechanicAction.Nova);
            Assert.AreEqual(4, nova.UnlockLevel);
            Assert.IsTrue(nova.Available);
            Assert.IsFalse(early.Actions.First(a => a.Mechanic.action == MechanicAction.Nova).Available);
            // Au niveau 3, un seul projectile ; au niveau 10, « Tir multiple » (débloqué au niveau 5) en ajoute un.
            var hit3 = Mathf.RoundToInt(attack.GetValue(3));
            StringAssert.StartsWith($"{hit3} dégâts", early.Actions.First(a => a.Mechanic.action == MechanicAction.Projectile).Summary);
            var hit10 = Mathf.RoundToInt(attack.GetValue(10));
            StringAssert.StartsWith($"{2 * hit10} dégâts", sim.Actions.First(a => a.Mechanic.action == MechanicAction.Projectile).Summary);

            TestData.Destroy(progression);
        }

        [Test]
        public void Simulation_CountsEnemiesBetweenLevels()
        {
            var progression = Default();
            try
            {
                var xp = progression.Find(StatRole.XpToNext);
                var counts = ProgressionSimulation.EnemiesPerLevel(progression, 25f);

                Assert.AreEqual(progression.MaxLevel, counts.Count);
                Assert.AreEqual(0, counts[0], "Le niveau 1 est le point de départ.");
                Assert.AreEqual(Mathf.CeilToInt(xp.GetValue(4) / 25f), counts[4], "Pour atteindre le niveau 5, il faut l'XP du niveau 4.");
                Assert.AreEqual(Mathf.CeilToInt(xp.GetValue(4) / 1f), ProgressionSimulation.EnemiesPerLevel(progression, 0f)[4], "Au moins 1 XP par ennemi.");

                ProgressionEditing.RemoveStat(new SerializedObject(progression), progression.Stats.ToList().IndexOf(xp));
                Assert.IsNull(ProgressionSimulation.EnemiesPerLevel(progression, 25f), "Sans stat d'XP, l'effort n'est pas calculable.");
            }
            finally
            {
                TestData.Destroy(progression);
            }
        }

        [Test]
        public void ImportedShape_IsScaledBetweenTheCurveExtremes()
        {
            var stat = new ProgressionStat { min = 0f, max = 100f, decimals = 0 };
            stat.values.AddRange(new[] { 10f, 20f, 50f });
            var values = ProgressionImportExport.FitShape(new[] { 0f, 0.5f, 1f }, stat, out var low, out var high);
            Assert.AreEqual((10f, 50f), (low, high));
            CollectionAssert.AreEqual(new[] { 10f, 30f, 50f }, values);

            var flat = new ProgressionStat { min = 0f, max = 10f, decimals = 1 };
            flat.values.AddRange(new[] { 5f, 5f });
            var spread = ProgressionImportExport.FitShape(new[] { 0f, 0.25f, 2f }, flat, out low, out high);
            Assert.AreEqual((0f, 10f), (low, high), "Une courbe plate prend toute la plage de la stat.");
            CollectionAssert.AreEqual(new[] { 0f, 2.5f, 10f }, spread, "Arrondi aux décimales et gardé dans les bornes.");
        }

        [Test]
        public void ChangingLevelCount_ExtendsCurvesAndDropsLaterRewards()
        {
            var progression = TestData.Create<PlayerProgression>("PRG_Test", so =>
            {
                ProgressionDefaults.Reset(so, limits);
                ProgressionEditing.SetLevelCount(so, 5);
            });

            Assert.AreEqual(5, progression.MaxLevel);
            Assert.IsTrue(progression.Stats.All(s => s.values.Count == 5));
            Assert.IsTrue(progression.Rewards.All(r => r.level <= 5));
            TestData.Destroy(progression);

            var longer = TestData.Create<PlayerProgression>("PRG_Test", so =>
            {
                ProgressionDefaults.Reset(so, limits);
                ProgressionEditing.SetLevelCount(so, 30);
            });
            var health = longer.Find(StatRole.MaxHealth);
            Assert.AreEqual(30, health.values.Count);
            Assert.AreEqual(health.values[19], health.values[29], "Les nouveaux niveaux reprennent la dernière valeur.");
            TestData.Destroy(longer);
        }

        [Test]
        public void RemovingAStat_RemovesItsAttributeBonuses()


        {
            var progression = TestData.Create<PlayerProgression>("PRG_Test", so =>
            {
                ProgressionDefaults.Reset(so, limits);
                ProgressionEditing.RemoveStat(so, 0);
            });

            CollectionAssert.IsEmpty(progression.Attributes.First(a => a.displayName == "Vigueur").bonuses);
            TestData.Destroy(progression);
        }


        [Test]
        public void CustomStat_IsReadByTheGame()
        {
            var progression = TestData.Create<PlayerProgression>("PRG_Test", so =>
            {
                ProgressionDefaults.Reset(so, limits);
                ProgressionEditing.AddStat(so, new ProgressionStat { displayName = "Chance critique", format = StatFormat.Percent, min = 0, max = 50 }, "Linéaire", 5, 24);
            });
            var state = new GameState(progression);
            var crit = progression.Stats.First(s => s.displayName == "Chance critique");

            state.AddXp(state.XpToNext);

            Assert.AreEqual(2, state.Level);
            Assert.AreEqual(6f, state.Value(crit));
            Assert.AreEqual("6 %", crit.Format(state.Value(crit)));
            TestData.Destroy(progression);
        }

        [Test]
        public void AttributePoints_AddedOnTopOfExistingOnes()
        {
            var progression = Default();
            try
            {
                var so = new SerializedObject(progression);
                ProgressionEditing.SetAttributePoints(so, 1, 2, 2);
                ProgressionEditing.AddAttributePoints(so, 2, 2, 4);
                int PointsAt(int level) => progression.Rewards.Where(r => r.type == RewardType.AttributePoints && r.level == level).Sum(r => r.amount);
                Assert.AreEqual(3, PointsAt(2), "Le niveau 2 donnait déjà 1 point : il en donne 3.");
                Assert.AreEqual(1, PointsAt(4), "Le niveau 4 n'est pas concerné par l'ajout (tous les 4 niveaux à partir du 2).");
                Assert.AreEqual(3, PointsAt(6));
                Assert.AreEqual(1, progression.Rewards.Count(r => r.type == RewardType.AttributePoints && r.level == 2), "Pas de doublon : le niveau garde une seule récompense.");
            }
            finally
            {
                TestData.Destroy(progression);
            }
        }
    }
}
