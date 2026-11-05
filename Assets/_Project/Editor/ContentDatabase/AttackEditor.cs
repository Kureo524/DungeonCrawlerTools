using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.ContentDatabase
{
    /// <summary>
    /// Édition d'une attaque, partagée par l'outil Attaques et la vue Comportement des ennemis :
    /// <list type="bullet">
    /// <item>Déclenchement (portée, recharge, dégâts, cadence) : côté ennemi, chaque réglage vient de l'attaque (grisé) et peut être remplacé.</item>
    /// <item>Enchaînement : les étapes, chacune avec sa condition (évaluée au moment où elle commence), son action et son rythme.</item>
    /// </list>
    /// </summary>
    public static class AttackEditor
    {
        static ForgeDesignLimits Limits => ForgeDesignLimits.Instance;

        /// <summary>Contexte d'un ennemi : quels réglages il remplace, et ceux de l'attaque d'origine.</summary>
        public sealed class Override
        {
            public SerializedProperty Mask;
            public AttackSettings Base;
        }

        sealed class Field
        {
            public AttackField Flag;
            public string Property;
            public string Before;
            public string After;
            public string Tooltip;
            public bool IsInt;
            public float Min;
            public float Max;
        }

        static IEnumerable<Field> SettingsFields()
        {
            var l = Limits;
            yield return new Field { Flag = AttackField.MinRange, Property = "minRange", Before = "joueur entre", Min = l.attackRange.min, Max = l.attackRange.max,
                Tooltip = "Distance minimale au joueur pour lancer l'attaque, en cases (0 = collé)." };
            yield return new Field { Flag = AttackField.MaxRange, Property = "maxRange", Before = "et", After = "cases", Min = l.attackRange.min, Max = l.attackRange.max,
                Tooltip = "Distance maximale au joueur pour lancer l'attaque, en cases. Une fois lancée, l'attaque va au bout même si le joueur s'éloigne." };
            yield return new Field { Flag = AttackField.Cooldown, Property = "cooldown", Before = "recharge", After = "s", Min = l.attackCooldown.min, Max = l.attackCooldown.max,
                Tooltip = "Délai avant de relancer cette attaque, compté à partir de sa fin. Les autres attaques restent possibles pendant ce temps." };
            yield return new Field { Flag = AttackField.Damage, Property = "damagePercent", Before = "dégâts", After = "%", IsInt = true, Min = l.attackDamage.min, Max = l.attackDamage.max,
                Tooltip = "Multiplie les dégâts de toutes les étapes. Pratique pour qu'un boss frappe plus fort avec la même attaque." };
            yield return new Field { Flag = AttackField.Tempo, Property = "tempoPercent", Before = "cadence", After = "%", IsInt = true, Min = l.attackTempo.min, Max = l.attackTempo.max,
                Tooltip = "100 % = durées normales ; 150 % = préparations et récupérations 1,5 fois plus rapides (attaque plus dure à esquiver)." };
        }

        /// <summary>Réglages de déclenchement. <paramref name="overrides"/> : null dans l'outil Attaques, le contexte de l'ennemi sinon.</summary>
        /// <param name="user">Arme ou compétence : pas de distance de lancement (c'est le joueur qui la déclenche).</param>
        public static VisualElement Settings(SerializedProperty settings, Override overrides, Action rebuild, AttackUser user = AttackUser.Enemy)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-chips");
            foreach (var field in SettingsFields().Where(f => user == AttackUser.Enemy || f.Flag is not (AttackField.MinRange or AttackField.MaxRange)))
                row.Add(Pill(field, settings, overrides, rebuild));
            return row;
        }

        static VisualElement Pill(Field field, SerializedProperty settings, Override overrides, Action rebuild)
        {
            var pill = new VisualElement { tooltip = $"{field.Tooltip}\nValeurs autorisées : {field.Min:0.##} à {field.Max:0.##}." };
            pill.AddToClassList("forge-chip");
            pill.AddToClassList("forge-chip--setting");
            pill.Add(ForgeUI.Label(field.Before, "forge-chip__text"));
            var property = settings.FindPropertyRelative(field.Property);
            var inherited = overrides != null && (overrides.Mask.intValue & (int)field.Flag) == 0;
            if (inherited)
            {
                // Réglage de l'attaque : grisé ; un clic le remplace pour cet ennemi (en partant de la même valeur).
                var baseValue = Value(overrides.Base, field.Property);
                pill.Add(ForgeUI.Label(field.IsInt ? $"{baseValue:0}" : $"{baseValue:0.##}", "forge-chip__inherited"));
                pill.AddToClassList("forge-chip--inherited");
                pill.tooltip = $"Valeur de l'attaque. Cliquez pour la changer pour cet ennemi.\n{field.Tooltip}";
                pill.RegisterCallback<ClickEvent>(_ =>
                {
                    var serializedObject = settings.serializedObject;
                    serializedObject.Update();
                    if (field.IsInt)
                        property.intValue = Mathf.RoundToInt(baseValue);
                    else
                        property.floatValue = baseValue;
                    overrides.Mask.intValue |= (int)field.Flag;
                    serializedObject.ApplyModifiedProperties();
                    rebuild();
                });
            }
            else
                pill.Add(field.IsInt ? IntField(property, field.Min, field.Max) : FloatField(property, field.Min, field.Max));
            if (!string.IsNullOrEmpty(field.After))
                pill.Add(ForgeUI.Label(field.After, "forge-chip__text"));
            if (overrides != null && !inherited)
            {
                pill.AddToClassList("forge-chip--overridden");
                pill.Add(ForgeUI.Button("↺", "Revenir à la valeur de l'attaque.", () =>
                {
                    var serializedObject = settings.serializedObject;
                    serializedObject.Update();
                    overrides.Mask.intValue &= ~(int)field.Flag;
                    serializedObject.ApplyModifiedProperties();
                    rebuild();
                }, "forge-chip__reset"));
            }
            return pill;
        }

        static float Value(AttackSettings settings, string property) => property switch
        {
            "minRange" => settings.minRange,
            "maxRange" => settings.maxRange,
            "cooldown" => settings.cooldown,
            "damagePercent" => settings.damagePercent,
            "tempoPercent" => settings.tempoPercent,
            _ => 0f
        };

        public static IntegerField IntField(SerializedProperty property, float min, float max)
        {
            var input = new IntegerField();
            input.AddToClassList("forge-chip__field");
            input.BindProperty(property);
            input.RegisterValueChangedCallback(evt =>
            {
                var clamped = Mathf.RoundToInt(Mathf.Clamp(evt.newValue, min, max));
                if (clamped != evt.newValue)
                    input.value = clamped;
            });
            return input;
        }

        public static FloatField FloatField(SerializedProperty property, float min, float max)
        {
            var input = new FloatField();
            input.AddToClassList("forge-chip__field");
            input.BindProperty(property);
            input.RegisterValueChangedCallback(evt =>
            {
                var clamped = Mathf.Clamp(evt.newValue, min, max);
                if (!Mathf.Approximately(clamped, evt.newValue))
                    input.value = clamped;
            });
            return input;
        }

        static VisualElement Pill(string before, VisualElement field, string after, string tooltip)
        {
            var pill = new VisualElement { tooltip = tooltip };
            pill.AddToClassList("forge-chip");
            pill.AddToClassList("forge-chip--setting");
            pill.Add(ForgeUI.Label(before, "forge-chip__text"));
            pill.Add(field);
            if (!string.IsNullOrEmpty(after))
                pill.Add(ForgeUI.Label(after, "forge-chip__text"));
            return pill;
        }

        static VisualElement FloatPill(SerializedProperty step, string property, string before, string after, FloatRange range, string tooltip) =>
            Pill(before, FloatField(step.FindPropertyRelative(property), range.min, range.max), after, $"{tooltip}\nValeurs autorisées : {range}.");

        static VisualElement IntPill(SerializedProperty step, string property, string before, string after, IntRange range, string tooltip) =>
            Pill(before, IntField(step.FindPropertyRelative(property), range.min, range.max), after, $"{tooltip}\nValeurs autorisées : {range}.");

        #region Étapes

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
            container.Add(Options(attack, path, action, rebuild));
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

        #region Options d'étape

        /// <summary>Option d'étape : affichée seulement quand elle est utilisée, ajoutée par « + Option ».</summary>
        sealed class StepOption
        {
            public string Name;
            public string Description;
            public Func<StepAction, AttackUser, bool> Applies = (_, _) => true;
            public Func<SerializedProperty, bool> Enabled;
            public Action<SerializedProperty> Enable;
            public Action<SerializedProperty> Disable;
            public Action<VisualElement, SerializedProperty> Fields;
        }

        static SerializedProperty P(SerializedProperty step, string name) => step.FindPropertyRelative(name);

        static readonly StepOption[] StepOptions =
        {
            new()
            {
                Name = "Répéter", Description = "L'étape est jouée plusieurs fois d'affilée, en reprenant la visée (double charge, triple coup…).",
                Applies = (a, _) => a != StepAction.Phase,
                Enabled = s => P(s, "repeat").intValue > 1, Enable = s => P(s, "repeat").intValue = 2, Disable = s => P(s, "repeat").intValue = 1,
                Fields = (c, s) => c.Add(IntPill(s, "repeat", "jouée", "fois", new IntRange(2, 10), "Nombre de fois d'affilée."))
            },
            new()
            {
                Name = "Se déplacer pendant l'étape", Description = "Avance, recule ou pas de côté pendant la préparation et l'action (marcher en frappant…).",
                Applies = (a, _) => a is StepAction.Strike or StepAction.Projectiles or StepAction.Pause or StepAction.Buff or StepAction.Summon,
                Enabled = s => P(s, "moveDuring").enumValueIndex != 0,
                Enable = s => { P(s, "moveDuring").enumValueIndex = (int)StepMove.TowardTarget; P(s, "moveSpeed").floatValue = 3f; },
                Disable = s => P(s, "moveDuring").enumValueIndex = 0,
                Fields = (c, s) =>
                {
                    var moves = ((StepMove[])Enum.GetValues(typeof(StepMove))).Skip(1).ToList();
                    var field = new DropdownField(moves.Select(EnemyPatternText.Label).ToList(), Mathf.Max(0, P(s, "moveDuring").enumValueIndex - 1));
                    field.AddToClassList("forge-chip__field");
                    field.AddToClassList("forge-chip__field--wide");
                    var path = s.propertyPath;
                    var so = s.serializedObject;
                    field.RegisterValueChangedCallback(_ =>
                    {
                        so.Update();
                        so.FindProperty(path).FindPropertyRelative("moveDuring").enumValueIndex = field.index + 1;
                        so.ApplyModifiedProperties();
                    });
                    c.Add(Pill("", field, null, "Sens du déplacement."));
                    c.Add(FloatPill(s, "moveSpeed", "vitesse", "cases/s", new FloatRange(0.5f, 15f), "Vitesse du déplacement."));
                }
            },
            new()
            {
                Name = "Zone qui dure", Description = "La zone reste en place et blesse à intervalles réguliers (flaque de putréfaction, mur de feu…).",
                Applies = (a, _) => a == StepAction.Strike,
                Enabled = s => P(s, "activeDuration").floatValue > 0f,
                Enable = s => { P(s, "activeDuration").floatValue = 3f; P(s, "tickInterval").floatValue = 0.5f; },
                Disable = s => { P(s, "activeDuration").floatValue = 0f; P(s, "travelSpeed").floatValue = 0f; P(s, "growth").floatValue = 0f; P(s, "sweepAngle").floatValue = 0f; P(s, "channel").boolValue = false; },
                Fields = (c, s) =>
                {
                    c.Add(FloatPill(s, "activeDuration", "dure", "s", new FloatRange(0.1f, 30f), "Durée de la zone."));
                    c.Add(FloatPill(s, "tickInterval", "dégâts toutes les", "s", new FloatRange(0.1f, 5f), "Intervalle entre deux dégâts."));
                    var channel = new Toggle("canalisée") { tooltip = "Coché : l'attaquant reste occupé tant que la zone dure (rayon). Décoché : la zone reste et l'attaque continue." };
                    channel.BindProperty(P(s, "channel"));
                    c.Add(channel);
                }
            },
            new()
            {
                Name = "Zone qui avance", Description = "La zone se déplace dans la direction visée (onde au sol, vague de glace…). Nécessite une zone qui dure.",
                Applies = (a, _) => a == StepAction.Strike,
                Enabled = s => P(s, "travelSpeed").floatValue != 0f,
                Enable = s => { P(s, "travelSpeed").floatValue = 6f; if (P(s, "activeDuration").floatValue <= 0f) P(s, "activeDuration").floatValue = 1.5f; P(s, "tickInterval").floatValue = 0.2f; },
                Disable = s => P(s, "travelSpeed").floatValue = 0f,
                Fields = (c, s) => c.Add(FloatPill(s, "travelSpeed", "avance à", "cases/s", new FloatRange(-20f, 20f), "Vitesse de la zone (négatif : vers l'attaquant)."))
            },
            new()
            {
                Name = "Zone qui grandit", Description = "La zone s'étend (anneau qui s'élargit, explosion qui gonfle). Nécessite une zone qui dure.",
                Applies = (a, _) => a == StepAction.Strike,
                Enabled = s => P(s, "growth").floatValue != 0f,
                Enable = s => { P(s, "growth").floatValue = 3f; if (P(s, "activeDuration").floatValue <= 0f) P(s, "activeDuration").floatValue = 1.5f; P(s, "tickInterval").floatValue = 0.2f; },
                Disable = s => P(s, "growth").floatValue = 0f,
                Fields = (c, s) => c.Add(FloatPill(s, "growth", "grandit de", "cases/s", new FloatRange(-10f, 20f), "Vitesse d'élargissement (négatif : rétrécit)."))
            },
            new()
            {
                Name = "Balayage", Description = "La zone pivote pendant sa durée (rayon qui balaie, coup circulaire lent). L'attaquant reste occupé.",
                Applies = (a, _) => a == StepAction.Strike,
                Enabled = s => P(s, "sweepAngle").floatValue != 0f,
                Enable = s => { P(s, "sweepAngle").floatValue = 120f; if (P(s, "activeDuration").floatValue <= 0f) P(s, "activeDuration").floatValue = 1.5f; P(s, "tickInterval").floatValue = 0.15f; P(s, "channel").boolValue = true; },
                Disable = s => P(s, "sweepAngle").floatValue = 0f,
                Fields = (c, s) => c.Add(FloatPill(s, "sweepAngle", "balaie", "°", new FloatRange(-720f, 720f), "Angle parcouru pendant la durée de la zone (négatif : dans l'autre sens)."))
            },
            new()
            {
                Name = "Effet sur la cible", Description = "En plus des dégâts : poison, brûlure, putréfaction, gel, saignement, étourdissement, repousse, projection.",
                Applies = (a, _) => a is StepAction.Strike or StepAction.Projectiles or StepAction.Dash,
                Enabled = s => P(s, "status").enumValueIndex != 0,
                Enable = s => { P(s, "status").enumValueIndex = (int)StatusEffect.Poison; P(s, "statusPower").floatValue = 4f; P(s, "statusDuration").floatValue = 4f; },
                Disable = s => P(s, "status").enumValueIndex = 0,
                Fields = (c, s) =>
                {
                    var statuses = ((StatusEffect[])Enum.GetValues(typeof(StatusEffect))).Skip(1).ToList();
                    var field = new DropdownField(statuses.Select(EnemyPatternText.Label).ToList(), Mathf.Max(0, P(s, "status").enumValueIndex - 1));
                    field.AddToClassList("forge-chip__field");
                    field.AddToClassList("forge-chip__field--wide");
                    var path = s.propertyPath;
                    var so = s.serializedObject;
                    field.RegisterValueChangedCallback(_ =>
                    {
                        so.Update();
                        so.FindProperty(path).FindPropertyRelative("status").enumValueIndex = field.index + 1;
                        so.ApplyModifiedProperties();
                    });
                    c.Add(Pill("", field, null, "Effet infligé à la cible touchée."));
                    var status = (StatusEffect)P(s, "status").enumValueIndex;
                    var unit = EnemyPatternText.Unit(status);
                    if (unit != null)
                        c.Add(FloatPill(s, "statusPower", "puissance", unit, new FloatRange(0f, 100f), "Puissance de l'effet."));
                    if (status is not (StatusEffect.Bleed or StatusEffect.Knockback))
                        c.Add(FloatPill(s, "statusDuration", "pendant", "s", new FloatRange(0.1f, 30f), "Durée de l'effet."));
                }
            },
            new()
            {
                Name = "Tête chercheuse", Description = "Les projectiles tournent vers la cible.",
                Applies = (a, _) => a == StepAction.Projectiles,
                Enabled = s => P(s, "homing").floatValue > 0f, Enable = s => P(s, "homing").floatValue = 90f, Disable = s => P(s, "homing").floatValue = 0f,
                Fields = (c, s) => c.Add(FloatPill(s, "homing", "tourne de", "°/s", new FloatRange(1f, 720f), "Vitesse de rotation vers la cible."))
            },
            new()
            {
                Name = "Saut", Description = "Ruée en l'air : invulnérable, passe par-dessus la cible (bond sur une tour, saut écrasant suivi d'une frappe).",
                Applies = (a, _) => a == StepAction.Dash,
                Enabled = s => P(s, "airborne").boolValue, Enable = s => P(s, "airborne").boolValue = true, Disable = s => P(s, "airborne").boolValue = false,
                Fields = (c, _) => c.Add(ForgeUI.Label("invulnérable pendant le saut ; pas de dégâts au contact", "forge-chip__text"))
            },
            new()
            {
                Name = "Garde", Description = "Réduit les dégâts reçus pendant l'étape (posture, bouclier levé). Avec « si l'attaquant a été touché », fait une parade.",
                Enabled = s => P(s, "guardPercent").floatValue > 0f, Enable = s => P(s, "guardPercent").floatValue = 50f, Disable = s => P(s, "guardPercent").floatValue = 0f,
                Fields = (c, s) => c.Add(FloatPill(s, "guardPercent", "dégâts reçus", "% en moins", new FloatRange(1f, 100f), "Réduction des dégâts reçus pendant l'étape."))
            },
            new()
            {
                Name = "Super-armure", Description = "Ni étourdi ni repoussé pendant l'étape.",
                Enabled = s => P(s, "superArmor").boolValue, Enable = s => P(s, "superArmor").boolValue = true, Disable = s => P(s, "superArmor").boolValue = false,
                Fields = (c, _) => c.Add(ForgeUI.Label("ni étourdi ni repoussé", "forge-chip__text"))
            },
            new()
            {
                Name = "Coût en PV", Description = "L'attaquant perd des PV en lançant l'étape (techniques de sang).",
                Enabled = s => P(s, "healthCost").floatValue > 0f, Enable = s => P(s, "healthCost").floatValue = 10f, Disable = s => P(s, "healthCost").floatValue = 0f,
                Fields = (c, s) => c.Add(FloatPill(s, "healthCost", "coûte", "% des PV max", new FloatRange(1f, 90f), "PV perdus en lançant l'étape (jamais mortel)."))
            },
            new()
            {
                Name = "Attaque chargée", Description = "Arme du joueur : maintenir la touche prolonge la préparation et augmente les dégâts.",
                Applies = (a, user) => user != AttackUser.Enemy && a is StepAction.Strike or StepAction.Projectiles or StepAction.Dash,
                Enabled = s => P(s, "chargeable").boolValue, Enable = s => P(s, "chargeable").boolValue = true, Disable = s => P(s, "chargeable").boolValue = false,
                Fields = (c, s) =>
                {
                    c.Add(FloatPill(s, "chargeTime", "charge max", "s", new FloatRange(0.1f, 5f), "Durée de charge maximale."));
                    c.Add(FloatPill(s, "chargeBonus", "+", "% de dégâts", new FloatRange(0f, 500f), "Dégâts en plus à pleine charge."));
                }
            }
        };

        /// <summary>Options utilisées par l'étape (une pastille chacune, retirable) et « + Option » pour en ajouter.</summary>
        static VisualElement Options(SerializedObject attack, string path, StepAction action, Action rebuild)
        {
            var step = attack.FindProperty(path);
            var user = (AttackUser)attack.FindProperty("user").enumValueIndex;
            var container = new VisualElement();
            container.AddToClassList("forge-step-options");
            var available = StepOptions.Where(o => o.Applies(action, user)).ToList();
            foreach (var option in available.Where(o => o.Enabled(step)))
            {
                var row = new VisualElement { tooltip = option.Description };
                row.AddToClassList("forge-step-option");
                row.Add(ForgeUI.Label(option.Name, "forge-step-option__name"));
                var fields = new VisualElement();
                fields.AddToClassList("forge-chips");
                fields.style.flexGrow = 1;
                option.Fields(fields, step);
                row.Add(fields);
                var chosen = option;
                row.Add(ForgeUI.CloseButton($"Retirer « {option.Name} ».", () =>
                {
                    attack.Update();
                    chosen.Disable(attack.FindProperty(path));
                    attack.ApplyModifiedProperties();
                    rebuild();
                }));
                container.Add(row);
            }
            var remaining = available.Where(o => !o.Enabled(step)).ToList();
            if (remaining.Count > 0)
            {
                var add = ForgeUI.Button("+ Option ▾", "Ajoute une option à cette étape : répéter, se déplacer, zone qui dure ou avance, effet sur la cible, garde…", null, "forge-chip-add");
                add.clicked += () =>
                {
                    var menu = ForgeDropdowns.Menu();
                    foreach (var option in remaining)
                    {
                        var chosen = option;
                        menu.AddItem($"{option.Name} — {option.Description}", false, () =>
                        {
                            attack.Update();
                            chosen.Enable(attack.FindProperty(path));
                            attack.ApplyModifiedProperties();
                            rebuild();
                        });
                    }
                    menu.DropDown(add.worldBound, add, DropdownMenuSizeMode.Content);
                };
                add.style.alignSelf = Align.FlexStart;
                container.Add(add);
            }
            return container;
        }

        #endregion

        static void SetEnum(SerializedObject attack, string path, string field, int value, Action rebuild)
        {
            if (value < 0)
                return;
            attack.Update();
            attack.FindProperty(path).FindPropertyRelative(field).enumValueIndex = value;
            attack.ApplyModifiedProperties();
            rebuild();
        }

        #endregion
    }
}
