using System;
using Object = UnityEngine.Object;

namespace TheDungeon.Editor.Common
{
    /// <summary>Fenêtre qui héberge les outils (Forge Studio) : état « non validé », fin de vie et navigation.</summary>
    public interface IToolHost
    {
        /// <summary>
        /// Fermeture définitive de la fenêtre. À utiliser pour libérer les copies de travail :
        /// l'interface peut être détachée puis rattachée sans être reconstruite (ancrage, déplacement,
        /// agrandissement), ce n'est donc pas un signal de fin de vie.
        /// </summary>
        event Action Closing;

        /// <summary>Déclare des modifications non validées ; à la fermeture, Unity propose de les valider.</summary>
        /// <param name="commit">Tente la validation ; renvoie false si les données contiennent des erreurs.</param>
        /// <param name="blocked">Les modifications contiennent des erreurs : « Tout valider » est refusé tant qu'elles restent.</param>
        void SetUnsaved(object owner, bool unsaved, Func<bool> commit = null, bool blocked = false);

        /// <summary>Ouvre l'outil qui gère cet asset et l'affiche.</summary>
        void Reveal(Object asset);

        void ShowSection(string id);
    }

    /// <summary>Propriétaire de modifications non validées qui sait se présenter et se montrer (indicateur de la barre latérale).</summary>
    public interface IUnsavedSource
    {
        /// <summary>Nom de la modification dans la liste, ex. « Objet 'Épée' ».</summary>
        string UnsavedLabel { get; }

        /// <summary>Affiche la modification, une fois sa section ouverte (vue, fiche…).</summary>
        void ShowUnsaved();
    }

    /// <summary>Outil capable d'afficher un asset précis (navigation depuis le tableau de bord).</summary>
    public interface IAssetNavigable
    {
        void Reveal(Object asset);
    }
}
