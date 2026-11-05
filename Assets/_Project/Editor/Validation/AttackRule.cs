using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;

namespace TheDungeon.Editor.Validation
{
    public sealed class AttackRule : ValidationRule
    {
        public override string Name => "Attaques";
        public override string Description => "Déclenchement et étapes cohérents (zones, conditions, suites possibles) ; attaques inutilisées signalées.";

        public override IEnumerable<ValidationIssue> Run(ValidationContext context)
        {
            var used = context.Enemies.SelectMany(e => e.Attacks).Select(a => a.attack)
                .Concat(context.Items.SelectMany(i => new[] { i.WeaponAttack, i.WeaponSkill })).Where(a => a != null).Select(context.Current).ToHashSet();
            var mechanics = context.Progressions.SelectMany(p => p.Mechanics).Select(m => m.id).ToHashSet();
            foreach (var attack in context.Attacks)
            {
                foreach (var issue in EnemyRule.SettingsIssues("L'attaque", attack.Settings, context.Limits, attack))
                    yield return issue;
                foreach (var issue in EnemyRule.StepIssues(attack, context.Limits, mechanics))
                    yield return issue;
                if (!used.Contains(attack))
                    yield return Warning(attack.User == AttackUser.Enemy ? "Aucun ennemi n'utilise cette attaque." : "Aucune arme n'utilise cette attaque.", attack);
            }
        }
    }
}
