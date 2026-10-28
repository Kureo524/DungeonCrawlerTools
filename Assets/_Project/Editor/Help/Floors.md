# Étages

Le GDD prévoit **3 étages de 10 salles**. Cet outil assemble les salles de chaque étage sur un plan et vérifie les règles de cohérence du GDD **en direct** :
- deux salles reliées ont des **portes compatibles** ;
- toute salle est **atteignable depuis l'entrée** ;
- chaque étage contient **exactement un boss**.

## Créer un étage
- **+ Nouveau** prend le premier numéro d'étage libre (1 à 3).
- L'étage est créé seulement au clic sur **Valider et créer**, s'il ne contient aucune erreur.

## Générer un plan
- **Générer le plan** assemble automatiquement 10 salles parmi celles disponibles : la salle de départ au centre, des salles dont les portes s'emboîtent, la salle du boss en fond d'étage.
- Changez la **graine** pour obtenir une autre variante. Le plan reste modifiable à la main ensuite.
- Si la génération échoue, l'outil explique ce qui manque (salle de départ, salle de boss, pas assez de salles, portes impossibles à relier).

## Placer à la main
- Cliquez sur une salle dans **Salles disponibles**, puis sur une case du plan. Les **cases vertes** sont celles où ses portes correspondent aux voisines.
- Cliquez sur une salle du plan pour la **sélectionner**, puis sur une case vide pour la **déplacer**.
- **Clic droit** : retirer une salle. **Double-clic** : l'ouvrir dans l'outil Salles.
- Sélectionnez la salle de départ et cliquez sur **Définir comme entrée**.

## Lire le plan
- **Couloir vert** : les deux portes face à face sont reliées.
- **Amorce rouge avec une croix** : la porte mène dans le vide, ou la salle voisine n'a pas de porte en face.
- **Cadre orange** : la salle n'est pas accessible depuis l'entrée.
- Cadre vert **ENTRÉE**, cadre rouge **BOSS**.
- Les cartes en haut résument l'état : salles 10/10, boss 1/1, portes non reliées, salles accessibles.

## Salles disponibles
Une salle est proposée si elle est **valide**, si son **biome est autorisé à cet étage**, et si elle n'est **pas déjà utilisée sur un autre étage** (chaque salle appartient à un seul étage).
