# Dialogues

Le GDD prévoit **25 PNJ et environ 150 répliques**, organisées en **arbres de dialogue avec conditions et conséquences**. Cet outil permet d'écrire ces conversations comme une messagerie, de voir l'arbre complet et de jouer chaque conversation comme en jeu.

## Créer un personnage
- **+ Nouveau** prépare un personnage avec une première réplique. Il est créé au clic sur **Valider et créer**.
- Renseignez son nom, son rôle (forgeron, marchand…), l'étage où on le rencontre, sa couleur et son portrait (galerie **Npcs**).

## Écrire comme dans une messagerie (vue Conversation)
- Choisissez qui parle dans la barre du bas : **PNJ**, **Réponse du joueur** ou **Narration**.
- Tapez la réplique et appuyez sur **Entrée**. Elle est ajoutée **à la suite de la bulle sélectionnée** et reliée automatiquement.
- **Réponse du joueur** ajoute un choix sous la réplique sélectionnée. Le mode repasse ensuite sur **PNJ** : la prochaine réplique tapée devient la réponse du PNJ à ce choix.
- Pour créer un embranchement, sélectionnez la réplique du PNJ, ajoutez plusieurs réponses du joueur, puis cliquez sur chaque réponse pour écrire sa suite.
- Cliquez sur une **pastille de réponse** pour sauter à la réplique où elle mène.

## Modifier une réplique (panneau de droite)
- **Qui parle**, **Texte** (avec compteur de caractères : au-delà de la limite, la bulle déborde en jeu), **Suite** quand il n'y a pas de réponse.
- **Réponses du joueur** : texte, réplique où elle **mène** (ou « + Nouvelle réplique du PNJ »), puis **Conditions et effets**.
- **Effets de la réplique** : appliqués dès qu'elle s'affiche (donner un objet, de l'or, de l'XP, activer un marqueur…).
- **Définir comme début**, **Supprimer**. Une réplique supprimée est remplacée par « Fin » partout où elle était citée.

## Conditions et effets
- **Conditions** (sur une réponse ou un début alternatif) : niveau minimum ou maximum, objet possédé ou non, marqueur actif ou inactif, or minimum, étage minimum.
- **Effets** : activer ou désactiver un marqueur, donner ou reprendre un objet, donner ou prendre de l'or, donner de l'XP, soigner.
- Un **marqueur d'histoire** est un mot-clé (ex : `quete_forgeron_acceptee`) activé par un effet et testé par une condition. Le bouton **▾** propose les marqueurs déjà utilisés, pour éviter les fautes de frappe.

## Débuts alternatifs
Ils sont testés dans l'ordre avant le début normal. Par exemple : si `quete_forgeron_acceptee` est actif, le forgeron commence par « Alors, ce minerai ? ».

## Vue Arbre
- Tout l'arbre d'un coup d'œil : une colonne par étape depuis le début.
- Lien gris : suite directe. Lien bleu : réponse. Lien violet : réponse avec condition. Les boucles passent sous les boîtes.
- Étiquettes **DÉBUT** et **FIN**. Cadre orange : réplique que rien ne permet d'atteindre.
- Cliquez sur une boîte pour la modifier.

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

## Relire hors d'Unity
**Copier le script** place la conversation dans le presse-papiers, au format texte (Markdown). Chaque réplique y figure avec son numéro, les réponses du joueur avec leurs conditions et leurs effets, et la suite. On peut ainsi la coller dans un document pour la relecture.
