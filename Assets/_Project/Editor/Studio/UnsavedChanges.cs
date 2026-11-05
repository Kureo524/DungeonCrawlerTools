using System;
using System.Collections.Generic;
using System.Linq;

namespace TheDungeon.Editor.Studio
{
    /// <summary>
    /// Modifications non validées des outils ouverts : pour chacune, la section où elle a été faite, la fonction qui la valide,
    /// et si elle contient des erreurs (« Tout valider » est alors bloqué).
    /// </summary>
    public sealed class UnsavedChanges
    {
        readonly Dictionary<object, Func<bool>> commits = new();
        readonly HashSet<object> blocked = new();
        readonly List<(object owner, string section)> order = new();

        public int Count => commits.Count;
        public int BlockedCount => blocked.Count;

        /// <summary>Modifications dans l'ordre où elles ont commencé.</summary>
        public IReadOnlyList<(object owner, string section)> Entries => order;

        public bool IsBlocked(object owner) => blocked.Contains(owner);

        /// <param name="commit">Valide la modification ; renvoie faux si elle n'a pas pu l'être.</param>
        /// <param name="section">Section où se trouve la modification, retenue quand elle commence.</param>
        public void Set(object owner, bool unsaved, Func<bool> commit, bool hasErrors, string section)
        {
            if (unsaved)
            {
                if (!commits.ContainsKey(owner))
                    order.Add((owner, section));
                commits[owner] = commit;
            }
            else
            {
                commits.Remove(owner);
                order.RemoveAll(entry => entry.owner == owner);
            }
            if (unsaved && hasErrors)
                blocked.Add(owner);
            else
                blocked.Remove(owner);
        }

        public void Clear()
        {
            commits.Clear();
            blocked.Clear();
            order.Clear();
        }

        /// <summary>Valide chaque modification ; renvoie le nombre de celles qui n'ont pas pu l'être.</summary>
        public int CommitAll() => commits.Values.ToList().Count(commit => commit != null && !commit());

        /// <summary>À la fermeture : valide tout, et oublie les modifications seulement si toutes ont été validées.</summary>
        public bool SaveAll()
        {
            var allSaved = true;
            foreach (var commit in commits.Values.ToList())
                allSaved &= commit == null || commit();
            if (allSaved)
                commits.Clear();
            return allSaved;
        }
    }
}
