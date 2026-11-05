using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static TheDungeon.Editor.ContentDatabase.AttackFields;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Enchaînement d'une attaque (outil Attaques) : les étapes, chacune avec sa condition (évaluée au moment où elle commence),
    /// son action, son rythme et ses options. Le déclenchement se règle avec <see cref="AttackSettingsEditor"/>.
    /// </summary>
    public static class AttackEditor
    {
        static ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        /// <summary>
        /// Liste des étapes d'une attaque (outil Attaques) : une carte par étape, réordonnable par glisser-déposer,
        /// et « + Étape » / modèles d'étapes.
        /// </summary>
        public static VisualElement Steps(SerializedObject attack, VisualElement overlay, Action rebuild)
        {
            var container = new VisualElement();
            var steps = attack.FindProperty("steps");
            var list = new VisualElement();
            var reorder = new ForgeReorder(list, overlay, container.GetFirstAncestorOfType<ScrollView>(), (from, to) =>
            {
                attack.Update();
                attack.FindProperty("steps").MoveArrayElement(from, to);
                attack.ApplyModifiedProperties();
                rebuild();
            });
            for (var i = 0; i < steps.arraySize; i++)
                list.Add(StepCard(attack, steps.GetArrayElementAtIndex(i).propertyPath, i, reorder, rebuild));
            container.Add(list);
            if (steps.arraySize == 0)
                container.Add(ForgeUI.Label("Aucune étape : l'attaque ne fait rien. Ajoutez-en une.", "forge-rule-editor__warning"));

            var add = ForgeUI.Button("+ Étape ▾", "Ajoute une étape à la fin de l'enchaînement.", null, "forge-button--primary");
            add.clicked += () =>
            {
                var menu = ForgeDropdowns.Menu();
                foreach (var preset in RulePresets.Steps)
                {
                    var chosen = preset;
                    menu.AddItem($"{preset.Name} — {preset.Description}", false, () =>
                    {
                        attack.Update();
                        var list = attack.FindProperty("steps");
                        if (list.arraySize >= Limits.maxAttackSteps)
                        {
                            ForgeLists.Notify($"Maximum atteint : {Limits.maxAttackSteps} étapes.");
                            return;
                        }
                        list.arraySize++;
                        RulePresets.Apply(chosen, list.GetArrayElementAtIndex(list.arraySize - 1));
                        attack.ApplyModifiedProperties();
                        rebuild();
                    });
                }
                menu.DropDown(add.worldBound, add, DropdownMenuSizeMode.Content);
            };
            add.style.alignSelf = Align.FlexStart;
            container.Add(add);
            return container;
        }

        static VisualElement StepCard(SerializedObject attack, string path, int index, ForgeReorder reorder, Action rebuild)
        {
            var step = attack.FindProperty(path);
            var card = new VisualElement();
            card.AddToClassList("forge-step-card");
            var action = (StepAction)step.FindPropertyRelative("action").enumValueIndex;

            // En-tête : poignée, numéro, nom, supprimer.
            var name = new TextField { tooltip = "Nom de l'étape (ex : « Ruée », « Retombée »)." };
            name.BindProperty(step.FindPropertyRelative("name"));
            name.style.flexGrow = 1;
            var handle = reorder.Handle(card, () => ForgeUI.Label($"{index + 1}  {step.FindPropertyRelative("name").stringValue}", "forge-reorder-ghost__name"),
                "Maintenez et glissez pour changer l'ordre des étapes.");
            var delete = ForgeUI.CloseButton("Supprimer cette étape.", () =>
            {
                attack.Update();
                attack.FindProperty("steps").DeleteArrayElementAtIndex(index);
                attack.ApplyModifiedProperties();
                rebuild();
            });
            card.Add(ForgeLists.Row(handle, ForgeUI.Label((index + 1).ToString(), "forge-rule-card__number"), name, delete));

            // Quand : la première étape est toujours jouée ; les suivantes peuvent dépendre de ce qui s'est passé avant.
            var when = new VisualElement();
            when.AddToClassList("forge-step-card__when");
            if (index == 0)
                when.Add(ForgeUI.Label("Première étape : jouée dès que l'attaque est lancée. Ses conditions éventuelles s'ajoutent.", "forge-hint"));
            else
                when.Add(TriggerRow(attack, path, rebuild));
            when.Add(new ConditionChips(attack, rebuild).Build(path, null));
            if (index > 0 || step.FindPropertyRelative("conditions").arraySize > 0)
                when.Add(FallbackRow(attack, path, rebuild));
            card.Add(when);

            // Action.
            var actions = new VisualElement();
            actions.AddToClassList("forge-tiles");
            foreach (var option in (StepAction[])Enum.GetValues(typeof(StepAction)))
                actions.Add(BehaviourGlyphs.Tile(EnemyPatternText.Label(option), EnemyPatternText.Describe(option),
                    (p, c, s) => BehaviourGlyphs.Action(p, c, s, option), option == action,
                    () => SetEnum(attack, path, "action", (int)option, rebuild)));
            card.Add(actions);

            var details = new VisualElement();
            details.AddToClassList("forge-step-card__details");
            var fields = new VisualElement();
            fields.AddToClassList("forge-step-card__fields");
            details.Add(fields);
            BuildActionFields(fields, attack, path, action, rebuild);
            if (action is StepAction.Strike or StepAction.Projectiles or StepAction.Dash)
                details.Add(new StepPreview(() => attack.targetObject is AttackData data && index < data.Steps.Count ? data.Steps[index] : null));
            card.Add(details);
            return card;
        }

        static VisualElement TriggerRow(SerializedObject attack, string path, Action rebuild)
        {
            var step = attack.FindProperty(path);
            var triggers = (StepTrigger[])Enum.GetValues(typeof(StepTrigger));
            var labels = triggers.Select(EnemyPatternText.Label).ToList();
            var current = (StepTrigger)step.FindPropertyRelative("trigger").enumValueIndex;
            var dropdown = new DropdownField(labels, Array.IndexOf(triggers, current))
                { tooltip = "Évalué au moment où l'étape commence. L'étape « précédente » est la dernière étape jouée." };
            dropdown.AddToClassList("forge-step-card__trigger");
            dropdown.RegisterValueChangedCallback(evt => SetEnum(attack, path, "trigger", labels.IndexOf(evt.newValue), rebuild));
            var row = ForgeLists.Row(ForgeUI.Label("Jouée", "forge-chip__text"), dropdown);
            if (current == StepTrigger.HitsAtLeast)
                row.Add(Pill("N =", IntField(step.FindPropertyRelative("hitCount"), 1, 20), "coups",
                    "Nombre de coups ayant touché le joueur depuis le début de l'attaque (projectiles compris)."));
            return row;
        }

        static VisualElement FallbackRow(SerializedObject attack, string path, Action rebuild)
        {
            var step = attack.FindProperty(path);
            var labels = new List<string> { "passer à l'étape suivante", "terminer l'attaque" };
            var dropdown = new DropdownField(labels, step.FindPropertyRelative("fallback").enumValueIndex)
                { tooltip = "Que faire si cette étape n'est pas jouée." };
            dropdown.AddToClassList("forge-step-card__trigger");
            dropdown.RegisterValueChangedCallback(evt => SetEnum(attack, path, "fallback", labels.IndexOf(evt.newValue), rebuild));
            return ForgeLists.Row(ForgeUI.Label("Sinon :", "forge-chip__text"), dropdown);
        }

        static void BuildActionFields(VisualElement container, SerializedObject attack, string path, StepAction action, Action rebuild)
        {
            var step = attack.FindProperty(path);
            container.Add(ForgeUI.Label(EnemyPatternText.Describe(action), "forge-hint"));
            switch (action)
            {
                case StepAction.Strike:
                    AddStrikeFields(container, attack, path, step, rebuild);
                    break;
                case StepAction.Projectiles:
                    AddProjectileFields(container, attack, path, step, rebuild);
                    break;
                case StepAction.Dash:
                    AddDashFields(container, attack, path, step, rebuild);
                    break;
                case StepAction.Summon:
                    AddSummonFields(container, step);
                    break;
                case StepAction.Teleport:
                    AddTeleportFields(container, attack, path, step, rebuild);
                    break;
                case StepAction.Buff:
                case StepAction.Absorb:
                    AddBuffFields(container, attack, path, step, action, rebuild);
                    break;
                case StepAction.Phase:
                    AddPhaseFields(container, step);
                    break;
            }
            AddRhythmFields(container, step, action);
            container.Add(AttackStepOptions.Options(attack, path, action, rebuild));
        }

        /// <summary>Tuiles illustrées pour choisir une valeur d'enum de l'étape (forme, salve, direction…).</summary>
        static VisualElement ChoiceTiles<T>(T current, Func<T, string> label, Func<T, string> tooltip, Func<T, Action<Painter2D, Vector2, float>> glyph,
            Action<T> select) where T : struct, Enum
        {
            var tiles = new VisualElement();
            tiles.AddToClassList("forge-tiles");
            tiles.AddToClassList("forge-tiles--small");
            foreach (var option in (T[])Enum.GetValues(typeof(T)))
                tiles.Add(BehaviourGlyphs.Tile(label(option), tooltip(option), glyph(option), option.Equals(current), () => select(option)));
            return tiles;
        }

        static VisualElement Chips()
        {
            var chips = new VisualElement();
            chips.AddToClassList("forge-chips");
            return chips;
        }

        /// <summary>Frappe : forme et taille de la zone, puis où elle apparaît et combien de zones à la fois.</summary>
        static void AddStrikeFields(VisualElement container, SerializedObject attack, string path, SerializedProperty step, Action rebuild)
        {
            var l = Limits;
            var shape = (ZoneShape)step.FindPropertyRelative("shape").enumValueIndex;
            container.Add(ChoiceTiles(shape, EnemyPatternText.Label, option => $"Zone en {EnemyPatternText.Label(option).ToLowerInvariant()}.",
                option => (p, c, s) => BehaviourGlyphs.Shape(p, c, s, option), option => SetEnum(attack, path, "shape", (int)option, rebuild)));
            var size = Chips();
            if (shape == ZoneShape.Ring)
                size.Add(FloatPill(step, "innerRadius", "de", null, l.zoneSize, "Rayon intérieur : le joueur collé à l'ennemi n'est pas touché."));
            size.Add(FloatPill(step, "radius", shape switch { ZoneShape.Line => "longueur", ZoneShape.Ring => "à", _ => "rayon" }, "cases", l.zoneSize,
                shape == ZoneShape.Line ? "Longueur de la ligne." : "Rayon de la zone."));
            if (shape == ZoneShape.Cone)
                size.Add(FloatPill(step, "angle", "ouverture", "°", new FloatRange(10f, 360f), "Ouverture du cône."));
            if (shape == ZoneShape.Line)
                size.Add(FloatPill(step, "width", "largeur", "cases", l.zoneSize, "Largeur de la ligne."));
            container.Add(size);
            AddZonePlacement(container, attack, path, step, rebuild);
        }

        static void AddZonePlacement(VisualElement container, SerializedObject attack, string path, SerializedProperty step, Action rebuild)
        {
            var anchors = (ZoneAnchor[])Enum.GetValues(typeof(ZoneAnchor));
            var anchorValue = (ZoneAnchor)step.FindPropertyRelative("anchor").enumValueIndex;
            var anchor = new DropdownField("Zone", anchors.Select(EnemyPatternText.Label).ToList(), (int)anchorValue)
            {
                tooltip = "Où apparaît la zone. Sur la cible : la force à bouger. Lancé : un projectile vole pendant la préparation et la zone apparaît où il tombe " +
                          "(boule de putréfaction…). Au hasard : pluie de zones."
            };
            anchor.RegisterValueChangedCallback(evt => SetEnum(attack, path, "anchor", anchor.index, rebuild));
            container.Add(anchor);
            var placement = Chips();
            if (anchorValue != ZoneAnchor.Self && anchorValue != ZoneAnchor.Target)
                placement.Add(FloatPill(step, "anchorDistance", anchorValue switch
                {
                    ZoneAnchor.Ahead => "à",
                    ZoneAnchor.Thrown => "lancer jusqu'à",
                    _ => "dispersion"
                }, "cases", new FloatRange(0f, 15f), "Distance devant l'attaquant, portée maximale du lancer, ou rayon de dispersion des zones au hasard."));
            placement.Add(IntPill(step, "zoneCount", "zones", null, new IntRange(1, 12),
                "Nombre de zones à la fois : chapelet devant l'attaquant, dispersées au hasard, ou autour du point d'impact."));
            container.Add(placement);
        }

        static void AddProjectileFields(VisualElement container, SerializedObject attack, string path, SerializedProperty step, Action rebuild)
        {
            var l = Limits;
            var pattern = (ProjectilePattern)step.FindPropertyRelative("pattern").enumValueIndex;
            container.Add(ChoiceTiles(pattern, EnemyPatternText.Label, option => $"Projectiles : {EnemyPatternText.Label(option).ToLowerInvariant()}.",
                option => (p, c, s) => BehaviourGlyphs.Projectiles(p, c, s, option), option => SetEnum(attack, path, "pattern", (int)option, rebuild)));
            var values = Chips();
            values.Add(IntPill(step, "projectiles", "projectiles", null, l.attackProjectiles, "Projectiles par salve."));
            values.Add(FloatPill(step, "projectileSpeed", "vitesse", "cases/s", l.projectileSpeed, "Vitesse des projectiles."));
            if (pattern == ProjectilePattern.Spread)
                values.Add(FloatPill(step, "spreadAngle", "ouverture", "°", l.attackSpread, "Angle total de l'éventail."));
            container.Add(values);
        }

        static void AddDashFields(VisualElement container, SerializedObject attack, string path, SerializedProperty step, Action rebuild)
        {
            var l = Limits;
            var direction = (DashDirection)step.FindPropertyRelative("dashDirection").enumValueIndex;
            container.Add(ChoiceTiles(direction, EnemyPatternText.Label, option => $"Ruée {EnemyPatternText.Label(option).ToLowerInvariant()}.",
                option => (p, c, s) => BehaviourGlyphs.Dash(p, c, s, option), option => SetEnum(attack, path, "dashDirection", (int)option, rebuild)));
            var values = Chips();
            values.Add(FloatPill(step, "dashDistance", "distance", "cases", l.dashDistance, "Distance parcourue (une ruée vers le joueur s'arrête à son contact)."));
            values.Add(FloatPill(step, "dashSpeed", "vitesse", "cases/s", l.dashSpeed, "Vitesse de la ruée."));
            container.Add(values);
        }

        static void AddSummonFields(VisualElement container, SerializedProperty step)
        {
            container.Add(new AssetPicker<EnemyData>("Ennemi invoqué", step.FindPropertyRelative("summon"),
                "Ennemi qui apparaît (clones, sbires…). Vide : un ennemi ordinaire du biome de la salle."));
            var values = Chips();
            values.Add(IntPill(step, "summons", "invoque", "ennemi(s)", Limits.summons, "Ennemis invoqués par cette étape (3 au plus en tout par invocateur)."));
            values.Add(FloatPill(step, "anchorDistance", "à", "cases", new FloatRange(0f, 8f), "Distance d'apparition autour de l'invocateur."));
            container.Add(values);
        }

        static void AddTeleportFields(VisualElement container, SerializedObject attack, string path, SerializedProperty step, Action rebuild)
        {
            var target = (TeleportTarget)step.FindPropertyRelative("teleportTo").enumValueIndex;
            container.Add(ChoiceTiles(target, EnemyPatternText.Label, option => $"Réapparaît : {EnemyPatternText.Label(option).ToLowerInvariant()}.",
                _ => (p, c, sz) => BehaviourGlyphs.Action(p, c, sz, StepAction.Teleport), option => SetEnum(attack, path, "teleportTo", (int)option, rebuild)));
            var values = Chips();
            values.Add(FloatPill(step, "anchorDistance", "distance", "cases", new FloatRange(0.5f, 15f), "Distance de la cible ou du point de départ."));
            container.Add(values);
            container.Add(ForgeUI.Label("Invulnérable pendant la préparation (disparition), réapparaît à la fin.", "forge-hint"));
        }

        /// <summary>Renforcement (sur soi) ou absorption (par allié absorbé) : effet, puissance et durée.</summary>
        static void AddBuffFields(VisualElement container, SerializedObject attack, string path, SerializedProperty step, StepAction action, Action rebuild)
        {
            var buffs = (BuffType[])Enum.GetValues(typeof(BuffType));
            var buff = new DropdownField(action == StepAction.Absorb ? "Par allié absorbé" : "Effet sur soi", buffs.Select(EnemyPatternText.Label).ToList(),
                step.FindPropertyRelative("buff").enumValueIndex) { tooltip = "Effet appliqué à l'attaquant." };
            buff.RegisterValueChangedCallback(_ => SetEnum(attack, path, "buff", buff.index, rebuild));
            container.Add(buff);
            var values = Chips();
            if (action == StepAction.Absorb)
                values.Add(FloatPill(step, "radius", "alliés à moins de", "cases", new FloatRange(0f, 30f), "Rayon d'absorption (0 = toute la salle)."));
            var type = (BuffType)step.FindPropertyRelative("buff").enumValueIndex;
            if (type != BuffType.Invulnerable)
                values.Add(FloatPill(step, "buffPower", type == BuffType.Heal ? "soigne" : "+", type == BuffType.Heal ? "% des PV max" : "%", new FloatRange(1f, 300f),
                    action == StepAction.Absorb ? "Par allié absorbé (s'additionne)." : "Puissance de l'effet."));
            if (type != BuffType.Heal)
                values.Add(FloatPill(step, "buffDuration", "pendant", "s", new FloatRange(0.5f, 120f), "Durée de l'effet."));
            container.Add(values);
        }

        static void AddPhaseFields(VisualElement container, SerializedProperty step)
        {
            var values = Chips();
            values.Add(IntPill(step, "phase", "passe en phase", null, new IntRange(0, 9),
                "Numéro de la phase (définie dans l'ennemi > Comportement > Phases ; 0 = retour à la phase de départ)."));
            container.Add(values);
            container.Add(ForgeUI.Label("Chaque ennemi qui utilise cette attaque définit ses phases (stats, aura, renforts) dans sa vue Comportement.", "forge-hint"));
        }

        /// <summary>Rythme et dégâts de l'étape.</summary>
        static void AddRhythmFields(VisualElement container, SerializedProperty step, StepAction action)
        {
            var l = Limits;
            var rhythm = Chips();
            rhythm.Add(FloatPill(step, "windup", "préparation", "s", l.attackWindup,
                "L'ennemi s'arrête et annonce l'étape (la zone s'affiche au sol) : le temps laissé au joueur pour esquiver."));
            if (action != StepAction.Pause)
                rhythm.Add(FloatPill(step, "recovery", "récupération", "s", l.attackRecovery, "L'ennemi reste immobile après l'étape : la fenêtre de riposte."));
            if (action is StepAction.Strike or StepAction.Projectiles or StepAction.Dash)
                rhythm.Add(IntPill(step, "damagePercent", "dégâts", "%", l.attackDamage, "En pourcentage des dégâts de l'ennemi (0 = aucun : par exemple une ruée pour se placer)."));
            container.Add(rhythm);
            if (action is StepAction.Strike or StepAction.Projectiles or StepAction.Dash)
            {
                var track = new Toggle("Suit la cible pendant la préparation")
                    { tooltip = "Coché : la visée suit la cible jusqu'au déclenchement. Décoché : elle est fixée au début de l'étape (la cible peut s'écarter)." };
                track.BindProperty(step.FindPropertyRelative("trackDuringWindup"));
                container.Add(track);
            }
        }

        static void SetEnum(SerializedObject attack, string path, string field, int value, Action rebuild)
        {
            if (value < 0)
                return;
            attack.Update();
            attack.FindProperty(path).FindPropertyRelative(field).enumValueIndex = value;
            attack.ApplyModifiedProperties();
            rebuild();
        }
    }
}
