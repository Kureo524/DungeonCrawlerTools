# Contenu : objets, ennemis, butin

Cet outil regroupe tout le contenu « fiche » du jeu : **objets**, **ennemis** et **tables de butin**. La progression du joueur a sa propre aide, dans la section Progression. Vous n'avez jamais besoin d'ouvrir un dossier ni de créer un fichier à la main.

## Créer un élément
- Choisissez l'onglet (Objets, Ennemis, Tables de butin).
- Cliquez sur **+ Nouveau** et remplissez la fiche à droite. Survolez un champ pour afficher son explication.
- Cliquez sur **Valider et créer** en bas de l'outil. L'élément n'est créé que s'il ne contient **aucune erreur**. Il est alors nommé (`ITM_`, `ENM_`, `BOSS_`, `LT_`) et rangé automatiquement.

## Le bouton Valider
- Toute modification se fait sur une **copie de travail** : le vrai fichier ne change qu'au clic sur **Valider et enregistrer**.
- La barre du bas indique l'état : **Valide**, **avertissements** (enregistrement possible) ou **erreurs bloquantes**.
- **Annuler les modifications** revient à la dernière version enregistrée.
- Si vous changez d'élément ou fermez la fenêtre avec des modifications en attente, l'outil vous propose de les valider.

## Modifier sans se tromper
- Les curseurs sont bornés par les limites fixées par l'équipe technique : impossible de saisir un prix ou des PV hors limites.
- Pour un objet, le prix, les bonus maximum et le nombre de passifs d'une arme dépendent de la **rareté** : changez la rareté d'abord.
- Les listes (bonus, effets, objets d'une table) ont un nombre maximum de lignes.
- Les listes de choix (table de butin d'un ennemi, objets d'une table) ne proposent que des éléments **valides**.
- Une nouvelle ligne de bonus prend automatiquement une statistique pas encore utilisée.
- Toute modification s'annule avec **Ctrl+Z**.

## Raretés
- La vue **Raretés** (en haut de l'outil Objets, ou **Gérer les raretés…** dans « Classement ») liste les raretés, de la plus commune à la plus rare. L'ordre sert au jeu pour décider si un objet ramassé est meilleur que celui équipé.
- Chaque rareté fixe sa couleur, la plage de prix, le bonus de stat maximum, le poids de butin proposé et le **nombre de passifs** d'une arme.
- **+ Nouvelle rareté**, les flèches ↑ ↓ et **Supprimer** modifient la liste ; rien n'est enregistré avant **Valider et enregistrer**.
- Renommer une rareté ne change rien aux objets. Supprimer une rareté encore utilisée met ses objets **en erreur** : le message indique combien d'objets sont à modifier, et le filtre **Rareté supprimée** de la liste les retrouve.

## Effets actifs et passifs
- Chaque ligne d'**Effets** est **Actif** (à l'impact pour une arme, à l'utilisation pour un consommable) ou **Passif** (permanent tant que l'arme est équipée).
- Seules les **armes** ont des passifs ; leur nombre maximum dépend de la rareté (ex. : aucun pour Commun, trois pour Légendaire).
- La liste ne propose que les effets qui marchent pour ce type d'objet et ce déclenchement : poison, brûlure, étourdissement, vol de vie à l'impact ; attaque accrue, coups critiques, dernier souffle, festin, régénération, peau de fer, pas léger en passif ; soin et régénération pour un consommable.
- Plusieurs passifs du même type s'additionnent.

## Lire les graphiques
- **Objets** : la jauge « Position du prix » montre la plage autorisée par la rareté (zone verte) et le prix de l'objet (repère vert dans la plage, rouge en dehors). Sous les bonus de statistiques, les barres de couleur comparent chaque bonus au maximum de la rareté.
- **Ennemis** : trois petits graphiques (PV, dégâts, XP) montrent l'évolution sur les étages. La courbe grise est la moyenne des autres ennemis du même rang : un ennemi bien au-dessus est un pic de difficulté.
- **Tables de butin** : l'anneau donne la chance d'obtenir un objet de chaque rareté ; les barres détaillent chaque objet.
- Survolez une courbe, un segment ou une barre pour lire les valeurs exactes.

## Lire les problèmes
- Un **!** rouge ou orange dans la liste signale un élément à corriger.
- La section **Problèmes détectés** en bas de la fiche explique quoi corriger.
- Quand un bouton **Renommer** ou **Corriger** apparaît, la correction est automatique.

## Tables de butin
- Glissez des objets dans la liste, ou utilisez **Ajouter tous les objets de cette rareté**.
- **Appliquer les poids par défaut** donne à chaque objet le poids prévu pour sa rareté.
- La section **Probabilités par tirage** montre en direct la chance de chaque objet.

## Supprimer
- **Supprimer** demande confirmation et indique si l'élément est encore utilisé (par une table de butin ou une salle, par exemple). Le fichier part dans la corbeille du système.
