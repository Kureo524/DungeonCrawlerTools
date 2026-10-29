using System;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Menus des listes déroulantes dans le style Forge. Dans l'éditeur, Unity ouvre un menu natif impossible à styliser ;
    /// juste avant l'ouverture, on demande au champ d'utiliser à la place un menu UI Toolkit, qui reçoit la feuille de style Forge.
    /// </summary>
    public static class ForgeDropdowns
    {
        const string CallbackField = "createMenuCallback";

        public static void Attach(VisualElement root)
        {
            // Phase de descente : on passe avant le champ, qui ouvre son menu sur ce même clic.
            root.RegisterCallback<PointerDownEvent>(evt => Prepare(evt.target as VisualElement), TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(evt => Prepare(evt.target as VisualElement), TrickleDown.TrickleDown);
        }

        /// <summary>Prépare la liste déroulante qui contient <paramref name="target"/> (ou en est un parent) à ouvrir un menu Forge.</summary>
        public static void Prepare(VisualElement target)
        {
            for (var element = target; element != null; element = element.parent)
            {
                var field = FindCallbackField(element.GetType());
                if (field == null)
                    continue;
                if (field.GetValue(element) == null)
                    field.SetValue(element, Delegate.CreateDelegate(field.FieldType, CreateMenuMethod));
                return;
            }
        }

        static FieldInfo FindCallbackField(Type type)
        {
            for (; type != null && type != typeof(VisualElement); type = type.BaseType)
            {
                var field = type.GetField(CallbackField, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                // Le type du rappel (Func<IGenericMenu>) est interne à Unity : on le reconnaît par sa forme.
                if (field != null && field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Func<>))
                    return field;
            }
            return null;
        }

        static readonly MethodInfo CreateMenuMethod = typeof(ForgeDropdowns).GetMethod(nameof(CreateMenu), BindingFlags.Static | BindingFlags.NonPublic);

        static GenericDropdownMenu CreateMenu()
        {
            var menu = new GenericDropdownMenu();
            // Le menu est ajouté à la racine du panneau, hors de la fenêtre : il reçoit lui-même les jetons et la feuille de style.
            var root = menu.contentContainer;
            while (root.parent != null)
                root = root.parent;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(DungeonPaths.StyleSheet);
            if (sheet != null)
                root.styleSheets.Add(sheet);
            root.AddToClassList("forge-theme--dark");
            root.AddToClassList("forge-menu");
            return menu;
        }
    }
}
