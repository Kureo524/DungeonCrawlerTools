using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TheDungeon.Editor.Studio
{
    /// <summary>
    /// Captures de Forge Studio pilotées de l'extérieur (outil de développement).
    /// Un fichier de commande déposé dans Temp/ForgeCapture/request.txt est lu par l'éditeur, même sans le focus :
    /// il ouvre une section, clique sur des boutons, fait défiler la fiche et enregistre la fenêtre en PNG.
    /// Une commande « refresh » recompile les scripts modifiés hors de Unity.
    /// </summary>
    [InitializeOnLoad]
    public static class ForgeCapture
    {
        const string Folder = "Temp/ForgeCapture";
        const string RequestFile = Folder + "/request.txt";
        const string ResultFile = Folder + "/result.txt";
        // Commandes qui suivent un « refresh » : mises de côté pendant le rechargement des scripts, puis reprises.
        const string PendingFile = Folder + "/pending.txt";
        // Date du dernier rafraîchissement : les fichiers modifiés depuis sont réimportés explicitement.
        const string StampFile = Folder + "/refresh.stamp";
        const string ResultPrefix = "#= ";
        const double PollInterval = 0.5;
        const double StepDelay = 0.6;

        static readonly Queue<(Action action, double delay)> steps = new();
        static readonly List<string> results = new();
        static double nextTime;

        static ForgeCapture()
        {
            EditorApplication.update += Update;
        }

        [MenuItem("Tools/Capture de Forge Studio (dev)", priority = 101)]
        static void CaptureFromMenu()
        {
            var window = EditorWindow.HasOpenInstances<ForgeStudioWindow>() ? EditorWindow.GetWindow<ForgeStudioWindow>() : ForgeStudioWindow.Open(ForgeStudioWindow.Home);
            Directory.CreateDirectory(Folder);
            var path = Save(window, "manuel");
            EditorUtility.RevealInFinder(path);
        }

        static void Update()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < nextTime)
                return;
            if (steps.Count > 0)
            {
                var (action, delay) = steps.Dequeue();
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    results.Add($"erreur : {exception.Message}");
                    steps.Clear();
                }
                nextTime = now + delay;
                if (steps.Count == 0 && !File.Exists(PendingFile))
                    Finish();
                return;
            }
            nextTime = now + PollInterval;
            // Après un refresh : on reprend la suite une fois la compilation terminée (le rechargement des scripts a vidé la file).
            if (File.Exists(PendingFile) && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                (DateTime.Now - File.GetLastWriteTime(PendingFile)).TotalSeconds > 3)
                File.Move(PendingFile, RequestFile);
            if (File.Exists(RequestFile))
                Start(File.ReadAllLines(RequestFile));
        }

        /// <summary>
        /// Commandes, une par ligne (exécutées dans l'ordre) :
        /// refresh · section=&lt;id&gt; · select=&lt;nom d'asset&gt; · click=&lt;texte du bouton&gt; · size=&lt;largeur&gt;x&lt;hauteur&gt; ·
        /// wait=&lt;secondes&gt; · capture=&lt;nom&gt; · pages=&lt;nombre&gt; (la capture suivante fait défiler la fiche sur ce nombre de pages).
        /// </summary>
        static void Start(string[] lines)
        {
            File.Delete(RequestFile);
            if (File.Exists(ResultFile))
                File.Delete(ResultFile);
            results.Clear();
            results.AddRange(lines.Where(l => l.StartsWith(ResultPrefix)).Select(l => l[ResultPrefix.Length..]));
            var pages = 1;
            ForgeStudioWindow window = null;
            ForgeStudioWindow Window() => window ??= EditorWindow.HasOpenInstances<ForgeStudioWindow>()
                ? EditorWindow.GetWindow<ForgeStudioWindow>()
                : ForgeStudioWindow.Open(ForgeStudioWindow.Home);

            var commands = lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            for (var c = 0; c < commands.Count; c++)
            {
                var raw = commands[c];
                var split = raw.IndexOf('=');
                var key = split < 0 ? raw : raw[..split].Trim();
                var value = split < 0 ? string.Empty : raw[(split + 1)..].Trim();
                switch (key)
                {
                    case "refresh":
                        var rest = commands.Skip(c + 1).ToList();
                        steps.Enqueue((() => Refresh(rest), StepDelay));
                        return;

                    case "section":
                        steps.Enqueue((() => ForgeStudioWindow.Open(value), StepDelay));
                        break;
                    case "select":
                        steps.Enqueue((() =>
                        {
                            var asset = FindAsset(value) ?? throw new ArgumentException($"asset « {value} » introuvable");
                            Window().Reveal(asset);
                        }, StepDelay));
                        break;
                    case "click":
                        steps.Enqueue((() => Click(Window().rootVisualElement, value), StepDelay));
                        break;
                    case "rulesdemo":
                        steps.Enqueue((() => RulesDemo(Window().rootVisualElement, value != "off"), StepDelay));
                        break;
                    case "scrollto":
                        steps.Enqueue((() => ScrollTo(Window().rootVisualElement, value), StepDelay));
                        break;
                    case "graphcard":
                        steps.Enqueue((() =>
                        {
                            var graph = Window().rootVisualElement.Q<TheDungeon.Editor.Dialogues.DialogueGraphView>() ?? throw new ArgumentException("aucun arbre affiché");
                            graph.PreviewCard(int.Parse(value));
                            results.Add($"carte de la réplique #{value}");
                        }, StepDelay));
                        break;
                    case "graphmove":
                        steps.Enqueue((() =>
                        {
                            var graph = Window().rootVisualElement.Q<TheDungeon.Editor.Dialogues.DialogueGraphView>() ?? throw new ArgumentException("aucun arbre affiché");
                            var parts = value.Split(',');
                            graph.MoveNode(int.Parse(parts[0]), new Vector2(float.Parse(parts[1]), float.Parse(parts[2])));
                            results.Add($"réplique #{parts[0]} déplacée");
                        }, StepDelay));
                        break;
                    case "pick":
                        steps.Enqueue((() =>
                        {
                            // Élément qui recevrait un clic à ce point de la fenêtre (comme la souris), et ses parents.
                            var root = Window().rootVisualElement;
                            var parts = value.Split(',');
                            var point = root.LocalToWorld(new Vector2(float.Parse(parts[0]), float.Parse(parts[1])));
                            var picked = root.panel.Pick(point);
                            var chain = new List<string>();
                            for (var e = picked; e != null && chain.Count < 12; e = e.parent)
                                chain.Add($"{e.GetType().Name}@{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e)}.{string.Join(".", e.GetClasses())} {e.worldBound}");
                            results.Add($"pick {value} : " + string.Join("\n   < ", chain));
                        }, StepDelay));
                        break;
                    case "undo":
                        steps.Enqueue((() =>
                        {
                            results.Add($"annulation de « {Undo.GetCurrentGroupName()} »");
                            Undo.PerformUndo();
                        }, StepDelay));
                        break;
                    case "exportgraph":
                        steps.Enqueue((() =>
                        {
                            var graph = Window().rootVisualElement.Q<TheDungeon.Editor.Dialogues.DialogueGraphView>() ?? throw new ArgumentException("aucun arbre affiché");
                            graph.ExportPng(Path.GetFullPath(value), ok => results.Add(ok ? $"arbre exporté : {value}" : "export de l'arbre impossible"));
                        }, StepDelay));
                        break;
                    case "dump":
                        steps.Enqueue((() => Dump(Window().rootVisualElement, value), StepDelay));
                        break;
                    case "choose":
                        steps.Enqueue((() => Choose(Window().rootVisualElement, value), StepDelay));
                        break;
                    case "drag":
                        steps.Enqueue((() => Drag(Window().rootVisualElement, value), StepDelay));
                        break;
                    case "press":
                        steps.Enqueue((() => Press(Window().rootVisualElement, value), StepDelay));
                        break;
                    case "tap":
                        steps.Enqueue((() =>
                        {
                            Press(Window().rootVisualElement, value);
                            Release(Window().rootVisualElement, string.Empty);
                        }, StepDelay));
                        break;
                    case "release":
                        steps.Enqueue((() => Release(Window().rootVisualElement, value), StepDelay));
                        break;
                    case "dumpclass":
                        steps.Enqueue((() =>
                        {
                            var element = Window().rootVisualElement.Query(className: value).Where(IsShown).First() ?? throw new ArgumentException($"aucun .{value}");
                            for (var e = element; e != null && e != Window().rootVisualElement; e = e.parent)
                                results.Add($"{e.GetType().Name} .{string.Join(" .", e.GetClasses())} {e.layout.width:0}x{e.layout.height:0} dir={e.resolvedStyle.flexDirection} wrap={e.resolvedStyle.flexWrap} grow={e.resolvedStyle.flexGrow} w={e.style.width} min={e.resolvedStyle.minWidth} max={e.resolvedStyle.maxWidth} basis={e.resolvedStyle.flexBasis} shrink={e.resolvedStyle.flexShrink}");
                        }, StepDelay));
                        break;
                    case "menu":
                        steps.Enqueue((() =>
                        {
                            // Élément d'un menu déroulant ouvert (ajouté à la racine du panneau) : vrai clic sur son libellé.
                            var tree = Window().rootVisualElement.panel.visualTree;
                            var label = tree.Query<Label>().Where(l => l.text != null && l.text.Contains(value) && IsShown(l)).Last()
                                        ?? throw new ArgumentException($"aucun élément de menu « {value} »");
                            SendPointer(label, EventType.MouseDown, label.worldBound.center);
                            SendPointer(label, EventType.MouseUp, label.worldBound.center);
                            results.Add($"menu : « {value} »");
                        }, StepDelay));
                        break;
                    case "deleteasset":
                        // Outil de test : supprime un asset créé pendant une vérification (par Unity, pour qu'il ne soit pas réécrit).
                        steps.Enqueue((() => results.Add(AssetDatabase.DeleteAsset(value) ? $"supprimé : {value}" : $"introuvable : {value}"), StepDelay));
                        break;
                    case "tests":
                        steps.Enqueue((() => RunTests(value), StepDelay));
                        break;
                    case "dropdown":
                        steps.Enqueue((() => OpenDropdown(Window().rootVisualElement, value), StepDelay));
                        break;
                    case "size":
                        steps.Enqueue((() => Resize(Window(), value), StepDelay * 2));
                        break;
                    case "wait":
                        steps.Enqueue((() => { }, double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1.0));
                        break;
                    case "pages":
                        pages = Mathf.Clamp(int.TryParse(value, out var p) ? p : 1, 1, 10);
                        break;
                    case "capture":
                        var name = string.IsNullOrEmpty(value) ? "capture" : value;
                        var count = pages;
                        for (var page = 0; page < count; page++)
                        {
                            var index = page;
                            steps.Enqueue((() =>
                            {
                                var w = Window();
                                if (index > 0 && !ScrollDetails(w, index))
                                    return;
                                w.Repaint();
                            }, StepDelay));
                            steps.Enqueue((() => results.Add(Save(Window(), count > 1 ? $"{name}_{index + 1}" : name)), 0.1));
                        }
                        steps.Enqueue((() => ScrollDetails(Window(), 0), 0.1));
                        pages = 1;
                        break;

                    default:
                        results.Add($"commande inconnue : {raw}");
                        break;
                }
            }
            if (steps.Count == 0)
                Finish();
        }

        /// <summary>
        /// Recompile : les fichiers modifiés hors de Unity depuis le dernier rafraîchissement sont réimportés un par un
        /// (la surveillance du disque peut les manquer), puis la suite des commandes attend la fin du rechargement.
        /// </summary>
        static void Refresh(List<string> rest)
        {
            var since = File.Exists(StampFile) ? File.GetLastWriteTimeUtc(StampFile) : DateTime.UtcNow.AddMinutes(-30);
            File.WriteAllText(StampFile, DateTime.Now.ToString("O"));
            var changed = Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta") && File.GetLastWriteTimeUtc(path) > since)
                .Select(path => path.Replace('\\', '/'))
                .ToList();
            foreach (var path in changed)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            results.Add($"rafraîchi : {changed.Count} fichier(s) réimporté(s)");
            File.WriteAllLines(PendingFile, results.Select(r => ResultPrefix + r).Concat(rest));
        }

        static void Finish()

        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(ResultFile, results.Append("terminé"));
        }

        static Object FindAsset(string name) =>
            AssetDatabase.FindAssets(name, new[] { "Assets/_Project/Data" })
                .Select(guid => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(asset => asset != null && (asset.name == name || (asset is TheDungeon.Data.IDisplayNamed named && named.DisplayName == name)))
            ?? AssetDatabase.FindAssets(name, new[] { "Assets/_Project/Data" }).Select(guid => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid))).FirstOrDefault();

        /// <summary>Clique sur les boutons dont le texte (ou celui d'un libellé enfant) correspond, comme le ferait un clic de souris.</summary>
        static void Click(VisualElement root, string text)
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
                results.Add($"clic sur « {text} »");
                return;
            }
            var field = typeof(Clickable).GetField("clicked", BindingFlags.Instance | BindingFlags.NonPublic);
            ((Action)field?.GetValue(buttons[0].clickable))?.Invoke();
            results.Add($"clic sur le bouton « {text} »");
        }

        /// <summary>
        /// Démo temporaire : un panneau de conditions de chaque type (sur un personnage en mémoire, jamais enregistré),
        /// inséré en haut de la fiche à la largeur du panneau de réplique, pour vérifier son rendu. « off » le retire.
        /// </summary>
        static void RulesDemo(VisualElement root, bool show)
        {
            root.Q("forge-capture-rules-demo")?.RemoveFromHierarchy();
            if (!show)
                return;
            var details = root.Q<ScrollView>(className: "forge-details") ?? throw new ArgumentException("aucune fiche affichée");
            var npc = ScriptableObject.CreateInstance<TheDungeon.Data.NpcData>();
            npc.hideFlags = HideFlags.DontSave;
            var serialized = new SerializedObject(npc);
            var id = TheDungeon.Editor.Dialogues.DialogueEditing.AddNode(serialized, TheDungeon.Data.DialogueSpeaker.Npc, "Démo");
            TheDungeon.Editor.Dialogues.DialogueEditing.AddChoice(serialized, id, "Réponse", -1, 4);
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
            demo.Add(TheDungeon.Editor.Dialogues.DialogueRulesEditor.Conditions(serialized, "nodes.Array.data[0].choices.Array.data[0].conditions",
                "Conditions (démo)", string.Empty, () => new[] { "quete_acceptee" }));
            details.contentContainer.Insert(0, demo);
            details.scrollOffset = Vector2.zero;
            results.Add("démo des conditions affichée");
        }

        /// <summary>Fait défiler la fiche jusqu'au premier élément visible portant cette classe USS.</summary>
        static void ScrollTo(VisualElement root, string className)
        {
            var element = root.Query<VisualElement>(className: className).Where(e => e.resolvedStyle.display != DisplayStyle.None && e.layout.height > 0).First()
                          ?? throw new ArgumentException($"aucun élément .{className}");
            var scroll = element.GetFirstAncestorOfType<ScrollView>() ?? throw new ArgumentException($".{className} n'est pas dans une zone qui défile");
            scroll.ScrollTo(element);
            results.Add($"défilement jusqu'à .{className}");
        }

        /// <summary>Décrit la structure (types, noms, classes USS, taille) du champ dont le libellé correspond.</summary>
        static void Dump(VisualElement root, string label)
        {
            var field = root.Query<VisualElement>(className: "unity-base-field")
                            .Where(e => e.Q<Label>(className: "unity-base-field__label")?.text == label).First()
                        ?? throw new ArgumentException($"aucun champ « {label} »");
            void Walk(VisualElement element, int depth)
            {
                results.Add($"{new string(' ', depth * 2)}{element.GetType().Name} #{element.name} .{string.Join(" .", element.GetClasses())} " +
                            $"{element.layout.width:0}x{element.layout.height:0} @({element.layout.x:0},{element.layout.y:0}) display={element.resolvedStyle.display}");
                foreach (var child in element.hierarchy.Children())
                    Walk(child, depth + 1);
            }
            Walk(field, 0);
        }

        /// <summary>Ouvre la liste déroulante dont le libellé correspond (pour capturer son menu).</summary>

        /// <summary>« drag=forge-drag-handle:0>2 » : glisse la 1re poignée de cette classe jusqu'au milieu bas de la 3e (vrais événements de pointeur).</summary>
        static void Drag(VisualElement root, string value)
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
                results.Add($"glisser en cours de {parts[1]} vers {parts[2]}");
                return;
            }
            Send(EventType.MouseUp, target);
            results.Add($"glissé de {parts[1]} vers {parts[2]}");
        }

        static VisualElement pressed;

        static void SendPointer(VisualElement target, EventType type, Vector2 position)
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
        static void Press(VisualElement root, string className)
        {
            pressed = root.Query(className: className).Where(IsShown).First() ?? throw new ArgumentException($"aucun élément .{className}");
            SendPointer(pressed, EventType.MouseDown, pressed.worldBound.center);
            results.Add($"appui sur .{className}");
        }

        /// <summary>« release=texte » : relâche au-dessus du libellé qui contient ce texte (ou sur place si vide).</summary>
        static void Release(VisualElement root, string text)
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
            results.Add($"relâché sur « {text} »");
        }

        static bool IsShown(VisualElement element)
        {
            for (; element != null; element = element.parent)
                if (element.resolvedStyle.display == DisplayStyle.None)
                    return false;
            return true;
        }

        /// <summary>« choose=Actif>Passif » : la première liste déroulante qui affiche « Actif » passe à « Passif », comme un choix au clic.</summary>
        static void Choose(VisualElement root, string value)
        {
            var parts = value.Split('>');
            var field = root.Query<DropdownField>().Where(d => d.value == parts[0] && d.resolvedStyle.display != DisplayStyle.None).First()
                        ?? throw new ArgumentException($"aucune liste qui affiche « {parts[0]} »");
            field.value = parts[1];
            results.Add($"« {parts[0]} » → « {parts[1]} »");
        }

        /// <summary>« tests=GameLogicTests,ContentRulesTests » : lance ces classes de tests ; bilan dans Temp/ForgeCapture/tests.txt.</summary>
        static void RunTests(string classes)
        {
            var runner = TypeCache.GetTypesDerivedFrom<object>().FirstOrDefault(t => t.FullName == "TheDungeon.Editor.Tests.CaptureTestRun")
                         ?? throw new ArgumentException("assembly de tests absente");
            runner.GetMethod("Run")!.Invoke(null, new object[] { classes });
            results.Add($"tests lancés : {classes}");
        }

        static void OpenDropdown(VisualElement root, string label)
        {
            var field = root.Query<VisualElement>().Where(e => e.GetType().GetMethod("ShowMenu", BindingFlags.Instance | BindingFlags.NonPublic) != null &&
                                                               e.Q<Label>(className: "unity-base-field__label")?.text == label).First()
                        ?? throw new ArgumentException($"aucune liste « {label} »");
            TheDungeon.Editor.Common.ForgeDropdowns.Prepare(field);
            field.GetType().GetMethod("ShowMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(field, null);
            results.Add($"liste « {label} » ouverte");
        }

        static void Resize(EditorWindow window, string value)

        {
            var parts = value.Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
                throw new ArgumentException($"taille invalide : {value}");
            if (window.docked)
            {
                results.Add("fenêtre ancrée : taille inchangée");
                return;
            }
            window.position = new Rect(window.position.x, window.position.y, width, height);
        }

        /// <summary>Fait défiler la fiche (ScrollView de détails) d'une hauteur de page. Renvoie false s'il n'y a plus rien à afficher.</summary>
        static bool ScrollDetails(EditorWindow window, int page)
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

        static string Save(EditorWindow window, string name)
        {
            Directory.CreateDirectory(Folder);
            var texture = Grab(window);
            var path = Path.Combine(Folder, $"{name}.png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            return Path.GetFullPath(path);
        }

        /// <summary>
        /// Rendu de la fenêtre. D'abord par l'éditeur lui-même (GUIView.GrabPixels, fonctionne même fenêtre masquée),
        /// sinon en lisant les pixels de l'écran (la fenêtre doit alors être visible).
        /// </summary>
        static Texture2D Grab(EditorWindow window)
        {
            var scale = EditorGUIUtility.pixelsPerPoint;
            var width = Mathf.Max(1, Mathf.RoundToInt(window.position.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(window.position.height * scale));

            var parent = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
            var grab = parent?.GetType().GetMethod("GrabPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(RenderTexture), typeof(Rect) }, null);
            if (grab != null)
            {
                var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                try
                {
                    window.Repaint();
                    grab.Invoke(parent, new object[] { target, new Rect(0, 0, width, height) });
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    texture.Apply();
                    RenderTexture.active = previous;
                    if (!IsBlank(texture))
                        return texture;
                    Object.DestroyImmediate(texture);
                    results.Add("GrabPixels a rendu une image vide : lecture de l'écran à la place");
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(target);
                }
            }

            var pixels = InternalEditorUtility.ReadScreenPixel(window.position.position, (int)window.position.width, (int)window.position.height);
            var screen = new Texture2D((int)window.position.width, (int)window.position.height, TextureFormat.RGBA32, false);
            screen.SetPixels(pixels);
            screen.Apply();
            return screen;
        }

        static bool IsBlank(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var first = pixels[0];
            for (var i = 0; i < pixels.Length; i += 97)
                if (pixels[i].r != first.r || pixels[i].g != first.g || pixels[i].b != first.b)
                    return false;
            return true;
        }
    }
}
