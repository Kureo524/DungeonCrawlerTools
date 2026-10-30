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

## Comportement des ennemis
- Dans la fiche d'un ennemi, la vue **Comportement** a deux listes : **Déplacement** et **Attaques**.
- **Déplacement** : des règles « Quand [conditions] → se déplace ainsi » (poursuivre, garder ses distances, tourner, fuir, rester sur place, charger), testées de haut en bas, puis **Sinon** (comportement de base). Glissez une règle par ses trois traits pour changer l'ordre. **Modèles** propose rage, recul, tireur, traque, encerclement, curée.
- **Attaques** : **+ Attaque** donne à l'ennemi une attaque de l'outil **Attaques**, ou en crée une depuis un modèle. Parmi les attaques **rechargées**, avec le joueur **à portée** et dont les conditions sont remplies, l'ennemi en **tire une au hasard selon leur poids** (60 et 40 = 60 % et 40 % ; 0 = jamais). Chaque carte affiche sa chance dans la situation de l'arène. Pendant une attaque il ne se déplace pas.
- Les réglages de déclenchement grisés viennent de l'attaque. Cliquez-en un pour le **changer pour cet ennemi seulement** (recharge plus courte, dégâts ou cadence plus élevés pour un boss) ; « ↺ » revient à celui de l'attaque. Les étapes se modifient dans l'outil Attaques. Les conditions ajoutées ici ne valent que pour cet ennemi.
- **Arène de test** : glissez le joueur et le mur. Le déplacement (flèche bleue) et l'attaque choisie (en rouge) sont dessinés, avec la zone de portée de l'attaque sélectionnée ; la ligne du bas dit pourquoi une attaque n'est pas utilisée (trop près, hors de portée, joueur caché…).

## Attaques
- L'outil **Attaques** regroupe les attaques réutilisables (fichiers `ATK_`) : attaques d'**ennemis et de boss**, **attaques d'armes** et **compétences d'armes** (champ « Pour »). Modifier une attaque change tout ce qui l'utilise, sauf les réglages qu'un ennemi remplace.
- **Déclenchement** : distance au joueur (ennemis), recharge, dégâts (%) et cadence (%). **Une fois lancée, l'attaque va jusqu'au bout**, même si la cible s'éloigne ou étourdit l'attaquant : elle peut rater.
- **Enchaînement** : des **étapes** jouées dans l'ordre, chacune avec une préparation (annonce : la zone s'affiche au sol), une action et une récupération (fenêtre de riposte). Actions :
  - **Frappe de zone** : cercle, anneau, cône ou ligne, placée **sur l'attaquant**, **sur la cible**, **devant** (en chapelet), **là où tombe un projectile lancé** (boule de putréfaction) ou **au hasard** (pluie). Plusieurs zones à la fois possibles.
  - **Projectiles** (visés, éventail, cercle), **Ruée** (vers la cible, en recul, de côté), **Téléportation** (derrière, devant, au loin, au hasard ; invulnérable pendant la disparition), **Invocation** (un ennemi choisi, ex. des clones, ou du biome), **Renforcement** (soin, dégâts, vitesse, armure, invulnérabilité), **Absorption** (absorbe les alliés proches, chacun renforce), **Changer de phase**, **Pause**.
- **+ Option** ajoute à une étape : répéter (double charge), se déplacer pendant l'étape (frapper en marchant), zone qui dure (flaque), qui avance (onde), qui grandit (anneau), balayage (rayon canalisé), effet sur la cible (poison, brûlure, putréfaction, gel, saignement, étourdissement, repousse, projection), tête chercheuse, saut (invulnérable, par-dessus la cible), garde, super-armure, coût en PV, attaque chargée (armes : maintenir la touche).
- **Suites conditionnelles** : chaque étape (sauf la première) est jouée *toujours*, *si l'étape précédente a touché / a raté*, *si au moins N coups ont touché*, ou *si l'attaquant a été touché* (riposte après une garde), plus des conditions sur la situation au moment où elle commence. **Sinon** : passer à la suivante, ou terminer l'attaque.
- **Modèles** (« Créer depuis un modèle ») : coup chargé, bond stellaire, combo, pluie ciblée, boule de putréfaction, assaut depuis les piliers (bond puis double charge), montagne de putréfaction (absorbe, explose, change de phase), saisie, rayon balayant, anneaux d'ombre, disparition, meute (clones), garde et riposte, marche écrasante ; pour les armes : combo d'épée, estoc chargé, bond du lion, pas du limier, phalange de lames, flamme, piétinement de givre, lame de sang, cri de guerre.
- **Frise** et **aperçu** de chaque étape à l'échelle (zone, lancer, zone qui avance ou grandit en pointillés).

## Phases d'un boss
- Dans la vue Comportement d'un ennemi, **Phases** : chaque phase change les dégâts, la vitesse et les dégâts reçus, peut ajouter une **aura** (effet continu autour de l'ennemi, ex. putréfaction) et des **renforts** à intervalle régulier.
- On y entre par une étape **Changer de phase** (dans une attaque). Règles et attaques peuvent ne servir que dans une phase : condition **Phase de l'ennemi**.

## Armes du joueur
- Dans la fiche d'une arme (outil Objets), **Attaques de l'arme** : l'**attaque** remplace le tir de base (touche de tir) ; la **compétence** est jouée par la mécanique « Compétence de l'arme » (outil Progression > Mécaniques).
- Dans ces attaques, « la cible » est l'ennemi visé (le plus proche dans la visée).

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
