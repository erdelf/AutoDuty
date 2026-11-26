using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace AutoDuty.Helpers;
using Logging;

internal static class ConfigOverrideHelper
{
    private readonly record struct OverrideEntry((PropertyInfo? property, object? instance) config, object? PreviousValue);
    private static readonly Dictionary<(PropertyInfo? property, object? instance), OverrideEntry> Active = [];

    internal static bool HasOverrides => Active.Count > 0;

    internal static bool Push(Dictionary<string, string> overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            ADLogging.Error("Overrides dictionary empty");
            return false;
        }

        List<(PropertyInfo? property, object? instance)>                   newlyTracked = [];
        List<((PropertyInfo? property, object? instance), object? Before)> touched      = [];

        foreach ((string name, string value) in overrides)
        {
            (PropertyInfo? property, object? instance)? config = ConfigHelper.FindConfig(name);

            if (!config.HasValue || config.Value.property == null)
            {
                ADLogging.Error($"Unable to find config: {name}");
                Revert(touched, newlyTracked);
                return false;
            }

            if (config.Value.property.PropertyType.ToString().Contains("Dalamud.Plugin", System.StringComparison.InvariantCultureIgnoreCase))
            {
                ADLogging.Error($"Cannot override plugin field: {name}");
                Revert(touched, newlyTracked);
                return false;
            }

            if (config.Value.property.PropertyType.IsAssignableTo(typeof(IList)))
            {
                ADLogging.Error($"List configs are not supported: {name}");
                Revert(touched, newlyTracked);
                return false;
            }

            object? newValue = ConfigHelper.ConvertConfigValue(config.Value.property.PropertyType, value, out string failReason);
            if (newValue is null)
            {
                ADLogging.Error($"Unable to set {name}: {failReason}");
                Revert(touched, newlyTracked);
                return false;
            }

            object? before = config.Value.property.GetValue(config.Value.instance);
            touched.Add((config.Value, before));
            if (!Active.ContainsKey(config.Value))
            {
                Active[config.Value] = new OverrideEntry(config.Value, before);
                newlyTracked.Add(config.Value);
            }

            config.Value.property.SetValue(config.Value.instance, newValue);
        }

        ADLogging.Debug($"Applied {overrides.Count} override(s); active={Active.Count}");
        return true;
    }

    internal static bool Pop()
    {
        if (Active.Count == 0)
            return false;

        ADLogging.Debug($"Restoring {Active.Count} override(s)");
        foreach (OverrideEntry entry in Active.Values)
            entry.config.property!.SetValue(entry.config.instance, entry.PreviousValue);
        Active.Clear();
        return true;
    }

    private static void Revert(List<((PropertyInfo? property, object? instance) config, object? Before)> touched, List<(PropertyInfo? property, object? instance)> newlyTracked)
    {
        for (int i = touched.Count - 1; i >= 0; i--)
            touched[i].config.property!.SetValue(touched[i].config.instance, touched[i].Before);
        foreach ((PropertyInfo? property, object? instance) field in newlyTracked)
            Active.Remove(field);
    }
}
