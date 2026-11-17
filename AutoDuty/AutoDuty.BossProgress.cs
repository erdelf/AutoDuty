using System;
using System.Linq;
using AutoDuty.Helpers;
using AutoDuty.IPC;
using ECommons.DalamudServices;

namespace AutoDuty;

public sealed partial class AutoDuty
{
    private readonly BossProgress bossProgress = new();
    private BossProgress.Key BossKey(PathAction step) => new(Svc.ClientState.TerritoryType, step.Note.Trim(), step.Position);

    internal bool BossAlreadyDefeated(PathAction step) => this.bossProgress.IsDefeated(this.BossKey(step));

    // Preserve the route index when encounter completion cannot be confirmed.
    // Defer queue cancellation until the framework tick, outside TaskManager callbacks.
    private PathAction? unconfirmedBoss;
    internal void PauseUnconfirmedBoss(PathAction step) => this.unconfirmedBoss ??= step;

    private void TickUnconfirmedBoss()
    {
        if (this.unconfirmedBoss is not { } step) return;
        this.unconfirmedBoss = null;
        if (this.Stage == Stage.Stopped || this.indexer < 0 || this.indexer >= this.Actions.Count
            || !ReferenceEquals(step, this.Actions[this.indexer])) return;
        this.taskManager.Abort();
        VNavmesh_IPCSubscriber.Path_Stop();
        this.Stage = Stage.Paused;
        this.previousStage = Stage.Reading_Path;
        Svc.Log.Warning($"Boss completion unconfirmed at step {this.indexer}: {step.Note}; paused for inspection.");
        Svc.Chat.PrintError("[AutoDuty] Boss completion could not be confirmed. Paused; inspect the encounter before resuming.");
    }

    private void TickBossProgress()
    {
        if (!InDungeon) { this.bossProgress.Reset(); return; }
        if (!Player.Available || Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]
            || Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51]) return;
        if (this.Actions.Count == 0) return;
        var objects = Svc.Objects.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleChara>().ToArray();
        var objectives = ObjectHelper.BossObjectives();
        foreach (var step in this.Actions.Where(a => a.Name == "Boss"))
        {
            var candidates = ObjectHelper.BossCandidatesForStep(step, objects);
            bool live = candidates.Any(o => o.CurrentHp > 0 && !o.IsDead);
            var names = ObjectHelper.BossNamesForStep(step);
            // An unnamed nearby corpse cannot be assigned to every Boss step in range.
            bool dead = candidates.Any(o => o.MaxHp > 0 && o.CurrentHp == 0 && o.IsDead
                && (names.Length > 0 || ((this.indexer >= 0 && this.indexer < this.Actions.Count && ReferenceEquals(step, this.Actions[this.indexer]))
                    && this.DutyData?.bossObject?.EntityId == o.EntityId)));
            bool objective = objectives.Any(item => BossProgress.ObjectiveComplete(item.Text, item.Current, item.Needed, names));
            if (this.bossProgress.Observe(this.BossKey(step), live, dead, objective))
            {
                Svc.Log.Info($"[AutoDuty] Boss defeated: {step.Note} ({(objective ? "completed-objective" : "observed-death")})");
            }
        }
    }
}
