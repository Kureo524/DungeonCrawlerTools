using System;
using System.Collections.Generic;
using System.Linq;
using TheDungeon.Data;
using TheDungeon.Editor.Common;
using UnityEditor;
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
        public IReadOnlyList<RoomData> Rooms { get; }
        public IReadOnlyList<FloorData> Floors { get; }
        public IReadOnlyList<PlayerProgression> Progressions { get; }
        public ForgeDesignLimits Limits { get; }

        public ValidationContext(
            IReadOnlyList<ItemData> items, IReadOnlyList<EnemyData> enemies, IReadOnlyList<LootTable> lootTables,
            IReadOnlyList<RoomData> rooms, IReadOnlyList<FloorData> floors, IReadOnlyList<PlayerProgression> progressions,
            ForgeDesignLimits limits)
        {
            Items = items;
            Enemies = enemies;
            LootTables = lootTables;
            Rooms = rooms;
            Floors = floors;
            Progressions = progressions;
            Limits = limits;
        }

        public static ValidationContext FromProject() => new(
            AssetFactory.LoadAll<ItemData>(), AssetFactory.LoadAll<EnemyData>(), AssetFactory.LoadAll<LootTable>(),
            AssetFactory.LoadAll<RoomData>(), AssetFactory.LoadAll<FloorData>(), AssetFactory.LoadAll<PlayerProgression>(),
            ForgeDesignLimits.Instance);

        public IEnumerable<Object> AllAssets() =>
            Items.Cast<Object>().Concat(Enemies).Concat(LootTables).Concat(Rooms).Concat(Floors).Concat(Progressions);
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
            return issues.OrderBy(issue => issue.Severity).ThenBy(issue => issue.Context != null ? issue.Context.name : string.Empty).ToList();
        }

        public static List<ValidationIssue> RunFor(Object asset)
        {
            return Run(ValidationContext.FromProject()).Where(issue => issue.Context == asset).ToList();
        }
    }
}
