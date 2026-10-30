# Dialogues

Le GDD prévoit **25 PNJ et environ 150 répliques**, organisées en **arbres de dialogue avec conditions et conséquences**. Cet outil permet d'écrire ces conversations comme une messagerie, de voir l'arbre complet et de jouer chaque conversation comme en jeu.

## Créer un personnage
- **+ Nouveau** prépare un personnage avec une première réplique. Il est créé au clic sur **Valider et créer**.
- Renseignez son nom, son rôle (forgeron, marchand…) et son portrait (galerie **Npcs**).
- **Étages** : cochez un ou plusieurs étages où on peut le rencontrer (par exemple les étages 1 et 3, sans le 2). En jeu, il apparaît dans une salle de PNJ de l'étage, sinon dans une salle calme (départ, boutique, trésor).
- Pour le mettre dans une salle précise, ou le faire apparaître une fois une salle de combat nettoyée, posez-le avec l'outil **Salles** (groupe **Personnages** de la palette).

## Apparence de la boîte de dialogue
- Couleurs propres au personnage : **plaque de nom** et son texte, plaque du joueur, **fond**, **contour** et **texte** de la boîte, **fond**, **contour** et **texte** des réponses du joueur.
- L'aperçu à côté des couleurs montre le résultat. Ces couleurs s'appliquent en jeu, dans l'aperçu en jeu et dans la vue Conversation.
- **Couleurs par défaut** remet les couleurs d'origine (la plaque de nom est conservée).
- Une **réponse** peut avoir ses propres couleurs : dans le panneau de la réplique, dépliez **Apparence** sous la réponse et cochez **Couleurs propres**. Elle part des couleurs du personnage.

## Se repérer dans la fiche
- Chaque partie (Personnage, Apparence, listes…) se **replie** d'un clic sur son titre, pour ne garder ouvert que ce sur quoi on travaille. Le choix est gardé pendant la session.
- **Ctrl+Z** annule la dernière modification (texte, lien, déplacement dans l'arbre…), **Ctrl+Y** la refait. Tant que la fiche n'est pas validée, **Annuler les modifications** revient à la dernière version enregistrée.

## Écrire comme dans une messagerie (vue Conversation)
- Choisissez qui parle dans la barre du bas : **PNJ**, **Réponse du joueur** ou **Narration**.
- Tapez la réplique et appuyez sur **Entrée**. Elle est ajoutée **à la suite de la bulle sélectionnée** et reliée automatiquement.
- **Réponse du joueur** ajoute un choix sous la réplique sélectionnée. Le mode repasse ensuite sur **PNJ** : la prochaine réplique tapée devient la réponse du PNJ à ce choix.
- Pour créer un embranchement, sélectionnez la réplique du PNJ, ajoutez plusieurs réponses du joueur, puis cliquez sur chaque réponse pour écrire sa suite.
- Cliquez sur une **pastille de réponse** pour sauter à la réplique où elle mène.

## Modifier une réplique (panneau de droite)
- **Qui parle**, **Texte** (avec compteur de caractères : au-delà de la limite, la bulle déborde en jeu), et **Suite**, affichée seulement quand la réplique n'a pas de réponse du joueur (sinon chaque réponse mène à sa propre suite).
- **Réponses du joueur** : texte, réplique où elle **mène** (ou « + Nouvelle réplique du PNJ »), puis **Conditions et effets**.
- **Effets de la réplique** : appliqués dès qu'elle s'affiche (donner un objet, de l'or, de l'XP, activer un marqueur…).
- **Définir comme début**, **Supprimer**. Une réplique supprimée est remplacée par « Fin » partout où elle était citée.

## Conditions et effets
- **Conditions** (sur une réponse ou un début alternatif) : niveau minimum ou maximum, objet possédé ou non, marqueur actif ou inactif, or minimum, étage minimum, **étages précis** (cochez les étages où la réponse est proposée, par exemple seulement aux étages 1 et 3), **mécanique** (le joueur possède une mécanique de la progression utilisée en jeu, au moins à tel niveau : 1 = débloquée).
- Dans l'aperçu en jeu, le **niveau** du joueur simulé va jusqu'au niveau maximum de la progression utilisée en jeu, et chaque mécanique citée par une condition a son curseur de niveau.
- **Effets** : activer ou désactiver un marqueur, donner ou reprendre un objet, donner ou prendre de l'or, donner de l'XP, soigner.
- Un **marqueur d'histoire** est un mot-clé (ex : `quete_forgeron_acceptee`) activé par un effet et testé par une condition. Le bouton **▾** propose les marqueurs déjà utilisés, pour éviter les fautes de frappe.

## Débuts alternatifs
Ils sont testés dans l'ordre avant le début normal. Par exemple : si `quete_forgeron_acceptee` est actif, le forgeron commence par « Alors, ce minerai ? ».

## Vue Arbre
- L'arbre se lit de haut en bas : une ligne par étape depuis le début. La réplique sélectionnée se modifie dans le panneau de droite.
- **Se déplacer** : molette pour zoomer (vers la souris), glisser le fond pour déplacer la vue. **Recentrer** (ou **F**) affiche tout l'arbre.
- **Ranger** : glissez une réplique pour la déplacer, comme un blueprint. Dès qu'une réplique est déplacée, l'arbre garde vos positions. **Réorganiser** revient au rangement automatique.
- **Changer le cours de la conversation** : chaque réponse du joueur a un rond **à gauche et à droite**, et une réplique sans réponse a un rond en bas. Glissez un rond sur une autre réplique pour la relier. Lâchez-le dans le vide pour créer la réplique suivante à cet endroit.
- Une réponse n'est reliée que d'**un seul côté** à la fois : tirer le lien depuis l'autre rond le déplace de ce côté (le rond inutilisé est estompé).
- **Ctrl+clic sur un lien** le retire sans supprimer de réplique (le lien survolé s'allume en rouge quand Ctrl est enfoncé). **Ctrl+clic**, **Alt+clic** ou **clic droit** sur un rond fait de même.
- **Clic droit** sur une réplique : modifier, ajouter une réponse, définir comme début, retirer la suite, supprimer. **Suppr** supprime la réplique sélectionnée. Clic droit sur le fond : nouvelle réplique à cet endroit.
- **Survolez une réplique** pour lire son texte complet, ses effets et chacune de ses réponses avec leurs conditions, leurs effets et leur destination.
- Lien gris : suite directe. Lien bleu : réponse. Lien violet : réponse avec condition. Étiquettes **DÉBUT** et **FIN**. Cadre orange : réplique que rien ne permet d'atteindre.
- **Exporter en PNG** enregistre une image de l'arbre complet, à taille réelle, même s'il dépasse de l'écran.

## Aperçu en jeu
- La conversation se joue comme en jeu : portrait, plaque de nom, texte qui s'écrit, réponses à choisir.
- Cliquez sur la boîte pour afficher tout le texte d'un coup, puis pour continuer.
- **Joueur simulé** : réglez le niveau, l'or, l'étage, cochez des marqueurs et des objets pour tester chaque branche.
- **Montrer les réponses verrouillées** affiche aussi, grisées, les réponses dont les conditions ne sont pas remplies, avec la raison.
- Le **journal** liste chaque réplique, chaque choix et chaque effet appliqué.

## Ce que vérifie la validation
- Liens vers des répliques existantes, textes non vides, longueur conseillée.
- Conditions et effets complets (objet, marqueur, valeur), objets cités existants.
- Marqueur testé mais activé nulle part (la condition ne serait jamais remplie).
- Répliques inaccessibles, conversation sans fin, réponses toutes conditionnelles (risque de blocage).

## Exporter et importer une discussion
- **Exporter la discussion…** enregistre tout le personnage dans un fichier JSON : nom, apparence, étages, répliques, réponses, conditions, effets et positions dans l'arbre.
- **Importer une discussion…** ouvre un tel fichier comme un **nouveau personnage**, à vérifier puis à valider. Le portrait et les objets cités sont retrouvés s'ils existent dans le projet.

## Relire hors d'Unity
**Copier le script** place la conversation dans le presse-papiers, au format texte (Markdown). Chaque réplique y figure avec son numéro, les réponses du joueur avec leurs conditions et leurs effets, et la suite. On peut ainsi la coller dans un document pour la relecture.
