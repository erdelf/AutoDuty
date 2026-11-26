using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Configurations;
    using ECommons.Automation.NeoTaskManager;

    public static class ActiveHelper
    {
        internal static readonly HashSet<IActiveHelper> activeHelpers = [];

        public static void InvokeAllHelpers()
        {
            Type baseType = typeof(ActiveHelperBase<,>);
            Assembly assembly = typeof(ActiveHelper).Assembly;

            foreach (Type type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract) 
                    continue;

                Type? current = type;
                while (current != null)
                {
                    if (current.IsGenericType && current.GetGenericTypeDefinition() == baseType) 
                        break;
                    if (current.BaseType is { IsGenericType: true } && current.BaseType.GetGenericTypeDefinition() == baseType)
                    {
                        Activator.CreateInstance(type);
                        break;
                    }
                    current = current.BaseType;
                }
            }
        }

        public static bool AnyHelperRunning() => 
            activeHelpers.Any(h => h.CurState == ActionState.Running);

        public static ActiveHelperBase<T,C> GetHelper<T,C>() where T : ActiveHelperBase<T,C>, new() 
                                                             where C : LoopActionConfig<C>, new() => 
            activeHelpers.OfType<T>().FirstOrDefault() ?? new T();

        public static void EnqueueActiveHelper<T, C>(LoopActionConfig<C>? config = null) where T : ActiveHelperBase<T,C>, new()
                                                                                      where C : LoopActionConfig<C>, new()
        {
            Plugin.taskManager.Enqueue(() => ADLogging.Debug($"Enqueueing {typeof(T).Name}"), "ActiveHelper");
            Plugin.taskManager.Enqueue(() =>
                                       {
                                           if (config != null)
                                               ActiveHelperBase<T, C>.Invoke(config);
                                           else
                                               ActiveHelperBase<T, C>.Invoke();
                                       }, $"{typeof(T).Name}");
            Plugin.taskManager.EnqueueDelay(50);
            Plugin.taskManager.Enqueue(() => ActiveHelperBase<T, C>.State != ActionState.Running, $"Wait-{typeof(T).Name}-Complete", new TaskManagerConfiguration(int.MaxValue));
            Plugin.taskManager.Enqueue(() => PlayerHelper.IsReadyFull,                            "WaitIsReadyFull");
        }
    }

    public interface IActiveHelper
    {
        internal void        StopIfRunning();
        public   string[]?   Commands           { get; init; }
        public   string?     CommandDescription { get; init; }
        public   void        OnCommand(string[] args);
        public   ActionState CurState { get; set; }
    }

    public abstract class ActiveHelperBase<T,C> : IActiveHelper where T : ActiveHelperBase<T,C>, new() 
                                                                where C : LoopActionConfig<C>, new()
    {
        public abstract string Name        { get; }
        public abstract string DisplayName { get; }

        public virtual string[]? Commands           { get; init; }
        public virtual string?   CommandDescription { get; init; }

        protected virtual string[] AddonsToClose { get; } = [];

        protected virtual int TimeOut { get; set; } = 300_000;

        public C ActionConfig
        {
            get => field ??= new C();
            private set;
        }

        private static T? instance;
        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    T helper = new();
                    ActiveHelper.activeHelpers.Add(helper);
                    instance = helper;
                }
                return instance;
            }
        }

        public ActiveHelperBase()
        {
            instance = (T)this;
            ActiveHelper.activeHelpers.Add(this);
        }


        internal static void Invoke() => 
            Instance.Start();

        internal static void Invoke(LoopActionConfig<C> config)
        {
            Instance.ActionConfig = (C) config;
            Invoke();
        }

        internal virtual void Start()
        {
            if(State == ActionState.Running)
            {
                this.DebugLog(this.Name + " already running");
                return;
            }


            this.InfoLog(this.Name + " started");

            if (!Plugin.States.HasFlag(PluginState.Looping) && !ActiveHelper.AnyHelperRunning())
                Plugin.SetGeneralSettings(false);

            State         =  ActionState.Running;
            Plugin.States |= PluginState.Other;

            if(this.TimeOut > 0)
                SchedulerHelper.ScheduleAction($"Helper_{this.Name}_TimeOut", this.Stop, this.TimeOut);

            if (this.DisplayName != string.Empty)
                Plugin.action = this.DisplayName;
            Svc.Framework.Update += this.HelperUpdate;
        }

        internal static ActionState State  = ActionState.None;

        public ActionState CurState
        {
            get => State;
            set => State = value;
        }

        internal static void ForceStop() => 
            instance?.Stop();

        public void StopIfRunning()
        {
            if(State == ActionState.Running)
                this.Stop();
        }


        internal virtual void Stop()
        {
            if (State == ActionState.Running)
                this.InfoLog(this.Name + " finished");

            if (this.DisplayName != string.Empty)
                Plugin.action = string.Empty;

            SchedulerHelper.DescheduleAction($"Helper_{this.Name}_TimeOut");

            Svc.Framework.Update += this.HelperStopUpdate;
            Svc.Framework.Update -= this.HelperUpdate;
        }

        protected abstract unsafe void HelperUpdate(IFramework framework);

        protected virtual int UpdateBaseThrottle { get; set; } = 500;

        protected bool UpdateBase()
        {
            if (Plugin.States.HasFlag(PluginState.Navigating) || InDungeon)
            {
                this.Stop();
                return false;
            }

            if (!EzThrottler.Throttle(this.Name, this.UpdateBaseThrottle))
                return false;

            if (GotoHelper.State == ActionState.Running)
                //ADLogging.Debug("Goto Running");
                return false;

            return true;
        }

        protected virtual unsafe void HelperStopUpdate(IFramework framework)
        {
            if (!this.CloseAddons())
                return;

            State         =  ActionState.None;
            Plugin.States &= ~PluginState.Other;

            if (!Plugin.States.HasFlag(PluginState.Looping) && !ActiveHelper.AnyHelperRunning())
                Plugin.SetGeneralSettings(true);
            Svc.Framework.Update -= this.HelperStopUpdate;
        }

        public unsafe bool CloseAddons()
        {
            for (int i = 0; i < this.AddonsToClose.Length; i++)
            {
                if (GenericHelpers.TryGetAddonByName(this.AddonsToClose[i], out AtkUnitBase* atkUnitBase) && atkUnitBase->IsReady())
                {
                    this.DebugLog("Closing Addon " + this.AddonsToClose[i]);
                    atkUnitBase->FireCallbackInt(-1);
                    return false;
                }
            }

            return true;
        }

        public virtual void OnCommand(string[] args) => 
            Invoke(Configuration.Overlay.LoopActions.FindConfig<C>() ?? Configuration.Loop.Between.Actions.FindConfig<C>() ?? Configuration.Loop.Pre.Actions.FindConfig<C>() ?? Configuration.Loop.Termination.Actions.FindConfig<C>() ?? new C());

        protected void DebugLog(string s) => 
            ADLogging.Debug($"{this.Name}: {s}");

        protected void InfoLog(string s) => 
            ADLogging.Info($"{this.Name}: {s}");
    }
}
