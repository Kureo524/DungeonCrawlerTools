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
    /// <summary>Options d'une étape d'attaque (répétition, déplacement, zone qui dure, effet, guidage…) : affichées seulement quand elles sont utilisées, ajoutées par « + Option ».</summary>
    internal static class AttackStepOptions
    {
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
        public static VisualElement Options(SerializedObject attack, string path, StepAction action, Action rebuild)
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
    }
}
