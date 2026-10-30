using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheDungeon.Editor.Common
{
    /// <summary>Champ sprite visuel : aperçu, nom, et galerie de choix limitée à un dossier d'art.</summary>
    public sealed class SpriteField : VisualElement
    {
        readonly SerializedProperty property;
        readonly Image preview;
        readonly Label nameLabel;

        /// <param name="artFolder">Sous-dossier de Assets/_Project/Art proposé dans la galerie (ex : « Items »).</param>
        public SpriteField(string label, SerializedProperty property, string artFolder, string tooltip)
        {
            this.property = property;
            AddToClassList("forge-row");
            AddToClassList("forge-sprite-field");
            this.tooltip = tooltip;

            if (!string.IsNullOrEmpty(label))
            {
                var title = new Label(label);
                title.AddToClassList("unity-base-field__label");
                title.AddToClassList("forge-sprite-field__label");
                Add(title);
            }

            preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.AddToClassList("forge-sprite-field__preview");
            preview.RegisterCallback<ClickEvent>(_ => OpenGallery(artFolder));
            Add(preview);

            nameLabel = new Label();
            nameLabel.AddToClassList("forge-sprite-field__name");
            Add(nameLabel);

            Add(ForgeUI.Button("Choisir", "Ouvre la galerie des sprites disponibles.", () => OpenGallery(artFolder)));
            Add(ForgeUI.CloseButton("Retirer le sprite.", () => Set(null)));

            Refresh();
            this.TrackPropertyValue(property, _ => Refresh());
        }

        void OpenGallery(string artFolder) => SpritePickerWindow.Show(artFolder, Set);

        void Set(Sprite sprite)
        {
            property.serializedObject.Update();
            property.objectReferenceValue = sprite;
            property.serializedObject.ApplyModifiedProperties();
            Refresh();
        }

        void Refresh()
        {
            var sprite = property.objectReferenceValue as Sprite;
            preview.sprite = sprite;
            nameLabel.text = sprite != null ? sprite.name : "<i>Aucun sprite</i>";
            nameLabel.enableRichText = true;
        }
    }
}
