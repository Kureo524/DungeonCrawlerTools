using UnityEditor;
using UnityEngine;

namespace TheDungeon.Editor.Common
{
    /// <summary>
    /// Boîtes de dialogue des outils. En mode batch (tests automatiques, intégration continue), aucune fenêtre
    /// modale n'est ouverte : la réponse prudente (annuler / rester) est renvoyée et le message est journalisé.
    /// </summary>
    public static class ForgeDialogs
    {
        public static bool Confirm(string title, string message, string ok, string cancel)
        {
            if (Application.isBatchMode)
            {
                Debug.Log($"[Forge] {title} : {message} → {cancel} (mode batch)");
                return false;
            }
            return EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        public static void Inform(string title, string message)
        {
            if (Application.isBatchMode)
            {
                Debug.Log($"[Forge] {title} : {message}");
                return;
            }
            EditorUtility.DisplayDialog(title, message, "OK");
        }

        /// <summary>Trois choix ; renvoie 0 (ok), 1 (cancel) ou 2 (alt). En mode batch : <paramref name="batchAnswer"/>.</summary>
        public static int Choose(string title, string message, string ok, string cancel, string alt, int batchAnswer)
        {
            if (Application.isBatchMode)
            {
                Debug.Log($"[Forge] {title} : {message} → choix {batchAnswer} (mode batch)");
                return batchAnswer;
            }
            return EditorUtility.DisplayDialogComplex(title, message, ok, cancel, alt);
        }
    }
}
