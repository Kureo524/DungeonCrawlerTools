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
| **▶ Jouer / Tester cet étage / Tester cette salle** | Lancement du jeu sur tout le donjon, un étage ou une salle | Équilibrage et debug |
| *(à venir)* Build | — | Pipeline |

Principes communs :
- **Navigation** à gauche, avec le nombre d'éléments par outil et un point rouge s'il y a des erreurs. En bas, l'état de validation global.
- **Aide** : le bouton « ? Aide » ouvre le mode d'emploi de l'outil à droite. Chaque élément affiche aussi une infobulle au survol.
- **Rien n'est créé ni modifié sans validation** : on travaille sur une copie, et le bouton **Valider** n'enregistre que si les données ne contiennent aucune erreur.
- Les listes de choix ne proposent que des éléments **valides**.
- **Graphiques interactifs** : survol pour lire les valeurs, palette lisible par les daltoniens, en thème clair comme en sombre.

## Jouer
Le jeu est un dungeon crawler en vue du dessus, construit **entièrement à partir des données produites par les outils** : étages, salles, biomes, ennemis, butin, progression et dialogues.

- **Forge Studio > ▶ Jouer** (ou **Tools > Jouer**) : partie complète depuis l'étage 1.
- **Étages > ▶ Tester cet étage** et **Salles > ▶ Tester cette salle** : lancement direct sur l'élément en cours d'édition.
- Ou ouvrir `Assets/_Project/Scenes/Game.unity` et appuyer sur Play. La base de jeu est mise à jour automatiquement avant chaque partie et chaque build.

| Commande | Clavier / souris | Manette |
|---|---|---|
| Se déplacer | ZQSD / WASD / flèches | Stick gauche |
| Viser, tirer | Souris, clic gauche | Stick droit, gâchette droite |
| Mécanique secondaire (ex : onde de choc) | Clic droit | Gâchette gauche |
| Esquive | Espace / Maj | B |
| Mécaniques débloquées en route | 1, 2, 3 | LB, RB, X |

| Interagir (coffre, PNJ, boutique, feu de camp, portail) | E | A |
| Consommable | F | Y |
| Inventaire / Pause | I / Échap | Select / Start |

Déroulement :
1. Entrer dans une salle de combat fait apparaître ses ennemis et **ferme les portes** jusqu'à la victoire.
2. Les ennemis lâchent XP, or et butin (tables de butin, bonus de rareté du biome, conditions d'étage). Les coffres s'ouvrent avec E, et la boutique vend des objets à leur prix.
3. Chaque salle a un **feu de camp** : il brûle dès l'arrivée dans une salle sans ennemis, et s'allume dans une salle de combat une fois les ennemis vaincus. C'est là que le joueur répartit les points d'attributs gagnés en montant de niveau.
4. Les PNJ de l'étage attendent dans les salles calmes et jouent leurs dialogues, avec conditions et effets appliqués à la partie.
5. Le boss de l'étage vaincu ouvre un **portail** vers l'étage suivant. Après le dernier étage, c'est la victoire.


## Organisation du projet
```
Assets/_Project/
├─ Runtime/        Code du jeu (assembly TheDungeon.Runtime) : données ScriptableObject (Data/),
│                  logique des dialogues (Dialogue/) et jeu (Game/)
├─ Scenes/         Game.unity : scène du jeu
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

Polices de l'interface (`Assets/_Project/Editor/Styles/Fonts/`) : *Gothic A1*, *IBM Plex Mono* et *Barlow Condensed*, sous licence **SIL Open Font License 1.1**. Textes des licences : `ThirdPartyLicenses/`.
