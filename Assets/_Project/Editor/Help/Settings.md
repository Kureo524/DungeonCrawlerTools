# Limites (dev)

Réservé aux **développeurs**. Ces limites encadrent ce que les designers peuvent saisir dans les outils.

## À quoi servent les limites
- Les curseurs des outils sont bornés par ces valeurs (PV, dégâts, prix, taille des salles…).
- Le nombre de lignes des listes est plafonné (bonus par objet, objets par table, ennemis par salle).
- Le **Validator** s'en sert comme référence pour signaler les données hors limites.

## Raretés
- Chaque rareté définit sa **couleur** (reprise partout dans les outils), son **bonus maximum**, sa **fourchette de prix** et son **poids de butin par défaut**.

## Bonnes pratiques
- Modifiez une limite seulement après accord de l'équipe : les données existantes hors limites seront signalées comme erreurs.
- L'asset est stocké dans `Assets/_Project/Settings/ForgeDesignLimits.asset` et versionné avec le projet.
