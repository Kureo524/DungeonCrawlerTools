using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEngine;

namespace TheDungeon.Editor.Validation
{
    public sealed class EnemyRule : ValidationRule
    {
        public override string Name => "Ennemis";
        public override string Description => "Stats dans les limites, nom et table de butin renseignés.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var limits = context.Limits;
            foreach (var enemy in context.Enemies)
            {
                if (enemy.Sprite == null)
                    yield return Warning("L'ennemi n'a pas de visuel.", enemy);
                if (enemy.LootTable == null)
                    yield return Warning("L'ennemi n'a pas de table de butin : il ne lâchera rien.", enemy);
                if (!limits.enemyHealth.Contains(enemy.GetHealth(1)))
                    yield return Error($"PV de base ({enemy.GetHealth(1)}) hors limites ({limits.enemyHealth}).", enemy);
                if (!limits.enemyDamage.Contains(enemy.GetDamage(1)))
                    yield return Error($"Dégâts de base ({enemy.GetDamage(1)}) hors limites ({limits.enemyDamage}).", enemy);
                if (!limits.enemyFloorScaling.Contains(enemy.FloorScaling))
                    yield return Error($"Croissance par étage ({enemy.FloorScaling:P0}) hors limites ({limits.enemyFloorScaling}).", enemy);
                foreach (var issue in PatternIssues(enemy, limits, context))
                    yield return issue;
            }
        }

        /// <summary>Règles de déplacement et attaques : références valides, conditions possibles, aucune règle rendue inutile par une autre.</summary>
        static IEnumerable<ValidationIssue> PatternIssues(EnemyData enemy, ForgeDesignLimits limits, ValidationContext context)
        {
            var patterns = enemy.Patterns;
            if (patterns.Count > limits.maxEnemyPatterns)
                yield return Error($"Trop de règles de déplacement ({patterns.Count}, max {limits.maxEnemyPatterns}).", enemy);
            var mechanics = context.Progressions.SelectMany(p => p.Mechanics).Select(m => m.id).ToHashSet();
            for (var i = 0; i < patterns.Count; i++)
            {
                var pattern = patterns[i];
                var name = $"La règle {i + 1}{(string.IsNullOrWhiteSpace(pattern.name) ? string.Empty : $" '{pattern.name}'")}";
                if (pattern.conditions.Count == 0 && i < patterns.Count - 1)
                    yield return Warning($"{name} n'a aucune condition : elle s'applique toujours, et les {patterns.Count - 1 - i} règle(s) suivante(s) ne seront jamais utilisées.", enemy);
                foreach (var issue in ConditionIssues(name, pattern.conditions, limits, mechanics, enemy))
                    yield return issue;
            }

            var attacks = enemy.Attacks;
            if (attacks.Count > limits.maxEnemyAttacks)
                yield return Error($"Trop d'attaques ({attacks.Count}, max {limits.maxEnemyAttacks}).", enemy);
            for (var i = 0; i < attacks.Count; i++)
            {
                var slot = attacks[i];
                var name = $"L'attaque {i + 1}{(slot.Name == null ? string.Empty : $" '{slot.Name}'")}";
                if (slot.attack == null)
                {
                    yield return Error($"{name} ne cite aucune attaque (manquante ou supprimée).", enemy);
                    continue;
                }
                foreach (var issue in SettingsIssues(name, slot.Resolve().Settings, limits, enemy))
                    yield return issue;
                if (slot.weight <= 0)
                    yield return Warning($"{name} a un poids de 0 : elle ne sera jamais lancée.", enemy);
                if (slot.attack.User != AttackUser.Enemy)
                    yield return Warning($"{name} est une {EnemyPatternLabel(slot.attack.User)} : elle est faite pour une arme du joueur.", enemy);
                foreach (var step in slot.attack.Steps.Where(st => st.action == StepAction.Phase && st.phase > enemy.Phases.Count))
                    yield return Error($"{name} fait passer en phase {step.phase}, mais cet ennemi n'a que {enemy.Phases.Count} phase(s) : ajoutez-la (Comportement > Phases).", enemy);
                foreach (var issue in ConditionIssues(name, slot.conditions, limits, mechanics, enemy))
                    yield return issue;
            }
            if (attacks.Where(a => a.attack != null).GroupBy(a => a.attack).Any(g => g.Count() > 1 && g.All(a => a.conditions.Count == 0 && a.overridden == AttackField.None)))
                yield return Warning("La même attaque est donnée deux fois sans différence : la seconde ne sert à rien.", enemy);
        }

        static string EnemyPatternLabel(AttackUser user) => user == AttackUser.Skill ? "compétence d'arme" : "attaque d'arme";

        /// <summary>Réglages de déclenchement cohérents.</summary>
        public static IEnumerable<ValidationIssue> SettingsIssues(string name, AttackSettings settings, ForgeDesignLimits limits, Object context)
        {
            if (settings.minRange > settings.maxRange)
                yield return Error($"{name} : distance minimale ({settings.minRange:0.#}) au-dessus de la maximale ({settings.maxRange:0.#}) : elle ne sera jamais lancée.", context);
            if (!limits.attackRange.Contains(settings.maxRange) || !limits.attackRange.Contains(settings.minRange))
                yield return Error($"{name} : distance de déclenchement hors limites ({limits.attackRange}).", context);
            if (!limits.attackCooldown.Contains(settings.cooldown))
                yield return Error($"{name} : recharge hors limites ({limits.attackCooldown}).", context);
            if (!limits.attackTempo.Contains(settings.tempoPercent))
                yield return Error($"{name} : cadence hors limites ({limits.attackTempo} %).", context);
        }

        /// <summary>Étapes cohérentes : au moins une, conditions qui peuvent se réaliser, zones qui peuvent toucher.</summary>
        public static IEnumerable<ValidationIssue> StepIssues(AttackData attack, ForgeDesignLimits limits, HashSet<string> mechanics)
        {
            var steps = attack.Steps;
            if (steps.Count == 0)
                yield return Error("L'attaque n'a aucune étape : elle ne fait rien.", attack);
            if (steps.Count > limits.maxAttackSteps)
                yield return Error($"Trop d'étapes ({steps.Count}, max {limits.maxAttackSteps}).", attack);
            var possibleHits = 0;
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var name = $"L'étape {i + 1}{(string.IsNullOrWhiteSpace(step.name) ? string.Empty : $" '{step.name}'")}";
                if (i > 0 && step.trigger == StepTrigger.HitsAtLeast && step.hitCount > possibleHits)
                    yield return Warning($"{name} demande {step.hitCount} coups touchés, mais les étapes d'avant ne peuvent en porter que {possibleHits} : elle ne sera jamais jouée.", attack);
                if (i > 0 && step.trigger is StepTrigger.PreviousHit or StepTrigger.PreviousMissed && !CanHit(steps[i - 1]))
                    yield return Warning($"{name} dépend du coup de l'étape précédente, qui ne fait pas de dégâts.", attack);
                if (step.action == StepAction.Strike && step.shape == ZoneShape.Ring && step.innerRadius >= step.radius)
                    yield return Error($"{name} : l'anneau a un rayon intérieur plus grand que l'extérieur : il ne touche rien.", attack);
                if (step.action == StepAction.Strike && step.anchor == ZoneAnchor.Self && i == 0 && step.radius + 0.3f < attack.Settings.maxRange &&
                    step.shape is ZoneShape.Circle or ZoneShape.Cone)
                    yield return Warning($"{name} porte à {step.radius:0.#} cases mais l'attaque se lance jusqu'à {attack.Settings.maxRange:0.#} cases : elle ratera souvent.", attack);
                if (step.action != StepAction.Pause && step.action != StepAction.Summon && CanHit(step) && step.windup <= 0f && step.damagePercent >= 150 && i == 0)
                    yield return Warning($"{name} : forts dégâts sans préparation, le joueur ne peut pas l'esquiver.", attack);
                if (step.action == StepAction.Strike && step.activeDuration <= 0f && (step.travelSpeed != 0f || step.growth != 0f || step.sweepAngle != 0f))
                    yield return Warning($"{name} : la zone avance, grandit ou pivote, mais ne dure pas : réglez une durée (option « Zone qui dure »).", attack);
                if (step.action == StepAction.Strike && step.activeDuration > 0f && step.tickInterval > step.activeDuration)
                    yield return Warning($"{name} : la zone dure moins longtemps que l'intervalle entre deux dégâts : elle ne blesse qu'une fois.", attack);
                if (step.action == StepAction.Phase && attack.User != AttackUser.Enemy)
                    yield return Warning($"{name} change de phase, ce qui n'a de sens que pour un ennemi.", attack);
                if (step.action is StepAction.Summon or StepAction.Absorb && attack.User != AttackUser.Enemy)
                    yield return Warning($"{name} : invoquer ou absorber des alliés n'a de sens que pour un ennemi.", attack);
                foreach (var issue in ConditionIssues(name, step.conditions, limits, mechanics, attack))
                    yield return issue;
                if (CanHit(step))
                    possibleHits += step.action == StepAction.Projectiles ? Mathf.Max(1, step.projectiles) : 1;
            }
        }

        static bool CanHit(AttackStep step) => step.CanHit;

        static IEnumerable<ValidationIssue> ConditionIssues(string name, IReadOnlyList<EnemyCondition> conditions, ForgeDesignLimits limits,
            HashSet<string> mechanics, Object context)
        {
            if (conditions.Count > limits.maxPatternConditions)
                yield return Error($"{name} a trop de conditions ({conditions.Count}, max {limits.maxPatternConditions}).", context);
            foreach (var condition in conditions)
            {
                switch (condition.type)
                {
                    case EnemyConditionType.MechanicAtLeast when string.IsNullOrEmpty(condition.flag) || !mechanics.Contains(condition.flag):
                        yield return Error($"{name} teste une mécanique qui n'existe pas (supprimée de la progression ?).", context);
                        break;
                    case EnemyConditionType.FlagSet when string.IsNullOrWhiteSpace(condition.flag):
                        yield return Error($"{name} teste un marqueur d'histoire vide.", context);
                        break;
                    case EnemyConditionType.BiomeIs when condition.biome == null:
                        yield return Error($"{name} teste un biome manquant ou supprimé.", context);
                        break;
                    case EnemyConditionType.FloorIn when condition.value == 0:
                        yield return Error($"{name} ne coche aucun étage : elle ne s'appliquera jamais.", context);
                        break;
                }
            }
            // Conditions incompatibles : jamais remplies ensemble.
            var closer = conditions.Where(c => c.type == EnemyConditionType.PlayerCloserThan).Select(c => c.value).DefaultIfEmpty(int.MaxValue).Min();
            var farther = conditions.Where(c => c.type == EnemyConditionType.PlayerFartherThan).Select(c => c.value).DefaultIfEmpty(-1).Max();
            if (closer != int.MaxValue && farther >= 0 && farther >= closer)
                yield return Warning($"{name} demande le joueur à plus de {farther} et à moins de {closer} cases : elle ne s'appliquera jamais.", context);
            if (conditions.Any(c => c.type == EnemyConditionType.PlayerInSight) && conditions.Any(c => c.type == EnemyConditionType.PlayerHidden))
                yield return Warning($"{name} demande le joueur à la fois en vue et caché : elle ne s'appliquera jamais.", context);
        }
    }
}
