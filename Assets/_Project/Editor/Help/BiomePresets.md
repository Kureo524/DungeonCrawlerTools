# Biomes

Un **biome** est un preset réutilisable : il définit l'apparence et le contenu autorisé d'un ensemble de salles (Crypte, Marais, Volcan…). L'outil **Salles** construit sa palette à partir du biome de la salle. **Impossible de créer une salle sans biome valide.**

## Créer un biome
- Cliquez sur **+ Nouveau**. Rien n'est créé tant que vous n'avez pas cliqué sur **Valider et créer** en bas de la fenêtre.
- Donnez un nom, une couleur et les étages où le biome peut apparaître.
- Choisissez les visuels : **Mur**, **Trou** et une ou plusieurs **variantes de sol**. Cliquez sur une vignette pour ouvrir la galerie de sprites.
- L'**Aperçu** montre une petite salle dessinée avec ces visuels.

## Contenu du biome
- **Ennemis du biome** : seuls ces ennemis pourront être placés dans les salles. Le **poids** règle leur fréquence. Ajoutez au moins un **boss** pour pouvoir créer la salle de boss.
- **Décors** : sprite, nom affiché dans la palette, et option **Bloque le passage** (colonne, rocher…).
- **Tables de butin des coffres** : les coffres des salles de ce biome ne peuvent utiliser que ces tables.
- **Bonus de butin par rareté** : par exemple ×2 sur Rare rend les objets rares deux fois plus fréquents dans ce biome. L'aperçu en bas montre les probabilités finales.

## Validation
- Les listes ne proposent que des éléments **valides** : un ennemi ou une table contenant des erreurs n'apparaît pas.
- Le bouton **Valider** reste bloqué tant qu'il reste une **erreur**. Les avertissements n'empêchent pas d'enregistrer.
- Si vous modifiez un biome utilisé par des salles, relancez la vérification de ces salles : un ennemi retiré du biome rend invalides les salles qui l'utilisent.
