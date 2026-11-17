using System.Numerics;
namespace AutoDuty {
 internal enum Stage { Stopped, Action, Paused, Reading_Path }
 internal class PathAction { public string Name="Boss"; public string Note=""; public Vector3 Position=Vector3.Zero; }
 internal static class Player { public static bool Available=true; }
 public sealed partial class AutoDuty {

 internal Stage CurrentStage=Stage.Action;
 internal Stage StageValue { get=>CurrentStage; set=>CurrentStage=value; }
 // Match the production setter's preservation of the previous stage.
 private Stage Stage { get=>CurrentStage; set { if(value==Stage.Paused) previousStage=CurrentStage; CurrentStage=value; } }
 private bool InDungeon=true;
 private List<PathAction> Actions=[];
 private PathAction pathAction=new();
 private int indexer=0;
 private Stage previousStage;
 private QueueStub taskManager=new();
 private DutyStub DutyData=new();
 internal class QueueStub { public int Aborts; public void Abort()=>Aborts++; }
 internal class DutyStub { public Dalamud.Game.ClientState.Objects.Types.IBattleChara? bossObject=null; }
 public static void TestLifecycle() {
   var plugin=new AutoDuty(); var step=new PathAction(); plugin.Actions.Add(step);
   plugin.PauseUnconfirmedBoss(step); plugin.TickUnconfirmedBoss();
   Assert(plugin.CurrentStage==Stage.Paused && plugin.previousStage==Stage.Reading_Path && plugin.taskManager.Aborts==1, "unconfirmed boss pauses the route action even after pathAction is cleared");
   plugin.CurrentStage=Stage.Stopped; plugin.PauseUnconfirmedBoss(step); plugin.TickUnconfirmedBoss();
   Assert(plugin.taskManager.Aborts==1,"pending pause cannot affect stopped run");
   plugin.CurrentStage=Stage.Action; plugin.Actions[0]=new(); plugin.PauseUnconfirmedBoss(step); plugin.TickUnconfirmedBoss();
   Assert(plugin.taskManager.Aborts==1,"stale encounter cannot pause another route action");
 }
 static void Assert(bool ok,string name) { if(!ok)throw new Exception(name); Console.WriteLine("PASS: "+name); }
 }
}
namespace AutoDuty.IPC { static class VNavmesh_IPCSubscriber { public static void Path_Stop() {} } }
namespace Dalamud.Game.ClientState.Conditions { enum ConditionFlag { BetweenAreas,BetweenAreas51 } }
namespace Dalamud.Game.ClientState.Objects.Types { interface IBattleChara { uint CurrentHp {get;} uint MaxHp {get;} bool IsDead {get;} uint EntityId {get;} } }
namespace ECommons.DalamudServices {
 static class Svc { public static ClientStub ClientState=new(); public static ConditionStub Condition=new(); public static List<Dalamud.Game.ClientState.Objects.Types.IBattleChara> Objects=[]; public static LogStub Log=new(); public static ChatStub Chat=new(); }
 class ClientStub { public uint TerritoryType=1; } class ConditionStub { public bool this[Dalamud.Game.ClientState.Conditions.ConditionFlag flag]=>false; }
 class LogStub { public void Warning(string s) {} public void Info(string s) {} } class ChatStub { public void PrintError(string s) {} }
}
namespace AutoDuty.Helpers { static class ObjectHelper {
 public static (string Text,int Current,int Needed)[] BossObjectives()=>[];
 public static Dalamud.Game.ClientState.Objects.Types.IBattleChara[] BossCandidatesForStep(global::AutoDuty.PathAction step,IEnumerable<Dalamud.Game.ClientState.Objects.Types.IBattleChara> objects)=>objects.ToArray();
 public static string[] BossNamesForStep(global::AutoDuty.PathAction step)=>[];
} }

