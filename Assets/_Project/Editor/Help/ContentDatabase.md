# Content Database

Cet outil regroupe tout le contenu « fiche » du jeu : **objets**, **ennemis**, **tables de butin** et **progression du joueur**. Vous n'avez jamais besoin d'ouvrir un dossier ni de créer un fichier à la main.

## Créer un élément
- Choisissez l'onglet (Objets, Ennemis, Tables de butin).
- Cliquez sur **+ Nouveau** et remplissez la fiche à droite. Survolez un champ pour afficher son explication.
- Cliquez sur **Valider et créer** en bas de la fenêtre. L'élément n'est créé que s'il ne contient **aucune erreur**. Il est alors nommé (`ITM_`, `ENM_`, `BOSS_`, `LT_`) et rangé automatiquement.

## Le bouton Valider
- Toute modification se fait sur une **copie de travail** : le vrai fichier ne change qu'au clic sur **Valider et enregistrer**.
- La barre du bas indique l'état : **Valide**, **avertissements** (enregistrement possible) ou **erreurs bloquantes**.
- **Annuler les modifications** revient à la dernière version enregistrée.
- Si vous changez d'élément ou fermez la fenêtre avec des modifications en attente, l'outil vous propose de les valider.

## Modifier sans se tromper
- Les curseurs sont bornés par les limites fixées par l'équipe technique : impossible de saisir un prix ou des PV hors limites.
- Pour un objet, le prix et les bonus maximum dépendent de la **rareté** : changez la rareté d'abord.
- Les listes (bonus, effets, objets d'une table) ont un nombre maximum de lignes.
- Les listes de choix (table de butin d'un ennemi, objets d'une table) ne proposent que des éléments **valides**.
- Une nouvelle ligne de bonus prend automatiquement une statistique pas encore utilisée.
- Toute modification s'annule avec **Ctrl+Z**.

## Lire les problèmes
- Un **!** rouge ou orange dans la liste signale un élément à corriger.
- La section **Problèmes détectés** en bas de la fiche explique quoi corriger.
- Quand un bouton **Renommer** ou **Corriger** apparaît, la correction est automatique.

## Tables de butin
- Glissez des objets dans la liste, ou utilisez **Ajouter tous les objets de cette rareté**.
- **Appliquer les poids par défaut** donne à chaque objet le poids prévu pour sa rareté.
- La section **Probabilités par tirage** montre en direct la chance de chaque objet.

## Progression du joueur
- Ouvrez **Générer depuis une formule**, réglez les PV, dégâts et XP de départ et leur croissance, puis cliquez sur **Générer les niveaux**.
- Ajustez ensuite les niveaux à la main dans le tableau. Une ligne rouge signale une valeur qui baisse par rapport au niveau précédent.

## Supprimer
- **Supprimer** demande confirmation et indique si l'élément est encore utilisé (par une table de butin ou une salle, par exemple). Le fichier part dans la corbeille du système.
