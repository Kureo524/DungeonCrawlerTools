using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheDungeon.Editor.Validation
{
    public enum Severity
    {
        Error,
        Warning
    }

    /// <summary>Un problème détecté, rédigé pour être compris par un designer.</summary>
    public sealed class ValidationIssue
    {
        public Severity Severity { get; }
        public string Message { get; }
        public Object Context { get; }
        public string RuleName { get; internal set; }

        /// <summary>Correction automatique proposée, ou null si le problème doit être corrigé à la main.</summary>
        public Action Fix { get; }
        public string FixLabel { get; }

        public ValidationIssue(Severity severity, string message, Object context, Action fix = null, string fixLabel = null)
        {
            Severity = severity;
            Message = message;
            Context = context;
            Fix = fix;
            FixLabel = fixLabel;
        }
    }

    /// <summary>Toutes les données du jeu chargées une fois, partagées par les règles.</summary>
    public sealed class ValidationContext
    {
        public IReadOnlyList<ItemData> Items { get; }
        public IReadOnlyList<EnemyData> Enemies { get; }
        public IReadOnlyList<LootTable> LootTables { get; }
        public IReadOnlyList<BiomeData> Biomes { get; }
        public IReadOnlyList<RoomData> Rooms { get; }
        public IReadOnlyList<FloorData> Floors { get; }
        public IReadOnlyList<PlayerProgression> Progressions { get; }
        public IReadOnlyList<NpcData> Npcs { get; }
        public IReadOnlyList<AttackData> Attacks { get; }
        public ForgeDesignLimits Limits { get; }

        public ValidationContext(
            IReadOnlyList<ItemData> items, IReadOnlyList<EnemyData> enemies, IReadOnlyList<LootTable> lootTables,
            IReadOnlyList<BiomeData> biomes, IReadOnlyList<RoomData> rooms, IReadOnlyList<FloorData> floors,
            IReadOnlyList<PlayerProgression> progressions, ForgeDesignLimits limits, IReadOnlyList<NpcData> npcs = null,
            IReadOnlyList<AttackData> attacks = null)
        {
            Npcs = npcs ?? new List<NpcData>();
            Attacks = attacks ?? new List<AttackData>();
            Items = items;
            Enemies = enemies;
            LootTables = lootTables;
            Biomes = biomes;
            Rooms = rooms;
            Floors = floors;
            Progressions = progressions;
            Limits = limits;
        }

        public static ValidationContext FromProject() => FromProject(null, null);

        /// <summary>Données du projet, où la copie de travail remplace l'asset d'origine (ou s'ajoute s'il est nouveau).</summary>
        public static ValidationContext FromProject(Object original, Object draft) => new(
            Load<ItemData>(original, draft), Load<EnemyData>(original, draft), Load<LootTable>(original, draft),
            Load<BiomeData>(original, draft), Load<RoomData>(original, draft), Load<FloorData>(original, draft),
            Load<PlayerProgression>(original, draft), ForgeDesignLimits.Instance, Load<NpcData>(original, draft), Load<AttackData>(original, draft));

        static List<T> Load<T>(Object original, Object draft) where T : Object
        {
            var assets = AssetFactory.LoadAll<T>();
            if (draft is T typedDraft)
            {
                if (original is T typedOriginal)
                    assets.Remove(typedOriginal);
                assets.Add(typedDraft);
            }
            return assets;
        }

        public IEnumerable<Object> AllAssets() =>
            Items.Cast<Object>().Concat(Enemies).Concat(LootTables).Concat(Biomes).Concat(Rooms).Concat(Floors).Concat(Progressions).Concat(Npcs).Concat(Attacks);
    }

    /// <summary>
    /// Une règle de cohérence. Pour en ajouter une, créer une classe qui hérite de ValidationRule :
    /// elle est détectée automatiquement par le Validator.
    /// </summary>
    public abstract class ValidationRule
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract IEnumerable<ValidationIssue> Run(ValidationContext context);

        protected static ValidationIssue Error(string message, Object context, Action fix = null, string fixLabel = null) =>
            new(Severity.Error, message, context, fix, fixLabel);

        protected static ValidationIssue Warning(string message, Object context, Action fix = null, string fixLabel = null) =>
            new(Severity.Warning, message, context, fix, fixLabel);
    }

    public static class ValidationRunner
    {
        const string ReferenceRuleName = "Références";

        static List<ValidationRule> rules;

        public static IReadOnlyList<ValidationRule> Rules => rules ??= TypeCache.GetTypesDerivedFrom<ValidationRule>()
            .Where(type => !type.IsAbstract)
            .Select(type => (ValidationRule)Activator.CreateInstance(type))
            .OrderBy(rule => rule.Name)
            .ToList();

        public static List<ValidationIssue> Run(ValidationContext context)
        {
            var issues = new List<ValidationIssue>();
            foreach (var rule in Rules)
            {
                foreach (var issue in rule.Run(context))
                {
                    issue.RuleName = rule.Name;
                    issues.Add(issue);
                }
            }
            issues.AddRange(PropagateInvalidReferences(context, issues));
            return issues.OrderBy(issue => issue.Severity).ThenBy(issue => issue.Context != null ? issue.Context.name : string.Empty).ToList();
        }

        public static List<ValidationIssue> RunFor(Object asset) =>
            Run(ValidationContext.FromProject()).Where(issue => issue.Context == asset).ToList();

        /// <summary>
        /// Une donnée qui utilise une donnée en erreur (ex : un ennemi avec une table de butin invalide)
        /// est elle-même en erreur. Répété jusqu'à stabilité pour suivre les chaînes de références.
        /// </summary>
        static List<ValidationIssue> PropagateInvalidReferences(ValidationContext context, List<ValidationIssue> issues)
        {
            var invalid = new HashSet<Object>(issues.Where(i => i.Severity == Severity.Error && i.Context != null).Select(i => i.Context));
            var references = context.AllAssets().ToDictionary(asset => asset, ReferencesOf);
            var added = new List<ValidationIssue>();

            bool changed;
            do
            {
                changed = false;
                foreach (var (asset, targets) in references)
                {
                    if (invalid.Contains(asset))
                        continue;
                    var broken = targets.FirstOrDefault(invalid.Contains);
                    if (broken == null)
                        continue;
                    added.Add(new ValidationIssue(Severity.Error, $"Utilise « {broken.name} », qui contient des erreurs. Corrigez-le ou choisissez-en un autre.", asset)
                        { RuleName = ReferenceRuleName });
                    invalid.Add(asset);
                    changed = true;
                }
            } while (changed);
            return added;
        }

        /// <summary>Données du jeu référencées par un asset (sprites et autres assets ignorés).</summary>
        static List<Object> ReferencesOf(Object asset)
        {
            var result = new List<Object>();
            var iterator = new SerializedObject(asset).GetIterator();
            while (iterator.NextVisible(true))
            {
                if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                    iterator.objectReferenceValue is ScriptableObject target && target != asset)
                    result.Add(target);
            }
            return result;
        }
    }
}
