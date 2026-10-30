using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace TheDungeon.Editor.Tests
{
    /// <summary>
    /// Outil de développement : lance des tests EditMode depuis ForgeCapture (commande « tests=Classe1,Classe2 »)
    /// dans l'éditeur ouvert, sans le Test Runner, et écrit le bilan dans Temp/ForgeCapture/tests.txt.
    /// </summary>
    public static class CaptureTestRun
    {
        const string ResultFile = "Temp/ForgeCapture/tests.txt";

        public static void Run(string classes)
        {
            File.Delete(ResultFile);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            var groups = classes.Split(',').Select(name => $"^TheDungeon\\.Editor\\.Tests\\.{name.Trim()}\\.").ToArray();
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, groupNames = groups }));
        }

        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var report = new StringBuilder();
                report.AppendLine($"réussis {result.PassCount} · échoués {result.FailCount} · ignorés {result.SkipCount}");
                Collect(result, report);
                File.WriteAllText(ResultFile, report.ToString());
            }

            static void Collect(ITestResultAdaptor result, StringBuilder report)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
                    report.AppendLine($"ÉCHEC {result.FullName}\n  {result.Message?.Trim()}");
                foreach (var child in result.Children)
                    Collect(child, report);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}
