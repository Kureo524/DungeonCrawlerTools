# Projet Forge : outils de production pour *The Dungeon*

Workshop B3 (La Horde). Outils intégrés à l'éditeur **Unity** pour produire le contenu du dungeon crawler *The Dungeon* (objets, ennemis, butin, salles, progression) vite et sans erreur, sans écrire de code.

## Ouvrir le projet
1. Installer **Unity 6000.3.23f1** (Unity 6 LTS) avec Unity Hub.
2. Unity Hub → **Add** → **Add project from disk** → sélectionner ce dossier.
3. Une fois le projet ouvert, les outils se trouvent dans le menu **Tools**, dans la barre du haut.

## Les outils (menu `Tools`)
| Outil | Famille | État |
|---|---|---|
| **Content Database** : objets, ennemis, tables de butin, progression du joueur | Création de contenu, Automatisation | ✅ Première version |
| **Room Editor** : salles et plan des étages | Création de contenu, Automatisation | ⏳ À venir |
| **Validator** : rapport de cohérence lisible par un designer | Validation et qualité | 🟡 Noyau de règles en place |
| **Balancing** : courbes de progression et de butin | Équilibrage et debug | ⏳ À venir |
| **Build** : build bloqué si les données sont invalides | Pipeline | ⏳ À venir |
| **Settings (dev)** : limites imposées aux designers | (configuration) | ✅ |

Chaque outil est une fenêtre dockable, comme Inspector ou Game. Chaque champ affiche une infobulle au survol, et chaque outil a un onglet **Help** qui donne le mode d'emploi pas à pas.

## Organisation du projet
```
Assets/_Project/
├─ Runtime/        Code du jeu (assembly TheDungeon.Runtime) : données ScriptableObject
├─ Editor/         Code des outils (assembly TheDungeon.Editor, éditeur uniquement, absent du build)
│  ├─ Common/      Socle partagé : fenêtre de base, nommage, création d'assets, limites
│  ├─ ContentDatabase/
│  ├─ Validation/  Règles de cohérence (une classe par règle)
│  ├─ Help/        Textes de l'onglet Help (Markdown, modifiables sans recompiler)
│  └─ Styles/      Feuille de style commune (USS)
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
