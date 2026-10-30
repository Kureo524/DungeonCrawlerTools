using TheDungeon.Data;
using TheDungeon.Editor.Common;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Libellés français des patterns d'ennemis, et phrase lisible pour chaque condition.</summary>
    public static class EnemyPatternText
    {
        public static string Label(PatternMovement movement) => movement switch
        {
            PatternMovement.Chase => "Poursuivre le joueur",
            PatternMovement.KeepDistance => "Garder ses distances",
            PatternMovement.Circle => "Tourner autour du joueur",
            PatternMovement.Flee => "Fuir le joueur",
            PatternMovement.Hold => "Rester sur place",
            PatternMovement.Charge => "Charger (annonce puis ruée)",
            _ => movement.ToString()
        };

        public static string Label(StepAction action) => action switch
        {
            StepAction.Strike => "Frappe de zone",
            StepAction.Projectiles => "Projectiles",
            StepAction.Dash => "Ruée",
            StepAction.Summon => "Invocation",
            StepAction.Pause => "Pause",
            StepAction.Teleport => "Téléportation",
            StepAction.Buff => "Renforcement",
            StepAction.Absorb => "Absorption",
            StepAction.Phase => "Changer de phase",
            _ => action.ToString()
        };

        public static string Describe(StepAction action) => action switch
        {
            StepAction.Strike => "Frappe une zone (cercle, anneau, cône ou ligne) : le joueur est touché s'il y est à l'impact. Pas de projectile.",
            StepAction.Projectiles => "Tire des projectiles (visés, en éventail ou en cercle) ; ils touchent en chemin.",
            StepAction.Dash => "L'ennemi se déplace vite (vers le joueur, en recul ou de côté) ; il blesse au contact si l'étape fait des dégâts.",
            StepAction.Summon => "Fait apparaître des ennemis : un ennemi choisi (clones, sbires) ou des ennemis ordinaires du biome (3 au plus en tout).",
            StepAction.Pause => "Temps mort : l'attaquant attend (pour rythmer un enchaînement, ou une annonce commune à plusieurs suites).",
            StepAction.Teleport => "L'attaquant disparaît pendant la préparation (invulnérable) puis réapparaît derrière la cible, près d'elle, loin d'elle ou au hasard.",
            StepAction.Buff => "Effet sur soi : soin, dégâts, vitesse, armure (dégâts reçus en moins) ou invulnérabilité, pour une durée.",
            StepAction.Absorb => "Absorbe les alliés proches (ils disparaissent) : chacun ajoute l'effet sur soi choisi (soin, dégâts…).",
            StepAction.Phase => "Fait passer l'ennemi dans une autre phase (définie dans l'ennemi : stats, aura, renforts). Les règles et attaques peuvent dépendre de la phase.",
            _ => string.Empty
        };

        public static string Label(ZoneShape shape) => shape switch
        {
            ZoneShape.Circle => "Cercle",
            ZoneShape.Ring => "Anneau",
            ZoneShape.Cone => "Cône",
            ZoneShape.Line => "Ligne",
            _ => shape.ToString()
        };

        public static string Label(ProjectilePattern pattern) => pattern switch
        {
            ProjectilePattern.Aimed => "Visés",
            ProjectilePattern.Spread => "Éventail",
            ProjectilePattern.Ring => "Cercle",
            _ => pattern.ToString()
        };

        public static string Label(DashDirection direction) => direction switch
        {
            DashDirection.TowardPlayer => "Vers le joueur",
            DashDirection.AwayFromPlayer => "En recul",
            DashDirection.Sideways => "De côté",
            _ => direction.ToString()
        };

        public static string Label(StepTrigger trigger) => trigger switch
        {
            StepTrigger.Always => "Toujours",
            StepTrigger.PreviousHit => "Si l'étape précédente a touché",
            StepTrigger.PreviousMissed => "Si l'étape précédente a raté",
            StepTrigger.HitsAtLeast => "Si au moins N coups ont touché",
            StepTrigger.WasHit => "Si l'attaquant a été touché (contre)",
            _ => trigger.ToString()
        };

        public static string Label(ZoneAnchor anchor) => anchor switch
        {
            ZoneAnchor.Self => "sur l'attaquant",
            ZoneAnchor.Target => "sur la cible (position au début de l'étape)",
            ZoneAnchor.Ahead => "devant l'attaquant",
            ZoneAnchor.Thrown => "là où tombe un projectile lancé",
            ZoneAnchor.RandomNearSelf => "au hasard autour de l'attaquant",
            ZoneAnchor.RandomNearTarget => "au hasard autour de la cible",
            _ => anchor.ToString()
        };

        public static string Label(StepMove move) => move switch
        {
            StepMove.None => "Immobile",
            StepMove.TowardTarget => "Avance vers la cible",
            StepMove.AwayFromTarget => "Recule",
            StepMove.Strafe => "Pas de côté",
            StepMove.Forward => "Avance tout droit",
            _ => move.ToString()
        };

        public static string Label(StatusEffect status) => status switch
        {
            StatusEffect.None => "Aucun",
            StatusEffect.Poison => "Poison",
            StatusEffect.Burn => "Brûlure",
            StatusEffect.Rot => "Putréfaction",
            StatusEffect.Frost => "Gel (ralentit)",
            StatusEffect.Bleed => "Saignement",
            StatusEffect.Stun => "Étourdissement",
            StatusEffect.Knockback => "Repousse",
            StatusEffect.Launch => "Projette en l'air",
            _ => status.ToString()
        };

        /// <summary>Unité de la puissance d'un effet subi.</summary>
        public static string Unit(StatusEffect status) => status switch
        {
            StatusEffect.Poison or StatusEffect.Burn or StatusEffect.Rot => "dégâts/s",
            StatusEffect.Frost => "% de lenteur",
            StatusEffect.Bleed => "% des PV max",
            StatusEffect.Knockback => "de force",
            _ => null
        };

        public static string Label(BuffType buff) => buff switch
        {
            BuffType.Heal => "Soin",
            BuffType.Damage => "Dégâts en plus",
            BuffType.Speed => "Vitesse en plus",
            BuffType.Armor => "Armure (dégâts reçus en moins)",
            BuffType.Invulnerable => "Invulnérabilité",
            _ => buff.ToString()
        };

        public static string Label(TeleportTarget target) => target switch
        {
            TeleportTarget.BehindTarget => "Derrière",
            TeleportTarget.NearTarget => "Devant",
            TeleportTarget.AwayFromTarget => "Au loin",
            TeleportTarget.RandomNearSelf => "Au hasard",
            _ => target.ToString()
        };

        public static string Label(AttackUser user) => user switch
        {
            AttackUser.Weapon => "Attaque d'arme",
            AttackUser.Skill => "Compétence d'arme",
            _ => "Ennemi"
        };

        /// <summary>Résumé d'une étape : « cercle 2,5 cases lancé · ×2 · prépa 0,8 s ».</summary>
        public static string Summary(AttackStep step)
        {
            var what = step.action switch
            {
                StepAction.Strike => (step.shape switch
                {
                    ZoneShape.Ring => $"anneau {step.innerRadius:0.#}–{step.radius:0.#} cases",
                    ZoneShape.Cone => $"cône {step.angle:0}° · {step.radius:0.#} cases",
                    ZoneShape.Line => $"ligne {step.radius:0.#} × {step.width:0.#} cases",
                    _ => $"cercle {step.radius:0.#} cases"
                }) + (step.zoneCount > 1 ? $" ×{step.zoneCount}" : string.Empty) + step.anchor switch
                {
                    ZoneAnchor.Target => " sur la cible",
                    ZoneAnchor.Ahead => $" à {step.anchorDistance:0.#} cases devant",
                    ZoneAnchor.Thrown => " lancé",
                    ZoneAnchor.RandomNearSelf or ZoneAnchor.RandomNearTarget => " au hasard",
                    _ => string.Empty
                } + (step.activeDuration > 0f ? $" · dure {step.activeDuration:0.#} s" : string.Empty),
                StepAction.Projectiles => $"{Label(step.pattern).ToLowerInvariant()} ×{step.projectiles}" + (step.homing > 0f ? " à tête chercheuse" : string.Empty),
                StepAction.Dash => $"{(step.airborne ? "saut" : "ruée")} {Label(step.dashDirection).ToLowerInvariant()} {step.dashDistance:0.#} cases",
                StepAction.Summon => $"invoque {(step.summon != null ? ForgeText.Name(step.summon.DisplayName) + " " : string.Empty)}×{step.summons}",
                StepAction.Teleport => $"téléportation {Label(step.teleportTo).ToLowerInvariant()}",
                StepAction.Buff => $"{Label(step.buff).ToLowerInvariant()}{(step.buff == BuffType.Invulnerable ? string.Empty : $" {step.buffPower:0} %")}",
                StepAction.Absorb => $"absorbe les alliés{(step.radius > 0f ? $" à {step.radius:0.#} cases" : string.Empty)} · {Label(step.buff).ToLowerInvariant()} par allié",
                StepAction.Phase => $"passe en phase {step.phase}",
                _ => "pause"
            };
            if (step.repeat > 1)
                what += $" · ×{step.repeat}";
            if (step.moveDuring != StepMove.None)
                what += $" · {Label(step.moveDuring).ToLowerInvariant()}";
            if (step.status != StatusEffect.None)
                what += $" · {Label(step.status).ToLowerInvariant()}";
            if (step.windup > 0f)
                what += $" · prépa {step.windup:0.##} s";
            return what;
        }

        /// <summary>Condition d'une étape en mots : « si l'étape précédente a touché et joueur à plus de 3 cases ».</summary>
        public static string When(AttackStep step, bool isFirst, System.Func<string, string> mechanicName = null)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!isFirst && step.trigger != StepTrigger.Always)
                parts.Add(step.trigger == StepTrigger.HitsAtLeast ? $"si au moins {step.hitCount} coups ont touché" : Label(step.trigger).ToLowerInvariant());
            foreach (var condition in step.conditions)
                parts.Add(Describe(condition, mechanicName));
            return parts.Count == 0 ? null : string.Join(" et ", parts);
        }

        public static string Label(EnemyConditionType type) => type switch
        {
            EnemyConditionType.PlayerCloserThan => "Joueur plus près que",
            EnemyConditionType.PlayerFartherThan => "Joueur plus loin que",
            EnemyConditionType.PlayerInSight => "Joueur en vue",
            EnemyConditionType.PlayerHidden => "Joueur caché par un mur",
            EnemyConditionType.PlayerHealthBelow => "PV du joueur sous",
            EnemyConditionType.OwnHealthBelow => "PV de l'ennemi sous",
            EnemyConditionType.PlayerLevelAtLeast => "Niveau du joueur au moins",
            EnemyConditionType.MechanicAtLeast => "Mécanique débloquée",
            EnemyConditionType.FlagSet => "Marqueur d'histoire actif",
            EnemyConditionType.FloorIn => "Étages",
            EnemyConditionType.BiomeIs => "Biome de la salle",
            EnemyConditionType.RoomTypeIs => "Type de salle",
            EnemyConditionType.AlliesAtMost => "Alliés restants au plus",
            EnemyConditionType.FightLongerThan => "Combat depuis plus de",
            EnemyConditionType.PhaseIs => "Phase de l'ennemi",
            _ => type.ToString()
        };

        /// <summary>Catégorie de la condition, pour regrouper la liste déroulante.</summary>
        public static string Group(EnemyConditionType type) => type switch
        {
            EnemyConditionType.PlayerCloserThan or EnemyConditionType.PlayerFartherThan or EnemyConditionType.PlayerInSight
                or EnemyConditionType.PlayerHidden => "Position du joueur",
            EnemyConditionType.PlayerHealthBelow or EnemyConditionType.PlayerLevelAtLeast or EnemyConditionType.MechanicAtLeast
                or EnemyConditionType.FlagSet => "État du joueur",
            EnemyConditionType.OwnHealthBelow or EnemyConditionType.FightLongerThan or EnemyConditionType.PhaseIs => "État de l'ennemi",
            _ => "Environnement"
        };

        /// <summary>Unité affichée après la valeur.</summary>
        public static string Unit(EnemyConditionType type) => type switch
        {
            EnemyConditionType.PlayerCloserThan or EnemyConditionType.PlayerFartherThan => "cases",
            EnemyConditionType.PlayerHealthBelow or EnemyConditionType.OwnHealthBelow => "%",
            EnemyConditionType.FightLongerThan => "s",
            _ => string.Empty
        };

        /// <summary>Bornes de la valeur, quand le type en utilise une sous forme de nombre.</summary>
        public static IntRange? Range(EnemyConditionType type, ForgeDesignLimits limits) => type switch
        {
            EnemyConditionType.PlayerCloserThan or EnemyConditionType.PlayerFartherThan => new IntRange(1, 20),
            EnemyConditionType.PlayerHealthBelow or EnemyConditionType.OwnHealthBelow => new IntRange(1, 100),
            EnemyConditionType.PlayerLevelAtLeast => new IntRange(1, limits.maxLevelCap),
            EnemyConditionType.AlliesAtMost => new IntRange(0, limits.maxEnemiesPerRoom),
            EnemyConditionType.FightLongerThan => new IntRange(1, 300),
            EnemyConditionType.PhaseIs => new IntRange(0, 9),
            _ => null
        };

        /// <summary>Libellé court d'une tuile de déplacement.</summary>
        public static string Short(PatternMovement movement) => movement switch
        {
            PatternMovement.Chase => "Poursuivre",
            PatternMovement.KeepDistance => "Distance",
            PatternMovement.Circle => "Tourner",
            PatternMovement.Flee => "Fuir",
            PatternMovement.Hold => "Sur place",
            PatternMovement.Charge => "Charger",
            _ => movement.ToString()
        };

        public static string Label(EnemyBehaviour behaviour) => behaviour switch
        {
            EnemyBehaviour.Melee => "Corps à corps",
            EnemyBehaviour.Ranged => "Tireur",
            EnemyBehaviour.Charger => "Chargeur",
            EnemyBehaviour.Summoner => "Invocateur",
            EnemyBehaviour.Stationary => "Tourelle",
            _ => behaviour.ToString()
        };

        public static string Describe(EnemyBehaviour behaviour) => behaviour switch
        {
            EnemyBehaviour.Melee => "Fonce sur le joueur et frappe au contact.",
            EnemyBehaviour.Ranged => "Garde ses distances et tire sur le joueur.",
            EnemyBehaviour.Charger => "Avance, s'arrête en clignotant, puis charge en ligne droite.",
            EnemyBehaviour.Summoner => "Reste à l'écart, tire et invoque des renforts du biome.",
            EnemyBehaviour.Stationary => "Ne bouge pas et tire des salves en cercle.",
            _ => string.Empty
        };

        /// <summary>Résumé d'un déplacement : « poursuit (150 %) ».</summary>
        public static string Action(EnemyPattern pattern)
        {
            var movement = pattern.movement switch
            {
                PatternMovement.Chase => "poursuit le joueur",
                PatternMovement.KeepDistance => "garde ses distances",
                PatternMovement.Circle => "tourne autour du joueur",
                PatternMovement.Flee => "fuit le joueur",
                PatternMovement.Hold => "reste sur place",
                PatternMovement.Charge => "charge le joueur",
                _ => string.Empty
            };
            return pattern.speedPercent != 100 && pattern.movement != PatternMovement.Hold ? $"{movement} ({pattern.speedPercent} %)" : movement;
        }

        /// <summary>« 3 étapes · jusqu'à 2 cases · recharge 3 s ».</summary>
        public static string Summary(ResolvedAttack attack) =>
            $"{attack.Steps.Count} étape{(attack.Steps.Count > 1 ? "s" : string.Empty)} · {Range(attack.Settings)} · recharge {attack.Settings.cooldown:0.#} s";

        public static string Range(AttackSettings settings) =>
            settings.minRange <= 0f ? $"jusqu'à {settings.maxRange:0.#} cases" : $"{settings.minRange:0.#} à {settings.maxRange:0.#} cases";

        /// <summary>Entrée du menu « + Condition ».</summary>
        public static string MenuLabel(EnemyConditionType type) => type switch
        {
            EnemyConditionType.PlayerCloserThan => "À moins de … cases",
            EnemyConditionType.PlayerFartherThan => "À plus de … cases",
            EnemyConditionType.PlayerInSight => "En vue (aucun mur entre eux)",
            EnemyConditionType.PlayerHidden => "Caché derrière un mur",
            EnemyConditionType.PlayerHealthBelow => "PV du joueur sous … %",
            EnemyConditionType.PlayerLevelAtLeast => "Niveau du joueur au moins …",
            EnemyConditionType.MechanicAtLeast => "Mécanique débloquée…",
            EnemyConditionType.FlagSet => "Marqueur d'histoire actif…",
            EnemyConditionType.OwnHealthBelow => "PV de l'ennemi sous … %",
            EnemyConditionType.FightLongerThan => "Combat depuis plus de … s",
            EnemyConditionType.PhaseIs => "Phase de l'ennemi = …",
            EnemyConditionType.FloorIn => "Étages…",
            EnemyConditionType.BiomeIs => "Biome…",
            EnemyConditionType.RoomTypeIs => "Type de salle…",
            EnemyConditionType.AlliesAtMost => "Alliés restants au plus …",
            _ => type.ToString()
        };

        /// <summary>Catégorie du menu « + Condition » : qui ou quoi est observé.</summary>
        public static string MenuGroup(EnemyConditionType type) => type switch
        {
            EnemyConditionType.OwnHealthBelow or EnemyConditionType.FightLongerThan or EnemyConditionType.PhaseIs => "L'ennemi",
            EnemyConditionType.FloorIn or EnemyConditionType.BiomeIs or EnemyConditionType.RoomTypeIs or EnemyConditionType.AlliesAtMost => "Le lieu",
            _ => "Le joueur"
        };

        /// <summary>Texte d'une pastille de condition, avant et après son champ : « joueur à moins de » [3] « cases ».</summary>
        public static (string before, string after) Chip(EnemyConditionType type) => type switch
        {
            EnemyConditionType.PlayerCloserThan => ("joueur à moins de", "cases"),
            EnemyConditionType.PlayerFartherThan => ("joueur à plus de", "cases"),
            EnemyConditionType.PlayerInSight => ("joueur en vue", null),
            EnemyConditionType.PlayerHidden => ("joueur caché par un mur", null),
            EnemyConditionType.PlayerHealthBelow => ("PV du joueur sous", "%"),
            EnemyConditionType.OwnHealthBelow => ("PV de l'ennemi sous", "%"),
            EnemyConditionType.PlayerLevelAtLeast => ("joueur niveau", "ou plus"),
            EnemyConditionType.MechanicAtLeast => ("mécanique", null),
            EnemyConditionType.FlagSet => ("marqueur", "actif"),
            EnemyConditionType.FloorIn => ("étages", null),
            EnemyConditionType.BiomeIs => ("biome", null),
            EnemyConditionType.RoomTypeIs => ("salle", null),
            EnemyConditionType.AlliesAtMost => ("alliés restants ≤", null),
            EnemyConditionType.FightLongerThan => ("combat depuis plus de", "s"),
            EnemyConditionType.PhaseIs => ("phase de l'ennemi =", "(0 = départ)"),
            _ => (type.ToString(), null)
        };

        /// <summary>Valeur proposée quand on choisit ce type de condition.</summary>
        public static int DefaultValue(EnemyConditionType type) => type switch
        {
            EnemyConditionType.PlayerCloserThan => 3,
            EnemyConditionType.PlayerFartherThan => 6,
            EnemyConditionType.PlayerHealthBelow or EnemyConditionType.OwnHealthBelow => 50,
            EnemyConditionType.PlayerLevelAtLeast => 5,
            EnemyConditionType.MechanicAtLeast => 1,
            EnemyConditionType.FloorIn => FloorSet.Of(1),
            EnemyConditionType.AlliesAtMost => 0,
            EnemyConditionType.FightLongerThan => 30,
            EnemyConditionType.PhaseIs => 1,
            _ => 0
        };

        /// <summary>Phrase lisible : « Joueur à moins de 3 cases », « PV de l'ennemi sous 50 % »…</summary>
        public static string Describe(EnemyCondition condition, System.Func<string, string> mechanicName = null) => condition.type switch
        {
            EnemyConditionType.PlayerCloserThan => $"joueur à moins de {condition.value} cases",
            EnemyConditionType.PlayerFartherThan => $"joueur à plus de {condition.value} cases",
            EnemyConditionType.PlayerInSight => "joueur en vue",
            EnemyConditionType.PlayerHidden => "joueur caché par un mur",
            EnemyConditionType.PlayerHealthBelow => $"PV du joueur sous {condition.value} %",
            EnemyConditionType.OwnHealthBelow => $"PV de l'ennemi sous {condition.value} %",
            EnemyConditionType.PlayerLevelAtLeast => $"joueur niveau {condition.value} ou plus",
            EnemyConditionType.MechanicAtLeast =>
                $"mécanique {ForgeText.Name(mechanicName?.Invoke(condition.flag) ?? condition.flag)}{(condition.value > 1 ? $" rang {condition.value}" : " débloquée")}",
            EnemyConditionType.FlagSet => $"marqueur {ForgeText.Name(condition.flag)} actif",
            EnemyConditionType.FloorIn => FloorSet.Describe(condition.value).ToLowerInvariant(),
            EnemyConditionType.BiomeIs => $"biome {ForgeText.Name(condition.biome != null ? condition.biome.DisplayName : null)}",
            EnemyConditionType.RoomTypeIs => $"salle de type {condition.roomType}",
            EnemyConditionType.AlliesAtMost => condition.value == 0 ? "plus aucun allié" : $"{condition.value} allié(s) ou moins",
            EnemyConditionType.FightLongerThan => $"combat depuis plus de {condition.value} s",
            EnemyConditionType.PhaseIs => condition.value == 0 ? "phase de départ" : $"phase {condition.value}",
            _ => condition.type.ToString()
        };
    }
}
