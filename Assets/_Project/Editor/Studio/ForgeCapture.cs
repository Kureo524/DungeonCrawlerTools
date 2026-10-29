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
                    case "dump":
                        steps.Enqueue((() => Dump(Window().rootVisualElement, value), StepDelay));
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
            var buttons = root.Query<Button>().Where(b => b.text == text || b.Query<Label>().ToList().Any(l => l.text == text)).ToList();
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

        /// <summary>Décrit la structure (types, noms, classes USS, taille) du champ dont le libellé correspond.</summary>
        static void Dump(VisualElement root, string label)
        {
            var field = root.Query<VisualElement>(className: "unity-base-field")
                            .Where(e => e.Q<Label>(className: "unity-base-field__label")?.text == label).First()
                        ?? throw new ArgumentException($"aucun champ « {label} »");
            void Walk(VisualElement element, int depth)
            {
                results.Add($"{new string(' ', depth * 2)}{element.GetType().Name} #{element.name} .{string.Join(" .", element.GetClasses())} " +
                            $"{element.layout.width:0}x{element.layout.height:0} display={element.resolvedStyle.display}");
                foreach (var child in element.hierarchy.Children())
                    Walk(child, depth + 1);
            }
            Walk(field, 0);
        }

        /// <summary>Ouvre la liste déroulante dont le libellé correspond (pour capturer son menu).</summary>

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
