using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEngine;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Situation de test de l'arène : positions du joueur et du mur, et valeurs réglées pour les autres conditions.</summary>
    public sealed class ArenaSituation : IEnemySituation
    {
        public static readonly Vector2 EnemyPosition = new(4f, 4f);
        public static readonly Vector2 WallSize = new(1f, 2.5f);

        public Vector2 Player = new(9f, 4f);
        public Vector2 WallCorner = new(6.5f, 1f);
        public Rect Wall => new(WallCorner, WallSize);
        public float PlayerDistance => Vector2.Distance(Player, EnemyPosition);
        public bool PlayerInSight => !SegmentHitsRect(EnemyPosition, Player, Wall);
        public float PlayerHealth { get; set; } = 1f;
        public float OwnHealth { get; set; } = 1f;
        public int PlayerLevel { get; set; } = 1;
        public int Floor { get; set; } = 1;
        public BiomeData Biome { get; set; }
        public RoomType RoomType { get; set; } = RoomType.Combat;
        public int Allies { get; set; } = 2;
        public float FightTime { get; set; }
        public int Phase { get; set; }
        public readonly Dictionary<string, int> Mechanics = new();
        public readonly HashSet<string> Flags = new();
        public int MechanicLevel(string mechanicId) => mechanicId != null && Mechanics.TryGetValue(mechanicId, out var rank) ? rank : 0;
        public bool HasFlag(string flag) => Flags.Contains(flag);

        /// <summary>Le segment a-b entre-t-il dans le rectangle ? (algorithme de Liang-Barsky)</summary>
        public static bool SegmentHitsRect(Vector2 a, Vector2 b, Rect rect)
        {
            var t0 = 0f;
            var t1 = 1f;
            var d = b - a;
            float[] p = { -d.x, d.x, -d.y, d.y };
            float[] q = { a.x - rect.xMin, rect.xMax - a.x, a.y - rect.yMin, rect.yMax - a.y };
            for (var i = 0; i < 4; i++)
            {
                if (Mathf.Approximately(p[i], 0f))
                {
                    if (q[i] < 0f)
                        return false;
                    continue;
                }
                var t = q[i] / p[i];
                if (p[i] < 0f)
                    t0 = Mathf.Max(t0, t);
                else
                    t1 = Mathf.Min(t1, t);
                if (t0 > t1)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Ce que l'ennemi ferait dans une situation de l'arène, avec la même logique qu'en jeu (<see cref="EnemyLogic"/>) :
    /// règle de déplacement déclenchée, chances de chaque attaque, et enchaînement simulé de l'attaque montrée.
    /// </summary>
    internal sealed class ArenaEvaluation
    {
        ArenaEvaluation(List<ResolvedAttack> resolved, int activePattern, int activeAttack, float[] chances)
        {
            Resolved = resolved;
            ActivePattern = activePattern;
            ActiveAttack = activeAttack;
            Chances = chances;
        }

        /// <summary>Avant la première évaluation : aucune règle, aucune attaque.</summary>
        public static readonly ArenaEvaluation None = new(new List<ResolvedAttack>(), -1, -1, Array.Empty<float>());

        public List<ResolvedAttack> Resolved { get; }

        /// <summary>Règle de déplacement déclenchée, ou -1 pour le comportement de base (« Sinon »).</summary>
        public int ActivePattern { get; }

        /// <summary>Attaque la plus probable ici, ou -1 si aucune ne peut être lancée.</summary>
        public int ActiveAttack { get; }

        /// <summary>Chances de chaque attaque d'être lancée ici (toutes supposées rechargées), d'après leurs poids.</summary>
        public float[] Chances { get; }

        /// <summary>Attaque déroulée : celle sélectionnée (même si elle ne serait pas lancée ici), sinon la plus probable.</summary>
        public int ShownAttack { get; private set; } = -1;

        public List<EnemyLogic.StepOutcome> Outcomes { get; private set; } = new();

        /// <summary>L'attaque déroulée peut-elle vraiment être lancée ici ?</summary>
        public bool ShownPossible => ShownAttack >= 0 && ShownAttack < Chances.Length && Chances[ShownAttack] > 0f;

        public static ArenaEvaluation Evaluate(EnemyData data, ArenaSituation situation, int selectedAttack)
        {
            var resolved = data.Attacks.Select(a => a.Resolve()).ToList();
            var pattern = EnemyLogic.ActivePattern(data.Patterns, situation);
            var chances = EnemyLogic.AttackChances(data.Attacks, resolved, situation);
            var evaluation = new ArenaEvaluation(resolved, pattern, EnemyLogic.ChooseAttack(data.Attacks, resolved, situation), chances);
            evaluation.ShownAttack = selectedAttack >= 0 && selectedAttack < resolved.Count ? selectedAttack : evaluation.ActiveAttack;
            if (evaluation.ShownAttack >= 0 && resolved[evaluation.ShownAttack] != null)
                evaluation.Outcomes = EnemyLogic.Simulate(resolved[evaluation.ShownAttack], situation, ArenaSituation.EnemyPosition, situation.Player,
                    (a, b) => !ArenaSituation.SegmentHitsRect(a, b, situation.Wall));
            return evaluation;
        }

        /// <summary>Deux lignes lisibles : le déplacement choisi (et pourquoi), puis l'attaque et le résultat de chaque étape.</summary>
        public string Describe(EnemyData data, ArenaSituation situation)
        {
            var where = $"distance {situation.PlayerDistance:0.0} cases · {(situation.PlayerInSight ? "joueur en vue" : "joueur caché par le mur")}";
            string move;
            if (ActivePattern >= 0)
            {
                var name = data.Patterns[ActivePattern].name;
                move = $"<b>{ForgeText.Name(string.IsNullOrWhiteSpace(name) ? $"Règle {ActivePattern + 1}" : name)}</b> : {EnemyPatternText.Action(data.Patterns[ActivePattern])}";
            }
            else
                move = $"<b>Sinon</b> : {EnemyPatternText.Label(data.Behaviour).ToLowerInvariant()}";
            return $"<color=#9ebd85>▶</color> Déplacement {move}   <color=#8a8a9c>{where}</color>\n<color=#e0606a>⚔</color> Attaque : {DescribeAttack(data, situation)}";
        }

        string DescribeAttack(EnemyData data, ArenaSituation situation)
        {
            string attack;
            if (data.Attacks.Count == 0)
                attack = "selon son comportement de base";
            else if (ActiveAttack < 0)
            {
                var reasons = data.Attacks.Select((a, i) => $"{ForgeText.Name(a.Name)} : {EnemyLogic.WhyNot(a, Resolved[i], situation)}");
                attack = $"aucune <color=#8a8a9c>({string.Join(" · ", reasons)})</color>";
            }
            else
                attack = string.Join("  ·  ", Enumerable.Range(0, Chances.Length).Where(i => Chances[i] > 0f).OrderByDescending(i => Chances[i])
                    .Select(i => $"<b>{Chances[i]:P0}</b> {ForgeText.Name(data.Attacks[i].Name)}"));
            if (ShownAttack < 0 || Resolved[ShownAttack] == null)
                return attack;
            var steps = Resolved[ShownAttack].Steps;
            var parts = Outcomes.Select((o, i) =>
            {
                var name = ForgeText.NameOrRedacted(steps[i].name);
                var color = !o.Played ? "#8a8a9c" : o.Hit ? "#e0606a" : "#a3a6b6";
                return $"<color={color}>{i + 1}. {name} : {o.Reason}</color>";
            });
            var prefix = ShownPossible
                ? $"\n   {ForgeText.Name(data.Attacks[ShownAttack].Name)} ({Chances[ShownAttack]:P0}) : "
                : $"\n   Si {ForgeText.Name(data.Attacks[ShownAttack].Name)} était lancée ici : ";
            return attack + prefix + string.Join("  →  ", parts);
        }
    }
}
