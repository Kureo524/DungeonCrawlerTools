using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.ContentDatabase;
using TheDungeon.Editor.Validation;
using UnityEngine;

namespace TheDungeon.Editor.Tests
{
    public class EnemyPatternTests
    {
        sealed class Situation : IEnemySituation
        {
            public float PlayerDistance { get; set; } = 5f;
            public bool PlayerInSight { get; set; } = true;
            public float PlayerHealth { get; set; } = 1f;
            public float OwnHealth { get; set; } = 1f;
            public int PlayerLevel { get; set; } = 1;
            public int Floor { get; set; } = 1;
            public BiomeData Biome { get; set; }
            public RoomType RoomType { get; set; } = RoomType.Combat;
            public int Allies { get; set; }
            public float FightTime { get; set; }
            public Dictionary<string, int> Mechanics { get; } = new();
            public int MechanicLevel(string mechanicId) => Mechanics.TryGetValue(mechanicId, out var rank) ? rank : 0;
            public bool HasFlag(string flag) => flag == "boss_reveille";
        }

        static EnemyPattern Pattern(string name, params EnemyCondition[] conditions) => new() { name = name, conditions = conditions.ToList() };

        static EnemyCondition Condition(EnemyConditionType type, int value = 0, string flag = null) => new() { type = type, value = value, flag = flag };

        [Test]
        public void FirstPatternWhoseConditionsAreMet_IsPlayed()
        {
            var patterns = new List<EnemyPattern>
            {
                Pattern("Rage", Condition(EnemyConditionType.OwnHealthBelow, 30)),
                Pattern("Corps à corps", Condition(EnemyConditionType.PlayerCloserThan, 3)),
                Pattern("Tir")
            };
            var situation = new Situation { PlayerDistance = 2f };

            Assert.AreEqual(1, EnemyLogic.ActivePattern(patterns, situation));
            situation.OwnHealth = 0.2f;
            Assert.AreEqual(0, EnemyLogic.ActivePattern(patterns, situation), "Le pattern prioritaire passe avant.");
            situation.OwnHealth = 1f;
            situation.PlayerDistance = 8f;
            Assert.AreEqual(2, EnemyLogic.ActivePattern(patterns, situation), "Sans condition : toujours joué.");
        }

        [Test]
        public void Arena_WallHidesThePlayer()
        {
            // Mur de x 6,5 à 7,5 et de y 3 à 5,5 ; l'ennemi est en (4, 4).
            var situation = new ArenaSituation { WallCorner = new Vector2(6.5f, 3f), Player = new Vector2(9f, 4f) };
            Assert.IsFalse(situation.PlayerInSight, "Le mur est entre l'ennemi et le joueur.");
            situation.Player = new Vector2(9f, 8f);
            Assert.IsTrue(situation.PlayerInSight, "La ligne de vue passe au-dessus du mur.");
            situation.Player = new Vector2(6f, 4f);
            Assert.IsTrue(situation.PlayerInSight, "Le joueur est devant le mur.");
            Assert.AreEqual(2f, situation.PlayerDistance, 0.001f);
        }

        [Test]
        public void BehaviourEditing_AddsUpToTheLimit()
        {
            var enemy = TestData.Create<EnemyData>("ENM_Test");
            var attack = TestData.Create<AttackData>("ATK_Test");
            try
            {
                var so = new UnityEditor.SerializedObject(enemy);
                Assert.AreEqual(0, EnemyBehaviourEditing.AddPhase(so, 2));
                Assert.AreEqual(1, EnemyBehaviourEditing.AddPhase(so, 2));
                Assert.AreEqual(-1, EnemyBehaviourEditing.AddPhase(so, 2), "Au maximum, rien n'est ajouté.");
                Assert.AreEqual("Phase 2", enemy.Phases[1].name);

                Assert.AreEqual(0, EnemyBehaviourEditing.AddAttack(so, attack, 1));
                Assert.AreEqual(-1, EnemyBehaviourEditing.AddAttack(so, attack, 1));
                Assert.AreEqual(attack, enemy.Attacks[0].attack);
                Assert.AreEqual(100, enemy.Attacks[0].weight);
            }
            finally
            {
                TestData.Destroy(enemy, attack);
            }
        }

        [Test]
        public void NoPatternMet_FallsBackToDefaultBehaviour()
        {
            var patterns = new List<EnemyPattern> { Pattern("Caché", Condition(EnemyConditionType.PlayerHidden)) };

            Assert.AreEqual(-1, EnemyLogic.ActivePattern(patterns, new Situation { PlayerInSight = true }));
        }

        [Test]
        public void Conditions_ReadMechanicsFlagsAndEnvironment()
        {
            var situation = new Situation { Floor = 2, RoomType = RoomType.Boss, Allies = 0, FightTime = 20f };
            situation.Mechanics["dash"] = 2;

            Assert.IsTrue(EnemyLogic.IsMet(Condition(EnemyConditionType.MechanicAtLeast, 2, "dash"), situation));
            Assert.IsFalse(EnemyLogic.IsMet(Condition(EnemyConditionType.MechanicAtLeast, 3, "dash"), situation));
            Assert.IsTrue(EnemyLogic.IsMet(Condition(EnemyConditionType.FlagSet, 0, "boss_reveille"), situation));
            Assert.IsTrue(EnemyLogic.IsMet(Condition(EnemyConditionType.FloorIn, FloorSet.Of(new[] { 2, 3 })), situation));
            Assert.IsFalse(EnemyLogic.IsMet(Condition(EnemyConditionType.FloorIn, FloorSet.Of(1)), situation));
            Assert.IsTrue(EnemyLogic.IsMet(new EnemyCondition { type = EnemyConditionType.RoomTypeIs, roomType = RoomType.Boss }, situation));
            Assert.IsTrue(EnemyLogic.IsMet(Condition(EnemyConditionType.AlliesAtMost, 0), situation));
            Assert.IsTrue(EnemyLogic.IsMet(Condition(EnemyConditionType.FightLongerThan, 15), situation));
        }

        static EnemyData Enemy(params (EnemyConditionType type, int value)[][] patterns) => TestData.Create<EnemyData>("ENM_Test", so =>
        {
            so.FindProperty("displayName").stringValue = "Test";
            var list = so.FindProperty("patterns");
            list.arraySize = patterns.Length;
            for (var i = 0; i < patterns.Length; i++)
            {
                var conditions = list.GetArrayElementAtIndex(i).FindPropertyRelative("conditions");
                conditions.arraySize = patterns[i].Length;
                for (var c = 0; c < patterns[i].Length; c++)
                {
                    conditions.GetArrayElementAtIndex(c).FindPropertyRelative("type").enumValueIndex = (int)patterns[i][c].type;
                    conditions.GetArrayElementAtIndex(c).FindPropertyRelative("value").intValue = patterns[i][c].value;
                }
            }
        });

        static List<ValidationIssue> Validate(EnemyData enemy)
        {
            var limits = ScriptableObject.CreateInstance<ForgeDesignLimits>();
            var context = new ValidationContext(new ItemData[0], new[] { enemy }, new LootTable[0], new BiomeData[0], new RoomData[0], new FloorData[0],
                new PlayerProgression[0], limits);
            var issues = new EnemyRule().Run(context).ToList();
            TestData.Destroy(limits);
            return issues;
        }

        [Test]
        public void PatternWithoutConditions_HidesTheFollowingOnes()
        {
            var enemy = Enemy(new (EnemyConditionType, int)[0], new[] { (EnemyConditionType.OwnHealthBelow, 50) });

            Assert.IsTrue(Validate(enemy).Any(i => i.Severity == Severity.Warning && i.Message.Contains("ne seront jamais utilisées")));
            TestData.Destroy(enemy);
        }

        [Test]
        public void ContradictoryDistances_AreReported()
        {
            var enemy = Enemy(new[] { (EnemyConditionType.PlayerCloserThan, 3), (EnemyConditionType.PlayerFartherThan, 5) });

            Assert.IsTrue(Validate(enemy).Any(i => i.Message.Contains("ne s'appliquera jamais")));
            TestData.Destroy(enemy);
        }

        [Test]
        public void Presets_ProduceValidRules()
        {
            foreach (var preset in TheDungeon.Editor.ContentDatabase.RulePresets.All)
            {
                var enemy = TestData.Create<EnemyData>("ENM_Test", so =>
                {
                    so.FindProperty("displayName").stringValue = "Test";
                    var patterns = so.FindProperty("patterns");
                    patterns.arraySize = 1;
                    TheDungeon.Editor.ContentDatabase.RulePresets.Apply(preset, patterns.GetArrayElementAtIndex(0));
                });
                Assert.AreEqual(preset.Name, enemy.Patterns[0].name);
                Assert.AreEqual(preset.Conditions.Length, enemy.Patterns[0].conditions.Count);
                CollectionAssert.IsEmpty(Validate(enemy).Where(i => i.Message.Contains("règle")).Select(i => i.Message), preset.Name);
                TestData.Destroy(enemy);
            }
        }

        static AttackData Preset(string name) => TestData.Create<AttackData>("ATK_Test", so =>
            TheDungeon.Editor.ContentDatabase.RulePresets.Apply(TheDungeon.Editor.ContentDatabase.RulePresets.Attacks.First(p => p.Name == name), so));

        static ResolvedAttack Resolve(AttackData attack) => new EnemyAttack { attack = attack }.Resolve();

        [Test]
        public void EnemyOverrides_ReplaceOnlyTheChosenSettings()
        {
            var attack = Preset("Coup de massue");
            var slot = new EnemyAttack { attack = attack, overridden = AttackField.Cooldown, overrides = new AttackSettings { cooldown = 9f, maxRange = 99f } };

            var resolved = slot.Resolve();

            Assert.AreEqual(9f, resolved.Settings.cooldown, "Réglage remplacé par l'ennemi.");
            Assert.AreEqual(attack.Settings.maxRange, resolved.Settings.maxRange, "Réglage non coché : celui de l'attaque.");
            Assert.AreSame(attack.Steps, resolved.Steps);
            TestData.Destroy(attack);
        }

        [Test]
        public void ChooseAttack_FollowsRangeSightCooldownAndOrder()
        {
            var melee = Preset("Coup de massue");
            var shot = Preset("Tir visé");
            var slots = new List<EnemyAttack> { new() { attack = melee }, new() { attack = shot } };
            var resolved = slots.Select(s => s.Resolve()).ToList();

            Assert.AreEqual(0, EnemyLogic.ChooseAttack(slots, resolved, new Situation { PlayerDistance = 1f }));
            Assert.AreEqual(1, EnemyLogic.ChooseAttack(slots, resolved, new Situation { PlayerDistance = 5f }));
            Assert.AreEqual(-1, EnemyLogic.ChooseAttack(slots, resolved, new Situation { PlayerDistance = 2.5f }), "Entre les deux portées.");
            Assert.AreEqual(-1, EnemyLogic.ChooseAttack(slots, resolved, new Situation { PlayerDistance = 5f, PlayerInSight = false }), "Un tir demande de voir le joueur.");
            Assert.AreEqual(-1, EnemyLogic.ChooseAttack(slots, resolved, new Situation { PlayerDistance = 1f }, i => i != 0), "Coup en recharge.");
            TestData.Destroy(melee, shot);
        }

        [Test]
        public void ChargedBlow_DashesOnlyWhenThePlayerIsFar()
        {
            var attack = Preset("Coup chargé");

            var near = EnemyLogic.Simulate(Resolve(attack), new Situation(), Vector2.zero, new Vector2(2f, 0f), (_, _) => true);
            var far = EnemyLogic.Simulate(Resolve(attack), new Situation(), Vector2.zero, new Vector2(6f, 0f), (_, _) => true);

            Assert.IsFalse(near[1].Played, "Joueur proche : pas de ruée.");
            Assert.IsTrue(near[2].Hit, "Le coup touche directement.");
            Assert.IsTrue(far[1].Played, "Joueur loin : la ruée rapproche l'ennemi…");
            Assert.IsTrue(far[2].Hit, "…puis le coup touche.");
            TestData.Destroy(attack);
        }

        [Test]
        public void Combo_StopsWhenAHitMisses_AndRain_CountsHits()
        {
            var combo = Preset("Combo");
            var far = EnemyLogic.Simulate(Resolve(combo), new Situation(), Vector2.zero, new Vector2(3f, 0f), (_, _) => true);
            Assert.IsFalse(far[0].Hit);
            Assert.AreEqual(2, far.Count, "Le premier coup rate : la deuxième étape n'est pas jouée et l'attaque s'arrête.");
            var near = EnemyLogic.Simulate(Resolve(combo), new Situation(), Vector2.zero, new Vector2(1f, 0f), (_, _) => true);
            Assert.IsTrue(near.All(o => o.Played && o.Hit), "Tout touche : les trois coups sont joués.");

            var beast = Preset("Bond stellaire");
            var outcome = EnemyLogic.Simulate(Resolve(beast), new Situation(), Vector2.zero, new Vector2(3.4f, 0f), (_, _) => true);
            Assert.IsFalse(outcome[0].Hit, "Hors de la première zone…");
            Assert.IsTrue(outcome[1].Played && outcome[1].Hit, "…la retombée est jouée quand même, et touche.");
            TestData.Destroy(combo, beast);
        }

        [Test]
        public void Zones_HitTheRightPlaces()
        {
            var ring = new AttackStep { shape = ZoneShape.Ring, innerRadius = 2f, radius = 4f };
            var cone = new AttackStep { shape = ZoneShape.Cone, radius = 3f, angle = 90f };
            var line = new AttackStep { shape = ZoneShape.Line, radius = 5f, width = 1f };

            Assert.IsFalse(EnemyLogic.InZone(ring, Vector2.zero, Vector2.right, new Vector2(1f, 0f)), "Collé à l'ennemi : hors de l'anneau.");
            Assert.IsTrue(EnemyLogic.InZone(ring, Vector2.zero, Vector2.right, new Vector2(0f, 3f)));
            Assert.IsTrue(EnemyLogic.InZone(cone, Vector2.zero, Vector2.right, new Vector2(2f, 1f)));
            Assert.IsFalse(EnemyLogic.InZone(cone, Vector2.zero, Vector2.right, new Vector2(-2f, 0f)), "Derrière l'ennemi.");
            Assert.IsTrue(EnemyLogic.InZone(line, Vector2.zero, Vector2.right, new Vector2(4.5f, 0.2f)));
            Assert.IsFalse(EnemyLogic.InZone(line, Vector2.zero, Vector2.right, new Vector2(3f, 2f)));
        }

        [Test]
        public void Weights_ShareTheChances_AndDriveTheDraw()
        {
            var club = Preset("Coup de massue");
            var grab = Preset("Saisie");
            var slots = new List<EnemyAttack> { new() { attack = club, weight = 60 }, new() { attack = grab, weight = 40 } };
            var resolved = slots.Select(s => s.Resolve()).ToList();
            var near = new Situation { PlayerDistance = 1f };

            var chances = EnemyLogic.AttackChances(slots, resolved, near);
            Assert.AreEqual(0.6f, chances[0], 0.001f);
            Assert.AreEqual(0.4f, chances[1], 0.001f);
            Assert.AreEqual(1f, EnemyLogic.AttackChances(slots, resolved, near, i => i != 0)[1], 0.001f, "Le coup en recharge : la saisie à coup sûr.");

            var random = new System.Random(7);
            var draws = Enumerable.Range(0, 2000).Select(_ => EnemyLogic.ChooseAttack(slots, resolved, near, null, random)).ToList();
            Assert.AreEqual(0.6, draws.Count(d => d == 0) / 2000.0, 0.05, "Le tirage suit les poids.");

            slots[1].weight = 0;
            Assert.AreEqual(0f, EnemyLogic.AttackChances(slots, resolved, near)[1], "Poids 0 : jamais.");
            TestData.Destroy(club, grab);
        }

        [Test]
        public void ZoneOrigins_FollowTheirAnchor()
        {
            var ahead = new AttackStep { anchor = ZoneAnchor.Ahead, anchorDistance = 2f, radius = 1f, zoneCount = 3 };
            var thrown = new AttackStep { anchor = ZoneAnchor.Thrown, anchorDistance = 4f };
            var target = new Vector2(10f, 0f);

            var chain = EnemyLogic.ZoneOrigins(ahead, Vector2.zero, Vector2.right, target);
            Assert.AreEqual(3, chain.Count);
            Assert.AreEqual(2f, chain[0].x, 0.01f, "Première zone à 2 cases devant.");
            Assert.Greater(chain[2].x, chain[1].x, "Chapelet le long de la visée.");
            Assert.AreEqual(4f, EnemyLogic.ZoneOrigins(thrown, Vector2.zero, Vector2.right, target)[0].x, 0.01f, "Le lancer s'arrête à sa portée.");
            Assert.AreEqual(3f, EnemyLogic.ZoneOrigins(thrown, Vector2.zero, Vector2.right, new Vector2(3f, 0f))[0].x, 0.01f, "Cible plus proche : il tombe sur elle.");
        }

        [Test]
        public void MovingGrowingAndSweepingZones_ReachTheTargetLater()
        {
            var wave = new AttackStep { shape = ZoneShape.Circle, radius = 1f, travelSpeed = 5f, activeDuration = 2f };
            var ring = new AttackStep { shape = ZoneShape.Ring, innerRadius = 0.5f, radius = 1f, growth = 4f, activeDuration = 2f };
            var beam = new AttackStep { shape = ZoneShape.Line, radius = 8f, width = 1f, sweepAngle = 180f, activeDuration = 2f };
            var target = new Vector2(5f, 0f);

            Assert.IsFalse(EnemyLogic.InZone(wave, Vector2.zero, Vector2.right, target, 0.3f, 0f));
            Assert.IsTrue(EnemyLogic.InZone(wave, Vector2.zero, Vector2.right, target, 0.3f, 1f), "L'onde a avancé de 5 cases.");
            Assert.IsTrue(EnemyLogic.InZone(ring, Vector2.zero, Vector2.right, target, 0.3f, 1f), "L'anneau s'est élargi jusqu'à 5 cases.");
            Assert.IsFalse(EnemyLogic.InZone(ring, Vector2.zero, Vector2.right, new Vector2(1f, 0f), 0.3f, 1f), "Le centre de l'anneau est sûr.");
            Assert.IsFalse(EnemyLogic.InZone(beam, Vector2.zero, Vector2.right, new Vector2(0f, 5f), 0.3f, 0f), "Le rayon commence d'un côté…");
            Assert.IsTrue(EnemyLogic.InZone(beam, Vector2.zero, Vector2.right, new Vector2(0f, 5f), 0.3f, 2f), "…et balaie jusqu'à l'autre.");
        }

        [Test]
        public void CounterStep_IsPlayedOnlyIfTheAttackerWasHit()
        {
            var riposte = new AttackStep { trigger = StepTrigger.WasHit };

            Assert.IsFalse(EnemyLogic.StepAllowed(riposte, false, false, 0, new Situation(), wasHit: false));
            Assert.IsTrue(EnemyLogic.StepAllowed(riposte, false, false, 0, new Situation(), wasHit: true));
        }

        [Test]
        public void Simulation_RepeatsSteps_AndFollowsTeleports()
        {
            var march = Preset("Marche écrasante");
            var vanish = Preset("Disparition");

            var marching = EnemyLogic.Simulate(Resolve(march), new Situation(), Vector2.zero, new Vector2(1.5f, 0f), (_, _) => true);
            Assert.AreEqual("touche (3×)", marching[0].Reason, "Trois coups en marchant.");
            var teleported = EnemyLogic.Simulate(Resolve(vanish), new Situation(), Vector2.zero, new Vector2(6f, 0f), (_, _) => true);
            Assert.Greater(teleported[0].End.x, 6f, "Réapparaît derrière la cible.");
            Assert.IsTrue(teleported[1].Hit, "Puis la taillade touche.");
            TestData.Destroy(march, vanish);
        }

        [Test]
        public void PhaseCondition_ReadsTheEnemyPhase()
        {
            var condition = new EnemyCondition { type = EnemyConditionType.PhaseIs, value = 1 };

            Assert.IsFalse(EnemyLogic.IsMet(condition, new Situation()), "Phase de départ (0).");
            Assert.IsTrue(EnemyLogic.IsMet(condition, new PhasedSituation()));
        }

        sealed class PhasedSituation : IEnemySituation
        {
            public float PlayerDistance => 1f;
            public bool PlayerInSight => true;
            public float PlayerHealth => 1f;
            public float OwnHealth => 1f;
            public int PlayerLevel => 1;
            public int MechanicLevel(string mechanicId) => 0;
            public bool HasFlag(string flag) => false;
            public int Floor => 1;
            public BiomeData Biome => null;
            public RoomType RoomType => RoomType.Combat;
            public int Allies => 0;
            public float FightTime => 0f;
            public int Phase => 1;
        }

        [Test]
        public void PhaseStep_BeyondTheEnemyPhases_IsAnError()
        {
            var mountain = Preset("Montagne de putréfaction");
            var enemy = TestData.Create<EnemyData>("ENM_Test", so =>
            {
                so.FindProperty("displayName").stringValue = "Test";
                var attacks = so.FindProperty("attacks");
                attacks.arraySize = 1;
                attacks.GetArrayElementAtIndex(0).FindPropertyRelative("attack").objectReferenceValue = mountain;
            });

            Assert.IsTrue(Validate(enemy).Any(i => i.Severity == Severity.Error && i.Message.Contains("phase 1")));
            TestData.Destroy(enemy, mountain);
        }

        [Test]
        public void AttackPresets_AreValid()
        {
            var limits = ScriptableObject.CreateInstance<ForgeDesignLimits>();
            foreach (var preset in TheDungeon.Editor.ContentDatabase.RulePresets.Attacks)
            {
                var attack = Preset(preset.Name);
                var issues = EnemyRule.StepIssues(attack, limits, new HashSet<string>())
                    .Concat(EnemyRule.SettingsIssues("L'attaque", attack.Settings, limits, attack)).ToList();
                CollectionAssert.IsEmpty(issues.Select(i => i.Message), preset.Name);
                TestData.Destroy(attack);
            }
            TestData.Destroy(limits);
        }

        [Test]
        public void AttackWithInvertedRange_IsAnError()
        {
            var attack = Preset("Tir visé");
            var so = new UnityEditor.SerializedObject(attack);
            so.FindProperty("settings.minRange").floatValue = 8f;
            so.FindProperty("settings.maxRange").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var limits = ScriptableObject.CreateInstance<ForgeDesignLimits>();
            var context = new ValidationContext(new ItemData[0], new EnemyData[0], new LootTable[0], new BiomeData[0], new RoomData[0], new FloorData[0],
                new PlayerProgression[0], limits, attacks: new[] { attack });

            Assert.IsTrue(new AttackRule().Run(context).Any(i => i.Severity == Severity.Error && i.Message.Contains("distance minimale")));
            TestData.Destroy(attack, limits);
        }

        [Test]
        public void UnknownMechanic_IsAnError()
        {
            var enemy = Enemy(new[] { (EnemyConditionType.MechanicAtLeast, 1) });

            Assert.IsTrue(Validate(enemy).Any(i => i.Severity == Severity.Error && i.Message.Contains("mécanique")));
            TestData.Destroy(enemy);
        }
    }
}
