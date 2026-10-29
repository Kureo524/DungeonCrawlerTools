using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Reflet de lumière qui suit le curseur sur le verre. Il n'y en a qu'un : il se place dans le panneau en verre
    /// le plus proche sous le curseur (le plus imbriqué), et s'efface dès que le curseur quitte le verre.
    /// </summary>
    public sealed class GlassLight
    {
        const float Size = 360f;

        /// <summary>Panneaux en verre (classes USS). « forge-glass » sert pour tout nouvel élément en verre.</summary>
        static readonly string[] GlassClasses =
        {
            "forge-glass", "forge-sidebar", "forge-main__header", "forge-help-drawer", "forge-list-pane", "forge-details-header",
            "forge-section", "forge-card", "forge-footer", "forge-seg"
        };

        static Texture2D spot;

        readonly VisualElement light;
        VisualElement current;

        GlassLight(VisualElement root)
        {
            light = new VisualElement { name = "glass-light", pickingMode = PickingMode.Ignore };
            light.AddToClassList("forge-glass-light");
            light.style.backgroundImage = Spot;
            root.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(_ => Hide());
        }

        public static void Attach(VisualElement root) => _ = new GlassLight(root);

        static bool IsGlass(VisualElement element) => GlassClasses.Any(element.ClassListContains);

        void OnMove(PointerMoveEvent evt)
        {
            var glass = evt.target as VisualElement;
            while (glass != null && !IsGlass(glass))
                glass = glass.parent;
            if (glass == null)
            {
                Hide();
                return;
            }
            // Le reflet passe sous le contenu du panneau (premier enfant), au-dessus de son fond.
            if (light.parent != glass)
                glass.hierarchy.Insert(0, light);
            current = glass;
            var local = glass.WorldToLocal(evt.position);
            light.style.left = local.x - Size / 2f;
            light.style.top = local.y - Size / 2f;
            light.style.opacity = 1f;
        }

        void Hide()
        {
            if (current == null)
                return;
            light.style.opacity = 0f;
            current = null;
        }

        /// <summary>Halo blanc très doux, du centre vers le bord.</summary>
        static Texture2D Spot
        {
            get
            {
                if (spot != null)
                    return spot;
                spot = Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(t => t.name == "ForgeGlassLight");
                if (spot != null)
                    return spot;
                const int size = 128;
                spot = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ForgeGlassLight", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    var alpha = Mathf.Clamp01(1f - distance);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, 0.09f * alpha * alpha);
                }
                spot.SetPixels(pixels);
                spot.Apply(false, true);
                return spot;
            }
        }
    }
}
