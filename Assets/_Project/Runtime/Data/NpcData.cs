using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDungeon.Data
{
    public enum DialogueSpeaker
    {
        Npc,
        Player,
        Narrator
    }

    public enum ConditionType
    {
        PlayerLevelAtLeast,
        PlayerLevelBelow,
        HasItem,
        LacksItem,
        FlagSet,
        FlagNotSet,
        GoldAtLeast,
        FloorAtLeast,
        FloorIn,
        MechanicAtLeast
    }

    public enum ConsequenceType
    {
        SetFlag,
        ClearFlag,
        GiveItem,
        TakeItem,
        GiveGold,
        TakeGold,
        GiveXp,
        Heal
    }

    /// <summary>Condition à remplir (niveau, objet possédé, marqueur d'histoire…).</summary>
    [Serializable]
    public class DialogueCondition
    {
        [Tooltip("Ce qui est vérifié.")]
        public ConditionType type;
        [Tooltip("Valeur numérique (niveau, or, étage). Pour « Étages précis » : les étages cochés (voir FloorSet).")]
        public int value = 1;
        [Tooltip("Nom du marqueur d'histoire (ex : quete_forgeron_acceptee). Pour « Mécanique » : identifiant de la mécanique.")]
        public string flag;
        [Tooltip("Objet concerné.")]
        public ItemData item;
    }

    /// <summary>Effet appliqué quand la réplique est dite ou le choix fait.</summary>
    [Serializable]
    public class DialogueConsequence
    {
        [Tooltip("Effet appliqué.")]
        public ConsequenceType type;
        [Tooltip("Valeur numérique (or, XP, PV).")]
        public int value = 10;
        [Tooltip("Nom du marqueur d'histoire.")]
        public string flag;
        [Tooltip("Objet donné ou repris.")]
        public ItemData item;
    }

    /// <summary>Réponse proposée au joueur.</summary>
    [Serializable]
    public class DialogueChoice
    {
        [Tooltip("Texte de la réponse du joueur.")]
        public string text;
        [Tooltip("Réplique suivante (identifiant), -1 = fin de la conversation.")]
        public int target = -1;
        [Tooltip("La réponse n'est proposée que si toutes ces conditions sont remplies.")]
        public List<DialogueCondition> conditions = new();
        [Tooltip("Effets appliqués quand le joueur choisit cette réponse.")]
        public List<DialogueConsequence> consequences = new();
        [Tooltip("La réponse a ses propres couleurs au lieu de celles du personnage.")]
        public bool customLook;
        [Tooltip("Fond de cette réponse (si elle a ses propres couleurs).")]
        public Color background = new(0.1f, 0.09f, 0.12f);
        [Tooltip("Contour de cette réponse (si elle a ses propres couleurs).")]
        public Color border = new(0.78f, 0.7f, 0.55f, 0.45f);
        [Tooltip("Texte de cette réponse (si elle a ses propres couleurs).")]
        public Color textColor = new(0.94f, 0.92f, 0.89f);
        [Tooltip("Dans l'arbre de l'outil Dialogues, le lien part du côté gauche de la réponse (sans effet en jeu).")]
        public bool linkFromLeft;
    }

    /// <summary>Une réplique de l'arbre de dialogue.</summary>
    [Serializable]
    public class DialogueNode
    {
        [Tooltip("Identifiant unique dans la conversation (attribué automatiquement).")]
        public int id;
        [Tooltip("Qui parle.")]
        public DialogueSpeaker speaker;
        [Tooltip("Texte de la réplique.")]
        public string text;
        [Tooltip("Réplique suivante quand il n'y a pas de choix, -1 = fin.")]
        public int next = -1;
        [Tooltip("Réponses proposées au joueur après cette réplique.")]
        public List<DialogueChoice> choices = new();
        [Tooltip("Effets appliqués quand la réplique est affichée.")]
        public List<DialogueConsequence> consequences = new();
        [Tooltip("Position de la réplique dans l'arbre de l'outil Dialogues (sans effet en jeu).")]
        public Vector2 graphPosition;
        [Tooltip("La réplique a été déplacée à la main dans l'arbre ; sinon elle est rangée automatiquement.")]
        public bool placed;
    }

    /// <summary>Couleurs de la boîte de dialogue de ce personnage, en jeu et dans l'aperçu.</summary>
    [Serializable]
    public class DialogueStyle
    {
        [Tooltip("Fond de la boîte de dialogue.")]
        public Color boxBackground = new(0.05f, 0.04f, 0.07f, 0.94f);
        [Tooltip("Contour de la boîte de dialogue et du portrait.")]
        public Color boxBorder = new(0.78f, 0.7f, 0.55f);
        [Tooltip("Texte des répliques.")]
        public Color text = new(0.94f, 0.92f, 0.89f);
        [Tooltip("Texte de la plaque de nom.")]
        public Color nameText = Color.white;
        [Tooltip("Plaque de nom quand c'est le joueur qui parle.")]
        public Color playerPlate = new(0.3f, 0.6f, 0.4f);
        [Tooltip("Fond des réponses du joueur.")]
        public Color choiceBackground = new(0.1f, 0.09f, 0.12f);
        [Tooltip("Contour des réponses du joueur.")]
        public Color choiceBorder = new(0.78f, 0.7f, 0.55f, 0.45f);
        [Tooltip("Texte des réponses du joueur.")]
        public Color choiceText = new(0.94f, 0.92f, 0.89f);

        /// <summary>Couleurs d'une réponse : les siennes si elle en a, sinon celles du personnage.</summary>
        public (Color background, Color border, Color text) ChoiceLook(DialogueChoice choice) =>
            choice != null && choice.customLook ? (choice.background, choice.border, choice.textColor) : (choiceBackground, choiceBorder, choiceText);
    }

    /// <summary>Début alternatif : utilisé à la place du début normal si ses conditions sont remplies (ex : quête déjà acceptée).</summary>
    [Serializable]
    public class DialogueEntry
    {
        [Tooltip("Conditions pour utiliser ce début.")]
        public List<DialogueCondition> conditions = new();
        [Tooltip("Première réplique de ce début.")]
        public int node = -1;
    }

    /// <summary>Personnage non joueur et son arbre de dialogue.</summary>
    [CreateAssetMenu(menuName = "The Dungeon/NPC", fileName = "NPC_NewNpc")]
    public class NpcData : ScriptableObject, IDisplayNamed
    {
        [SerializeField, Tooltip("Nom affiché dans la boîte de dialogue.")]
        string displayName;
        [SerializeField, Tooltip("Rôle ou titre (ex : forgeron, marchand).")]
        string role;
        [SerializeField, Tooltip("Portrait affiché à côté des répliques.")]
        Sprite portrait;
        [SerializeField, Tooltip("Couleur des bulles et de la plaque de nom.")]
        Color color = new(0.35f, 0.55f, 0.85f);
        [SerializeField, Tooltip("Couleurs de la boîte de dialogue.")]
        DialogueStyle style = new();
        [SerializeField, Tooltip("Étages où l'on peut rencontrer ce personnage (voir FloorSet).")]
        int floors;
        [SerializeField, HideInInspector, Tooltip("Ancien réglage : un seul étage. Utilisé tant que « floors » est vide.")]
        int floor = 1;
        [SerializeField, Tooltip("Première réplique quand aucun début alternatif ne s'applique.")]
        int startNode = -1;
        [SerializeField, Tooltip("Débuts alternatifs, testés dans l'ordre.")]
        List<DialogueEntry> entries = new();
        [SerializeField, Tooltip("Toutes les répliques de la conversation.")]
        List<DialogueNode> nodes = new();
        [SerializeField, HideInInspector]
        int nextId;

        public string DisplayName => displayName;
        public string Role => role;
        public Sprite Portrait => portrait;
        public Color Color => color;
        public DialogueStyle Style => style ??= new DialogueStyle();
        /// <summary>Étages où le personnage apparaît (voir FloorSet).</summary>
        public int Floors => floors != 0 ? floors : FloorSet.Of(floor);
        /// <summary>Premier de ses étages (fond de l'aperçu, filtres).</summary>
        public int Floor => FloorSet.First(Floors);
        public bool AppearsOn(int floorNumber) => FloorSet.Contains(Floors, floorNumber);
        public int StartNode => startNode;
        public IReadOnlyList<DialogueEntry> Entries => entries;
        public IReadOnlyList<DialogueNode> Nodes => nodes;

        public DialogueNode FindNode(int id)
        {
            foreach (var node in nodes)
                if (node.id == id)
                    return node;
            return null;
        }

        /// <summary>Nombre de répliques au sens du GDD : répliques du dialogue + réponses du joueur.</summary>
        public int LineCount
        {
            get
            {
                var count = nodes.Count;
                foreach (var node in nodes)
                    count += node.choices.Count;
                return count;
            }
        }
    }
}
