# Progression du joueur

Une progression décrit les **stats du joueur à chaque niveau** : une courbe par stat. S'y ajoutent les **attributs** où le joueur répartit ses points, les **compétences** et les **récompenses** gagnées à chaque niveau. Vous pouvez créer plusieurs progressions (« Standard », « Difficile »…) et choisir celle que le jeu utilise.

## Organisation de l'onglet
La fiche d'une progression est rangée en catégories, en haut : **Aperçu**, **Stats et courbes**, **Mécaniques**, **Attributs et compétences**, **Récompenses** et **Simulation des niveaux**. L'onglet se souvient de la dernière catégorie ouverte.
- **Aperçu** : une tuile par catégorie (mini-courbes des stats, mécaniques et leur touche, personnage au dernier niveau, frise des récompenses). Cliquer sur un élément ou sur **Détails** ouvre sa catégorie.
- Le **nombre de niveaux** se règle dans la tuile *Progression* de l'Aperçu (de 2 au plafond fixé par l'équipe technique). Ajouter des niveaux prolonge les courbes avec leur dernière valeur ; en retirer coupe les courbes et supprime les récompenses des niveaux retirés, après confirmation.

## Plusieurs progressions
- **+ Nouveau** prépare une progression complète : PV, ATQ, DÉF et XP, trois attributs (Vigueur, Force, Endurance), trois compétences et un point d'attribut par niveau. **Dupliquer** part d'une progression existante.
- Donnez-lui un **nom** : le fichier s'appelle `PRG_<Nom>`.
- **Utiliser dans le jeu** choisit la progression jouée. Elle est marquée en vert dans la liste et porte le badge « Utilisée par le jeu ».

## Les stats
- **+ Ajouter une stat** crée une stat libre (chance critique, mana…). **Supprimer** la retire. ▲ ▼ changent l'ordre d'affichage.
- **Rôle** : ce que le jeu fait de la stat (PV max, dégâts, défense, vitesse, XP pour le niveau suivant). PV max, dégâts et XP sont obligatoires. Un rôle impose les bornes fixées par l'équipe technique.
- **Affichage joueur** : *Masquée*, *À l'écran (HUD)* ou *Fiche du personnage* (écran d'inventaire).
- **Présentation** : *Nombre* (12), *Barre* (se remplit jusqu'au maximum ; pour les PV et l'XP, elle montre la valeur actuelle) ou *Pourcentage* (12 %).
- **Min / Max** bornent la courbe. **Évolution** demande au Validator de signaler une valeur qui baisse ou qui stagne.
- **Jauge consommable** transforme la stat en réserve (énergie, mana…) que les mécaniques dépensent. La valeur du niveau devient le maximum de la jauge, et **Recharge par seconde** règle sa vitesse de remplissage.

## Modifier une courbe à la souris
- Dans **Stats et courbes**, choisissez la stat dans la liste de gauche : sa courbe, ses réglages et le générateur s'affichent à côté.
- L'axe vertical va du **min** au **max** de la stat : le haut du graphique est la valeur maximale autorisée.
- **Glisser un point** change sa valeur. **Maj + glisser** entraîne aussi les niveaux voisins, pour garder une courbe douce. **Ctrl + glisser** dessine à main levée.
- **Agrandir** donne toute la hauteur de la fenêtre à la courbe. **Ctrl+Z** annule un glisser entier.

## Générer avec une fonction
- Ouvrez **Générer la courbe avec une fonction**, choisissez un preset et réglez la valeur au niveau 1 (`start`) et au dernier niveau (`end`). L'aperçu s'affiche en pointillés ; **Appliquer** remplace la courbe.
- Écrivez votre propre **formule**, par exemple `start + (end - start) * t^1.5` ou `start * pow(1.15, n - 1)`.
  - Variables : `n` (niveau), `N` (dernier niveau), `t` (0 au niveau 1, 1 au dernier), `start`, `end`.
  - Fonctions : `sqrt`, `abs`, `exp`, `ln`, `log`, `pow`, `min`, `max`, `floor`, `ceil`, `round`, `clamp`, `lerp`, `sin`, `cos`.
- **Enregistrer comme preset** ajoute la formule à la liste pour toute l'équipe (fichier `Settings/ProgressionPresets.asset`, versionné avec le projet).

## Attributs (points à répartir)
- Comme dans Elden Ring, le joueur gagne des **points d'attributs** et les répartit lui-même en se reposant à un **feu de camp** (touche **E**, puis bouton **+**). Chaque salle en a un : il brûle dès l'arrivée dans une salle sans ennemis, et s'allume dans une salle de combat une fois tous les ennemis vaincus. La fiche du personnage (touche **I**) montre les attributs sans permettre de les modifier.
- Chaque attribut a un **nombre de points maximum** et un ou plusieurs **bonus** : la stat améliorée, le **gain par point**, un **seuil** et la **part conservée après le seuil**. Exemple : +15 PV par point jusqu'à 5 points, puis la moitié (+7,5).
- Sous chaque attribut, un aperçu montre le gain à mi-parcours et au maximum. Le résumé en haut compare les points gagnés sur la partie aux points réellement dépensables.

## Compétences
- Une compétence a un **effet** (projectiles en plus, coups critiques, cadence de tir, recharge de l'esquive, régénération) ou aucun (la description seule est montrée au joueur).
- Une **valeur par rang** : le rang 1 est obtenu au déblocage, chaque amélioration fait passer au rang suivant. **+ rang** / **- rang** changent le nombre d'améliorations possibles.
- L'aperçu indique à quel niveau chaque rang est obtenu.

## Mécaniques du personnage
- Ce sont les actions du joueur. Chaque mécanique a une **action** : *projectile visé*, *onde tout autour*, *ruée*, *soin*, *bouclier* ou *sprint*. Elle a aussi une **touche** (clic gauche, clic droit, Espace, touches 1 à 3, avec leurs équivalents manette).
- **Consomme** : la stat dépensée à chaque utilisation (une jauge, ou les PV, sans jamais pouvoir tuer le joueur), puis la quantité **par utilisation** et la **recharge** en secondes.
- Les autres réglages dépendent de l'action : dégâts en % de l'attaque et nombre de projectiles, distance et durée d'une ruée, PV rendus, durée d'invincibilité ou du bonus de vitesse.
- **Débloquée dès le début** : sinon, ajoutez une récompense *Débloquer une mécanique* au niveau voulu.
- **+ Amélioration** ajoute un rang : ce qu'il ajoute ou retire (dégâts, nombre, durée, coût, recharge). On l'obtient avec une récompense *Améliorer une mécanique*. L'aperçu sous la carte donne chaque rang, le niveau où il est obtenu et ce qu'il fait.
- Sans aucune mécanique, le joueur garde le tir (clic gauche) et l'esquive (Espace) de base. En jeu, la barre d'actions en bas de l'écran montre la touche, la recharge et le coût de chaque mécanique (en rouge si la jauge ne suffit pas).

## Récompenses par niveau
- Chaque récompense a un **niveau** (1 = dès le début) et un **type** : *Points d'attributs*, *Débloquer / Améliorer une compétence*, *Débloquer / Améliorer une mécanique* ou *Note* (texte affiché au joueur).

- La **frise** montre chaque niveau avec une pastille par récompense (vert : points, bleu : compétence, lavande : mécanique, gris : note). Cliquez sur un niveau pour voir et modifier ses récompenses, ou en ajouter. Changer le niveau d'une récompense la déplace sur la frise.
- **Donner des points d'attributs à intervalle régulier** remplace toutes les récompenses de points par un rythme (ex : 1 point tous les niveaux à partir du niveau 2).
- Le Validator signale une compétence améliorée avant d'être débloquée ou déjà au rang maximum, une compétence jamais débloquée, des points sans attribut où les dépenser, et une stat que les attributs peuvent pousser au-delà de son maximum.

## Simulation des niveaux
- Le **simulateur** montre le personnage à un niveau, choisi avec le curseur, les flèches ‹ › ou en cliquant sur une barre :
  - ses **stats**, avec l'écart par rapport au niveau précédent et leur position entre le min et le max ;
  - ses **actions** avec leurs chiffres réels à ce niveau, compétences comprises (dégâts par utilisation et par seconde, utilisations avec la jauge pleine, temps de recharge…). Celles qui ne sont pas encore débloquées sont grisées ;
  - l'**effort** : XP cumulée, ennemis vaincus au total, ennemis pour passer au niveau suivant, points d'attributs gagnés.
- **XP moyenne par ennemi** sert à estimer le nombre d'ennemis. Par défaut, c'est la moyenne des ennemis ordinaires du projet au premier étage (**Moyenne des ennemis** la recalcule).
- Les points d'attributs ne sont pas investis : c'est le joueur qui les répartit en jeu.
- **Niveau par niveau** : une ligne par niveau, avec une pastille de la couleur de chaque stat qui change (cerclée de rouge en cas de problème), puis les pastilles des récompenses. Cliquez sur une ligne pour la déplier : vous y modifiez les valeurs du niveau et ses récompenses.

## Import et export SVG


- **Exporter en SVG…** enregistre une image des courbes. Elle s'ouvre dans un navigateur et se retouche dans Inkscape ou Figma.
- **Importer un SVG…** (sous la liste) recrée une progression à partir de ce fichier. Si vous avez déplacé les points d'une courbe dans un logiciel de dessin, ce sont les points déplacés qui sont repris.
- **Importer une courbe…** (à côté du titre de la courbe, dans Stats et courbes) reprend la **forme** de n'importe quelle courbe SVG pour la stat sélectionnée, entre ses valeurs actuelles la plus basse et la plus haute.
- Une progression importée n'est créée qu'au clic sur **Valider et créer**.
