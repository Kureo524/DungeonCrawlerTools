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

## Tests

`Assets/_Project/Tests/Editor` contient 10 tests EditMode : conversion des noms, préfixe des boss, objet manquant dans une table, bonus trop élevé pour la rareté, PV qui baissent dans la progression, calcul des probabilités de butin. **Résultat actuel : 10/10.**

## Prochaines étapes
- Room Editor : grille de salle, portes, placement des ennemis et des coffres, plan d'étage. Règles de portes, d'accessibilité et de boss unique.
- Runtime minimal (scène Bootstrap qui charge un étage).
- Validator (fenêtre de rapport complète), Balancing, Build.
- Contenu d'échantillon produit avec les outils, mesures chronométrées, document final et vidéo.
