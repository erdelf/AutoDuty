using AutoDuty.Helpers;
using ECommons.EzIpcManager;
// ReSharper disable UnusedMember.Global
#pragma warning disable CA1822

#nullable disable

namespace AutoDuty.IPC
{
    using ECommons.DalamudServices;
    using Newtonsoft.Json.Linq;

    internal class IPCProvider
    {
        internal IPCProvider() => 
            EzIPC.Init(this);

        [EzIPC] public void   ListConfig()             => ConfigHelper.ListConfig();
        [EzIPC] public string GetConfig(string config) => ConfigHelper.GetConfig(config);

        [EzIPC] 
        public void SetConfig(string config, object setting)
        {
            Svc.Log.Debug("Config called with: " + setting.GetType().FullName);
            switch (setting)
            {
                case string s:
                    ConfigHelper.ModifyConfig(config, s);
                    break;
                case JArray js:
                    Svc.Log.Warning("JArray converted to: " + string.Join(" | ", js.ToObject<string[]>()));
                    ConfigHelper.ModifyConfig(config, js.ToObject<string[]>() ?? []);
                    break;
                case string[] ss:
                    ConfigHelper.ModifyConfig(config, ss);
                    break;
                default:
                    Svc.Log.Warning("setting has to be string or string[]");
                    break;
            }
        }

        [EzIPC] public void Run(uint   territoryType, int loops = 0, bool bareMode = false) => Plugin.Run(territoryType, loops, startFromZero: true, bareMode: bareMode);
        [EzIPC] public void Start(bool startFromZero = true)   => Plugin.Run(Svc.ClientState.TerritoryType, 1, startFromZero);
        [EzIPC] public void Stop()                             => Plugin.Stage = Stage.Stopped;
        [EzIPC] public bool IsNavigating()                     => Plugin.states.HasFlag(PluginState.Navigating);
        [EzIPC] public bool IsLooping()                        => Plugin.states.HasFlag(PluginState.Looping);
        [EzIPC] public bool IsStopped()                        => Plugin.Stage == Stage.Stopped;
        [EzIPC] public bool ContentHasPath(uint territoryType) => ContentPathsManager.DictionaryPaths.ContainsKey(territoryType);
        
        //Added in IPC for SetLevelingMode to allow other addons to set the leveling mode.
        [EzIPC] public void SetLevelingMode(int mode) => Plugin.LevelingModeEnum = (LevelingMode)mode;

        //Callback for Wrath Combo Lease Cancel
        [EzIPC] public void WrathComboCallback(int reason, string s) => Wrath_IPCSubscriber.CancelActions(reason, s);
    }
}
