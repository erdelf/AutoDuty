using ECommons.DalamudServices;
using System.Reflection;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using Configurations;
    using System;
    using System.Collections;
    using System.Globalization;
    using System.Linq;

    internal static class ConfigHelper
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        internal static string GetConfig(string configName)
        {
            (PropertyInfo? property, object? instance)? config;
            if (!(config = FindConfig(configName)).HasValue || config?.property == null)
            {
                ADLogging.Error($"Unable to find config: {configName}, please type /autoduty cfg list to see all available configs");
                return string.Empty;
            }

            if (config.Value.property.ToString()!.Contains("Dalamud.Plugin", StringComparison.InvariantCultureIgnoreCase))
                return string.Empty;

            return config.Value.property.GetValue(config.Value.instance)!.ToString() ?? string.Empty;
        }

        internal static object? ConvertConfigValue(Type configType, string configValue, out string failReason)
        {
            failReason = $"value must be of type: {configType.ToString().Replace("System.", "")}";

            if (configType == typeof(string))
            {
                return configValue;
            }
            else if (configType.IsEnum)
            {
                if (Enum.TryParse(configType, configValue, true, out object? configEnum))
                    return configEnum;
            } else if (configType is { IsGenericType: true, IsGenericTypeDefinition: false } && configType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return Activator.CreateInstance(configType, ConvertConfigValue(configType.GetGenericArguments()[0], configValue, out failReason));
            }
            else if (configType.GetInterface(nameof(IConvertible)) != null)
            {
                return Convert.ChangeType(configValue, configType, CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static object? ModifyConfig(Type configType, string configValue, out string failReason) =>
            ConvertConfigValue(configType, configValue, out failReason);

        internal static bool ModifyConfig(string configName, params string[] configValues)
        {
            switch (configName)
            {
                case "leveling" when configValues.Length < 1:
                    ADLogging.Info("Leveling Mode not set, correct usage: /autoduty cfg leveling LevelingModeEnum");
                    return false;
                case "leveling":
                {
                    if (!Enum.TryParse(configValues[0], true, out LevelingMode levelingMode))
                    {
                        ADLogging.Info($"Argument must be a LevelingMode Type, you inputted {configValues[0]}. Valid values are {string.Join("|", Enum.GetValues<LevelingMode>().Select(lm => lm.ToString()))}");
                        return false;
                    }

                    switch (levelingMode)
                    {
                        case LevelingMode.Support:
                            AutoDuty.Configuration.Meta.DutyModeEnum = DutyMode.Support;
                            break;
                        case LevelingMode.Trust_Group:
                        case LevelingMode.Trust_Solo:
                            AutoDuty.Configuration.Meta.DutyModeEnum = DutyMode.Trust;
                            break;
                        case LevelingMode.None:
                        default:
                            break;
                    }

                    Plugin.LevelingModeEnum = levelingMode;
                    return true;
                }
            }

            (PropertyInfo? property, object? instance)? config;
            if (!(config = FindConfig(configName)).HasValue || config?.property == null)
            {
                ADLogging.Error($"Unable to find config: {configName}, please type /autoduty cfg list to see all available configs");
                return false;
            }
            else if (config.Value.property.PropertyType.ToString().Contains("Dalamud.Plugin", StringComparison.InvariantCultureIgnoreCase))
            {
                return false;
            }
            else
            {
                void PrintError(string failReason)
                {
                    ADLogging.Error($"Unable to set config setting: {config.Value.property.Name}: {failReason}");
                }

                Type configType = config.Value.property.PropertyType;// ConfigType(field);

                if (configType.IsAssignableTo(typeof(IList)))
                {
                    IList valueList      = (IList) config.Value.property.GetValue(config.Value.instance)!;
                    Type  enumerableType = configType.GetElementType() ?? configType.GenericTypeArguments.First();

                    switch (configValues[0])
                    {
                        case "set":
                            // /ad cfg SelectedTrustMembers set Yshtola Graha Thancred
                            // /ad cfg CustomCommandsTermination set "/snd run Wep" "/snd run Leveling"

                            if (!valueList.IsFixedSize)
                                valueList.Clear();

                            for (int i = 1; i < configValues.Length; i++)
                            {
                                object? val = ModifyConfig(enumerableType, configValues[i], out _);
                                if (val != null)
                                    if (!valueList.IsFixedSize)
                                        valueList.Add(val);
                                    else
                                        valueList[i-1] = val;
                            }
                            break;
                        case "add":
                            // /ad cfg CustomCommandsTermination add "/test"

                            if (valueList.IsFixedSize)
                                PrintError("Can't use add on fixed size configs");

                            foreach (string t in configValues[1..])
                            {
                                object? val = ModifyConfig(enumerableType, t, out _);
                                if (val != null)
                                    valueList.Add(val);
                            }
                            break;
                        case "del":
                        case "delete":
                        case "rem":
                        case "remove":
                            // /ad cfg CustomCommandsTermination del 2 1

                            if (valueList.IsFixedSize)
                                PrintError("Can't use delete on fixed size configs");

                            for (int i = 1; i < configValues.Length; i++)
                            {
                                if (int.TryParse(configValues[i], out int index))
                                    valueList.RemoveAt(index);
                            }
                            break;
                        case "delentry":
                        case "deleteentry":
                        case "rementry":
                        case "removeentry":
                            // /ad cfg CustomCommandsTermination delEntry /test

                            if (valueList.IsFixedSize)
                                PrintError("Can't use delete on fixed size configs");

                            for (int i = 1; i < configValues.Length; i++)
                            {
                                object? entry = ModifyConfig(enumerableType, configValues[i], out string _);
                                
                                if (entry != null)
                                {
                                    int index = valueList.IndexOf(entry);
                                    if (i >= 0)
                                        valueList.RemoveAt(index);
                                }
                            }
                            break;
                        case "insert":
                            // /ad cfg CustomCommandsTermination insert 1 "/test 1" "/test 2"
                            if (valueList.IsFixedSize)
                                PrintError("Can't use insert on fixed size configs");

                            if (int.TryParse(configValues[1], out int insertIndex))
                                for (int i = 2; i < configValues.Length; i++)
                                {
                                    object? entry = ModifyConfig(enumerableType, configValues[i], out string _);

                                    if (entry != null) 
                                        valueList.Insert(insertIndex++, entry);
                                }

                            break;
                    }
                }
                else
                {
                    object? newValue = ModifyConfig(configType, configValues[0], out string failReason);

                    if (newValue != null)
                        config.Value.property.SetValue(config.Value.instance, newValue);
                    else
                        PrintError(failReason);
                }

                ConfigurationProfileV2.Save();
            }
            return false;
        }

        internal static void ListConfig(object? instance = null, string? prefix = null)
        {
            instance ??= ConfigurationMain.Instance.GetCurrentConfig;

            PropertyInfo[] properties = instance.GetType().GetProperties(instance.GetType().IsAssignableTo(typeof(LoopActionConfig)) ? All : All | BindingFlags.DeclaredOnly);
            if (properties.Length == 0)
                return;

            foreach (PropertyInfo property in properties)
            {
                if (property.SetMethod != null && property.PropertyType.IsAssignableTo(typeof(string)))
                    ADLogging.Info($"{prefix}{property.Name} = {property.GetValue(instance)} ({property.PropertyType.Name}");

                if (property.PropertyType.IsAssignableTo(typeof(IList)) && !property.PropertyType.IsAssignableTo(typeof(string))) 
                {
                    IList valueList      = (IList)property.GetValue(instance)!;

                    Type? enumerableType = property.PropertyType.GetElementType() ?? property.PropertyType.GenericTypeArguments.FirstOrDefault() ?? property.PropertyType.BaseType?.GetElementType() ?? property.PropertyType.BaseType?.GenericTypeArguments.FirstOrDefault();

                    if (enumerableType != null)
                    {
                        if (enumerableType.IsGenericType && enumerableType.GetGenericTypeDefinition().IsAssignableTo(typeof(Nullable<>)))
                            enumerableType = enumerableType.GetGenericArguments().First();

                        if (enumerableType.IsAssignableTo(typeof(Enum)) || enumerableType.IsAssignableTo(typeof(string)) || enumerableType.IsAssignableTo(typeof(uint)) && property.SetMethod != null)
                        {
                            ADLogging.Info($"{prefix}{property.Name} = {property.GetValue(instance)} ({property.PropertyType.Name}{(enumerableType.IsEnum ? $" {string.Join(", ", Enum.GetNames(enumerableType))}" : "")})");
                            return;
                        }
                    }

                    for (int index = 0; index < valueList.Count; index++)
                    {
                        object? value = valueList[index];
                        if(value != null)
                            ListConfig(value, $"{prefix}{property.Name}.[{index}/{value?.GetType().Name}].");
                    }
                } 
                else if ((property.PropertyType.FullName?.Contains(nameof(ConfigurationProfileV2)) ?? false) || (property.PropertyType.FullName?.Contains(nameof(LoopActionConfig)) ?? false))
                {
                    object? value = property.GetValue(instance);
                    if(value != null)
                        ListConfig(value, $"{prefix}{property.Name}.");
                }
                else if(property.SetMethod != null)
                {
                    ADLogging.Info($"{prefix}{property.Name} = {property.GetValue(instance)} ({property.PropertyType.Name}{(property.PropertyType.IsEnum ? $" {string.Join(", ", Enum.GetNames(property.PropertyType))}" : "")})");
                }
            }
        }

        internal static (PropertyInfo? property, object? instance)? FindConfig(string configName, Type? type = null, object? instance = null)
        {
            instance ??= ConfigurationMain.Instance.GetCurrentConfig;
            type     ??= instance.GetType() ?? typeof(ConfigurationProfileV2);

            ADLogging.Debug($"Config find : {configName} in {type.Name}");

            int dotIndex = configName.IndexOf('.');
            if (dotIndex > 0)
            {
                string subConfig = configName[..dotIndex];
                ADLogging.Debug("Subconfig found: " + subConfig);

                if (subConfig[0] == '[')
                    if (type.IsAssignableTo(typeof(IList)))
                    {
                        IList  valueList   = (IList)instance;
                        string indexString = subConfig[1..^1];

                        if (int.TryParse(indexString, out int index))
                            if (valueList.Count <= index)
                            {
                                object? value = valueList[index];
                                if (value != null)
                                    return FindConfig(configName[(dotIndex + 1)..], value.GetType(), value);
                            }

                        foreach (object o in valueList)
                            if (o.GetType().Name.Equals(indexString, StringComparison.InvariantCultureIgnoreCase))
                                return FindConfig(configName[(dotIndex + 1)..], o.GetType(), o);
                    }


                PropertyInfo[] p = type.GetProperties(All);
                foreach (PropertyInfo property in p)
                    if (property.Name.Equals(subConfig, StringComparison.InvariantCultureIgnoreCase))
                        return FindConfig(configName[(dotIndex+1)..], property.PropertyType, property.GetValue(instance));
            }

            PropertyInfo[] i = type.GetProperties(All);
            foreach (PropertyInfo property in i)
                if (property.Name.Equals(configName, StringComparison.InvariantCultureIgnoreCase))
                    return (property, instance);

            return null;
        }
    }
}
