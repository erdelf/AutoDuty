using AutoDuty.Helpers;
using ECommons.EzIpcManager;
using AutoDuty.Logging;
// ReSharper disable UnusedMember.Global
#pragma warning disable CA1822

#nullable disable

namespace AutoDuty.IPC
{
    using System.Collections.Generic;
    using Configurations;
    using ECommons.DalamudServices;
    using Newtonsoft.Json.Linq;

    public class IPCProvider
    {
        internal IPCProvider() => 
            EzIPC.Init(this);

        [EzIPC] public void   ListConfig()             => ConfigHelper.ListConfig();
        [EzIPC] public string GetConfig(string config) => ConfigHelper.GetConfig(config);

        [EzIPC] 
        public void SetConfig(string config, object setting)
        {
            ADLogging.Debug("Config called with: " + setting.GetType().FullName);
            switch (setting)
            {
                case string s:
                    ConfigHelper.ModifyConfig(config, s);
                    break;
                case JArray js:
                    ADLogging.Warning("JArray converted to: " + string.Join(" | ", js.ToObject<string[]>()));
                    ConfigHelper.ModifyConfig(config, js.ToObject<string[]>() ?? []);
                    break;
                case string[] ss:
                    ConfigHelper.ModifyConfig(config, ss);
                    break;
                default:
                    ADLogging.Warning("setting has to be string or string[]");
                    break;
            }
        }

        [EzIPC]
        public bool PushConfigOverrides(object overrides)
        {
            Dictionary<string, string> dict = overrides switch
            {
                Dictionary<string, string> d => d,
                JObject jo => jo.ToObject<Dictionary<string, string>>() ?? [],
                _ => null
            };
            if (dict is null)
            {
                ADLogging.Warning("Expected Dictionary<string, string>");
                return false;
            }
            return ConfigOverrideHelper.Push(dict);
        }

        [EzIPC] public bool PopConfigOverrides() => ConfigOverrideHelper.Pop(); // don't really need to ever call this since AD does already on stop

        [EzIPC] public void Run(uint   territoryType, int loops = 0, bool bareMode = false)
        {
            Configuration.Meta.AutoDutyModeEnum = AutoDutyMode.Looping;
            Plugin.Run(territoryType, loops, startFromZero: true, bareMode: bareMode);
        }

        [EzIPC] public void Start(bool startFromZero = true)   => Plugin.Run(Svc.ClientState.TerritoryType, 1, startFromZero);
        [EzIPC] public void Stop()                             => Plugin.Stage = Stage.Stopped;
        [EzIPC] public bool IsNavigating()                     => Plugin.States.HasFlag(PluginState.Navigating);
        [EzIPC] public bool IsLooping()                        => Plugin.States.HasFlag(PluginState.Looping);
        [EzIPC] public bool IsStopped()                        => Plugin.Stage == Stage.Stopped;
        [EzIPC] public bool ContentHasPath(uint territoryType) => ContentPathsManager.DictionaryPaths.ContainsKey(territoryType);
        [EzIPC] public void SetLevelingMode(int mode) => Plugin.LevelingModeEnum = (LevelingMode)mode;
        [EzIPC] public void PluginConfig(string name, bool enable) => ConfigurationMain.Instance.ActivatePluginProfile(name, enable);
        [EzIPC] public void RunForPlugin(string pluginName, uint territoryType, int loops = 0, bool bareMode = false)
        {
            Configuration.Meta.AutoDutyModeEnum = AutoDutyMode.Looping;
            Plugin.currentlyActivePluginConfig  = pluginName;
            this.PluginConfig(pluginName, true);
            Plugin.Run(territoryType, loops, startFromZero: true, bareMode: bareMode);
        }

        //Callback for Wrath Combo Lease Cancel
        [EzIPC] public void WrathComboCallback(int reason, string s) => Wrath_IPCSubscriber.CancelActions(reason, s);
    }
}
