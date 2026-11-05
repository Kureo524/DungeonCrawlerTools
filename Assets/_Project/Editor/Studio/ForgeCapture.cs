using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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
        internal const string Folder = "Temp/ForgeCapture";
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
            var path = WindowGrab.Save(window, "manuel");
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
                    case "size":
                        steps.Enqueue((() => CaptureCommands.Resize(Window(), value), StepDelay * 2));
                        break;
                    case "wait":
                        steps.Enqueue((() => { }, double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1.0));
                        break;
                    case "pages":
                        pages = Mathf.Clamp(int.TryParse(value, out var p) ? p : 1, 1, 10);
                        break;
                    case "capture":
                        EnqueueCapture(string.IsNullOrEmpty(value) ? "capture" : value, pages, Window);
                        pages = 1;
                        break;
                    default:
                        var action = Command(key, value, () => Window().rootVisualElement, Window);
                        if (action != null)
                            steps.Enqueue((action, StepDelay));
                        else
                            results.Add($"commande inconnue : {raw}");
                        break;
                }
            }
            if (steps.Count == 0)
                Finish();
        }

        /// <summary>Action d'une commande d'interface (exécutée plus tard, une étape à la fois), ou null si la commande est inconnue.</summary>
        static Action Command(string key, string value, Func<VisualElement> root, Func<ForgeStudioWindow> window) => key switch
        {
            "section" => () => ForgeStudioWindow.Open(value),
            "select" => () => window().Reveal(CaptureCommands.FindAsset(value) ?? throw new ArgumentException($"asset « {value} » introuvable")),
            "click" => () => CaptureCommands.Click(root(), value),
            "rulesdemo" => () => CaptureCommands.RulesDemo(root(), value != "off"),
            "scrollto" => () => CaptureCommands.ScrollTo(root(), value),
            "graphcard" => () => CaptureCommands.GraphCard(root(), value),
            "graphmove" => () => CaptureCommands.GraphMove(root(), value),
            "pick" => () => CaptureCommands.Pick(root(), value),
            "undo" => CaptureCommands.PerformUndo,
            "exportgraph" => () => CaptureCommands.ExportGraph(root(), value),
            "dump" => () => CaptureCommands.Dump(root(), value),
            "choose" => () => CaptureCommands.Choose(root(), value),
            "drag" => () => CaptureCommands.Drag(root(), value),
            "press" => () => CaptureCommands.Press(root(), value),
            "tap" => () =>
            {
                CaptureCommands.Press(root(), value);
                CaptureCommands.Release(root(), string.Empty);
            },
            "release" => () => CaptureCommands.Release(root(), value),
            "dumpclass" => () => CaptureCommands.DumpClass(root(), value),
            "menu" => () => CaptureCommands.MenuItem(root(), value),
            "deleteasset" => () => CaptureCommands.DeleteAsset(value),
            "tests" => () => CaptureCommands.RunTests(value),
            "dropdown" => () => CaptureCommands.OpenDropdown(root(), value),
            _ => null
        };

        /// <summary>Une image par page : la fiche défile d'une page entre deux images, puis revient en haut.</summary>
        static void EnqueueCapture(string name, int count, Func<ForgeStudioWindow> window)
        {
            for (var page = 0; page < count; page++)
            {
                var index = page;
                steps.Enqueue((() =>
                {
                    var w = window();
                    if (index > 0 && !CaptureCommands.ScrollDetails(w, index))
                        return;
                    w.Repaint();
                }, StepDelay));
                steps.Enqueue((() => results.Add(WindowGrab.Save(window(), count > 1 ? $"{name}_{index + 1}" : name)), 0.1));
            }
            steps.Enqueue((() => CaptureCommands.ScrollDetails(window(), 0), 0.1));
        }

        /// <summary>Ajoute une ligne au compte rendu de la commande en cours.</summary>
        internal static void Report(string line) => results.Add(line);

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
    }
}
