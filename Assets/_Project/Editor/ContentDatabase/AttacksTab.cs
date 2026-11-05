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
    /// Attaques réutilisables : créées une fois, données à plusieurs ennemis (vue Comportement), qui peuvent en remplacer certaines valeurs.
    /// Modifier une attaque ici change tous les ennemis qui l'utilisent, sauf pour les valeurs qu'ils remplacent.
    /// </summary>
    public sealed class AttacksTab : AssetListTab<AttackData>
    {
        static readonly Color AttackColor = new(1f, 0.55f, 0.35f);

        public AttacksTab(IToolHost window) : base(window)
        {
        }

        protected override string NewAssetName => "Nouvelle attaque";
        protected override string Noun => "attaque";
        protected override bool FeminineNoun => true;

        protected override Color GetColor(AttackData attack) =>
            attack.Steps.Any(s => s.action == StepAction.Projectiles) ? new Color(0.66f, 0.77f, 0.94f) : AttackColor;

        protected override IEnumerable<VisualElement> GetBadges(AttackData attack)
        {
            yield return ForgeUI.Badge($"{attack.Steps.Count} étape(s)", GetColor(attack), string.Join(" → ", attack.Steps.Select(s => s.name)));
            if (attack.User == AttackUser.Enemy)
                yield return ForgeUI.Badge(EnemyPatternText.Range(attack.Settings), new Color(0.7f, 0.7f, 0.7f), "Distance au joueur pour lancer l'attaque.");
            else
                yield return ForgeUI.Badge(EnemyPatternText.Label(attack.User), new Color(0.95f, 0.8f, 0.3f), "Utilisée par une arme.");
            var users = Users(attack);
            yield return ForgeUI.Badge(users.Count == 0 ? "aucun ennemi" : $"{users.Count} ennemi(s)", new Color(0.65f, 0.59f, 0.75f),
                users.Count == 0 ? "Aucun ennemi n'utilise cette attaque." : "Utilisée par : " + string.Join(", ", users.Select(u => u.DisplayName)));
        }

        protected override void InitializeNew(SerializedObject draft) => RulePresets.Apply(RulePresets.Attacks[0], draft);

        protected override VisualElement BuildFilters()
        {
            var presets = ForgeUI.Button("Créer depuis un modèle ▾", "Prépare une attaque toute prête (coup de massue, frappe au sol, tir visé, rafale, ruée, appel à l'aide), à ajuster.", null);
            presets.clicked += () =>
            {
                var menu = ForgeDropdowns.Menu();
                AttackUser? group = null;
                foreach (var preset in RulePresets.Attacks.OrderBy(p => p.User))
                {
                    if (group != preset.User)
                    {
                        if (group != null)
                            menu.AddSeparator(string.Empty);
                        menu.AddDisabledItem(preset.User switch
                        {
                            AttackUser.Weapon => "Attaques d'arme",
                            AttackUser.Skill => "Compétences d'arme",
                            _ => "Ennemis et boss"
                        }, false);
                        group = preset.User;
                    }
                    menu.AddItem($"   {preset.Name} — {preset.Description}", false, () => OpenDraft(() =>
                    {
                        var created = DraftSession<AttackData>.CreateNew(preset.Name);
                        var serialized = new SerializedObject(created.Draft);
                        RulePresets.Apply(preset, serialized);
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        return created;
                    }));
                }
                menu.DropDown(presets.worldBound, presets, DropdownMenuSizeMode.Content);
            };
            return presets;
        }

        protected override void BuildDetails(VisualElement container, SerializedObject serializedObject, AttackData attack)
        {
            var identity = ForgeUI.Section("Identité", container);
            identity.Add(ForgeUI.Text("Nom", "displayName", "Nom de l'attaque, annoncé en jeu quand un boss l'utilise. Le fichier sera nommé ATK_<Nom>."));
            identity.Add(ForgeUI.Text("Note", "description", "À quoi sert cette attaque, sur quels ennemis (pour l'équipe).", multiline: true));
            var userKinds = (AttackUser[])System.Enum.GetValues(typeof(AttackUser));
            var user = new DropdownField("Pour", userKinds.Select(EnemyPatternText.Label).ToList(), (int)attack.User)
            {
                tooltip = "Ennemi : lancée selon la distance au joueur. Attaque d'arme : jouée par la touche de tir quand l'arme est équipée. " +
                          "Compétence d'arme : jouée par la mécanique « Compétence d'arme » (outil Progression)."
            };
            user.RegisterValueChangedCallback(_ =>
            {
                serializedObject.Update();
                serializedObject.FindProperty("user").enumValueIndex = user.index;
                serializedObject.ApplyModifiedProperties();
                container.schedule.Execute(RebuildDetails);
            });
            identity.Add(user);

            var trigger = ForgeUI.Section("Déclenchement", container, attack.User == AttackUser.Enemy
                ? "L'attaque est lancée quand elle est rechargée et que le joueur est à cette distance. Une fois lancée, elle va jusqu'au bout, " +
                  "même si le joueur s'éloigne : elle peut rater. Un ennemi peut remplacer ces réglages pour lui seul."
                : "Le joueur la déclenche avec sa touche (tir ou compétence d'arme). Dégâts en % de son attaque. Une fois lancée, elle va jusqu'au bout. " +
                  "Dans les étapes, « la cible » est l'ennemi visé (le plus proche dans la visée).");
            trigger.Add(AttackEditor.Settings(serializedObject.FindProperty("settings"), null, null, attack.User));

            var chain = ForgeUI.Section("Enchaînement", container,
                "Les étapes sont jouées dans l'ordre. Chacune décide au moment où elle commence si elle est jouée : selon la situation " +
                "(distance, PV…) et selon ce qui s'est passé avant (l'étape précédente a touché ou raté, nombre de coups qui ont touché).");
            var timelineSlot = new VisualElement();
            chain.Add(timelineSlot);
            var stepsSlot = new VisualElement();
            chain.Add(stepsSlot);
            void RebuildSteps()
            {
                serializedObject.Update();
                stepsSlot.Clear();
                stepsSlot.Add(AttackEditor.Steps(serializedObject, Root, RebuildSteps));
                stepsSlot.Bind(serializedObject);
            }
            timelineSlot.Add(new AttackTimeline(() => new ResolvedAttack { Settings = attack.Settings, Steps = attack.Steps }));
            RebuildSteps();

            var usedBy = ForgeUI.Section(attack.User == AttackUser.Enemy ? "Ennemis qui l'utilisent" : "Armes qui l'utilisent", container);
            var users = Users(attack);
            var weapons = Weapons(attack);
            if (users.Count == 0 && weapons.Count == 0)
                usedBy.Add(ForgeUI.Label(attack.User == AttackUser.Enemy
                    ? "Aucun pour l'instant : donnez-la à un ennemi dans Ennemis > Comportement > + Attaque."
                    : "Aucune pour l'instant : choisissez-la dans la fiche d'une arme (Objets > Attaques de l'arme).", "forge-hint"));
            foreach (var weapon in weapons)
            {
                var row = ForgeLists.Row(ForgeUI.Label($"{ForgeText.Name(weapon.DisplayName)}  <color=#8a8a9c>{(weapon.WeaponSkill == Original ? "compétence" : "attaque")}</color>"),
                    ForgeUI.Button("Ouvrir", "Affiche cette arme.", () => Window.Reveal(weapon)));
                row.Q<Label>().style.flexGrow = 1;
                usedBy.Add(row);
            }
            foreach (var enemy in users)
            {
                var slots = enemy.Attacks.Where(a => a.attack == Original && Original != null).ToList();
                var changed = slots.Any(s => s.overridden != AttackField.None || s.conditions.Count > 0);
                var row = ForgeLists.Row(
                    ForgeUI.Label($"{ForgeText.Name(enemy.DisplayName)}{(changed ? "  <color=#a597c0>valeurs ou conditions propres</color>" : string.Empty)}"),
                    ForgeUI.Button("Ouvrir", "Affiche cet ennemi.", () => Window.Reveal(enemy)));
                row.Q<Label>().style.flexGrow = 1;
                usedBy.Add(row);
            }
        }

        List<ItemData> Weapons(AttackData attack)
        {
            var original = Original != null && attack == Draft ? Original : attack;
            return AssetFactory.LoadAll<ItemData>().Where(i => i.WeaponAttack == original || i.WeaponSkill == original).ToList();
        }

        List<EnemyData> Users(AttackData attack)
        {
            var original = Original != null && attack == Draft ? Original : attack;
            return AssetFactory.LoadAll<EnemyData>().Where(e => e.Attacks.Any(a => a.attack == original)).ToList();
        }
    }
}
