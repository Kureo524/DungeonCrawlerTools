# Projet Forge : outils de production pour *The Dungeon*

Workshop B3 (La Horde). Outils intégrés à l'éditeur **Unity** pour produire le contenu du dungeon crawler *The Dungeon* (objets, ennemis, butin, salles, progression) vite et sans erreur, sans écrire de code.

## Ouvrir le projet
1. Installer **Unity 6000.3.23f1** (Unity 6 LTS) avec Unity Hub.
2. Unity Hub → **Add** → **Add project from disk** → sélectionner ce dossier.
3. Une fois le projet ouvert, les outils se trouvent dans le menu **Tools**, dans la barre du haut.

## Les outils (menu `Tools`)
| Outil | Famille | État |
|---|---|---|
| **Content Database** : objets, ennemis, tables de butin, progression du joueur | Création de contenu, Automatisation | ✅ |
| **Biome Presets** : visuels, ennemis, décors, coffres et bonus de butin d'un biome | Création de contenu | ✅ |
| **Room Editor** : salles dessinées sur une grille avec la palette du biome | Création de contenu, Automatisation | ✅ Salles (plan des étages à venir) |
| **Import automatique des sprites** (`Assets/_Project/Art`) | Automatisation | ✅ |
| **Validator** : rapport de cohérence lisible par un designer | Validation et qualité | 🟡 Règles en place, fenêtre de rapport à venir |
| **Balancing** : courbes de progression et de butin | Équilibrage et debug | ⏳ À venir |
| **Build** : build bloqué si les données sont invalides | Pipeline | ⏳ À venir |
| **Settings (dev)** : limites imposées aux designers | (configuration) | ✅ |

Principes communs à tous les outils :
- Chaque outil est une fenêtre dockable, comme Inspector ou Game.
- Chaque élément affiche une infobulle au survol, et chaque outil a un onglet **Help** qui donne le mode d'emploi pas à pas.
- **Rien n'est créé ni modifié sans validation.** On travaille sur une copie, et le bouton **Valider** en bas de la fenêtre n'enregistre que si les données ne contiennent aucune erreur.
- Les listes de choix ne proposent que des éléments **valides**.

## Organisation du projet
```
Assets/_Project/
├─ Runtime/        Code du jeu (assembly TheDungeon.Runtime) : données ScriptableObject
├─ Editor/         Code des outils (assembly TheDungeon.Editor, éditeur uniquement, absent du build)
│  ├─ Common/      Socle partagé : fenêtre de base, nommage, création d'assets, limites
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
Sprites : *Dungeon Crawl Stone Soup* tiles (licence **CC0**, domaine public), via [OpenGameArt](https://opengameart.org/content/dungeon-crawl-32x32-tiles). Licence complète : `Assets/_Project/Art/LICENSE_DungeonCrawl_CC0.txt`.
