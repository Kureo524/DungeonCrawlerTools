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

## Étape 6 : une seule fenêtre, des graphiques et une passe de finition

### Forge Studio : tous les outils dans une fenêtre
Demande : regrouper les outils. Les quatre fenêtres séparées sont remplacées par **Forge Studio** (`Studio/ForgeStudioWindow`) :
- **Barre latérale** regroupée par thème (Vue d'ensemble, Contenu, Monde, Équipe technique), avec une icône tirée des sprites du jeu, le nombre d'éléments et un point rouge en cas d'erreur. L'état de validation global est en bas.
- **En-tête** de section (icône, titre, rôle) et bouton **? Aide**, qui ouvre le mode d'emploi dans un panneau latéral au lieu d'un onglet.
- **Navigation directe** : un clic sur un problème du tableau de bord ouvre l'outil concerné sur l'élément fautif (`IToolHost.Reveal`).
- Le menu **Tools** garde une entrée par outil, qui ouvre Forge Studio sur la bonne section.
- Les outils ne dépendent plus d'une fenêtre précise mais de l'interface `IToolHost`. La fenêtre reste ainsi interchangeable, et les tests peuvent construire chaque outil isolément.

### Tableau de bord
- **Avancement de la production** : anneaux de progression par rapport aux objectifs du GDD (40 objets, 12 ennemis, 3 boss, 20 tables, 30 salles, 20 niveaux). Ils sont réglables dans Limites (dev).
- **Santé des données** : nombre d'erreurs et d'avertissements, part d'assets valides, problèmes par règle, liste des problèmes à corriger en priorité (cliquables).
- **Répartition** : objets par rareté, salles par type, salles par biome, courbe de PV du joueur.

### Graphiques (`Common/Charts`)
Composants dessinés avec `Painter2D` (UI Toolkit). Règles de visualisation appliquées :
- **Palette catégorielle validée** pour le daltonisme et le contraste, en version claire et sombre selon le thème de l'éditeur, avec un ordre des couleurs fixe. Les couleurs de **statut** (erreur, avertissement, valide) sont réservées aux états et toujours accompagnées d'une icône ou d'un texte.
- **Jamais deux échelles sur un graphique.** PV, dégâts et XP n'ont pas le même ordre de grandeur, donc ils sont affichés en **petits graphiques côte à côte**.
- Infobulle au survol sur chaque courbe (ligne de repère et toutes les valeurs), chaque segment et chaque barre. Légende dès deux séries. Anneaux limités à 6 segments, le reste étant regroupé dans « Autres ».
- **Animations** : tracé progressif à la première apparition, barres qui glissent vers leur nouvelle valeur, fondu entre sections, survols animés (transitions USS).

Où on les trouve :
- **Progression** : courbes PV, dégâts et XP par niveau, avec les baisses marquées en rouge.
- **Ennemis** : courbes par étage comparées à la moyenne des ennemis du même rang, pour repérer les pics de difficulté.
- **Objets** : jauge du prix dans la plage autorisée par la rareté, et puissance de chaque bonus par rapport au maximum.
- **Tables de butin** et **Biomes** : anneau de chance par rareté (bonus de biome inclus) et barres par objet avec icône. Pour les biomes, s'ajoute la répartition des apparitions d'ennemis.

### Problèmes corrigés
| Problème | Cause | Correction |
|---|---|---|
| `NullReferenceException` en ajoutant une ligne après avoir ancré ou déplacé la fenêtre | Unity détache puis rattache l'interface sans la reconstruire ; le code prenait ce détachement pour une fermeture et détruisait la copie de travail | Les copies de travail et les abonnements sont libérés à la **fermeture réelle** de la fenêtre (`OnDestroy` → `Closing`). Test de non-régression avec une vraie fenêtre et un ancrage simulé |
| Abonnements aux événements de l'éditeur laissés actifs après fermeture | Désabonnement confié au détachement de l'interface, qui n'a pas toujours lieu | Désabonnement explicite à la fermeture |
| Tests automatiques bloqués | Boîtes de dialogue modales attendant un clic en mode batch | `ForgeDialogs` : en mode batch, aucune fenêtre modale, réponse prudente journalisée ; les tests abandonnent les modifications avant de fermer |

## Étape 7 : direction « Soul Knight », en suivant le GDD

Le jeu prend la direction d'un dungeon crawler façon Soul Knight : vue du dessus, salles en grille reliées par des portes, biomes, étages assemblés à partir de salles. **Le GDD reste la référence** : quand les deux divergent, on suit le GDD. On garde ainsi la progression du joueur 1 à 20 avec XP gagnée pendant la partie, et les volumes du GDD.

Comparaison du GDD avec les outils existants :

| Exigence du GDD | Outil |
|---|---|
| Salles : dimensions, portes, ennemis et placement, coffres, difficulté, tags | Salles |
| Ennemis : stats, évolution par étage, comportement, table de butin | Ennemis |
| Items, tables de butin, progression 1 à 20 | Objets, Tables de butin, Progression |
| **3 étages × 10 salles**, portes compatibles, salles atteignables depuis l'entrée, un boss par étage | **Étages (nouveau)** |
| Dialogues | Dialogues (étape 8) |

### Outil Étages
Il couvre **trois des quatre règles de cohérence du GDD**.
- **Plan sur une grille** : chaque salle est dessinée en miniature (sol teinté par le biome, murs, ennemis, boss, coffres). Les couloirs entre salles sont **verts** quand les portes se font face, **rouges avec une croix** quand une porte mène dans le vide ou vers une salle sans porte en face. Les salles inaccessibles depuis l'entrée ont un cadre orange.
- **Placement guidé** : quand on choisit une salle à placer, les cases où ses portes s'emboîtent avec les voisines sont surlignées en vert. Les autres gestes : clic pour sélectionner, clic sur une case vide pour déplacer, clic droit pour retirer, double-clic pour ouvrir la salle dans l'outil Salles.
- **Générateur de plans** (à la Soul Knight) : salle de départ au centre, salles dont toutes les portes sont reliées, boss en dernier, en fond d'étage. Recherche avec retour arrière et nombre d'essais limité, à partir d'une graine reproductible. En cas d'échec, l'outil explique ce qui manque. Le plan reste ensuite modifiable à la main.
- **Salles proposées** : seulement les salles **valides**, d'un **biome autorisé à cet étage**, et **pas déjà utilisées sur un autre étage**.
- **Règle `FloorRule`** : portes compatibles, salles atteignables depuis l'entrée (parcours du graphe des portes), exactement un boss, 10 salles par étage, numéros d'étage uniques, biome compatible avec l'étage, et une salle sur un seul étage. Elle signale aussi les salles placées sur aucun étage.
- `FloorGraph` regroupe la logique du graphe des portes. Elle est partagée par la validation, l'affichage du plan et le générateur, pour qu'ils ne puissent pas diverger.

### Tableau de bord : les règles du GDD
Une carte par règle de cohérence du GDD, chacune marquée respectée ou non, avec le nombre de cas et un clic vers l'outil concerné :
1. ennemis et objets cités existent ;
2. portes compatibles et salles atteignables ;
3. exactement un boss par étage ;
4. convention de nommage.

## Étape 8 : outil Dialogues

GDD : **25 PNJ, environ 150 répliques, arbres de dialogue avec conditions et conséquences**. Demande : un outil visuel en apparence de messagerie, avec un aperçu du rendu.

### Données (`NpcData`, runtime)
- Un **PNJ** = nom, rôle, portrait, couleur, étage, et son arbre : des **répliques** (qui parle, texte, suite) et les **réponses du joueur** (texte, réplique où elle mène, conditions, effets).
- **Conditions** : niveau minimum ou maximum, objet possédé ou non, marqueur d'histoire actif ou inactif, or minimum, étage minimum. **Effets** : activer ou désactiver un marqueur, donner ou reprendre un objet, de l'or, de l'XP, soigner.
- **Débuts alternatifs** : un autre accueil selon l'état du jeu (ex : quête déjà acceptée).
- Les liens sont des **identifiants attribués automatiquement** : le designer ne les saisit jamais. Supprimer une réplique redirige vers « Fin » tout ce qui y menait.
- Stockage en **ScriptableObject** comme l'impose le sujet. Pour relire hors d'Unity, un bouton **Copier le script** produit une version texte (Markdown).
- La logique d'exécution (`DialogueLogic` : début, réponses disponibles, application des effets) est dans le **runtime**. Le jeu et l'aperçu de l'outil utilisent le même code, via l'interface `IDialogueState`.

### Outil (`Forge Studio > Dialogues`), trois vues
1. **Conversation façon messagerie** :
   - bulles du PNJ à gauche avec son portrait, réponses du joueur en pastilles à droite, narration centrée ;
   - conditions (violet), effets (orange) et destination de chaque réponse en étiquettes ;
   - clic sur une pastille : saut à la suite ;
   - **barre de saisie** : on tape la réplique puis Entrée, elle est créée et reliée à la suite de la bulle sélectionnée. Une « réponse du joueur » s'ajoute comme choix, puis la saisie repasse sur le PNJ pour écrire sa réaction. On écrit donc un arbre au rythme d'une discussion ;
   - panneau de droite : qui parle, texte avec compteur de caractères, suite, réponses avec leurs conditions et effets (champ adapté à chaque type, marqueurs existants proposés pour éviter les fautes de frappe).
2. **Arbre** : une colonne par étape depuis le début, liens colorés (suite, réponse, réponse conditionnelle), boucles dessinées sous les boîtes, étiquettes DÉBUT et FIN, répliques inaccessibles encadrées en orange.
3. **Aperçu en jeu** :
   - boîte de dialogue (portrait, plaque de nom, texte qui s'écrit lettre par lettre, réponses numérotées) sur un fond aux couleurs du biome de l'étage ;
   - **joueur simulé** (niveau, or, étage, marqueurs, inventaire) pour tester chaque branche ;
   - option pour afficher les réponses verrouillées avec leur raison ;
   - **journal** des répliques, choix et effets appliqués.

### Validation (`DialogueRule`)
- Liens vers des répliques supprimées, textes vides, longueur conseillée dépassée.
- Conditions et effets incomplets, objets cités inexistants (règle du GDD « tout objet cité existe », étendue aux dialogues).
- **Marqueur testé mais activé nulle part** : la condition ne serait jamais remplie, typiquement à cause d'une faute de frappe.
- Répliques inaccessibles, conversation sans fin, réponses toutes conditionnelles (risque de blocage).
- Le tableau de bord suit l'avancement **PNJ x/25** et **répliques x/150**.

## Étape 9 : le jeu jouable

Le sujet demande un « runtime minimal » qui charge le contenu. On va plus loin : un **jeu jouable** du genre (dungeon crawler en vue du dessus, salles verrouillées pendant les combats), construit **uniquement à partir des données des outils**.

### Chargement des données
- `GameDatabase` (asset dans `Settings/`) rassemble étages, progression, PNJ, salles, couleurs de rareté et sprites communs. Elle est **remplie automatiquement** par `GameLauncher` avant chaque partie, même lancée avec le bouton Play d'Unity, et avant chaque build (`IPreprocessBuildWithReport`). Le designer n'a jamais à la tenir à jour.
- Scène `Scenes/Game.unity` : créée, ou réparée, par **Tools > Jeu > Créer ou réparer la scène de jeu**, puis ajoutée aux scènes du build.

### Construction du monde (`FloorBuilder`)
- Chaque salle est peinte dans des tilemaps (sol, murs, trous) avec les sprites de son biome.
- Chaque case du plan d'étage devient un emplacement assez grand pour la plus grande salle plus un couloir. Les salles y sont **centrées de façon à aligner leurs portes** : `FloorLayout`, couvert par un test.
- Les portes reliées sont jointes par un couloir. Une porte qui ne mène nulle part est murée, ce qui rend le jeu robuste même sur des données imparfaites.
- Les trous sont sur un calque à part : ils bloquent la marche mais pas les projectiles.

### Partie (`DungeonGame`)
- **Salles** : entrer dans une salle de combat fait apparaître ses ennemis (animation d'apparition, sans attaque immédiate) et **verrouille les portes** jusqu'à la victoire.
- **Ennemis** : une IA par comportement défini dans l'outil Ennemis :
  - corps à corps : poursuite ;
  - distance : garde ses distances et tire ;
  - charge : annonce puis fonce ;
  - invocateur : appelle des ennemis du biome ;
  - tourelle : salves en cercle.
  Les stats sont calculées pour l'étage. Les boss sont deux fois plus grands, avec des salves supplémentaires.
- **Joueur** : stats de la **progression** (PV et dégâts par niveau, XP pour monter), plus les bonus de l'équipement ramassé. L'arme équipée applique ses **effets** (poison, brûlure, étourdissement, vol de vie), avec une chance de coup critique.
- **Butin** : tables de butin des ennemis et des coffres, avec les **conditions d'apparition** (étages de la table) et les **bonus de rareté du biome**. Les objets sont équipés automatiquement s'ils sont meilleurs, et le **prix** des objets sert dans la boutique.
- **PNJ** : placés dans les salles calmes de leur étage. Leurs dialogues utilisent la **même logique que l'aperçu de l'outil** (`DialogueLogic`), appliquée à l'état réel de la partie.
- **Boss** vaincu : un portail mène à l'étage suivant ; après le dernier, c'est la victoire.
- **Interface** : barres de vie et d'XP, or, équipement, mini-carte des salles découvertes, barre du boss, chiffres de dégâts, inventaire, boîte de dialogue, écrans titre, pause, game over et victoire.

### Outils de test rapide (famille « Équilibrage et debug »)
- **▶ Jouer** dans la barre latérale de Forge Studio, **▶ Tester cet étage** dans Étages, **▶ Tester cette salle** dans Salles.
- Les données invalides sont signalées avant le lancement. Le test utilise la version enregistrée, et l'outil prévient si des modifications ne sont pas encore validées.

### Vérification
Un script de vérification, exécuté uniquement sur une **copie** du projet, a :
1. créé un petit donjon (salles de départ, combat, boutique et boss, quatre comportements d'ennemis, coffre, PNJ) ;
2. lancé une vraie partie et l'a pilotée : entrée en salle de combat, portes verrouillées, ennemis vaincus, XP et butin, coffre, dialogue, boss (qui inflige bien des dégâts), portail, victoire ;
3. pris des captures du rendu.

Aucune erreur. L'interface (dessinée en IMGUI) n'apparaît pas sur les captures de caméra : elle reste à vérifier en jouant.

## Tests

`Assets/_Project/Tests/Editor` contient **59 tests EditMode**. **Résultat actuel : 59/59.**
- Nommage : conversion des noms, préfixe des boss.
- Règles de contenu : objet manquant, bonus trop élevé, progression qui baisse, probabilités de butin.
- Salles : salle neuve correcte, ennemi hors biome, pose sur un mur refusée, entrée murée refusée, ennemi inaccessible, boss hors salle de boss, redimensionnement sans perte, salle sans biome.
- Étages : plan correct, porte dans le vide, portes incompatibles, salle inaccessible, étage sans boss ; le générateur produit un plan entièrement relié avec un seul boss, et explique un échec.
- Dialogues : identifiants et début automatiques, enchaînement de la saisie messagerie, suppression qui répare les liens, réponse conditionnelle masquée puis disponible, effets appliqués, début alternatif, marqueur jamais activé, réplique inaccessible, export texte. Construction des trois vues avec un vrai personnage et envoi d'une réplique par la barre de saisie.
- Jeu : portes alignées entre salles de tailles différentes, montée de niveau qui rend la vie, équipement du meilleur objet, défense (au moins 1 dégât), effets d'un choix de dialogue appliqués à la partie.
- Copie de travail : un objet invalide **n'est pas créé** ; un objet valide est créé avec le bon nom et au bon endroit.
- Construction de chaque outil et du tableau de bord, y compris l'éditeur de salles avec de vraies données temporaires, pour détecter les exceptions sans ouvrir Unity.
- **Vraie fenêtre Forge Studio** : ouverture de chaque section, et ajout de lignes après un ancrage simulé (régression du `NullReferenceException`).

Remarque : les tests qui ouvrent une vraie fenêtre ont besoin d'un affichage. On lance donc Unity en `-batchmode` **sans** `-nographics`.

**Limite connue de l'environnement de test** : en mode batch avec affichage, un enregistrement d'asset effectué *après* la fermeture de vraies fenêtres bloque parfois Unity (processus inactif, sans erreur). La classe `WindowLifecycleTests` est donc nommée pour s'exécuter en dernier. Avec cet ordre, deux runs complets consécutifs sont passés sans blocage. Le phénomène n'a pas été observé dans l'éditeur en usage normal.

## Prochaines étapes
- Plan des étages : assembler les salles, vérifier les portes compatibles, les salles atteignables depuis l'entrée et le boss unique par étage.
- Runtime minimal (scène Bootstrap qui charge un étage).
- Validator (fenêtre de rapport complète), Balancing, Build.
- Contenu d'échantillon produit avec les outils, mesures chronométrées, document final et vidéo.
