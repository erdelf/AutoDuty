using AutoDuty;
using AutoDuty.Helpers;
using AutoDuty.Updater;
using AutoDuty.Windows;
using System.Diagnostics;
var directory = Plugin.pathsDirectory;
directory.Create();
try
{
    string first = Path.Combine(directory.FullName, "(1)route.json");
    File.WriteAllText(first, "{}");
    FileHelper.Init();
    Check(MainTab.Count == 1, "initial catalog published");
    var original = ContentPathsManager.DictionaryPaths;
    for (int i=0; i<20; i++) { File.ReadAllText(first); FileHelper.CalculateMD5(first); }
    Pump(700);
    Check(ReferenceEquals(original, ContentPathsManager.DictionaryPaths), "route reads and hashing preserve catalog identity");
    File.WriteAllText(first, "changed");
    Thread.Sleep(650);
    Check(ReferenceEquals(original, ContentPathsManager.DictionaryPaths), "watcher thread does not publish catalog");
    int mainThread = Environment.CurrentManagedThreadId;
    FileHelper.Tick();
    Check(!ReferenceEquals(original, ContentPathsManager.DictionaryPaths) && MainTab.LastThread == mainThread, "framework tick publishes pending write on caller thread");
    Pump(700);
    int before = MainTab.Count;
    for (int i=0; i<5; i++) File.WriteAllText(first, i.ToString());
    Pump(900);
    Check(MainTab.Count == before+1, "burst writes debounce to one catalog publication");
    string renamed = Path.Combine(directory.FullName, "(1)renamed.json");
    File.Move(first, renamed); Pump(700);
    Check(ContentPathsManager.DictionaryPaths[1].Files.SequenceEqual(new[] { renamed }), "rename refreshes route path");
    string second = Path.Combine(directory.FullName, "(1)second.json");
    File.WriteAllText(second, "{}"); Pump(700);
    Check(ContentPathsManager.DictionaryPaths[1].Files.Count == 2, "new route is discovered");
    File.Delete(renamed); File.Delete(second); Pump(700);
    Check(ContentPathsManager.DictionaryPaths.Count == 0, "deleted routes disappear");
}
finally { FileHelper.FileSystemWatcher.Dispose(); FileHelper.fileWatcher.Dispose(); directory.Delete(true); }
static void Pump(int ms) { var timer=Stopwatch.StartNew(); while(timer.ElapsedMilliseconds<ms) { FileHelper.Tick(); Thread.Sleep(20); } }
static void Check(bool condition, string label) { if(!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); }
