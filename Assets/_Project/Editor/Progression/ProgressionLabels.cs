using TheDungeon.Data;

namespace TheDungeon.Editor.Progression
{
    /// <summary>Libellés en français des réglages d'une progression (rôles, affichage, actions, touches, récompenses…).</summary>
    public static class ProgressionLabels
    {
        /// <summary>Réglages utiles pour une action : libellés de « puissance », « nombre » et « durée » (null = réglage inutilisé).</summary>
        public static (string power, string count, string duration) ActionFields(MechanicAction action) => action switch
        {
            MechanicAction.Projectile => ("Dégâts (% de l'ATQ)", "Projectiles par tir", null),
            MechanicAction.Nova => ("Dégâts (% de l'ATQ)", "Projectiles autour", null),
            MechanicAction.Dash => ("Distance (cases)", null, "Durée (s)"),
            MechanicAction.Heal => ("PV rendus", null, null),
            MechanicAction.Shield => (null, null, "Invincibilité (s)"),
            MechanicAction.WeaponSkill => (null, null, null),
            _ => ("Vitesse en plus (%)", null, "Durée (s)")
        };

        public static string ActionLabel(MechanicAction action) => action switch
        {
            MechanicAction.Projectile => "Attaque : projectile visé (ou attaque de l'arme)",
            MechanicAction.WeaponSkill => "Attaque : compétence de l'arme",
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
