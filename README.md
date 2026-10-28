# Projet Forge : outils de production pour *The Dungeon*

Workshop B3 (La Horde). Outils intégrés à l'éditeur **Unity** pour produire le contenu du dungeon crawler *The Dungeon* (objets, ennemis, butin, salles, progression) vite et sans erreur, sans écrire de code.

## Ouvrir le projet
1. Installer **Unity 6000.3.23f1** (Unity 6 LTS) avec Unity Hub.
2. Unity Hub → **Add** → **Add project from disk** → sélectionner ce dossier.
3. Une fois le projet ouvert, les outils se trouvent dans le menu **Tools**, dans la barre du haut.

## Forge Studio : tous les outils dans une seule fenêtre
Menu **Tools > Forge Studio**. Chaque outil est aussi accessible directement depuis le menu **Tools**, et la fenêtre est dockable comme Inspector ou Game.

| Section | Rôle | Famille |
|---|---|---|
| **Tableau de bord** | Avancement face aux objectifs du GDD, état des 4 règles de cohérence du GDD, santé des données, répartition du contenu | Validation, Équilibrage |
| **Objets / Ennemis / Tables de butin / Progression** | Création et édition du contenu, avec graphiques (courbes par étage et par niveau, probabilités de butin) | Création de contenu, Équilibrage |
| **Dialogues** | PNJ et arbres de dialogue écrits comme une messagerie, vue en arbre, aperçu jouable avec joueur simulé, export texte | Création de contenu, Validation |
| **Biomes** | Presets : visuels, ennemis, décors, coffres, bonus de butin | Création de contenu |
| **Salles** | Salles dessinées sur une grille avec la palette du biome | Création de contenu, Automatisation |
| **Étages** | Plan de chaque étage (3 × 10 salles), générateur de plans, contrôle des portes, de l'accessibilité et du boss unique | Création de contenu, Automatisation, Validation |
| **Limites (dev)** | Bornes imposées aux designers et objectifs de production | Configuration |
| *(automatique)* Import des sprites | Configuration des images déposées dans `Assets/_Project/Art` | Automatisation |
| *(à venir)* Build | — | Pipeline |

Principes communs :
- **Navigation** à gauche, avec le nombre d'éléments par outil et un point rouge s'il y a des erreurs. En bas, l'état de validation global.
- **Aide** : le bouton « ? Aide » ouvre le mode d'emploi de l'outil à droite. Chaque élément affiche aussi une infobulle au survol.
- **Rien n'est créé ni modifié sans validation** : on travaille sur une copie, et le bouton **Valider** n'enregistre que si les données ne contiennent aucune erreur.
- Les listes de choix ne proposent que des éléments **valides**.
- **Graphiques interactifs** : survol pour lire les valeurs, palette lisible par les daltoniens, en thème clair comme en sombre.

## Organisation du projet
```
Assets/_Project/
├─ Runtime/        Code du jeu (assembly TheDungeon.Runtime) : données ScriptableObject
├─ Editor/         Code des outils (assembly TheDungeon.Editor, éditeur uniquement, absent du build)
│  ├─ Studio/      Fenêtre Forge Studio, tableau de bord, limites
│  ├─ Common/      Socle partagé : copie de travail, sélecteurs, nommage, limites, graphiques (Charts/)
│  ├─ ContentDatabase/
│  ├─ Biomes/
│  ├─ RoomEditor/
│  ├─ Validation/  Règles de cohérence (une classe par règle)
│  ├─ Help/        Textes de l'onglet Help (Markdown, modifiables sans recompiler)
│  └─ Styles/      Feuille de style commune (USS)
├─ Art/            Sprites CC0 (configurés automatiquement à l'import)
├─ Data/           Contenu produit par les outils (rangé automatiquement)
├─ Settings/       ForgeDesignLimits.asset (limites fixées par les développeurs)
└─ Tests/Editor/   Tests automatiques (EditMode)
Docs/              Documentation du projet (journal, document de rendu)
```

## Lancer les tests
Dans Unity : **Window → General → Test Runner → EditMode → Run All**.
En ligne de commande :
```
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```

## Documentation
- [Docs/Journal.md](Docs/Journal.md) : avancement, choix techniques et raisons.

## Crédits
Sprites (objets, ennemis, biomes, portraits de PNJ) : *Dungeon Crawl Stone Soup* tiles (licence **CC0**, domaine public), via [OpenGameArt](https://opengameart.org/content/dungeon-crawl-32x32-tiles). Licence complète : `Assets/_Project/Art/LICENSE_DungeonCrawl_CC0.txt`.
