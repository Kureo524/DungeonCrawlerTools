using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Configure automatiquement toute image déposée dans Assets/_Project/Art : sprite pixel art net,
    /// sans compression ni mipmaps. Signale aussi les fichiers qui ne suivent pas la convention SPR_.
    /// </summary>
    public class ArtImportPostprocessor : AssetPostprocessor
    {
        public const int PixelsPerUnit = 32;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(DungeonPaths.Art))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!fileName.StartsWith("SPR_"))
                Debug.LogWarning($"[Forge] « {fileName} » ne suit pas la convention de nommage des sprites (SPR_Nom).", AssetDatabase.LoadMainAssetAtPath(assetPath));
        }

        [MenuItem("Tools/Art/Reconfigurer tous les sprites", priority = 60)]
        static void ReimportAll()
        {
            AssetDatabase.ImportAsset(DungeonPaths.Art, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("[Forge] Tous les sprites de Assets/_Project/Art ont été reconfigurés.");
        }
    }
}
