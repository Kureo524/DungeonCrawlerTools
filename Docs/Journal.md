# Journal de développement

Ce journal retrace ce qui a été fait, dans quel ordre et pourquoi. Il servira de base au document de rendu.

## Analyse du sujet et du GDD

Le contenu à produire est volumineux : 30 salles, 15 ennemis, 40 objets, environ 20 tables de butin, 20 niveaux de progression et environ 150 répliques. Il sera saisi par des designers qui ne programment pas. Le GDD impose aussi quatre règles de cohérence : références existantes, portes compatibles et salles atteignables, un boss par étage, convention de nommage.

Outils retenus, du plus rentable au moins rentable :

| Besoin du GDD | Problème sans outil | Outil |
|---|---|---|
| 40 objets, 15 ennemis, 20 tables, 20 niveaux | créer chaque fichier à la main, le nommer, le ranger, remplir un inspecteur générique | **Content Database** |
| 30 salles avec portes, ennemis et coffres | placement manuel dans des scènes, aucune vue d'ensemble d'un étage | **Room Editor** |
| 4 règles de cohérence | vérification à l'œil, erreurs découvertes en jeu | **Validator** |
| Progression 1 → 20, stats par étage, butin pondéré | équilibrage au feeling dans un tableur | **Balancing** |
| Build | build lancé avec des données cassées | **Build** (bonus) |

**Non retenu : l'éditeur de dialogues.** Un éditeur de graphe (nœuds, conditions, conséquences) coûte trop cher pour le temps du workshop. En production, on intégrerait un outil existant comme Yarn Spinner ou Ink.

## Choix du moteur : Unity 6

- Les **ScriptableObjects** répondent directement à la contrainte « données au format natif ».
- **UI Toolkit** permet de construire des fenêtres d'éditeur riches (listes, curseurs, styles CSS) entièrement en code. Résultat : un historique Git lisible, contrairement aux Blueprints Unreal, qui sont binaires.
- Les **assemblies séparées** (`.asmdef`) garantissent que le code des outils n'est pas embarqué dans le build.

## Étape 1 : socle du projet

- Projet Unity 6000.3.23f1. Dépôt Git avec un `.gitignore` Unity (dossiers générés exclus, ainsi que le sujet du workshop).
- Trois assemblies :
  - `TheDungeon.Runtime` pour le jeu ;
  - `TheDungeon.Editor`, limitée à la plateforme Editor, donc absente du build ;
  - `TheDungeon.Editor.Tests` pour les tests.
- Modèle de données en ScriptableObjects (`Runtime/Data`) :
  - `ItemData` : nom, icône, rareté, type, bonus de stats, effets, prix, description ;
  - `EnemyData` : stats de l'étage 1 et croissance par étage, calculée par `GetHealth(floor)` et les méthodes voisines. Un seul asset décrit donc l'ennemi sur les 3 étages, au lieu de 3 copies à maintenir ;
  - `LootTable` : objets pondérés, chance de butin, nombre de tirages, étages où la table s'applique ;
  - `RoomData` : grille de cases, portes N/E/S/O (flags), ennemis et coffres placés, difficulté, tags ;
  - `FloorData` : position des salles sur le plan de l'étage et salle d'entrée ;
  - `PlayerProgression` : une ligne par niveau (PV, dégâts, XP, déblocage).
- Les champs sont privés (`[SerializeField]`) et exposés en lecture seule. Le jeu ne peut pas modifier les données par erreur, et les outils passent par `SerializedObject`, ce qui donne l'annulation (Ctrl+Z) gratuitement.
- Les références entre données sont des **références directes d'assets**, pas des identifiants texte. Une faute de frappe ne peut donc pas casser un lien. La règle « l'ennemi cité doit exister » revient alors à détecter les références vides ou vers un asset supprimé.

## Étape 2 : socle commun des outils (`Editor/Common`)

Choix d'ergonomie appliqués à tous les outils :
- **Menu `Tools`** dans la barre principale. Tous les outils y sont regroupés.
- **`ForgeToolWindow`** : classe de base de chaque fenêtre. Elle fournit l'en-tête, les onglets, un onglet **Help** ajouté automatiquement et la feuille de style commune. Les fenêtres sont dockables et retiennent l'onglet ouvert.
- **Infobulles partout** : sur les champs, les boutons, les lignes de liste et les problèmes. Les bornes autorisées sont rappelées dans l'infobulle.
- **Aide en Markdown** (`Editor/Help/*.md`), affichée dans l'onglet Help. Un designer ou un lead peut la modifier sans recompiler.
- **`ForgeDesignLimits`** : asset réservé aux développeurs (`Tools > Settings (dev)`). Il fixe les bornes : PV, dégâts, prix par rareté, taille des salles, nombre max d'ennemis par salle, d'objets par table, etc. Les outils **brident** la saisie (curseurs bornés, listes plafonnées) et le Validator utilise les mêmes valeurs. Le designer compose librement, mais à l'intérieur du cadre décidé par l'équipe.
- **`NamingConvention`** : `PRÉFIXE_NomEnPascalCase` (`ITM_`, `ENM_`, `BOSS_`, `LT_`, `RM_`, `FLR_`, `PRG_`). Les accents et caractères spéciaux sont retirés automatiquement : « Épée longue +1 » devient `ITM_EpeeLongue1`.
- **`AssetFactory`** : crée, duplique, renomme et supprime les données au bon endroit et avec le bon nom. Avant une suppression, il avertit si l'asset est encore utilisé ailleurs.

## Étape 3 : noyau de validation (`Editor/Validation`)

- Chaque règle est une classe qui hérite de `ValidationRule`. Elle est détectée automatiquement (`TypeCache`) : ajouter une règle ne demande aucune autre modification.
- Un problème (`ValidationIssue`) porte une gravité (erreur ou avertissement), un **message en français pour un designer**, l'asset concerné et, si possible, une **correction automatique** (par exemple « Renommer »).
- Règles en place : convention de nommage, objets (prix et bonus selon la rareté, icône, doublons), ennemis (stats dans les limites, butin), tables de butin (objet manquant, poids nul, étages incohérents), progression (20 niveaux, valeurs croissantes).
- Les règles sur les salles et les étages (portes, accessibilité, boss unique) viendront avec le Room Editor.

## Étape 4 : Content Database (`Tools > Content Database`)

Une fenêtre, quatre onglets :
- **Objets / Ennemis / Tables de butin** : liste à gauche (recherche, filtres par rareté, type ou boss, pastille de couleur, **!** si l'élément a un problème) et fiche à droite.
  - **+ Nouveau / Dupliquer / Supprimer** : l'asset est créé, nommé et rangé tout seul.
  - Le fichier est renommé automatiquement quand on change le nom affiché, ou quand un ennemi devient boss (`ENM_` → `BOSS_`).
  - Pour un objet, changer la rareté ajuste aussitôt les bornes du prix et des bonus.
  - Pour un ennemi, un tableau calcule ses stats sur chaque étage.
  - Pour une table de butin :
    - **barres de probabilité** en direct ;
    - raccourci « ajouter tous les objets d'une rareté » ;
    - application des poids par défaut de chaque rareté.
  - La section **Problèmes détectés** de chaque fiche se met à jour pendant la saisie (avec un délai de 300 ms pour ne pas tout revalider à chaque touche).
- **Progression** : générateur par formule (valeur de départ et croissance) qui remplit les 20 niveaux en un clic, puis un tableau éditable. Une saisie hors limites est ramenée dans les bornes avec un message, et une ligne dont une valeur baisse est surlignée en rouge.

## Étape 5 : retours du premier test utilisateur

Problèmes signalés après une première prise en main dans Unity, et réponses apportées :

| Retour | Cause | Correction |
|---|---|---|
| Avertissement « Input Manager déprécié » | Le projet utilisait l'ancien système d'entrées | Package **Input System 1.20** installé, ancien système désactivé |
| Fenêtre trop petite (Linux), onglets inaccessibles | Taille d'ouverture laissée au gestionnaire de fenêtres | Ouverture centrée et dimensionnée (`ForgeToolWindow.Open`), taille minimale, onglets qui passent à la ligne |
| « Une statistique apparaît plusieurs fois » alors que non | Une ligne ajoutée copiait la précédente, donc la même stat | Une nouvelle ligne prend une **stat pas encore utilisée** ; message plus précis (quelle stat, combien de fois) |
| Pas de sprites | — | Import d'environ 180 sprites **CC0** (Dungeon Crawl), configurés automatiquement |
| On peut donner une table de butin invalide à un ennemi | Le champ acceptait n'importe quel asset | **Sélecteurs filtrés** (seuls les assets valides sont proposés) et **propagation des erreurs** : une donnée qui utilise une donnée invalide devient invalide |
| Pas d'outil de salle | — | **Room Editor** (voir ci-dessous) |
| Besoin de presets de biome | — | **Biome Presets** (voir ci-dessous) |
| Un asset ne doit exister que s'il est valide | Les outils créaient l'asset immédiatement | **Copie de travail + bouton Valider** dans tous les outils |

### Copie de travail et bouton « Valider »
- `DraftSession<T>` : l'outil modifie une **copie en mémoire** de l'asset, jamais l'asset lui-même.
- Le pied de fenêtre (`ValidationFooter`) affiche l'état (valide, avertissements, erreurs bloquantes) et propose **Valider et créer / Valider et enregistrer** ou **Annuler les modifications**.
- La validation évalue la copie **avec le reste du projet**. Cela détecte par exemple un nom déjà pris ou une référence vers une donnée invalide. L'enregistrement est refusé tant qu'il reste une erreur ; les avertissements ne bloquent pas.
- Changer d'élément ou fermer la fenêtre avec des modifications en attente propose de les valider (mécanisme natif `hasUnsavedChanges` d'Unity).
- Conséquence voulue : **le dossier Data ne contient jamais de donnée invalide créée par les outils.** Une donnée ne peut devenir invalide qu'indirectement, par exemple si un élément qu'elle utilise est supprimé ou modifié.

### Références sûres
- `AssetPicker<T>` remplace les champs objet génériques. C'est une liste déroulante des seuls assets **valides** du type, avec aperçu du sprite.
- `ValidationRunner` propage les erreurs le long des références, jusqu'à stabilité. Une salle qui utilise un ennemi dont la table de butin est cassée est donc signalée, avec un message qui nomme le maillon fautif.

### Biome Presets (`Tools > Biome Presets`)
Le jeu prend une direction « type Soul Knight » : des salles en grille regroupées par biomes. Un preset de biome définit :
- ses **visuels** : variantes de sol mélangées automatiquement, mur, trou, avec un aperçu dessiné ;
- les **ennemis autorisés**, avec un poids d'apparition. Un boss est nécessaire pour faire une salle de boss ;
- les **décors**, qui peuvent bloquer le passage ou non ;
- les **tables de butin des coffres** ;
- des **bonus de butin par rareté**, avec un aperçu des probabilités finales.

### Room Editor (`Tools > Room Editor`)
- Une salle **appartient obligatoirement à un biome valide** : sans biome, la création est impossible et l'outil explique quoi faire.
- La **palette** est construite à partir du biome : terrain, ennemis, boss, coffres (un par table du biome), décors, apparition du joueur, gomme. On ne peut donc pas placer un contenu étranger au biome.
- La **grille** se peint au clic gauche maintenu et se gomme au clic droit. Une infobulle et une barre d'état décrivent chaque case. Un coup de pinceau s'annule en entier avec Ctrl+Z.
- Le **sol inaccessible** depuis les entrées est teinté en rouge en direct, par un parcours en largeur partagé avec la règle de validation.
- Opérations refusées proprement, avec un message : poser sur un mur, murer une entrée, dépasser le nombre maximum d'ennemis ou de coffres.
- Redimensionnement sans perte de contenu, entrées N/E/S/O, actions rapides (contour de murs, réinitialisation).
- Règles de validation : biome obligatoire, contenu appartenant au biome, au moins une entrée, entrées praticables, tout accessible, contraintes par type de salle (départ, combat, trésor, boss), un boss uniquement en salle de boss.

### Import automatique des sprites
`ArtImportPostprocessor` configure toute image déposée dans `Assets/_Project/Art` : sprite unique, filtre Point (pixel art net), 32 px par unité, sans compression ni mipmaps. Il signale aussi les fichiers qui ne suivent pas la convention `SPR_`. Le menu `Tools > Art > Reconfigurer tous les sprites` réapplique ces réglages.

## Tests

`Assets/_Project/Tests/Editor` contient **29 tests EditMode**. **Résultat actuel : 29/29.**
- Nommage : conversion des noms, préfixe des boss.
- Règles de contenu : objet manquant, bonus trop élevé, progression qui baisse, probabilités de butin.
- Salles : salle neuve correcte, ennemi hors biome, pose sur un mur refusée, entrée murée refusée, ennemi inaccessible, boss hors salle de boss, redimensionnement sans perte, salle sans biome.
- Copie de travail : un objet invalide **n'est pas créé** ; un objet valide est créé avec le bon nom et au bon endroit.
- Construction de chaque fenêtre d'outil, y compris le Room Editor avec de vraies données temporaires, pour détecter les exceptions sans ouvrir Unity.

## Prochaines étapes
- Plan des étages : assembler les salles, vérifier les portes compatibles, les salles atteignables depuis l'entrée et le boss unique par étage.
- Runtime minimal (scène Bootstrap qui charge un étage).
- Validator (fenêtre de rapport complète), Balancing, Build.
- Contenu d'échantillon produit avec les outils, mesures chronométrées, document final et vidéo.
