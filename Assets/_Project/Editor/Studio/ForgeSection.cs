using System;
using TheDungeon.Editor.Common;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Studio
{
    /// <summary>Une section de Forge Studio (un outil), construite à sa première ouverture.</summary>
    public sealed class ForgeSection
    {
        public readonly string Id;
        public readonly string Group;
        public readonly string Title;
        public readonly string Subtitle;
        public readonly string HelpFile;
        public readonly string IconSprite;
        /// <summary>Type d'asset géré par la section (pour le compteur et la navigation), ou null.</summary>
        public readonly Type AssetType;
        readonly Func<VisualElement> build;
        VisualElement content;

        public ForgeSection(string id, string group, string title, string subtitle, string helpFile, string iconSprite, Type assetType,
            Func<VisualElement> build)
        {
            Id = id;
            Group = group;
            Title = title;
            Subtitle = subtitle;
            HelpFile = helpFile;
            IconSprite = iconSprite;
            AssetType = assetType;
            this.build = build;
        }

        public VisualElement Content => content ??= build();

        /// <summary>Outil de la section, quand il sait afficher un asset précis.</summary>
        public IAssetNavigable Navigable { get; set; }
    }
}
