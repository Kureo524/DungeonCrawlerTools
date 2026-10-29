# Salles

Dessinez les salles du donjon sur une grille, comme dans un jeu de type Soul Knight : terrain, entrées, ennemis, coffres et décors. Tout le contenu proposé vient du **biome** de la salle.

## Créer une salle
- Un **preset de biome valide** est obligatoire (Forge Studio > Biomes). Sans biome, le bouton **+ Nouveau** explique quoi faire.
- Le filtre **biome** en haut de la liste sert aussi de biome par défaut pour la nouvelle salle.
- La salle est créée seulement au clic sur **Valider et créer**, si elle ne contient aucune erreur.

## Peindre
- Choisissez un pinceau dans la **palette** : Sol, Mur, Trou, un ennemi, un coffre, un décor, l'apparition du joueur ou la Gomme.
- **Clic gauche** (maintenu) pour peindre, **clic droit** pour gommer.
- Survolez une case pour voir son contenu dans l'infobulle et la barre d'état.
- Un mur ou un trou efface ce qui était posé sur la case. On ne peut rien poser sur un mur.
- **Ctrl+Z** annule un coup de pinceau entier.

## Entrées et dimensions
- Cochez **Nord / Est / Sud / Ouest** pour ouvrir une porte au milieu du côté. Les entrées sont encadrées en vert.
- Les curseurs **Largeur / Hauteur** redimensionnent la salle en gardant son contenu. Le contour redevient un mur.

## Lire la grille
- Les cases **rouges** sont du sol inaccessible depuis les entrées : rien de ce qui s'y trouve ne pourra être atteint.
- Un cadre **rouge** signale un boss.

## Règles vérifiées à la validation
- Le biome est obligatoire. Ennemis, coffres et décors doivent appartenir au biome.
- Au moins une entrée, et aucune entrée bloquée.
- Tout élément posé doit être accessible depuis les entrées.
- **Départ** : apparition du joueur obligatoire. **Combat** : au moins un ennemi. **Trésor** : au moins un coffre. **Boss** : exactement un boss, et les boss sont interdits ailleurs.
- Nombre maximum d'ennemis et de coffres fixé par les limites de l'équipe.

## Feu de camp
- Par défaut, le feu de camp de la salle soigne selon la règle de son étage (outil Étages).
- Cochez **Soin propre à la salle** pour lui donner sa propre valeur (en % des PV max, 0 = aucun soin) : par exemple aucun soin avant le boss, ou un soin complet dans une salle de repos.
