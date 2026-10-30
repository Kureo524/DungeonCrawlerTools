using System;
using System.Collections.Generic;
using TheDungeon.Data;
using UnityEditor;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>Règles de déplacement et attaques prêtes à l'emploi : un point de départ, à ajuster ensuite.</summary>
    public static class RulePresets
    {
        public sealed class Preset
        {
            public string Name;
            public string Description;
            public (EnemyConditionType type, int value)[] Conditions = Array.Empty<(EnemyConditionType, int)>();
            public PatternMovement Movement;
            public int Speed = 100;
        }

        /// <summary>Étape toute prête (une valeur non renseignée garde la valeur par défaut d'une étape).</summary>
        public sealed class StepPreset
        {
            public string Name;
            public string Description;
            public StepAction Action;
            public StepTrigger Trigger;
            public int HitCount = 2;
            public (EnemyConditionType type, int value)[] Conditions = Array.Empty<(EnemyConditionType, int)>();
            public StepFallback Fallback;
            public float Windup = 0.4f;
            public float Recovery = 0.2f;
            public bool Track;
            public int Damage = 100;
            public ZoneShape Shape;
            public ZoneAnchor Anchor;
            public float Radius = 1.5f;
            public float InnerRadius = 1f;
            public float Angle = 90f;
            public float Width = 1f;
            public ProjectilePattern Pattern;
            public int Projectiles = 1;
            public float Spread = 60f;
            public float ProjectileSpeed = 6.5f;
            public DashDirection Dash;
            public float DashDistance = 4f;
            public float DashSpeed = 12f;
            public int Summons = 1;
            public int Repeat = 1;
            public StepMove Move;
            public float MoveSpeed = 2f;
            public float AnchorDistance = 3f;
            public int ZoneCount = 1;
            public float ActiveDuration;
            public float Tick = 0.5f;
            public float Travel;
            public float Growth;
            public float Sweep;
            public bool Channel;
            public float Homing;
            public bool Airborne;
            public TeleportTarget TeleportTo;
            public BuffType Buff;
            public float BuffPower = 20f;
            public float BuffDuration = 8f;
            public int Phase = 1;
            public StatusEffect Status;
            public float StatusPower = 5f;
            public float StatusDuration = 3f;
            public float Guard;
            public bool SuperArmor;
            public float HealthCost;
            public bool Chargeable;
            public float ChargeTime = 1f;
            public float ChargeBonus = 100f;
        }

        public sealed class AttackPreset
        {
            public string Name;
            public string Description;
            public float MinRange;
            public float MaxRange = 2f;
            public float Cooldown = 3f;
            public AttackUser User;
            public StepPreset[] Steps;
        }

        public static readonly IReadOnlyList<Preset> All = new List<Preset>
        {
            new() { Name = "Rage", Description = "Sous 30 % de PV : poursuit le joueur beaucoup plus vite.",
                Conditions = new[] { (EnemyConditionType.OwnHealthBelow, 30) }, Movement = PatternMovement.Chase, Speed = 160 },
            new() { Name = "Recul", Description = "Le joueur approche à moins de 3 cases : s'éloigne.",
                Conditions = new[] { (EnemyConditionType.PlayerCloserThan, 3) }, Movement = PatternMovement.Flee, Speed = 120 },
            new() { Name = "Tireur", Description = "Joueur en vue : garde ses distances.",
                Conditions = new[] { (EnemyConditionType.PlayerInSight, 0) }, Movement = PatternMovement.KeepDistance },
            new() { Name = "Traque", Description = "Joueur caché derrière un mur : va le chercher.",
                Conditions = new[] { (EnemyConditionType.PlayerHidden, 0) }, Movement = PatternMovement.Chase, Speed = 120 },
            new() { Name = "Encerclement", Description = "Joueur à moins de 5 cases : tourne autour de lui.",
                Conditions = new[] { (EnemyConditionType.PlayerCloserThan, 5) }, Movement = PatternMovement.Circle },
            new() { Name = "Curée", Description = "Joueur sous 30 % de PV : fonce pour l'achever.",
                Conditions = new[] { (EnemyConditionType.PlayerHealthBelow, 30) }, Movement = PatternMovement.Chase, Speed = 140 }
        };

        /// <summary>Étapes proposées par « + Étape ».</summary>
        public static readonly IReadOnlyList<StepPreset> Steps = new List<StepPreset>
        {
            new() { Name = "Coup en cône", Description = "Frappe devant l'ennemi.", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 2f, Angle = 100f, Windup = 0.5f, Recovery = 0.3f },
            new() { Name = "Onde de choc", Description = "Cercle autour de l'ennemi.", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 3f, Windup = 0.8f, Recovery = 0.4f },
            new() { Name = "Anneau", Description = "Touche à distance, pas au contact.", Action = StepAction.Strike, Shape = ZoneShape.Ring, InnerRadius = 2f, Radius = 4f, Windup = 0.8f, Recovery = 0.4f },
            new() { Name = "Zone sous le joueur", Description = "Cercle là où se tient le joueur.", Action = StepAction.Strike, Shape = ZoneShape.Circle, Anchor = ZoneAnchor.Target, Radius = 1.5f, Windup = 0.9f, Recovery = 0.2f },
            new() { Name = "Estoc", Description = "Ligne droite devant l'ennemi.", Action = StepAction.Strike, Shape = ZoneShape.Line, Radius = 4f, Width = 1f, Windup = 0.6f, Recovery = 0.4f, Track = true },
            new() { Name = "Tir visé", Description = "Un projectile vers le joueur.", Action = StepAction.Projectiles, Pattern = ProjectilePattern.Aimed, Windup = 0.3f, Recovery = 0.1f },
            new() { Name = "Éventail", Description = "Cinq projectiles en éventail.", Action = StepAction.Projectiles, Pattern = ProjectilePattern.Spread, Projectiles = 5, Spread = 70f, Windup = 0.5f, Recovery = 0.3f },
            new() { Name = "Cercle de projectiles", Description = "Douze projectiles dans toutes les directions.", Action = StepAction.Projectiles, Pattern = ProjectilePattern.Ring, Projectiles = 12, ProjectileSpeed = 5f, Windup = 0.6f, Recovery = 0.3f },
            new() { Name = "Ruée", Description = "Fonce vers le joueur et le blesse au contact.", Action = StepAction.Dash, Dash = DashDirection.TowardPlayer, DashDistance = 5f, DashSpeed = 14f, Windup = 0.6f, Recovery = 0.5f, Track = true },
            new() { Name = "Recul", Description = "S'éloigne d'un bond, sans dégâts.", Action = StepAction.Dash, Dash = DashDirection.AwayFromPlayer, DashDistance = 3f, DashSpeed = 12f, Windup = 0.1f, Recovery = 0.1f, Damage = 0 },
            new() { Name = "Pause", Description = "Temps mort (pour rythmer).", Action = StepAction.Pause, Windup = 0.5f, Recovery = 0f },
            new() { Name = "Invocation", Description = "Un renfort du biome.", Action = StepAction.Summon, Windup = 0.8f, Recovery = 0.3f },
            new() { Name = "Boule lancée", Description = "Une boule vole vers la cible et laisse une flaque.", Action = StepAction.Strike, Shape = ZoneShape.Circle,
                Anchor = ZoneAnchor.Thrown, AnchorDistance = 9f, Radius = 2f, Windup = 1f, Recovery = 0.3f, ActiveDuration = 3f, Tick = 0.5f, Damage = 40,
                Status = StatusEffect.Rot, StatusPower = 4f, StatusDuration = 4f },
            new() { Name = "Pluie de zones", Description = "Plusieurs zones au hasard autour de la cible.", Action = StepAction.Strike, Shape = ZoneShape.Circle,
                Anchor = ZoneAnchor.RandomNearTarget, AnchorDistance = 3f, ZoneCount = 5, Radius = 1.2f, Windup = 1f, Recovery = 0.3f },
            new() { Name = "Onde au sol", Description = "Une zone qui avance tout droit.", Action = StepAction.Strike, Shape = ZoneShape.Circle, Anchor = ZoneAnchor.Ahead,
                AnchorDistance = 1f, Radius = 1.2f, Travel = 8f, ActiveDuration = 1.2f, Tick = 0.2f, Windup = 0.6f, Recovery = 0.4f },
            new() { Name = "Rayon balayant", Description = "Une ligne qui pivote ; l'attaquant reste occupé.", Action = StepAction.Strike, Shape = ZoneShape.Line,
                Radius = 9f, Width = 1f, ActiveDuration = 1.8f, Sweep = 120f, Channel = true, Tick = 0.15f, Windup = 1f, Recovery = 0.6f, Damage = 40 },
            new() { Name = "Anneau qui s'étend", Description = "Un anneau qui grandit : il faut le sauter (ruée) ou s'éloigner.", Action = StepAction.Strike,
                Shape = ZoneShape.Ring, InnerRadius = 0.6f, Radius = 1.2f, Growth = 5f, ActiveDuration = 1.2f, Tick = 0.15f, Windup = 0.6f, Recovery = 0.3f },
            new() { Name = "Saut écrasant", Description = "Bond par-dessus la cible (invulnérable), à suivre d'une frappe.", Action = StepAction.Dash,
                Dash = DashDirection.TowardPlayer, Airborne = true, DashDistance = 5f, DashSpeed = 10f, Windup = 0.5f, Recovery = 0f, Damage = 0, Track = true },
            new() { Name = "Téléportation", Description = "Disparaît puis réapparaît derrière la cible.", Action = StepAction.Teleport, TeleportTo = TeleportTarget.BehindTarget,
                AnchorDistance = 2f, Windup = 0.6f, Recovery = 0.1f },
            new() { Name = "Garde", Description = "Posture : 90 % de dégâts en moins. Suivre d'une étape « si l'attaquant a été touché » pour une riposte.",
                Action = StepAction.Pause, Windup = 1.5f, Recovery = 0f, Guard = 90f },
            new() { Name = "Renforcement", Description = "Dégâts en plus pour un temps.", Action = StepAction.Buff, Buff = BuffType.Damage, BuffPower = 30f, BuffDuration = 12f,
                Windup = 1f, Recovery = 0.3f, SuperArmor = true },
            new() { Name = "Absorption", Description = "Absorbe les alliés proches : chacun le renforce.", Action = StepAction.Absorb, Radius = 6f, Buff = BuffType.Damage,
                BuffPower = 15f, BuffDuration = 20f, Windup = 1f, Recovery = 0.3f },
            new() { Name = "Changer de phase", Description = "Fait passer l'ennemi en phase 1 (définie dans l'ennemi).", Action = StepAction.Phase, Phase = 1, Windup = 0.5f, Recovery = 0.5f }
        };

        public static readonly IReadOnlyList<AttackPreset> Attacks = new List<AttackPreset>
        {
            new() { Name = "Coup de massue", Description = "Un coup en cône, bien annoncé.", MaxRange = 2f, Cooldown = 1.5f, Steps = new[]
            {
                new StepPreset { Name = "Coup", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 2f, Angle = 100f, Windup = 0.5f, Recovery = 0.4f, Damage = 120 }
            } },
            new() { Name = "Coup chargé", Description = "Même annonce ; ruée d'abord si le joueur s'est éloigné, puis le coup.", MaxRange = 7f, Cooldown = 5f, Steps = new[]
            {
                new StepPreset { Name = "Armer", Action = StepAction.Pause, Windup = 0.9f, Recovery = 0f },
                new StepPreset { Name = "Ruée", Action = StepAction.Dash, Dash = DashDirection.TowardPlayer, DashDistance = 6f, DashSpeed = 16f, Windup = 0f, Recovery = 0f, Damage = 0,
                    Conditions = new[] { (EnemyConditionType.PlayerFartherThan, 3) }, Fallback = StepFallback.Skip },
                new StepPreset { Name = "Coup", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 2.2f, Angle = 110f, Windup = 0.1f, Recovery = 0.6f, Damage = 150 }
            } },
            new() { Name = "Bond stellaire", Description = "Zone qui soulève, puis dégâts à la retombée, que la première ait touché ou non.", MaxRange = 3f, Cooldown = 6f, Steps = new[]
            {
                new StepPreset { Name = "Soulèvement", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 3f, Windup = 0.9f, Recovery = 0.3f, Damage = 60 },
                new StepPreset { Name = "Retombée", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 3.5f, Windup = 0.6f, Recovery = 0.7f, Damage = 120 }
            } },
            new() { Name = "Combo", Description = "Trois coups : chaque suite n'a lieu que si le coup d'avant a touché.", MaxRange = 2f, Cooldown = 4f, Steps = new[]
            {
                new StepPreset { Name = "Premier coup", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 2f, Angle = 90f, Windup = 0.4f, Recovery = 0.1f, Damage = 80 },
                new StepPreset { Name = "Deuxième coup", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 2f, Angle = 90f, Windup = 0.2f, Recovery = 0.1f, Damage = 80,
                    Trigger = StepTrigger.PreviousHit, Fallback = StepFallback.EndAttack, Track = true },
                new StepPreset { Name = "Coup final", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 2.5f, Windup = 0.4f, Recovery = 0.8f, Damage = 150,
                    Trigger = StepTrigger.PreviousHit, Fallback = StepFallback.EndAttack }
            } },
            new() { Name = "Pluie ciblée", Description = "Trois zones sous le joueur ; une explosion finale si au moins deux ont touché.", MinRange = 2f, MaxRange = 9f, Cooldown = 7f, Steps = new[]
            {
                new StepPreset { Name = "Impact 1", Action = StepAction.Strike, Shape = ZoneShape.Circle, Anchor = ZoneAnchor.Target, Radius = 1.3f, Windup = 0.8f, Recovery = 0.1f },
                new StepPreset { Name = "Impact 2", Action = StepAction.Strike, Shape = ZoneShape.Circle, Anchor = ZoneAnchor.Target, Radius = 1.3f, Windup = 0.6f, Recovery = 0.1f },
                new StepPreset { Name = "Impact 3", Action = StepAction.Strike, Shape = ZoneShape.Circle, Anchor = ZoneAnchor.Target, Radius = 1.3f, Windup = 0.6f, Recovery = 0.1f },
                new StepPreset { Name = "Explosion", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 4f, Windup = 0.5f, Recovery = 0.8f, Damage = 150,
                    Trigger = StepTrigger.HitsAtLeast, HitCount = 2, Fallback = StepFallback.EndAttack }
            } },
            new() { Name = "Rafale", Description = "Éventail de cinq projectiles à moyenne distance.", MinRange = 2f, MaxRange = 7f, Cooldown = 3f, Steps = new[]
            {
                new StepPreset { Name = "Rafale", Action = StepAction.Projectiles, Pattern = ProjectilePattern.Spread, Projectiles = 5, Spread = 70f, Windup = 0.5f, Recovery = 0.3f }
            } },
            new() { Name = "Tir visé", Description = "Un projectile vers le joueur, à distance.", MinRange = 3f, MaxRange = 9f, Cooldown = 1.5f, Steps = new[]
            {
                new StepPreset { Name = "Tir", Action = StepAction.Projectiles, Pattern = ProjectilePattern.Aimed, Windup = 0.25f, Recovery = 0.1f }
            } },
            new() { Name = "Appel à l'aide", Description = "Invoque un renfort du biome, à toute distance.", MaxRange = 15f, Cooldown = 10f, Steps = new[]
            {
                new StepPreset { Name = "Invocation", Action = StepAction.Summon, Windup = 1f, Recovery = 0.5f }
            } },
            new() { Name = "Boule de putréfaction", Description = "Lance une boule qui laisse une flaque de putréfaction (Dreglord).", MinRange = 2f, MaxRange = 9f, Cooldown = 6f, Steps = new[]
            {
                new StepPreset { Name = "Lancer", Action = StepAction.Strike, Shape = ZoneShape.Circle, Anchor = ZoneAnchor.Thrown, AnchorDistance = 9f, Radius = 2.2f,
                    Windup = 1f, Recovery = 0.4f, ActiveDuration = 4f, Tick = 0.5f, Damage = 30, Status = StatusEffect.Rot, StatusPower = 4f, StatusDuration = 4f }
            } },
            new() { Name = "Assaut depuis les piliers", Description = "Bondit en arrière sur un promontoire, puis charge deux fois sur le joueur.", MaxRange = 10f, Cooldown = 9f, Steps = new[]
            {
                new StepPreset { Name = "Bond", Action = StepAction.Dash, Dash = DashDirection.AwayFromPlayer, Airborne = true, DashDistance = 5f, DashSpeed = 10f, Windup = 0.4f, Recovery = 0.4f, Damage = 0 },
                new StepPreset { Name = "Charge", Action = StepAction.Dash, Dash = DashDirection.TowardPlayer, DashDistance = 9f, DashSpeed = 16f, Windup = 0.7f, Recovery = 0.3f,
                    Damage = 130, Repeat = 2, Track = true, SuperArmor = true, Status = StatusEffect.Knockback, StatusPower = 10f }
            } },
            new() { Name = "Montagne de putréfaction", Description = "Absorbe les sbires proches, explose, puis passe en phase de putréfaction (aura + sbires).", MaxRange = 6f, Cooldown = 25f, Steps = new[]
            {
                new StepPreset { Name = "Rassemblement", Action = StepAction.Pause, Windup = 1.4f, Recovery = 0f, Guard = 50f, SuperArmor = true },
                new StepPreset { Name = "Absorption", Action = StepAction.Absorb, Radius = 8f, Buff = BuffType.Damage, BuffPower = 15f, BuffDuration = 20f, Windup = 0.2f, Recovery = 0f },
                new StepPreset { Name = "Éruption", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 4.5f, Windup = 0.6f, Recovery = 0.8f, Damage = 120,
                    Status = StatusEffect.Rot, StatusPower = 6f, StatusDuration = 5f, SuperArmor = true },
                new StepPreset { Name = "Putréfaction", Action = StepAction.Phase, Phase = 1, Windup = 0f, Recovery = 0.3f }
            } },
            new() { Name = "Saisie", Description = "Attrape (étourdit) ; morsure puis projection seulement si la saisie a pris (Adel).", MaxRange = 2f, Cooldown = 8f, Steps = new[]
            {
                new StepPreset { Name = "Saisie", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 1.8f, Angle = 70f, Windup = 0.6f, Recovery = 0.1f, Damage = 30,
                    Status = StatusEffect.Stun, StatusDuration = 1.6f },
                new StepPreset { Name = "Morsure", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 1.8f, Windup = 0.4f, Recovery = 0.1f, Damage = 200,
                    Trigger = StepTrigger.PreviousHit, Fallback = StepFallback.EndAttack, SuperArmor = true },
                new StepPreset { Name = "Projection", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 2f, Windup = 0.2f, Recovery = 0.8f, Damage = 60,
                    Trigger = StepTrigger.PreviousHit, Fallback = StepFallback.EndAttack, Status = StatusEffect.Launch, StatusDuration = 1f }
            } },
            new() { Name = "Rayon balayant", Description = "Rayon canalisé qui balaie l'arène (Libra, Heolstor).", MinRange = 3f, MaxRange = 10f, Cooldown = 10f, Steps = new[]
            {
                new StepPreset { Name = "Rayon", Action = StepAction.Strike, Shape = ZoneShape.Line, Radius = 10f, Width = 1.2f, ActiveDuration = 2f, Sweep = 140f, Channel = true,
                    Tick = 0.15f, Windup = 1.1f, Recovery = 0.8f, Damage = 35, SuperArmor = true }
            } },
            new() { Name = "Anneaux d'ombre", Description = "Trois anneaux qui s'étendent depuis l'ennemi, à esquiver en rythme.", MaxRange = 6f, Cooldown = 9f, Steps = new[]
            {
                new StepPreset { Name = "Anneau", Action = StepAction.Strike, Shape = ZoneShape.Ring, InnerRadius = 0.5f, Radius = 1.1f, Growth = 5f, ActiveDuration = 1.3f,
                    Tick = 0.15f, Windup = 0.5f, Recovery = 0.2f, Damage = 60, Repeat = 3 }
            } },
            new() { Name = "Disparition", Description = "Se téléporte derrière le joueur et frappe aussitôt.", MinRange = 3f, MaxRange = 12f, Cooldown = 7f, Steps = new[]
            {
                new StepPreset { Name = "Disparition", Action = StepAction.Teleport, TeleportTo = TeleportTarget.BehindTarget, AnchorDistance = 1.8f, Windup = 0.6f, Recovery = 0f },
                new StepPreset { Name = "Taillade", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 2.2f, Angle = 120f, Windup = 0.35f, Recovery = 0.6f, Damage = 130 }
            } },
            new() { Name = "Meute", Description = "Se divise : invoque deux clones et accélère (Gladius). Choisissez le clone dans l'étape.", MaxRange = 12f, Cooldown = 20f, Steps = new[]
            {
                new StepPreset { Name = "Division", Action = StepAction.Summon, Summons = 2, AnchorDistance = 3f, Windup = 1f, Recovery = 0.2f, SuperArmor = true },
                new StepPreset { Name = "Frénésie", Action = StepAction.Buff, Buff = BuffType.Speed, BuffPower = 40f, BuffDuration = 10f, Windup = 0.2f, Recovery = 0.2f }
            } },
            new() { Name = "Garde et riposte", Description = "Se met en garde ; riposte seulement si le joueur frappe la garde.", MaxRange = 3f, Cooldown = 8f, Steps = new[]
            {
                new StepPreset { Name = "Garde", Action = StepAction.Pause, Windup = 1.6f, Recovery = 0f, Guard = 90f, SuperArmor = true },
                new StepPreset { Name = "Riposte", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 2.4f, Angle = 140f, Windup = 0.1f, Recovery = 0.6f, Damage = 160,
                    Trigger = StepTrigger.WasHit, Fallback = StepFallback.EndAttack }
            } },
            new() { Name = "Marche écrasante", Description = "Avance vers le joueur en enchaînant trois coups.", MaxRange = 2.2f, Cooldown = 6f, Steps = new[]
            {
                new StepPreset { Name = "Coups en marchant", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 2f, Angle = 100f, Windup = 0.35f, Recovery = 0.1f,
                    Damage = 90, Repeat = 3, Move = StepMove.TowardTarget, MoveSpeed = 3f, Track = true, SuperArmor = true }
            } },
            new() { Name = "Combo d'épée", Description = "Arme : trois coups enchaînés en avançant.", User = AttackUser.Weapon, Cooldown = 0.2f, Steps = new[]
            {
                new StepPreset { Name = "Taille", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 1.8f, Angle = 110f, Windup = 0.1f, Recovery = 0.1f, Damage = 100,
                    Move = StepMove.Forward, MoveSpeed = 2f },
                new StepPreset { Name = "Revers", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 1.8f, Angle = 110f, Windup = 0.12f, Recovery = 0.1f, Damage = 100,
                    Move = StepMove.Forward, MoveSpeed = 2f },
                new StepPreset { Name = "Coup final", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 2f, Windup = 0.25f, Recovery = 0.3f, Damage = 150 }
            } },
            new() { Name = "Estoc chargé", Description = "Arme : maintenir pour charger un estoc qui porte loin.", User = AttackUser.Weapon, Cooldown = 0.3f, Steps = new[]
            {
                new StepPreset { Name = "Estoc", Action = StepAction.Strike, Shape = ZoneShape.Line, Radius = 3f, Width = 0.9f, Windup = 0.2f, Recovery = 0.25f, Damage = 110,
                    Chargeable = true, ChargeTime = 1f, ChargeBonus = 150f }
            } },
            new() { Name = "Bond du lion", Description = "Compétence : saut puis frappe circulaire à l'atterrissage.", User = AttackUser.Skill, Cooldown = 4f, Steps = new[]
            {
                new StepPreset { Name = "Saut", Action = StepAction.Dash, Dash = DashDirection.TowardPlayer, Airborne = true, DashDistance = 4f, DashSpeed = 12f, Windup = 0.15f, Recovery = 0f, Damage = 0 },
                new StepPreset { Name = "Impact", Action = StepAction.Strike, Shape = ZoneShape.Circle, Radius = 2f, Windup = 0.05f, Recovery = 0.4f, Damage = 250, SuperArmor = true,
                    Status = StatusEffect.Knockback, StatusPower = 8f }
            } },
            new() { Name = "Pas du limier", Description = "Compétence : ruée invulnérable dans la direction visée.", User = AttackUser.Skill, Cooldown = 1.5f, Steps = new[]
            {
                new StepPreset { Name = "Pas", Action = StepAction.Dash, Dash = DashDirection.TowardPlayer, Airborne = true, DashDistance = 4f, DashSpeed = 20f, Windup = 0f, Recovery = 0.1f, Damage = 0 }
            } },
            new() { Name = "Phalange de lames", Description = "Compétence : cinq lames à tête chercheuse.", User = AttackUser.Skill, Cooldown = 5f, Steps = new[]
            {
                new StepPreset { Name = "Lames", Action = StepAction.Projectiles, Pattern = ProjectilePattern.Spread, Projectiles = 5, Spread = 60f, ProjectileSpeed = 9f,
                    Homing = 120f, Windup = 0.5f, Recovery = 0.2f, Damage = 70 }
            } },
            new() { Name = "Flamme", Description = "Compétence : jet de flammes en cône, canalisé.", User = AttackUser.Skill, Cooldown = 6f, Steps = new[]
            {
                new StepPreset { Name = "Flamme", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 4f, Angle = 50f, ActiveDuration = 1.2f, Channel = true, Tick = 0.2f,
                    Windup = 0.3f, Recovery = 0.3f, Damage = 35, Status = StatusEffect.Burn, StatusPower = 6f, StatusDuration = 3f }
            } },
            new() { Name = "Piétinement de givre", Description = "Compétence : onde de glace qui avance et ralentit.", User = AttackUser.Skill, Cooldown = 6f, Steps = new[]
            {
                new StepPreset { Name = "Onde", Action = StepAction.Strike, Shape = ZoneShape.Circle, Anchor = ZoneAnchor.Ahead, AnchorDistance = 1f, Radius = 1.2f, Travel = 8f,
                    ActiveDuration = 1.2f, Tick = 0.2f, Windup = 0.4f, Recovery = 0.4f, Damage = 60, Status = StatusEffect.Frost, StatusPower = 50f, StatusDuration = 3f }
            } },
            new() { Name = "Lame de sang", Description = "Compétence : coûte des PV, large taillade qui fait saigner.", User = AttackUser.Skill, Cooldown = 3f, Steps = new[]
            {
                new StepPreset { Name = "Taillade", Action = StepAction.Strike, Shape = ZoneShape.Cone, Radius = 3.5f, Angle = 140f, Windup = 0.35f, Recovery = 0.4f, Damage = 220,
                    HealthCost = 8f, Status = StatusEffect.Bleed, StatusPower = 10f }
            } },
            new() { Name = "Cri de guerre", Description = "Compétence : dégâts en plus pendant 15 s.", User = AttackUser.Skill, Cooldown = 20f, Steps = new[]
            {
                new StepPreset { Name = "Cri", Action = StepAction.Buff, Buff = BuffType.Damage, BuffPower = 25f, BuffDuration = 15f, Windup = 0.6f, Recovery = 0.2f, SuperArmor = true }
            } }
        };

        public static readonly Preset Empty = new() { Name = "Nouvelle règle", Movement = PatternMovement.Chase };


        static void SetConditions(SerializedProperty conditions, (EnemyConditionType type, int value)[] values)
        {
            conditions.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                var condition = conditions.GetArrayElementAtIndex(i);
                condition.FindPropertyRelative("type").enumValueIndex = (int)values[i].type;
                condition.FindPropertyRelative("value").intValue = values[i].value;
                condition.FindPropertyRelative("flag").stringValue = string.Empty;
                condition.FindPropertyRelative("biome").objectReferenceValue = null;
                condition.FindPropertyRelative("roomType").enumValueIndex = 0;
            }
        }

        /// <summary>Remplit une règle (élément de « patterns ») avec ce modèle.</summary>
        public static void Apply(Preset preset, SerializedProperty pattern)
        {
            pattern.FindPropertyRelative("name").stringValue = preset.Name;
            SetConditions(pattern.FindPropertyRelative("conditions"), preset.Conditions);
            pattern.FindPropertyRelative("movement").enumValueIndex = (int)preset.Movement;
            pattern.FindPropertyRelative("speedPercent").intValue = preset.Speed;
        }

        /// <summary>Remplit une attaque (AttackData) avec ce modèle.</summary>
        public static void Apply(AttackPreset preset, SerializedObject attack)
        {
            attack.FindProperty("displayName").stringValue = preset.Name;
            attack.FindProperty("description").stringValue = preset.Description;
            attack.FindProperty("user").enumValueIndex = (int)preset.User;
            var settings = attack.FindProperty("settings");
            settings.FindPropertyRelative("minRange").floatValue = preset.MinRange;
            settings.FindPropertyRelative("maxRange").floatValue = preset.MaxRange;
            settings.FindPropertyRelative("cooldown").floatValue = preset.Cooldown;
            settings.FindPropertyRelative("damagePercent").intValue = 100;
            settings.FindPropertyRelative("tempoPercent").intValue = 100;
            var steps = attack.FindProperty("steps");
            steps.arraySize = preset.Steps.Length;
            for (var i = 0; i < preset.Steps.Length; i++)
                Apply(preset.Steps[i], steps.GetArrayElementAtIndex(i));
        }

        /// <summary>Remplit une étape (élément de « steps ») avec ce modèle.</summary>
        public static void Apply(StepPreset preset, SerializedProperty step)
        {
            step.FindPropertyRelative("name").stringValue = preset.Name;
            step.FindPropertyRelative("action").enumValueIndex = (int)preset.Action;
            step.FindPropertyRelative("trigger").enumValueIndex = (int)preset.Trigger;
            step.FindPropertyRelative("hitCount").intValue = preset.HitCount;
            SetConditions(step.FindPropertyRelative("conditions"), preset.Conditions);
            step.FindPropertyRelative("fallback").enumValueIndex = (int)preset.Fallback;
            step.FindPropertyRelative("windup").floatValue = preset.Windup;
            step.FindPropertyRelative("recovery").floatValue = preset.Recovery;
            step.FindPropertyRelative("trackDuringWindup").boolValue = preset.Track;
            step.FindPropertyRelative("damagePercent").intValue = preset.Damage;
            step.FindPropertyRelative("shape").enumValueIndex = (int)preset.Shape;
            step.FindPropertyRelative("anchor").enumValueIndex = (int)preset.Anchor;
            step.FindPropertyRelative("radius").floatValue = preset.Radius;
            step.FindPropertyRelative("innerRadius").floatValue = preset.InnerRadius;
            step.FindPropertyRelative("angle").floatValue = preset.Angle;
            step.FindPropertyRelative("width").floatValue = preset.Width;
            step.FindPropertyRelative("pattern").enumValueIndex = (int)preset.Pattern;
            step.FindPropertyRelative("projectiles").intValue = preset.Projectiles;
            step.FindPropertyRelative("spreadAngle").floatValue = preset.Spread;
            step.FindPropertyRelative("projectileSpeed").floatValue = preset.ProjectileSpeed;
            step.FindPropertyRelative("dashDirection").enumValueIndex = (int)preset.Dash;
            step.FindPropertyRelative("dashDistance").floatValue = preset.DashDistance;
            step.FindPropertyRelative("dashSpeed").floatValue = preset.DashSpeed;
            step.FindPropertyRelative("summons").intValue = preset.Summons;
            step.FindPropertyRelative("repeat").intValue = preset.Repeat;
            step.FindPropertyRelative("moveDuring").enumValueIndex = (int)preset.Move;
            step.FindPropertyRelative("moveSpeed").floatValue = preset.MoveSpeed;
            step.FindPropertyRelative("anchorDistance").floatValue = preset.AnchorDistance;
            step.FindPropertyRelative("zoneCount").intValue = preset.ZoneCount;
            step.FindPropertyRelative("activeDuration").floatValue = preset.ActiveDuration;
            step.FindPropertyRelative("tickInterval").floatValue = preset.Tick;
            step.FindPropertyRelative("travelSpeed").floatValue = preset.Travel;
            step.FindPropertyRelative("growth").floatValue = preset.Growth;
            step.FindPropertyRelative("sweepAngle").floatValue = preset.Sweep;
            step.FindPropertyRelative("channel").boolValue = preset.Channel;
            step.FindPropertyRelative("homing").floatValue = preset.Homing;
            step.FindPropertyRelative("airborne").boolValue = preset.Airborne;
            step.FindPropertyRelative("teleportTo").enumValueIndex = (int)preset.TeleportTo;
            step.FindPropertyRelative("summon").objectReferenceValue = null;
            step.FindPropertyRelative("buff").enumValueIndex = (int)preset.Buff;
            step.FindPropertyRelative("buffPower").floatValue = preset.BuffPower;
            step.FindPropertyRelative("buffDuration").floatValue = preset.BuffDuration;
            step.FindPropertyRelative("phase").intValue = preset.Phase;
            step.FindPropertyRelative("status").enumValueIndex = (int)preset.Status;
            step.FindPropertyRelative("statusPower").floatValue = preset.StatusPower;
            step.FindPropertyRelative("statusDuration").floatValue = preset.StatusDuration;
            step.FindPropertyRelative("guardPercent").floatValue = preset.Guard;
            step.FindPropertyRelative("superArmor").boolValue = preset.SuperArmor;
            step.FindPropertyRelative("healthCost").floatValue = preset.HealthCost;
            step.FindPropertyRelative("chargeable").boolValue = preset.Chargeable;
            step.FindPropertyRelative("chargeTime").floatValue = preset.ChargeTime;
            step.FindPropertyRelative("chargeBonus").floatValue = preset.ChargeBonus;
        }
    }
}
