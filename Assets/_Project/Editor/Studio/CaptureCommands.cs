using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TheDungeon.Editor.Common;
using TheDungeon.Editor.Dialogues;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TheDungeon.Editor.Studio
{
    /// <summary>Actions de <see cref="ForgeCapture"/> sur l'interface : clics, glisser, défilement, listes, inspection de la mise en page.</summary>
    internal static class CaptureCommands
    {
        static DialogueGraphView Graph(VisualElement root) => root.Q<DialogueGraphView>() ?? throw new ArgumentException("aucun arbre affiché");

        /// <summary>« graphcard=3 » : affiche la carte de survol de la réplique n°3.</summary>
        public static void GraphCard(VisualElement root, string value)
        {
            Graph(root).PreviewCard(int.Parse(value));
            ForgeCapture.Report($"carte de la réplique #{value}");
        }

        /// <summary>« graphmove=3,40,0 » : déplace la réplique n°3 de (40, 0).</summary>
        public static void GraphMove(VisualElement root, string value)
        {
            var parts = value.Split(',');
            Graph(root).MoveNode(int.Parse(parts[0]), new Vector2(float.Parse(parts[1]), float.Parse(parts[2])));
            ForgeCapture.Report($"réplique #{parts[0]} déplacée");
        }

        /// <summary>« exportgraph=chemin.png » : exporte l'arbre affiché en PNG.</summary>
        public static void ExportGraph(VisualElement root, string value) =>
            Graph(root).ExportPng(Path.GetFullPath(value), ok => ForgeCapture.Report(ok ? $"arbre exporté : {value}" : "export de l'arbre impossible"));

        /// <summary>« pick=x,y » : élément qui recevrait un clic à ce point de la fenêtre (comme la souris), et ses parents.</summary>
        public static void Pick(VisualElement root, string value)
        {
            var parts = value.Split(',');
            var point = root.LocalToWorld(new Vector2(float.Parse(parts[0]), float.Parse(parts[1])));
            var picked = root.panel.Pick(point);
            var chain = new List<string>();
            for (var e = picked; e != null && chain.Count < 12; e = e.parent)
                chain.Add($"{e.GetType().Name}@{RuntimeHelpers.GetHashCode(e)}.{string.Join(".", e.GetClasses())} {e.worldBound}");
            ForgeCapture.Report($"pick {value} : " + string.Join("\n   < ", chain));
        }

        public static void PerformUndo()
        {
            ForgeCapture.Report($"annulation de « {Undo.GetCurrentGroupName()} »");
            Undo.PerformUndo();
        }

        /// <summary>« dumpclass=classe » : mise en page du premier élément visible de cette classe et de ses parents.</summary>
        public static void DumpClass(VisualElement root, string className)
        {
            var element = root.Query(className: className).Where(IsShown).First() ?? throw new ArgumentException($"aucun .{className}");
            for (var e = element; e != null && e != root; e = e.parent)
                ForgeCapture.Report($"{e.GetType().Name} .{string.Join(" .", e.GetClasses())} {e.layout.width:0}x{e.layout.height:0} dir={e.resolvedStyle.flexDirection} wrap={e.resolvedStyle.flexWrap} grow={e.resolvedStyle.flexGrow} w={e.style.width} min={e.resolvedStyle.minWidth} max={e.resolvedStyle.maxWidth} basis={e.resolvedStyle.flexBasis} shrink={e.resolvedStyle.flexShrink}");
        }

        /// <summary>« menu=texte » : élément d'un menu déroulant ouvert (ajouté à la racine du panneau) ; vrai clic sur son libellé.</summary>
        public static void MenuItem(VisualElement root, string value)
        {
            var tree = root.panel.visualTree;
            var label = tree.Query<Label>().Where(l => l.text != null && l.text.Contains(value) && IsShown(l)).Last()
                        ?? throw new ArgumentException($"aucun élément de menu « {value} »");
            SendPointer(label, EventType.MouseDown, label.worldBound.center);
            SendPointer(label, EventType.MouseUp, label.worldBound.center);
            ForgeCapture.Report($"menu : « {value} »");
        }

        /// <summary>Outil de test : supprime un asset créé pendant une vérification (par Unity, pour qu'il ne soit pas réécrit).</summary>
        public static void DeleteAsset(string path) =>
            ForgeCapture.Report(AssetDatabase.DeleteAsset(path) ? $"supprimé : {path}" : $"introuvable : {path}");

        public static Object FindAsset(string name) =>
            AssetDatabase.FindAssets(name, new[] { "Assets/_Project/Data" })
                .Select(guid => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(asset => asset != null && (asset.name == name || (asset is TheDungeon.Data.IDisplayNamed named && named.DisplayName == name)))
            ?? AssetDatabase.FindAssets(name, new[] { "Assets/_Project/Data" }).Select(guid => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid))).FirstOrDefault();

        /// <summary>Clique sur les boutons dont le texte (ou celui d'un libellé enfant) correspond, comme le ferait un clic de souris.</summary>
        public static void Click(VisualElement root, string text)
        {
            // « click=+ Ajouter#2 » : le 2e bouton visible portant ce texte.
            var nth = 1;
            var hash = text.LastIndexOf('#');
            if (hash > 0 && int.TryParse(text[(hash + 1)..], out var parsed))
            {
                nth = parsed;
                text = text[..hash];
            }
            // Boutons visibles d'abord : une vue masquée peut avoir un bouton de même texte (« Annuler les modifications »…).
            var buttons = root.Query<Button>().Where(b => b.text == text || b.Query<Label>().ToList().Any(l => l.text == text))
                .ToList().OrderBy(b => IsShown(b) ? 0 : 1).Skip(nth - 1).ToList();
            if (buttons.Count == 0)
            {
                // Éléments cliquables qui ne sont pas des boutons (lignes de liste, cases de la frise…) : on envoie un vrai clic.
                var label = root.Query<Label>().Where(l => l.text == text && l.resolvedStyle.display != DisplayStyle.None).First();
                if (label == null)
                    throw new ArgumentException($"aucun bouton « {text} »");
                using var click = ClickEvent.GetPooled();
                click.target = label;
                label.SendEvent(click);
                ForgeCapture.Report($"clic sur « {text} »");
                return;
            }
            var field = typeof(Clickable).GetField("clicked", BindingFlags.Instance | BindingFlags.NonPublic);
            ((Action)field?.GetValue(buttons[0].clickable))?.Invoke();
            ForgeCapture.Report($"clic sur le bouton « {text} »");
        }

        /// <summary>
        /// Démo temporaire : un panneau de conditions de chaque type (sur un personnage en mémoire, jamais enregistré),
        /// inséré en haut de la fiche à la largeur du panneau de réplique, pour vérifier son rendu. « off » le retire.
        /// </summary>
        public static void RulesDemo(VisualElement root, bool show)
        {
            root.Q("forge-capture-rules-demo")?.RemoveFromHierarchy();
            if (!show)
                return;
            var details = root.Q<ScrollView>(className: "forge-details") ?? throw new ArgumentException("aucune fiche affichée");
            var npc = ScriptableObject.CreateInstance<TheDungeon.Data.NpcData>();
            npc.hideFlags = HideFlags.DontSave;
            var serialized = new SerializedObject(npc);
            var id = DialogueEditing.AddNode(serialized, TheDungeon.Data.DialogueSpeaker.Npc, "Démo");
            DialogueEditing.AddChoice(serialized, id, "Réponse", -1, 4);
            var conditions = serialized.FindProperty("nodes.Array.data[0].choices.Array.data[0].conditions");
            var types = (TheDungeon.Data.ConditionType[])Enum.GetValues(typeof(TheDungeon.Data.ConditionType));
            conditions.arraySize = types.Length;
            for (var i = 0; i < types.Length; i++)
            {
                conditions.GetArrayElementAtIndex(i).FindPropertyRelative("type").enumValueIndex = (int)types[i];
                conditions.GetArrayElementAtIndex(i).FindPropertyRelative("value").intValue = 1;
                conditions.GetArrayElementAtIndex(i).FindPropertyRelative("flag").stringValue = "quete_acceptee";
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var demo = new VisualElement { name = "forge-capture-rules-demo" };
            demo.style.width = 380;
            demo.Add(DialogueRulesEditor.Conditions(serialized, "nodes.Array.data[0].choices.Array.data[0].conditions",
                "Conditions (démo)", string.Empty, () => new[] { "quete_acceptee" }));
            details.contentContainer.Insert(0, demo);
            details.scrollOffset = Vector2.zero;
            ForgeCapture.Report("démo des conditions affichée");
        }

        /// <summary>Fait défiler la fiche jusqu'au premier élément visible portant cette classe USS.</summary>
        public static void ScrollTo(VisualElement root, string className)
        {
            var element = root.Query<VisualElement>(className: className).Where(e => e.resolvedStyle.display != DisplayStyle.None && e.layout.height > 0).First()
                          ?? throw new ArgumentException($"aucun élément .{className}");
            var scroll = element.GetFirstAncestorOfType<ScrollView>() ?? throw new ArgumentException($".{className} n'est pas dans une zone qui défile");
            scroll.ScrollTo(element);
            ForgeCapture.Report($"défilement jusqu'à .{className}");
        }

        /// <summary>Décrit la structure (types, noms, classes USS, taille) du champ dont le libellé correspond.</summary>
        public static void Dump(VisualElement root, string label)
        {
            var field = root.Query<VisualElement>(className: "unity-base-field")
                            .Where(e => e.Q<Label>(className: "unity-base-field__label")?.text == label).First()
                        ?? throw new ArgumentException($"aucun champ « {label} »");
            void Walk(VisualElement element, int depth)
            {
                ForgeCapture.Report($"{new string(' ', depth * 2)}{element.GetType().Name} #{element.name} .{string.Join(" .", element.GetClasses())} " +
                            $"{element.layout.width:0}x{element.layout.height:0} @({element.layout.x:0},{element.layout.y:0}) display={element.resolvedStyle.display}");
                foreach (var child in element.hierarchy.Children())
                    Walk(child, depth + 1);
            }
            Walk(field, 0);
        }

        /// <summary>« drag=forge-drag-handle:0>2 » : glisse la 1re poignée de cette classe jusqu'au milieu bas de la 3e (vrais événements de pointeur).</summary>
        public static void Drag(VisualElement root, string value)
        {
            var parts = value.Split(':', '>');
            var handles = root.Query(className: parts[0]).Where(IsShown).ToList();
            var handle = handles[int.Parse(parts[1])];
            var target = handles[int.Parse(parts[2])].worldBound.center + new Vector2(0f, 4f);
            void Send(EventType type, Vector2 position)
            {
                var imgui = new Event { type = type, mousePosition = position, button = 0 };
                using EventBase evt = type switch
                {
                    EventType.MouseDown => PointerDownEvent.GetPooled(imgui),
                    EventType.MouseDrag => PointerMoveEvent.GetPooled(imgui),
                    _ => PointerUpEvent.GetPooled(imgui)
                };
                handle.SendEvent(evt);
            }
            var start = handle.worldBound.center;
            Send(EventType.MouseDown, start);
            for (var step = 1; step <= 8; step++)
                Send(EventType.MouseDrag, Vector2.Lerp(start, target, step / 8f));
            // « drag=…:0>3>hold » : le pointeur reste enfoncé, pour capturer l'état pendant le glisser.
            if (parts.Length > 3 && parts[3] == "hold")
            {
                ForgeCapture.Report($"glisser en cours de {parts[1]} vers {parts[2]}");
                return;
            }
            Send(EventType.MouseUp, target);
            ForgeCapture.Report($"glissé de {parts[1]} vers {parts[2]}");
        }

        static VisualElement pressed;

        public static void SendPointer(VisualElement target, EventType type, Vector2 position)
        {
            var imgui = new Event { type = type, mousePosition = position, button = 0 };
            using EventBase evt = type switch
            {
                EventType.MouseDown => PointerDownEvent.GetPooled(imgui),
                EventType.MouseDrag => PointerMoveEvent.GetPooled(imgui),
                _ => PointerUpEvent.GetPooled(imgui)
            };
            target.SendEvent(evt);
        }

        /// <summary>« press=classe » : enfonce le pointeur au centre du premier élément visible de cette classe (sans relâcher).</summary>
        public static void Press(VisualElement root, string className)
        {
            pressed = root.Query(className: className).Where(IsShown).First() ?? throw new ArgumentException($"aucun élément .{className}");
            SendPointer(pressed, EventType.MouseDown, pressed.worldBound.center);
            ForgeCapture.Report($"appui sur .{className}");
        }

        /// <summary>« release=texte » : relâche au-dessus du libellé qui contient ce texte (ou sur place si vide).</summary>
        public static void Release(VisualElement root, string text)
        {
            if (pressed == null)
                throw new ArgumentException("aucun appui en cours");
            var position = pressed.worldBound.center;
            if (!string.IsNullOrEmpty(text))
            {
                var label = root.Query<Label>().Where(l => IsShown(l) && l.text.Contains(text)).Last() ?? throw new ArgumentException($"aucun libellé « {text} »");
                position = label.worldBound.center;
                SendPointer(pressed, EventType.MouseDrag, position);
            }
            SendPointer(pressed, EventType.MouseUp, position);
            pressed = null;
            ForgeCapture.Report($"relâché sur « {text} »");
        }

        public static bool IsShown(VisualElement element)
        {
            for (; element != null; element = element.parent)
                if (element.resolvedStyle.display == DisplayStyle.None)
                    return false;
            return true;
        }

        /// <summary>« choose=Actif>Passif » : la première liste déroulante qui affiche « Actif » passe à « Passif », comme un choix au clic.</summary>
        public static void Choose(VisualElement root, string value)
        {
            var parts = value.Split('>');
            var field = root.Query<DropdownField>().Where(d => d.value == parts[0] && d.resolvedStyle.display != DisplayStyle.None).First()
                        ?? throw new ArgumentException($"aucune liste qui affiche « {parts[0]} »");
            field.value = parts[1];
            ForgeCapture.Report($"« {parts[0]} » → « {parts[1]} »");
        }

        /// <summary>« tests=GameLogicTests,ContentRulesTests » : lance ces classes de tests ; bilan dans Temp/ForgeCapture/tests.txt.</summary>
        public static void RunTests(string classes)
        {
            var runner = TypeCache.GetTypesDerivedFrom<object>().FirstOrDefault(t => t.FullName == "TheDungeon.Editor.Tests.CaptureTestRun")
                         ?? throw new ArgumentException("assembly de tests absente");
            runner.GetMethod("Run")!.Invoke(null, new object[] { classes });
            ForgeCapture.Report($"tests lancés : {classes}");
        }

        /// <summary>Ouvre la liste déroulante dont le libellé correspond (pour capturer son menu).</summary>
        public static void OpenDropdown(VisualElement root, string label)
        {
            var field = root.Query<VisualElement>().Where(e => e.GetType().GetMethod("ShowMenu", BindingFlags.Instance | BindingFlags.NonPublic) != null &&
                                                               e.Q<Label>(className: "unity-base-field__label")?.text == label).First()
                        ?? throw new ArgumentException($"aucune liste « {label} »");
            ForgeDropdowns.Prepare(field);
            field.GetType().GetMethod("ShowMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(field, null);
            ForgeCapture.Report($"liste « {label} » ouverte");
        }

        public static void Resize(EditorWindow window, string value)
        {
            var parts = value.Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
                throw new ArgumentException($"taille invalide : {value}");
            if (window.docked)
            {
                ForgeCapture.Report("fenêtre ancrée : taille inchangée");
                return;
            }
            window.position = new Rect(window.position.x, window.position.y, width, height);
        }

        /// <summary>Fait défiler la fiche (ScrollView de détails) d'une hauteur de page. Renvoie false s'il n'y a plus rien à afficher.</summary>
        public static bool ScrollDetails(EditorWindow window, int page)
        {
            var scroll = window.rootVisualElement.Query<ScrollView>(className: "forge-details")
                .Where(s => s.resolvedStyle.display != DisplayStyle.None && s.worldBound.height > 0).First();
            if (scroll == null)
                return page == 0;
            var max = Mathf.Max(0f, scroll.contentContainer.layout.height - scroll.contentViewport.layout.height);
            var target = page * scroll.contentViewport.layout.height * 0.9f;
            scroll.scrollOffset = new Vector2(0f, Mathf.Min(target, max));
            return page == 0 || target <= max + 1f;
        }
    }
}
