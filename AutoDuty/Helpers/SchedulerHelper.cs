using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;

    internal static class SchedulerHelper
    {
        public class Schedule
        {
            internal List<Action> Action { get; set; } = [() => { }];

            internal int TimeMS   { get; set; } = 0;
            internal int Interval { get; set; } 

            internal Func<bool>? Condition { get; set; } = null;

            internal bool RunOnce { get; set; } = true;
        }

        internal static Dictionary<string, Schedule> Schedules = [];

        internal static void ScheduleAction(string name, Action action, int timeMS, bool runOnce = true) => ScheduleAction(name, [action], null, timeMS, runOnce);

        internal static void ScheduleAction(string name, List<Action> action, int timeMS, bool runOnce = true) => ScheduleAction(name, action, null, timeMS, runOnce);

        internal static void ScheduleAction(string name, Action action, Func<bool> condition, bool runOnce = true) => ScheduleAction(name, [action], condition, 0, runOnce);

        internal static void ScheduleAction(string name, List<Action> action, Func<bool> condition, bool runOnce = true) => ScheduleAction(name, action, condition, 0, runOnce);

        private static void ScheduleAction(string name, List<Action> action, Func<bool>? condition, int timeMS, bool runOnce = true)
        {
            ADLogging.Debug($"Scheduler Helper - {name} enqueued for scheduling");

            _schedulesToAdd.Enqueue((name, new Schedule { Action = action, Condition = condition, TimeMS = timeMS > 0 ? Environment.TickCount + timeMS : 0, Interval = timeMS, RunOnce = runOnce }));
        }

        private static readonly Queue<(string, Schedule)> _schedulesToAdd = [];


        internal static void DescheduleAction(string name) => _schedulesToRemove.Enqueue(name);

        private static readonly Queue<string> _schedulesToRemove = [];

        internal static void ScheduleInvoker(IFramework _)
        {
            foreach (KeyValuePair<string, Schedule> schedule in Schedules)
                if (schedule.Value.TimeMS != 0 ? Environment.TickCount >= schedule.Value.TimeMS : schedule.Value.Condition?.Invoke() ?? false)
                {
                    ADLogging.Debug($"SchedulerHelper - Executing action {schedule.Key}");
                    schedule.Value.Action.ForEach(a => a.Invoke());
                    if (schedule.Value.RunOnce || schedule.Value.Condition != null)
                        _schedulesToRemove.Enqueue(schedule.Key);
                    else
                        schedule.Value.TimeMS = Environment.TickCount + schedule.Value.Interval;
                }

            while (_schedulesToRemove.Count !=0)
            {
                string? schedule = _schedulesToRemove.Dequeue();
                if (schedule.IsNullOrEmpty())
                    return;
                ADLogging.Debug($"SchedulerHelper - {schedule} Removed from Scheduler");
                Schedules.Remove(schedule);
            }

            while (_schedulesToAdd.Count != 0)
            {
                (string? name, Schedule? schedule) = _schedulesToAdd.Dequeue();
                if (name.IsNullOrEmpty())
                    return;

                if (!Schedules.TryAdd(name, schedule))
                {
                    ADLogging.Debug($"SchedulerHelper - {name} already exists in Scheduler, updating");
                    Schedules[name] = schedule;
                }
                else
                {
                    ADLogging.Debug($"SchedulerHelper - {name} Added to Scheduler");
                }
            }
        }
    }
}
