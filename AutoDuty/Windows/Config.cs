using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;

namespace AutoDuty.Windows;

using Configurations;
using Dalamud.Game.ClientState.Objects.Types;
using Data;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.IPC.Subscribers.RotationSolverReborn;
using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Lumina.Excel.Sheets;
using Multibox;
using NightmareUI.Censoring;
using Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using Achievement = Lumina.Excel.Sheets.Achievement;
using Vector2 = FFXIVClientStructs.FFXIV.Common.Math.Vector2;

public static class ConfigTab
{
    internal static string followName = "";

    private static ConfigurationProfileV2     Configuration => AutoDuty.Configuration; 
    public static Dictionary<uint, Item>     Items         { get; set; } = Svc.Data.GetExcelSheet<Item>()?.Where(x => !x.Name.ToString().IsNullOrEmpty()).ToDictionary(x => x.RowId, x => x) ?? [];
    private static string                     stopItemQtyItemNameInput = "";
    private static KeyValuePair<uint, string> stopItemQtySelectedItem  = new(0, "");

    private static string profileRenameInput = "";

    private static bool overlayHeaderSelected      = false;
    private static bool multiboxHeaderSelected     = false;
    private static bool devHeaderSelected          = false;
    private static bool dutyConfigHeaderSelected   = false;
    private static bool bmaiSettingHeaderSelected  = false;
    private static bool wrathSettingHeaderSelected = false;
    private static bool rsrSettingHeaderSelected   = false;
    private static bool w2wSettingHeaderSelected   = false;
    private static bool advModeHeaderSelected      = false;
    private static bool preLoopHeaderSelected      = false;
    private static bool betweenLoopHeaderSelected  = false;
    private static bool terminationHeaderSelected  = false;

    public static void Draw()
    {
        if (MainWindow.CurrentTabName != "Config")
            MainWindow.CurrentTabName = "Config";

        //Language Selector
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(Loc.Get("ConfigTab.Language"));
        ImGui.SameLine();

        string   currentLang  = ConfigurationMain.Instance.Language;
        string[] languages    = LocalizationManager.availableLanguages;
        int      currentIndex = Array.IndexOf(languages, currentLang);

        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##Language", ref currentIndex, languages, languages.Length))
        {
            LocalizationManager.SetLanguage(languages[currentIndex]);
           ConfigurationProfileV2.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Get("ConfigTab.LanguageHelp"));

        ImGui.Separator();

        bool overridesActive = ConfigOverrideHelper.HasOverrides;
        if (overridesActive)
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Profile.ConfigOverridesActiveNote"));
        using ImRaii.DisabledDisposable overrideLock = ImRaii.Disabled(overridesActive);

        //Start of Profile Selection
        ImGui.AlignTextToFramePadding();
        ImGui.Text(Loc.Get("ConfigTab.Profile.CurrentlySelected"));
        ImGui.SameLine();
        if (ConfigurationMain.Instance.ActiveProfileName == ConfigurationMain.CONFIGNAME_BARE)
            ImGuiHelper.DrawIcon(FontAwesomeIcon.Lock);
        if (ConfigurationMain.Instance.ActiveProfileName == ConfigurationMain.Instance.DefaultConfigName)
            ImGuiHelper.DrawIcon(FontAwesomeIcon.CheckCircle);
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 180 * ImGuiHelpers.GlobalScale);
        ImGui.SetItemAllowOverlap();
        using (ImRaii.ComboDisposable configCombo = ImRaii.Combo("##ConfigCombo", ConfigurationMain.Instance.ActiveProfileName))
        {
            if (configCombo)
                foreach (string key in ConfigurationMain.Instance.ConfigNames)
                {
                    float selectableX = ImGui.GetCursorPosX();
                    if (key == ConfigurationMain.CONFIGNAME_BARE)
                        ImGuiHelper.DrawIcon(FontAwesomeIcon.Lock);
                    if (key == ConfigurationMain.Instance.DefaultConfigName)
                        ImGuiHelper.DrawIcon(FontAwesomeIcon.CheckCircle);

                    float textX = ImGui.GetCursorPosX();
                        
                    ImGui.SetCursorPosX(selectableX);
                    ImGui.SetItemAllowOverlap();
                    if (ImGui.Selectable($"###{key}ConfigSelectable", key == ConfigurationMain.Instance.ActiveProfileName))
                        ConfigurationMain.Instance.SetProfile(key);
                    ImGui.SameLine(textX);
                    ImGui.Text(key);

                    ConfigurationMain.ProfileData? profile = ConfigurationMain.Instance.GetProfile(key);
                    if(profile?.CIDs.Count != 0)
                    {
                        ImGui.SameLine();
                        ImGuiEx.TextWrapped(ImGuiHelper.VersionColor, string.Join(", ",
                                                                                  profile!.CIDs.Select(cid => ConfigurationMain.Instance.charByCID.TryGetValue(cid, out ConfigurationMain.CharData cd) ?
                                                                                                                  cd.GetName() :
                                                                                                                  cid.ToString())));
                    }
                }
        }

        ImGui.PopItemWidth();
        ImGui.SameLine();

        if (ImGui.IsPopupOpen("##RenameProfile"))
        {
            bool    open     = true;
            Vector2 textSize = ImGui.CalcTextSize(profileRenameInput);
            ImGui.SetNextWindowSize(new Vector2(textSize.X + 200, textSize.Y + 120) * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginPopupModal($"##RenameProfile", ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove))
            {
                ImGuiHelper.CenterNextElement(ImGui.CalcTextSize(Loc.Get("ConfigTab.Profile.NewProfileName")).X);
                ImGui.Text(Loc.Get("ConfigTab.Profile.NewProfileName"));
                ImGui.NewLine();
                ImGui.SameLine(50);
                ImGui.SetNextItemWidth((textSize.X + 100) * ImGuiHelpers.GlobalScale);

                ImGui.InputText("##RenameProfileInput", ref profileRenameInput, 100);
                ImGui.Spacing();
                ImGuiHelper.CenterNextElement(ImGui.CalcTextSize(Loc.Get("ConfigTab.Profile.ChangeProfileName")).X);
                if (ImGui.Button(Loc.Get("ConfigTab.Profile.ChangeProfileName")))
                    if (ConfigurationMain.Instance.RenameCurrentProfile(profileRenameInput))
                    {
                        open = false;
                        ImGui.CloseCurrentPopup();
                    }

                ImGui.EndPopup();
            }
        }



        bool bareProfile = ConfigurationMain.Instance.ActiveProfileName == ConfigurationMain.CONFIGNAME_BARE;

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            ConfigurationMain.Instance.CreateNewProfile();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.CreateNew"));

        ImGui.SameLine(0, 15f);
        using (ImRaii.Disabled(bareProfile))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Pen))
            {
                profileRenameInput = ConfigurationMain.Instance.ActiveProfileName;
                ImGui.OpenPopup("##RenameProfile");
            }
        }

        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.Rename"));

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
            ConfigurationMain.Instance.DuplicateCurrentProfile();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.Duplicate"));

        ImGui.SameLine();
        using (ImRaii.Disabled(ImGui.GetIO().KeyCtrl ? 
                                   ConfigurationMain.Instance.GetCurrentProfile.CIDs.Contains(Player.CID) != ImGui.GetIO().KeyShift : 
                                   ConfigurationMain.Instance.DefaultConfigName == ConfigurationMain.Instance.ActiveProfileName))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.CheckCircle))
                if (ImGui.GetIO().KeyCtrl)
                    if (ImGui.GetIO().KeyShift)
                        ConfigurationMain.Instance.RemoveCharacterDefault();
                    else
                        ConfigurationMain.Instance.SetCharacterDefault();
                else
                    ConfigurationMain.Instance.SetProfileAsDefault();
        }

        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.MakeDefaultHelp"));


        ImGui.SameLine();
        using (ImRaii.Disabled(bareProfile || !ImGui.GetIO().KeyCtrl))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
                ConfigurationMain.Instance.RemoveCurrentProfile();
        }

        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.DeleteHelp"));

        if (bareProfile)
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Profile.BareProfileNote"));
        using ImRaii.DisabledDisposable _ = ImRaii.Disabled(bareProfile);

        //Start of Window & Overlay Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool overlayHeader = ImGui.Selectable(Loc.Get("ConfigTab.Overlay.Header"), overlayHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();      
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (overlayHeader)
            overlayHeaderSelected = !overlayHeaderSelected;

        if (overlayHeaderSelected == true)
        {
            bool showOverlay = Configuration.Overlay.Show;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.ShowOverlay"), ref showOverlay))
            {
                Configuration.Overlay.Show = showOverlay;
                ConfigurationProfileV2.Save();
            }
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Overlay.ShowOverlayHelp"));
            if (Configuration.Overlay.Show)
            {
                ImGui.Indent();
                ImGui.Columns(2, "##OverlayColumns", false);

                //ImGui.SameLine(0, 53);
                bool overlayWhenStopped = Configuration.Overlay.HideWhenStopped;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.HideWhenStopped"), ref overlayWhenStopped))
                {
                    Configuration.Overlay.HideWhenStopped = overlayWhenStopped;
                    ConfigurationProfileV2.Save();
                }
                ImGui.NextColumn();
                bool lockOverlay = Configuration.Overlay.Lock;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.LockOverlay"), ref lockOverlay))
                {
                    Configuration.Overlay.Lock = lockOverlay;
                    ConfigurationProfileV2.Save();
                }
                ImGui.NextColumn();
                //ImGui.SameLine(0, 57);

                bool loopText = Configuration.Overlay.ShowDutyLoopText;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.ShowDutyLoopText"), ref loopText))
                {
                    Configuration.Overlay.ShowDutyLoopText = loopText;
                    ConfigurationProfileV2.Save();
                }

                ImGui.NextColumn();
                bool overlayNoBG = Configuration.Overlay.NoBG;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.TransparentBG"), ref overlayNoBG))
                {
                    Configuration.Overlay.NoBG = overlayNoBG;
                    ConfigurationProfileV2.Save();
                }
                ImGui.NextColumn();
                bool showActionText = Configuration.Overlay.ShowActionText;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.ShowActionText"), ref showActionText))
                {
                    Configuration.Overlay.ShowActionText = showActionText;
                    ConfigurationProfileV2.Save();
                }
                ImGui.NextColumn();
                bool overlayAnchorBottom = Configuration.Overlay.AnchorBottom;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.AnchorBottom"), ref overlayAnchorBottom))
                {
                    Configuration.Overlay.AnchorBottom = overlayAnchorBottom;
                    ConfigurationProfileV2.Save();
                }
                ImGui.NextColumn();

                ImGui.Columns(1);
                ImGui.Separator();

                ImGui.Text(Loc.Get("ConfigTab.Overlay.ButtonsHeader"));

                bool gotoActions = Configuration.Overlay.GoToActions;
                if (ImGui.Checkbox(Loc.Get("Overlay.Button.Goto"), ref gotoActions))
                {
                    Configuration.Overlay.GoToActions = gotoActions;
                    ConfigurationProfileV2.Save();
                }

                Configuration.Overlay.LoopActions.OnGui("OverlayActions", startOpened: false);

                ImGui.Unindent();
            }
            bool onStartup = Configuration.Meta.ShowMainWindowOnStartup;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.ShowMainWindowOnStartup"), ref onStartup))
            {
                Configuration.Meta.ShowMainWindowOnStartup = onStartup;
                ConfigurationProfileV2.Save();
            }

            ImGui.SameLine();
            bool sliderInputs = Configuration.Meta.UseSliderInputs;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.SliderInputs"), ref sliderInputs))
            {
                Configuration.Meta.UseSliderInputs = sliderInputs;
                ConfigurationProfileV2.Save();
            }
            ImGui.SameLine();
            bool loopActionsOpen = Configuration.Meta.LoopActionsOpenByDefault;
            if (ImGuiEx.CheckboxWrapped(Loc.Get("ConfigTab.Overlay.LoopActionsOpenByDefault"), ref loopActionsOpen))
            {
                Configuration.Meta.LoopActionsOpenByDefault = loopActionsOpen;
                ConfigurationProfileV2.Save();
            }
        }

        if (Plugin.isDev)
        {
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
            bool devHeader = ImGui.Selectable("Dev###DevHeader", devHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (devHeader)
                devHeaderSelected = !devHeaderSelected;

            if (devHeaderSelected)
            {
                if (ImGui.Checkbox("Update Paths On Startup##DevUpdatePathsOnStartup", ref ConfigurationMain.Instance.updatePathsOnStartup))
                    ConfigurationProfileV2.Save();

                if (ImGui.Button("Print Mod List##DevPrintModList")) 
                    Svc.Log.Info(string.Join("\n", PluginInterface.InstalledPlugins.Where(pl => pl.IsLoaded).GroupBy(pl => pl.Manifest.InstalledFromUrl).OrderByDescending(g => g.Count()).Select(g => g.Key+"\n\t"+string.Join("\n\t", g.Select(pl => pl.Name)))));

                if (ImGui.Button("Rotation On##DevRotationOn")) 
                    Plugin.SetRotationPluginSettings(true, ignoreConfig: true, ignoreTimer: true);

                ImGui.SameLine();
                if (ImGui.Button("Rotation Off##DevRotationOff"))
                {
                    Plugin.SetRotationPluginSettings(false, ignoreConfig: true, ignoreTimer: true);
                    if(Wrath_IPCSubscriber.IsEnabled)
                        Wrath_IPCSubscriber.Release();
                }

                if (ImGui.Button("Between Loop Actions##DevBetweenLoops"))
                {
                    Plugin.CurrentTerritoryContent =  ContentHelper.DictionaryContent.Values.First();
                    Plugin.States                  |= PluginState.Other;
                    Plugin.LoopTasks(false);
                }

                if (ImGui.Button("Boss Loot Test##DevBossLootTest"))
                {
                    IEnumerable<IGameObject> treasures = ObjectHelper.GetObjectsByObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)?.
                                                                      Where(x => ObjectHelper.BelowDistanceToPoint(x.Position, Player.Position, 50, 10)) ?? [];
                    Svc.Log.Debug(treasures.Count() + "\n" + string.Join("\n", treasures.Select(igo => igo.Position.ToString())));
                }

                unsafe
                {
                    ImGui.Text($"In Area: " + GotoHousingHelper.InHousingArea(Housing.FC_Estate));
                    ImGui.Text($"Indoors: " + GotoHousingHelper.InPrivateHouse(Housing.FC_Estate));
                }

                unsafe
                {
                    static V* FindPtr<K, V>(ref StdMap<K, Pointer<V>> map, K key) where K : unmanaged, IComparable where V : unmanaged => 
                        map.TryGetValuePointer(key, out Pointer<V>* ptr) && ptr != null ? ptr->Value : null;

                    LayoutManager*                           layout = LayoutWorld.Instance()->ActiveLayout;
                    StdMap<ulong, Pointer<ILayoutInstance>>* insts  = layout != null ? FindPtr(ref layout->InstancesByType, InstanceType.CollisionBox) : null;
                    ILayoutInstance*                         inst   = insts  != null ? FindPtr(ref *insts,                  0x0063AE21_0B000000ul) : null;
                    Collider*                                coll   = inst   != null ? inst->GetCollider() : null;
                    ImGui.Text("Collider present: " + (inst != null && inst->IsColliderActive()));
                }

                unsafe
                {
                    if (ImGui.CollapsingHeader("ToDo Director"))
                    {
                        ContentDirector* cd = EventFramework.Instance()->GetContentDirector();
                        if (cd != null)
                        {
                            StdVector<DirectorTodo>* todo = cd->GetDirectorTodos();
                            if (todo != null)
                            {
                                for (int i = 0; i < todo->Count; i++)
                                {
                                    DirectorTodo item = (*todo)[i];
                                    ImGuiEx.Text($"{item.Text} - {item.CurrentCount}/{item.NeededCount} - {item.NeededPercentage}% ~ {item.Enabled} - {item.Type} - Complete: {item.Complete} - {item.CurrentCount == item.NeededCount}");
                                }
                            }
                        }
                    }
                }

                if (ImGui.CollapsingHeader("XBM"))
                {
                    ImGui.Indent();
                    if (ImGui.CollapsingHeader("XBMBestiary"))
                        unsafe
                        {
                            if (GenericHelpers.TryGetAddonByName("XBMMonsterNotebook", out AtkUnitBase* addon))
                            {
                                ReaderXBMMonsterNotebook x = new(addon);

                                ImGui.Text("Page: " + (x.CurrentPage + 1) + "/" + x.PageCount);

                                foreach (ReaderXBMMonsterNotebook.MonsterEntry entry in x.CurrentPageEntries)
                                    ImGui.Text($"Mob: {entry.Number} | {entry.Caught}");
                            }
                        }

                    if (ImGui.CollapsingHeader("XBMShop"))
                        unsafe
                        {
                            if (GenericHelpers.TryGetAddonByName("XBMContentsItemShop", out AtkUnitBase* addon))
                            {
                                ReaderXBMContentsItemShop x = new(addon);
                                ImGui.Text($"Coins: {x.Coins}");
                                ImGui.Text("Stock:");
                                ImGui.Indent();
                                foreach (ReaderXBMContentsItemShop.StockEntry entry in x.StockEntries)
                                    ImGui.Text($"Shop: {entry.Listed} | {entry.Item} | {entry.PriceString} | {entry.Price} | {entry.Discounted} | {entry.Bought}");
                                ImGui.Unindent();

                                ImGui.Text("Gear:");
                                ImGui.Indent();
                                foreach (ReaderXBMContentsItemShop.ItemEntry entry in x.ItemEntries)
                                    ImGui.Text($"Gear: {entry.Unk0} | {entry.Sellable} | {entry.IconId} | {entry.Id} | {entry.Name}");
                                ImGui.Unindent();

                                ImGui.Text("Owned:");
                                ImGui.Indent();
                                foreach (ReaderXBMContentsItemShop.GearEntry entry in x.OwnedEntriesOwned)
                                    ImGui.Text($"Owned: {entry.Owned} | {entry.Unk2} | {entry.Id} | {entry.Name}");
                                ImGui.Unindent();
                            }
                        }

                    if (ImGui.CollapsingHeader("XBMParty"))
                        unsafe
                        {
                            if (GenericHelpers.TryGetAddonByName("XBMPetParty", out AtkUnitBase* addon))
                            {
                                ReaderXBMPetParty x = new(addon);
                                ImGui.Text("Pets:");
                                ImGui.Indent();
                                foreach (ReaderXBMPetParty.MonsterEntry entry in x.TeamEntries)
                                    ImGui.Text($"Pet: {entry.Rank} | {entry.Name.GetText()} | {entry.Number} | {entry.IconId} | {entry.Disabled} | {entry.FedCurrent}/{entry.FedMax} | {string.Join(", ", entry.FedItems.Select(fe => fe.ItemId))}");
                                ImGui.Unindent();

                                ImGui.Text("Feed Item: " + x.FeedItem.ItemId);
                            }
                        }

                    if (ImGui.CollapsingHeader("XBMTreasure"))
                        unsafe
                        {
                            if (GenericHelpers.TryGetAddonByName("XBMContentsTreasure", out AtkUnitBase* addon))
                            {
                                ReaderXBMContentsTreasure x = new(addon);
                                ImGui.Text("Treasure:");
                                ImGui.Indent();

                                HashSet<uint> items = x.ItemEntriesValid.Select(ie => ie.Id).ToHashSet();
                                HashSet<uint> gear  = x.OwnedEntriesOwned.Select(ie => ie.Id).ToHashSet();

                                foreach (ReaderXBMContentsTreasure.TreasureChoice entry in x.TreasureChoices)
                                    ImGui.Text($"Treasure: {entry.Bought} | {entry.Item} | {entry.treasureIndex} | {entry.Unk0} | gear: {CrucibleItemData.ShopGear.Contains(entry.Item)} | item: {CrucibleItemData.ShopHealing.Contains(entry.Item)} | {!entry.Bought &&
                                        (!CrucibleItemData.ShopGear.Contains(entry.Item)    || (gear.Count  < 10 && !gear.Contains(entry.Item)))                                                                                                                      &&
                                        (!CrucibleItemData.ShopHealing.Contains(entry.Item) || (items.Count < 10 && !items.Contains(entry.Item)))}");
                                ImGui.Unindent();
                            }
                        }

                    if (ImGui.CollapsingHeader("XBMBooty"))
                        unsafe
                        {
                            if (GenericHelpers.TryGetAddonByName("XBMContentsBooty", out AtkUnitBase* addon))
                            {
                                ReaderXBMContentsBooty x = new(addon);
                                ImGui.Text("Loot:");
                                ImGui.Indent();
                                foreach (ReaderXBMContentsBooty.LootChoice entry in x.LootChoices)
                                    ImGui.Text($"Loot: {entry.Item} | {entry.Taken} | {entry.lootIndex}");
                                ImGui.Unindent();

                            }
                        }

                    if (ImGui.CollapsingHeader("XBMResult"))
                        unsafe
                        {
                            if (GenericHelpers.TryGetAddonByName("XBMResult", out AtkUnitBase* addon))
                            {
                                ReaderXBMResult x = new(addon);
                                ImGui.Text("Results:");

                                ImGui.Text("Performance: " + x.Performance);
                                ImGui.Text("Moves: "       + x.MovesMadeScore);
                                ImGui.Text("Enemy: "       + x.EnemyScore);
                                ImGui.Text("Elite Enemy: " + x.EliteScore);
                                ImGui.Text("Remaining HP: " + x.RemainingHP);
                                ImGui.SameLine();
                                ImGui.Text("| Score: " + x.RemainingHPScore);
                                ImGui.NewLine();
                                ImGui.Text("Bonus Score: " + x.BonusBoostScore);
                                ImGui.Indent();
                                foreach (ReaderXBMResult.BonusBoostEntry entry in x.BonusBoostEntries)
                                    ImGui.Text($"Bonus Boost: {entry.Name} | {entry.Score}");
                                ImGui.Unindent();

                                ImGui.Text("Loot");
                                ImGui.Indent();
                                foreach (ReaderXBMResult.LootEntry entry in x.LootEntries)
                                    ImGui.Text($"Bonus Boost: {entry.Name} | {entry.Count}x | {entry.ItemId}");
                                ImGui.Unindent();

                                ImGui.Text("Beasts");
                                ImGui.Indent();
                                foreach (ReaderXBMResult.BeastEntry entry in x.BeastEntries)
                                    ImGui.Text($"Beast: {entry.Number} | XP: {entry.PrevXP} -> {entry.NewXP} | Rank: {entry.PrevRank} -> {entry.NewRank}");
                                ImGui.Unindent();
                            }
                        }

                    ImGui.Unindent();
                }

                if (ImGui.CollapsingHeader("Sheet Check"))
                {
                    ImGuiEx.Text($"{typeof(Achievement).Assembly.GetTypes().Where(x => x.FullName?.StartsWith("Lumina.Excel.Sheets") ?? false).
                                                        Select(x => (x, x.GetProperties().Where(f => f.PropertyType.Name == "RowRef`1" && f.PropertyType.GenericTypeArguments[0] == typeof(Tutorial)))).
                                                        Where(x => x.Item2.Any()).
                                                        Select(x => $"{x.x} references {x.Item2.Select(pi => pi.Name).Print(", ")}").Print("\n")}");
                }
            }
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool dutyConfigHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.Header"), dutyConfigHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (dutyConfigHeader)
            dutyConfigHeaderSelected = !dutyConfigHeaderSelected;

        if (dutyConfigHeaderSelected == true)
        {
            ImGui.Columns(2, "##DutyConfigHeaderColumns");
            bool exitDuty = Configuration.DutyConfig.AutoExitDuty;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.AutoLeaveDuty"), ref exitDuty))
            {
                Configuration.DutyConfig.AutoExitDuty = exitDuty;
                ConfigurationProfileV2.Save();
            }
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.AutoLeaveDutyHelp"));

            ImGui.NextColumn();
            bool onlyExitWhenDutyDone = Configuration.DutyConfig.OnlyExitWhenDutyDone;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.BlockLeavingDuty"), ref onlyExitWhenDutyDone))
            {
                Configuration.DutyConfig.OnlyExitWhenDutyDone = onlyExitWhenDutyDone;
                ConfigurationProfileV2.Save();
            }

            //ImGuiComponents.HelpMarker("Blocks leaving dungeon before duty is completed");
            ImGui.Columns(1);
            bool manageRotations = Configuration.DutyConfig.AutoManageRotationPluginState;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.AutoManageRotation"), ref manageRotations))
            {
                Configuration.DutyConfig.AutoManageRotationPluginState = manageRotations;
                ConfigurationProfileV2.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.AutoManageRotationHelp"));

            using (ImRaii.Disabled(!Configuration.DutyConfig.AutoManageRotationPluginState))
            {
                ImGui.SameLine(0, 5);
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 3 * 2);
                if (ImGui.BeginCombo("##RotationPluginSelection", Configuration.DutyConfig.RotationPlugin.ToCustomString()))
                {
                    foreach (RotationPlugin rotationPlugin in Enum.GetValues(typeof(RotationPlugin)).Cast<RotationPlugin>().Reverse())
                        using (rotationPlugin.HasFlag(RotationPlugin.All) ? (IDisposable?) null : ImGuiHelper.RequiresPlugin(rotationPlugin switch
                               {
                                   RotationPlugin.BossMod => ExternalPlugin.BossMod,
                                   RotationPlugin.RotationSolverReborn => ExternalPlugin.RotationSolverReborn,
                                   RotationPlugin.WrathCombo => ExternalPlugin.WrathCombo,
                                   _ => throw new ArgumentOutOfRangeException()
                               }, "RotationPluginSelection", inline: true))
                        {
                            if (ImGui.Selectable(rotationPlugin.ToCustomString(), Configuration.DutyConfig.RotationPlugin == rotationPlugin, ImGuiSelectableFlags.AllowItemOverlap))
                            {
                                Configuration.DutyConfig.RotationPlugin = rotationPlugin;
                                ConfigurationProfileV2.Save();
                            }
                        }

                    ImGui.EndCombo();
                }
            }


            if (Configuration.DutyConfig.AutoManageRotationPluginState)
            {
                if (Configuration.DutyConfig.RotationPlugin is RotationPlugin.WrathCombo or RotationPlugin.All && Wrath_IPCSubscriber.IsEnabled)
                    using (ImGuiHelper.RequiresPlugin(ExternalPlugin.WrathCombo, "WrathConfig", write: false))
                    {
                        ImGui.Indent();
                        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                        bool wrathSettingHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.Wrath.Header"), wrathSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
                        ImGui.PopStyleVar();
                        if (ImGui.IsItemHovered())
                            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (wrathSettingHeader)
                            wrathSettingHeaderSelected = !wrathSettingHeaderSelected;

                        if (wrathSettingHeaderSelected)
                        {
                            bool wrath_AutoSetupJobs = Configuration.DutyConfig.Wrath.AutoSetupJobs;
                            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.Wrath.AutoSetupJobs"), ref wrath_AutoSetupJobs))
                            {
                                Configuration.DutyConfig.Wrath.AutoSetupJobs = wrath_AutoSetupJobs;
                                ConfigurationProfileV2.Save();
                            }

                            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.Wrath.AutoSetupJobsHelp"));

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.Wrath.TargetingTank"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigWrathTargetingTank", Configuration.DutyConfig.Wrath.TargetingTank.ToCustomString()))
                            {
                                foreach (WrathCombo.API.Enum.DPSRotationMode targeting in Enum.GetValues<WrathCombo.API.Enum.DPSRotationMode>())
                                {
                                    if (targeting == WrathCombo.API.Enum.DPSRotationMode.Tank_Target)
                                        continue;

                                    if (ImGui.Selectable(targeting.ToCustomString(), Configuration.DutyConfig.Wrath.TargetingTank == targeting))
                                    {
                                        Configuration.DutyConfig.Wrath.TargetingTank = targeting;
                                        ConfigurationProfileV2.Save();
                                    }
                                }

                                ImGui.EndCombo();
                            }

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.Wrath.TargetingNonTank"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigWrathTargetingNonTank", Configuration.DutyConfig.Wrath.TargetingNonTank.ToCustomString()))
                            {
                                foreach (WrathCombo.API.Enum.DPSRotationMode targeting in Enum.GetValues<WrathCombo.API.Enum.DPSRotationMode>())
                                    if (ImGui.Selectable(targeting.ToCustomString(), Configuration.DutyConfig.Wrath.TargetingNonTank == targeting))
                                    {
                                        Configuration.DutyConfig.Wrath.TargetingNonTank = targeting;
                                        ConfigurationProfileV2.Save();
                                    }

                                ImGui.EndCombo();
                            }

                            ImGui.Separator();
                        }

                        ImGui.Unindent();
                    }

                if (Configuration.DutyConfig.RotationPlugin is RotationPlugin.RotationSolverReborn or RotationPlugin.All && RSR_IPCSubscriber.IsEnabled)
                    using (ImGuiHelper.RequiresPlugin(ExternalPlugin.RotationSolverReborn, "RSRConfig", write: false))
                    {
                        ImGui.Indent();
                        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                        bool rsrSettingHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.RSR.Header"), rsrSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
                        ImGui.PopStyleVar();
                        if (ImGui.IsItemHovered())
                            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (rsrSettingHeader)
                            rsrSettingHeaderSelected = !rsrSettingHeaderSelected;

                        if (rsrSettingHeaderSelected)
                        {
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.RSR.EngageSettings"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigRSREngage", RSR_IPCSubscriber.GetHostileTypeDescription(Configuration.DutyConfig.RSR.TargetHostileType)))
                            {
                                foreach (RotationSolverRebornIPC.TargetHostileType hostileType in Enum.GetValues<RotationSolverRebornIPC.TargetHostileType>())
                                    if (ImGui.Selectable(RSR_IPCSubscriber.GetHostileTypeDescription(hostileType), hostileType == Configuration.DutyConfig.RSR.TargetHostileType))
                                    {
                                        Configuration.DutyConfig.RSR.TargetHostileType = hostileType;
                                        ConfigurationProfileV2.Save();
                                    }

                                ImGui.EndCombo();
                            }


                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.RSR.TargetingTank"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigRSRTargetTank", Configuration.DutyConfig.RSR.TargetingTypeTank.ToCustomString()))
                            {
                                foreach (RotationSolverRebornIPC.TargetingType targetingType in Enum.GetValues<RotationSolverRebornIPC.TargetingType>())
                                    if (ImGui.Selectable(targetingType.ToCustomString(), targetingType == Configuration.DutyConfig.RSR.TargetingTypeTank))
                                    {
                                        Configuration.DutyConfig.RSR.TargetingTypeTank = targetingType;
                                        ConfigurationProfileV2.Save();
                                    }

                                ImGui.EndCombo();
                            }

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.RSR.TargetingNonTank"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigRSRTargetNonTank", Configuration.DutyConfig.RSR.TargetingTypeNonTank.ToCustomString()))
                            {
                                foreach (RotationSolverRebornIPC.TargetingType targetingType in Enum.GetValues<RotationSolverRebornIPC.TargetingType>())
                                    if (ImGui.Selectable(targetingType.ToCustomString(), targetingType == Configuration.DutyConfig.RSR.TargetingTypeNonTank))
                                    {
                                        Configuration.DutyConfig.RSR.TargetingTypeNonTank = targetingType;
                                        ConfigurationProfileV2.Save();
                                    }

                                ImGui.EndCombo();
                            }

                            ImGui.Separator();
                        }

                        ImGui.Unindent();
                    }
            }

            bool manageBossModAISettings = Configuration.DutyConfig.AutoManageBossModAISettings;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.AutoManageBMAI"), ref manageBossModAISettings))
            {
                Configuration.DutyConfig.AutoManageBossModAISettings = manageBossModAISettings;
                ConfigurationProfileV2.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.AutoManageBMAIHelp"));

            if (Configuration.DutyConfig.AutoManageBossModAISettings)
            {
                ImGui.Indent();
                ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                bool bmaiSettingHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.BMAI.Header"), bmaiSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
                ImGui.PopStyleVar();
                if (ImGui.IsItemHovered())
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (bmaiSettingHeader)
                    bmaiSettingHeaderSelected = !bmaiSettingHeaderSelected;
            
                if (bmaiSettingHeaderSelected == true)
                {
                    if (ImGui.Button(Loc.Get("ConfigTab.Duty.BMAI.UpdatePresets")))
                    {
                        BossMod_IPCSubscriber.RefreshPreset("AutoDuty", Resources.AutoDutyPreset);
                        BossMod_IPCSubscriber.RefreshPreset("AutoDuty Passive", Resources.AutoDutyPassivePreset);
                    }

                    bool updatePresetsAutomatically = Configuration.DutyConfig.BossMod.UpdatePresetsAutomatically;
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.BMAI.UpdatePresetsAuto"), ref updatePresetsAutomatically))
                    {
                        Configuration.DutyConfig.BossMod.UpdatePresetsAutomatically = updatePresetsAutomatically;
                        ConfigurationProfileV2.Save();
                    }

                    bool distanceToTargetRoleBased = Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleBased;
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.BMAI.MaxDistanceRoleBased"), ref distanceToTargetRoleBased))
                    {
                        Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleBased = distanceToTargetRoleBased;
                        ConfigurationProfileV2.Save();
                    }
                    using (ImRaii.Disabled(Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleBased))
                    {
                        ImGui.PushItemWidth(195 * ImGuiHelpers.GlobalScale);
                        float distanceToTargetFloat = Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat;
                        if (ImGui.SliderFloat(Loc.Get("ConfigTab.Duty.BMAI.MaxDistance"), ref distanceToTargetFloat, 1, 30))
                        {
                            Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat = Math.Clamp(distanceToTargetFloat, 1, 30);
                            ConfigurationProfileV2.Save();
                        }

                        float distanceToTargetAoEFloat = Configuration.DutyConfig.BossMod.MaxDistanceToTargetAoEFloat;
                        if (ImGui.SliderFloat(Loc.Get("ConfigTab.Duty.BMAI.MaxDistanceAoE"), ref distanceToTargetAoEFloat, 1, 10))
                        {
                            Configuration.DutyConfig.BossMod.MaxDistanceToTargetAoEFloat = Math.Clamp(distanceToTargetAoEFloat, 1, 10);
                            ConfigurationProfileV2.Save();
                        }
                        ImGui.PopItemWidth();
                    }
                    using (ImRaii.Disabled(!Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleBased))
                    {
                        ImGui.PushItemWidth(195 * ImGuiHelpers.GlobalScale);

                        float distanceToTargetRoleMelee = Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleMelee;
                        if (ImGui.SliderFloat(Loc.Get("ConfigTab.Duty.BMAI.MaxDistanceMelee"), ref distanceToTargetRoleMelee, 1, 30))
                        {
                            Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleMelee = Math.Clamp(distanceToTargetRoleMelee, 1, 30);
                            ConfigurationProfileV2.Save();
                        }

                        float distanceToTargetRoleRanged = Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleRanged;
                        if (ImGui.SliderFloat(Loc.Get("ConfigTab.Duty.BMAI.MaxDistanceRanged"), ref distanceToTargetRoleRanged, 1, 30))
                        {
                            Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleRanged = Math.Clamp(distanceToTargetRoleRanged, 1, 30);
                            ConfigurationProfileV2.Save();
                        }
                        ImGui.PopItemWidth();
                    }

                    bool positionalRoleBased = Configuration.DutyConfig.BossMod.PositionalRoleBased;
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.BMAI.PositionalRoleBased"), ref positionalRoleBased))
                    {
                        Configuration.DutyConfig.BossMod.PositionalRoleBased = positionalRoleBased;
                        BMRoleChecks();
                        ConfigurationProfileV2.Save();
                    }
                    using (ImRaii.Disabled(Configuration.DutyConfig.BossMod.PositionalRoleBased))
                    {
                        ImGui.SameLine(0, 10);
                        if (ImGui.Button(Configuration.DutyConfig.BossMod.PositionalEnum.ToLocalizedString()))
                            ImGui.OpenPopup("PositionalPopup");
            
                        if (ImGui.BeginPopup("PositionalPopup"))
                        {
                            foreach (Positional positional in Enum.GetValues(typeof(Positional)))
                                if (ImGui.Selectable(positional.ToLocalizedString(), Configuration.DutyConfig.BossMod.PositionalEnum == positional))
                                {
                                    Configuration.DutyConfig.BossMod.PositionalEnum = positional;
                                    ConfigurationProfileV2.Save();
                                }

                            ImGui.EndPopup();
                        }
                    }
                    if (ImGui.Button(Loc.Get("ConfigTab.Duty.BMAI.UseDefaultSettings")))
                    {
                        Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleBased = true;
                        Configuration.DutyConfig.BossMod.PositionalRoleBased = true;
                        ConfigurationProfileV2.Save();
                    }
                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.BMAI.UseDefaultSettingsHelp"));

                    ImGui.Separator();
                }
                ImGui.Unindent();
            }

            bool vnavAlignCamera = Configuration.DutyConfig.AutoManageVnavAlignCamera;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.AutoManageVnavCamera"), ref vnavAlignCamera))
            {
                Configuration.DutyConfig.AutoManageVnavAlignCamera = vnavAlignCamera;
                ConfigurationProfileV2.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.AutoManageVnavCameraHelp"));

            bool lootTreasure = Configuration.DutyConfig.LootTreasure;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.LootTreasure"), ref lootTreasure))
            {
                Configuration.DutyConfig.LootTreasure = lootTreasure;
                ConfigurationProfileV2.Save();
            }

            if (Configuration.DutyConfig.LootTreasure)
            {
                ImGui.Indent();
                ImGui.Text(Loc.Get("ConfigTab.Duty.SelectMethod"));
                ImGui.SameLine(0, 5);
                ImGui.PushItemWidth(200 * ImGuiHelpers.GlobalScale);
                if (ImGui.BeginCombo("##ConfigLootMethod", Configuration.DutyConfig.LootMethodEnum.ToCustomString()))
                {
                    foreach (LootMethod lootMethod in Enum.GetValues(typeof(LootMethod)))
                        using (lootMethod == LootMethod.Pandora ? ImGuiHelper.RequiresPlugin(ExternalPlugin.Pandora, $"{lootMethod}_Looting", inline: true) : (IDisposable?)null)
                        {
                            if (ImGui.Selectable(lootMethod.ToCustomString(), Configuration.DutyConfig.LootMethodEnum == lootMethod))
                            {
                                Configuration.DutyConfig.LootMethodEnum = lootMethod;
                                ConfigurationProfileV2.Save();
                            }
                        }

                    ImGui.EndCombo();
                }

                bool lootBossTreasureOnly = Configuration.DutyConfig.LootBossTreasureOnly;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.LootBossTreasureOnly"), ref lootBossTreasureOnly))
                {
                    Configuration.DutyConfig.LootBossTreasureOnly = lootBossTreasureOnly;
                    ConfigurationProfileV2.Save();
                }

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.LootBossTreasureOnlyHelp"));
                ImGui.PopItemWidth();
                ImGui.Unindent();
            }
            ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
            int stuckMinStuckTime = Configuration.DutyConfig.Stuck.MinStuckTime;
            if (ImGui.InputInt(Loc.Get("ConfigTab.Duty.MinStuckTime"), ref stuckMinStuckTime, 10, 100))
            {
                Configuration.DutyConfig.Stuck.MinStuckTime = Math.Max(250, stuckMinStuckTime);
                ConfigurationProfileV2.Save();
            }
            ImGui.Indent();

            bool stuckOnStep = Configuration.DutyConfig.Stuck.StuckOnStep;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.ResetStuckOnStep"), ref stuckOnStep))
            {
                Configuration.DutyConfig.Stuck.StuckOnStep = stuckOnStep;
                ConfigurationProfileV2.Save();
            }

            bool rebuildNavmeshOnStuck = Configuration.DutyConfig.Stuck.RebuildNavmeshOnStuck;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.RebuildNavmeshOnStuck"), ref rebuildNavmeshOnStuck))
            {
                Configuration.DutyConfig.Stuck.RebuildNavmeshOnStuck = rebuildNavmeshOnStuck;
                ConfigurationProfileV2.Save();
            }

            if (Configuration.DutyConfig.Stuck.RebuildNavmeshOnStuck)
            {
                ImGui.SameLine();
                int rebuildX = Configuration.DutyConfig.Stuck.RebuildNavmeshAfterStuckXTimes;
                if(ImGui.InputInt(Loc.Get("ConfigTab.Duty.RebuildNavmeshTimes") + "###RebuildTimesConfig", ref rebuildX, 1))
                {
                    Configuration.DutyConfig.Stuck.RebuildNavmeshAfterStuckXTimes = (byte) Math.Clamp(rebuildX, byte.MinValue + 2, byte.MaxValue);
                    ConfigurationProfileV2.Save();
                }
            }

            bool stuckReturn = Configuration.DutyConfig.Stuck.StuckReturn;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.UseReturnWhenStuck") + "###UseReturnConfig", ref stuckReturn))
            {
                Configuration.DutyConfig.Stuck.StuckReturn = stuckReturn;
                ConfigurationProfileV2.Save();
            }

            if (Configuration.DutyConfig.Stuck.StuckReturn)
            {
                ImGui.SameLine();
                int returnX = Configuration.DutyConfig.Stuck.StuckReturnX;
                if (ImGui.InputInt(Loc.Get("ConfigTab.Duty.ReturnTimes") + "###ReturnTimesConfig", ref returnX, 1))
                {
                    Configuration.DutyConfig.Stuck.StuckReturnX = (byte)Math.Clamp(returnX, byte.MinValue + 2, byte.MaxValue);
                    ConfigurationProfileV2.Save();
                }
            }

            if (Configuration is { DutyConfig.Stuck: { RebuildNavmeshOnStuck: true, StuckReturn: true } } && 
                Configuration.DutyConfig.Stuck.RebuildNavmeshAfterStuckXTimes >= Configuration.DutyConfig.Stuck.StuckReturnX)
            {
                Configuration.DutyConfig.Stuck.StuckReturnX = Configuration.DutyConfig.Stuck.RebuildNavmeshAfterStuckXTimes + 1;
                ConfigurationProfileV2.Save();
            }


            ImGui.Unindent();

            bool pathDrawEnabled = Configuration.DutyConfig.PathDrawEnabled;
            if(ImGui.Checkbox(Loc.Get("ConfigTab.Duty.DrawPath"), ref pathDrawEnabled))
            {
                Configuration.DutyConfig.PathDrawEnabled = pathDrawEnabled;
                ConfigurationProfileV2.Save();
            }
            ImGui.PopItemWidth();
            if (Configuration.DutyConfig.PathDrawEnabled)
            {
                ImGui.Indent();
                ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                int pathDrawStepCount = Configuration.DutyConfig.PathDrawStepCount;
                if (ImGui.InputInt(Loc.Get("ConfigTab.Duty.DrawPathSteps"), ref pathDrawStepCount, 1))
                {
                    Configuration.DutyConfig.PathDrawStepCount = Math.Max(1, pathDrawStepCount);
                    ConfigurationProfileV2.Save();
                }
                ImGui.PopItemWidth();
                ImGui.Unindent();
            }

            bool disableRenderWhileActive = Configuration.DutyConfig.DisableRenderWhileActive;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.DisableRenderWhileActive"), ref disableRenderWhileActive))
            {
                Configuration.DutyConfig.DisableRenderWhileActive = disableRenderWhileActive;
                ConfigurationProfileV2.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.DisableRenderWhileActiveHelp"));


            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
            bool w2wSettingHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.W2W.Header"), w2wSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (w2wSettingHeader)
                w2wSettingHeaderSelected = !w2wSettingHeaderSelected;

            if (w2wSettingHeaderSelected)
            {
                bool treatUnsyncAsW2W = Configuration.DutyConfig.TreatUnsyncAsW2W;
                if(ImGui.Checkbox(Loc.Get("ConfigTab.Duty.W2W.TreatUnsyncAsW2W"), ref treatUnsyncAsW2W))
                {
                    Configuration.DutyConfig.TreatUnsyncAsW2W = treatUnsyncAsW2W;
                    ConfigurationProfileV2.Save();
                }

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.W2W.TreatUnsyncAsW2WHelp"));


                ImGui.BeginListBox("##W2WConfig", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 300));
                JobWithRole w2WJobs = Configuration.DutyConfig.W2WJobs;
                if(JobWithRoleHelper.DrawCategory(JobWithRole.All, ref w2WJobs))
                {
                    Configuration.DutyConfig.W2WJobs = w2WJobs;
                    ConfigurationProfileV2.Save();
                }

                ImGui.EndListBox();
            }

            bool overridePartyValidation = Configuration.DutyConfig.OverridePartyValidation;
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.OverridePartyValidation"), ref overridePartyValidation))
            {
                Configuration.DutyConfig.OverridePartyValidation = overridePartyValidation;
                ConfigurationProfileV2.Save();
            }
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.OverridePartyValidationHelp"));


            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
            bool advModeHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.Advanced.Header"), advModeHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (advModeHeader)
                advModeHeaderSelected = !advModeHeaderSelected;

            if (advModeHeaderSelected == true)
            {
                bool usingAlternativeRotationPlugin = Configuration.DutyConfig.UsingAlternativeRotationPlugin;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.Advanced.UsingAltRotation"), ref usingAlternativeRotationPlugin))
                {
                    Configuration.DutyConfig.UsingAlternativeRotationPlugin = usingAlternativeRotationPlugin;
                    ConfigurationProfileV2.Save();
                }
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.Advanced.UsingAltRotationHelp"));

                bool usingAlternativeMovementPlugin = Configuration.DutyConfig.UsingAlternativeMovementPlugin;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.Advanced.UsingAltMovement"), ref usingAlternativeMovementPlugin))
                {
                    Configuration.DutyConfig.UsingAlternativeMovementPlugin = usingAlternativeMovementPlugin;
                    ConfigurationProfileV2.Save();
                }
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.Advanced.UsingAltMovementHelp"));

                bool usingAlternativeBossPlugin = Configuration.DutyConfig.UsingAlternativeBossPlugin;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.Advanced.UsingAltBoss"), ref usingAlternativeBossPlugin))
                {
                    Configuration.DutyConfig.UsingAlternativeBossPlugin = usingAlternativeBossPlugin;
                    ConfigurationProfileV2.Save();
                }
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.Advanced.UsingAltBossHelp"));
            }
        }

        //Start of Pre-Loop Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool preLoopHeader = ImGui.Selectable(Loc.Get("ConfigTab.PreLoop.Header"), preLoopHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (preLoopHeader)
            preLoopHeaderSelected = !preLoopHeaderSelected;

        if (preLoopHeaderSelected)
        {
            bool preLoopEnabled = Configuration.Loop.Pre.Enabled;
            if (ImGui.Checkbox($"{Loc.Get("ConfigTab.PreLoop.Enable")}###PreLoopEnable", ref preLoopEnabled))
            {
                Configuration.Loop.Pre.Enabled = preLoopEnabled;
                ConfigurationProfileV2.Save();
            }

            if(ImGui.Button(Loc.Get("ConfigTab.PreLoop.ActionsCopyFromBetweenLoop")))
            {
                Configuration.Loop.Pre.Actions = Configuration.Loop.Between.Actions.JSONClone(ConfigurationMain.JsonSerializerSettings);
                Configuration.Loop.Pre.Actions.Add(new PlaylistPreLoopActionConfig());
                Configuration.Loop.Pre.Actions.RemoveAll(lac => lac is PlaylistSwitchLoopActionConfig);
                ConfigurationProfileV2.Save();
            }

            using (ImRaii.Disabled(!Configuration.Loop.Pre.Enabled))
                Configuration.Loop.Pre.Actions.OnGui("PreLoopActions", LoopActionCategory.Pre);
        }

        //Between Loop Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool betweenLoopHeader = ImGui.Selectable(Loc.Get("ConfigTab.BetweenLoop.Header"), betweenLoopHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (betweenLoopHeader)
            betweenLoopHeaderSelected = !betweenLoopHeaderSelected;

        if (betweenLoopHeaderSelected)
        {
            ImGui.Columns(2, "##BetweenLoopHeaderColumns");

            bool betweenEnabled = Configuration.Loop.Between.Enabled;
            if (ImGui.Checkbox($"{Loc.Get("ConfigTab.BetweenLoop.Enable")}###BetweenLoopEnable", ref betweenEnabled))
            {
                Configuration.Loop.Between.Enabled = betweenEnabled;
                ConfigurationProfileV2.Save();
            }

            using (ImRaii.Disabled(!Configuration.Loop.Between.Enabled))
            {
                ImGui.NextColumn();

                bool executeLastLoop = Configuration.Loop.Between.ExecuteLastLoop;
                if (ImGui.Checkbox($"{Loc.Get("ConfigTab.BetweenLoop.RunOnLastLoop")}###BetweenLoopEnableLastLoop", ref executeLastLoop))
                {
                    Configuration.Loop.Between.ExecuteLastLoop = executeLastLoop;
                    ConfigurationProfileV2.Save();
                }

                ImGui.Columns(1);

                ImGui.Separator();
                Configuration.Loop.Between.Actions.OnGui("BetweenLoopActions");
                ImGui.Separator();

            }
        }

        //Loop Termination Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool terminationHeader = ImGui.Selectable(Loc.Get("ConfigTab.Termination.Header"), terminationHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (terminationHeader)
            terminationHeaderSelected = !terminationHeaderSelected;
        if (terminationHeaderSelected == true)
        {
            bool terminationEnabled = Configuration.Loop.Termination.Enabled;
            if (ImGui.Checkbox($"{Loc.Get("ConfigTab.Termination.Enable")}###TerminationEnable", ref terminationEnabled))
            {
                Configuration.Loop.Termination.Enabled = terminationEnabled;
                ConfigurationProfileV2.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Termination.Help"));
            using (ImRaii.Disabled(!Configuration.Loop.Termination.Enabled))
            {
                ImGui.Separator();

                bool terminationStopLevel = Configuration.Loop.Termination.StopLevel;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopAtLevel"), ref terminationStopLevel))
                {
                    Configuration.Loop.Termination.StopLevel = terminationStopLevel;
                    ConfigurationProfileV2.Save();
                }

                if (Configuration.Loop.Termination.StopLevel)
                {
                    ImGui.SameLine(0, 10);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);

                    int stopLevelInt = Configuration.Loop.Termination.StopLevelInt;
                    if(MakeSliderOrInput(ref stopLevelInt, "##Level", 1, 100))
                    {
                        Configuration.Loop.Termination.StopLevelInt = Math.Clamp(stopLevelInt, 1, 100);
                        ConfigurationProfileV2.Save();
                    }

                    ImGui.PopItemWidth();
                }

                bool restedXP = Configuration.Loop.Termination.StopNoRestedXP;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopNoRestedXP"), ref restedXP))
                {
                    Configuration.Loop.Termination.StopNoRestedXP = restedXP;
                    ConfigurationProfileV2.Save();
                }

                bool terminationiLvl = Configuration.Loop.Termination.TerminationiLvl;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopAtILevel"), ref terminationiLvl))
                {
                    Configuration.Loop.Termination.TerminationiLvl = terminationiLvl;
                    ConfigurationProfileV2.Save();
                }

                if (Configuration.Loop.Termination.TerminationiLvl)
                {
                    ImGui.SameLine(0, 10);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    int terminationiLvlInt = Configuration.Loop.Termination.TerminationiLvlInt;

                    if (MakeSliderOrInput(ref terminationiLvlInt, "##ItemLevel", 1, 100))
                    {
                        Configuration.Loop.Termination.TerminationiLvlInt = Math.Clamp(terminationiLvlInt, 1, 100);
                        ConfigurationProfileV2.Save();
                    }
                    ImGui.PopItemWidth();
                }


                bool stopItemQty = Configuration.Loop.Termination.StopItemQty;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopAtItemQty"), ref stopItemQty))
                {
                    Configuration.Loop.Termination.StopItemQty = stopItemQty;
                    ConfigurationProfileV2.Save();
                }

                if (Configuration.Loop.Termination.StopItemQty)
                {
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 125 * ImGuiHelpers.GlobalScale);
                    if (ImGui.BeginCombo(Loc.Get("ConfigTab.Termination.SelectItem"), stopItemQtySelectedItem.Value))
                    {
                        ImGui.InputTextWithHint(Loc.Get("ConfigTab.Termination.ItemName"), Loc.Get("ConfigTab.Termination.ItemNameHint"), ref stopItemQtyItemNameInput, 1000);
                        foreach (KeyValuePair<uint, Item> item in Items.Where(x => x.Value.Name.ToString().Contains(stopItemQtyItemNameInput, StringComparison.InvariantCultureIgnoreCase))!)
                            if (ImGui.Selectable($"{item.Value.Name.ToString()}"))
                                stopItemQtySelectedItem = new KeyValuePair<uint, string>(item.Key, item.Value.Name.ToString());
                        ImGui.EndCombo();
                    }
                    ImGui.PopItemWidth();
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 220 * ImGuiHelpers.GlobalScale);
                    int stopItemQtyInt = Configuration.Loop.Termination.StopItemQtyInt;
                    if (ImGui.InputInt(Loc.Get("ConfigTab.Termination.Quantity"), ref stopItemQtyInt, 1, 10))
                    {
                        Configuration.Loop.Termination.StopItemQtyInt = stopItemQtyInt;
                        ConfigurationProfileV2.Save();
                    }

                    ImGui.SameLine(0, 5);
                    using (ImRaii.Disabled(stopItemQtySelectedItem.Value.IsNullOrEmpty()))
                    {
                        if (ImGui.Button(Loc.Get("ConfigTab.Termination.AddItem")))
                        {
                            if (!Configuration.Loop.Termination.StopItemQtyItemDictionary.TryAdd(stopItemQtySelectedItem.Key, new KeyValuePair<string, int>(stopItemQtySelectedItem.Value, Configuration.Loop.Termination.StopItemQtyInt)))
                            {
                                Configuration.Loop.Termination.StopItemQtyItemDictionary.Remove(stopItemQtySelectedItem.Key);
                                Configuration.Loop.Termination.StopItemQtyItemDictionary.Add(stopItemQtySelectedItem.Key, new KeyValuePair<string, int>(stopItemQtySelectedItem.Value, Configuration.Loop.Termination.StopItemQtyInt));
                            }
                            ConfigurationProfileV2.Save();
                        }
                    }
                    ImGui.PopItemWidth();
                    if (!ImGui.BeginListBox("##ItemList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * Configuration.Loop.Termination.StopItemQtyItemDictionary.Count) + 5)))
                        return;

                    foreach (KeyValuePair<uint, KeyValuePair<string, int>> item in Configuration.Loop.Termination.StopItemQtyItemDictionary)
                    {
                        ImGui.Selectable($"{item.Value.Key} (Qty: {item.Value.Value})");
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            Configuration.Loop.Termination.StopItemQtyItemDictionary.Remove(item);
                            ConfigurationProfileV2.Save();
                        }
                    }
                    ImGui.EndListBox();
                    bool stopItemAll = Configuration.Loop.Termination.StopItemAll;
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopOnlyWhenAllItems"), ref stopItemAll))
                    {
                        Configuration.Loop.Termination.StopItemAll = stopItemAll;
                        ConfigurationProfileV2.Save();
                    }
                }

                using (ImGuiHelper.RequiresPlugin(ExternalPlugin.GlamourLog, "StopWhenDutyGatheredGlamourLog", inline: true))
                {
                    bool stopWhenDutyGathered = Configuration.Loop.Termination.StopWhenDutyGathered;
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopWhenDutyGathered"), ref stopWhenDutyGathered))
                    {
                        Configuration.Loop.Termination.StopWhenDutyGathered = stopWhenDutyGathered;
                        ConfigurationProfileV2.Save();
                    }
                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Termination.StopWhenDutyGatheredHelp"));
                }

                bool bluSpellsEnabled = Configuration.Loop.Termination.TerminationBLUSpellsEnabled;
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopBLUSpell"), ref bluSpellsEnabled))
                {
                    Configuration.Loop.Termination.TerminationBLUSpellsEnabled = bluSpellsEnabled;
                    ConfigurationProfileV2.Save();
                }

                if (Configuration.Loop.Termination.TerminationBLUSpellsEnabled)
                {
                    ImGui.Indent();

                    if(ImGui.BeginCombo("##TerminationBlueSpell", Loc.Get("ConfigTab.Termination.SelectBLUSpell")))
                    {
                        foreach (BLUHelper.BLUSpell bluSpell in BLUHelper.spells)
                        {
                            if (!BLUHelper.SpellUnlocked(bluSpell))
                                if (ImGui.Selectable($"({bluSpell.Entry}) {bluSpell.Name}"))
                                {
                                    Configuration.Loop.Termination.TerminationBLUSpells.Add(bluSpell.ID);
                                    Configuration.Loop.Termination.TerminationBLUSpells = [..Configuration.Loop.Termination.TerminationBLUSpells.OrderBy(sp => BLUHelper.spellsById[sp].Entry)];
                                    ConfigurationProfileV2.Save();
                                }
                        }

                        ImGui.EndCombo();
                    }

                    if (ImGui.BeginListBox("##TerminationBluSpellList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * Configuration.Loop.Termination.TerminationBLUSpells.Count) + 5)))
                    {
                        foreach (uint bluSpell in Configuration.Loop.Termination.TerminationBLUSpells)
                        {
                            BLUHelper.BLUSpell spell = BLUHelper.spellsById[bluSpell];
                            ImGui.Selectable($"({spell.Entry}) {spell.Name}");
                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            {
                                Configuration.Loop.Termination.TerminationBLUSpells.Remove(bluSpell);
                                ConfigurationProfileV2.Save();
                                return;
                            }
                        }
                        ImGui.EndListBox();
                    }

                    bool bluSpellsAll = Configuration.Loop.Termination.TerminationBLUSpellsAll;
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopOnlyWhenAllSpells"), ref bluSpellsAll))
                    {
                        Configuration.Loop.Termination.TerminationBLUSpellsAll = bluSpellsAll;
                        ConfigurationProfileV2.Save();
                    }

                    ImGui.Unindent();
                }

                bool inventoryFree = Configuration.Loop.Termination.TerminationInventoryFree;
                if(ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopWhenInventoryFull") + "###StopWhenInventoryFull", ref inventoryFree))
                {
                    Configuration.Loop.Termination.TerminationInventoryFree = inventoryFree;
                    ConfigurationProfileV2.Save();
                }

                if (Configuration.Loop.Termination.TerminationInventoryFree)
                {
                    ImGui.Indent();
                    ImGui.PushItemWidth(150f.Scale());

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(Loc.Get("ConfigTab.Termination.StopWhenInventoryFullSlots"));
                    ImGui.SameLine();
                    int inventoryFreeSlots = Configuration.Loop.Termination.TerminationInventoryFreeSlots;
                    if(ImGui.InputInt("###StopWhenInventoryFullSlotsInput", ref inventoryFreeSlots, 1, 5))
                    {
                        Configuration.Loop.Termination.TerminationInventoryFreeSlots = Math.Clamp(inventoryFreeSlots, 0, 139);
                        ConfigurationProfileV2.Save();
                    }

                    ImGui.PopItemWidth();
                    ImGui.Unindent();
                }

                ImGui.Separator();

                Configuration.Loop.Termination.Actions.OnGui("TerminationActions", LoopActionCategory.Termination);

                ImGui.Separator();

                ImGui.Text(Loc.Get("ConfigTab.Termination.OnCompletionOfAllLoops"));
                ImGui.SameLine(0, 10);
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.BeginCombo("##ConfigTerminationMethod", Configuration.Loop.Termination.TerminationMethodEnum.ToLocalizedString()))
                {
                    foreach (TerminationMode terminationMode in Enum.GetValues(typeof(TerminationMode)))
                        if (terminationMode != TerminationMode.Kill_PC || OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                            if (ImGui.Selectable(terminationMode.ToLocalizedString(), Configuration.Loop.Termination.TerminationMethodEnum == terminationMode))
                            {
                                Configuration.Loop.Termination.TerminationMethodEnum = terminationMode;
                                ConfigurationProfileV2.Save();
                            }

                    ImGui.EndCombo();
                }

                if (Configuration.Loop.Termination.TerminationMethodEnum is TerminationMode.Kill_Client or TerminationMode.Kill_PC or TerminationMode.Logout)
                {
                    ImGui.Indent();
                    bool keepActive = Configuration.Loop.Termination.TerminationKeepActive;
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.KeepTerminationActive"), ref keepActive))
                    {
                        Configuration.Loop.Termination.TerminationKeepActive = keepActive;
                        ConfigurationProfileV2.Save();
                    }

                    ImGui.Unindent();
                }
            }
        }

        ImGui.Separator();
        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));

        ImGui.SetItemAllowOverlap();
        if (ImGui.Selectable(Loc.Get("ConfigTab.Multiboxing.Header"), multiboxHeaderSelected, ImGuiSelectableFlags.DontClosePopups))
            multiboxHeaderSelected = !multiboxHeaderSelected;

        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (multiboxHeaderSelected)
        {
            ImGui.TextColored(GradientColor.Get(ImGuiHelper.ExperimentalColor, ImGuiHelper.ExperimentalColor2, 500), Loc.Get("ConfigTab.Multiboxing.ExperimentalWarning"));

            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step1"));
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step2"));
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step3"));
            ImGui.Separator();

            uint text     = ImGui.GetColorU32(ImGuiCol.Text);
            uint disabled = ImGui.GetColorU32(ImGuiCol.TextDisabled);

            TransportType transportType = MultiboxUtility.Config.TransportType;
            using (ImRaii.PushColor(ImGuiCol.Text, transportType == TransportType.NamedPipe ? text : disabled))
            {
                ImGui.TextWrapped(Loc.Get("ConfigTab.Multiboxing.NamedPipes"));
                ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step4Pipes"));
                ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step5Pipes"));
            }
            ImGui.Separator();
            using (ImRaii.PushColor(ImGuiCol.Text, transportType == TransportType.Tcp ? text : disabled))
            {
                ImGui.TextWrapped(Loc.Get("ConfigTab.Multiboxing.TCP"));
                ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step4TCP"));
                ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step5TCP"));
                if(OperatingSystem.IsWindows())
                    ImGui.TextWrapped(Loc.Get("ConfigTab.Multiboxing.UACWarning"));
            }

            ImGui.Separator();
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step6"));
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step7"));
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step8"));

            bool multiBox = MultiboxUtility.Config.MultiBox;
            if (ImGui.Checkbox(nameof(MultiboxUtility.Config.MultiBox), ref multiBox))
            {
                MultiboxUtility.Config.MultiBox = multiBox;
                ConfigurationProfileV2.Save();
            }

            using(ImRaii.Disabled(MultiboxUtility.Config.MultiBox))
            {
                ImGui.Indent();
                if(ImGuiEx.EnumCombo(Loc.Get("ConfigTab.Multiboxing.TransportType"), ref transportType))
                {
                    MultiboxUtility.Config.TransportType = transportType;
                    ConfigurationProfileV2.Save();
                }

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Multiboxing.TransportTypeHelp"));

                switch (transportType)
                {
                    case TransportType.NamedPipe:
                    {
                        string pipeName = MultiboxUtility.Config.PipeName;
                        if(ImGui.InputText(Loc.Get("ConfigTab.Multiboxing.PipeName"), ref pipeName))
                        {
                            MultiboxUtility.Config.PipeName = pipeName;
                            ConfigurationProfileV2.Save();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetPipeName"))
                        {
                            MultiboxUtility.Config.PipeName = "AutoDutyPipe";
                               ConfigurationProfileV2.Save();
                        }

                        if (!MultiboxUtility.Config.Host)
                        {
                            string serverName = MultiboxUtility.Config.ServerName;
                            if (ImGui.InputText(Loc.Get("ConfigTab.Multiboxing.ServerName"), ref serverName))
                            {
                                MultiboxUtility.Config.ServerName = serverName;
                                ConfigurationProfileV2.Save();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetServerName"))
                            {
                                MultiboxUtility.Config.ServerName = ".";
                                ConfigurationProfileV2.Save();
                            }
                        }

                        break;
                    }
                    case TransportType.Tcp:
                    {
                        if (!MultiboxUtility.Config.Host)
                        {
                            string serverAddress = MultiboxUtility.Config.ServerAddress;
                            if (ImGui.InputText(Loc.Get("ConfigTab.Multiboxing.ServerAddress"), ref serverAddress))
                            {
                                MultiboxUtility.Config.ServerAddress = serverAddress;
                                ConfigurationProfileV2.Save();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetServerAddress"))
                            {
                                MultiboxUtility.Config.ServerAddress = "127.0.0.1";
                                ConfigurationProfileV2.Save();
                            }
                        }

                        int serverPort = MultiboxUtility.Config.ServerPort;
                        if (ImGui.InputInt(Loc.Get("ConfigTab.Multiboxing.ServerPort"), ref serverPort))
                        {
                            MultiboxUtility.Config.ServerPort = serverPort;
                            ConfigurationProfileV2.Save();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetServerPort"))
                        {
                            MultiboxUtility.Config.ServerPort = 1716;
                            ConfigurationProfileV2.Save();
                        }

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                bool host = MultiboxUtility.Config.Host;
                if (ImGui.Checkbox($"{Loc.Get("ConfigTab.Multiboxing.Host")}##MultiboxHost", ref host))
                {
                    MultiboxUtility.Config.Host = host;
                    ConfigurationProfileV2.Save();
                }

                ImGui.Unindent();
            }

            bool synchronizePath = MultiboxUtility.Config.SynchronizePath;
            if (ImGui.Checkbox($"{Loc.Get("ConfigTab.Multiboxing.SynchronizePaths")}##MultiboxSynchronizePaths", ref synchronizePath))
            {
                MultiboxUtility.Config.SynchronizePath = synchronizePath;
                ConfigurationProfileV2.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Multiboxing.SynchronizePathsHelp"));

            if (MultiboxUtility.Config.MultiBox)
            {
                ImGui.Indent();
                ImGuiEx.Text(string.Format(Loc.Get("ConfigTab.Multiboxing.Blocking"), MultiboxUtility.stepBlock));

                if(MultiboxUtility.Config.Host)
                {
                    unsafe
                    {
                        ImGui.Separator();

                        if (ImGui.Checkbox(Loc.Get("ConfigTab.Multiboxing.ScrambleNames"), ref Censor.Config.Enabled))
                            ConfigurationProfileV2.Save();

                        ImGui.Columns(5);

                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.Name"));
                        ImGui.NextColumn();
                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.InParty"));
                        ImGui.NextColumn();
                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.Job"));
                        ImGui.NextColumn();
                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.BlockingStatus"));
                        ImGui.NextColumn();
                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.LastHeard"));
                        ImGui.Separator();
                        ImGui.NextColumn();

                        InfoProxyPartyMember* partyMembers = InfoProxyPartyMember.Instance();

                        for (int i = 0; i < MultiboxUtility.Server.MAX_SERVERS; i++)
                        {
                            MultiboxUtility.Server.ClientInfo? info = MultiboxUtility.Server.clients[i];

                            if(info != null)
                            {
                                ImGuiEx.Text(Censor.Character(info.CName));
                                ImGui.NextColumn();
                                bool inParty = PartyHelper.IsPartyMember(info.CID);
                                ImGuiEx.Text(inParty ? ImGuiHelper.StateGoodColor : ImGuiHelper.StateBadColor, inParty ? Loc.Get("ConfigTab.Multiboxing.InPartyYes") : Loc.Get("ConfigTab.Multiboxing.InPartyNo"));
                                ImGui.NextColumn();
                                if(partyMembers != null)
                                {
                                    InfoProxyCommonList.CharacterData* data = partyMembers->GetEntryByContentId(info.CID);
                                    if(data != null)
                                    {
                                        Job job = (Job) data->Job;
                                        ImGuiEx.Text(job.GetCombatRole() switch
                                        {
                                            CombatRole.Tank => ImGuiHelper.RoleTankColor,
                                            CombatRole.Healer => ImGuiHelper.RoleHealerColor,
                                            CombatRole.DPS => ImGuiHelper.RoleDPSColor,
                                            _ => ImGuiHelper.StateBadColor
                                        }, job.ToCustomString());
                                    }
                                }

                                ImGui.NextColumn();
                                ImGuiEx.Text(MultiboxUtility.Server.stepConfirms[i].ToString());
                                ImGui.NextColumn();
                                double totalSeconds = DateTime.Now.Subtract(MultiboxUtility.Server.keepAlives[i]).TotalSeconds;
                                ImGuiEx.Text(totalSeconds < 10 ? ImGuiHelper.StateGoodColor : ImGuiHelper.StateBadColor, $"{totalSeconds:F3}s ago");
                                ImGui.NextColumn();
                            }
                            else
                            {
                                ImGui.Text(string.Format(Loc.Get("ConfigTab.Multiboxing.NoInfo"), i));
                                for (int j = 0; j < 5; j++)
                                    ImGui.NextColumn();
                            }
                        }
                        ImGui.Columns(1);

                        using(ImRaii.Disabled(!InDungeon))
                        {
                            if(ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.ResynchronizeStep")}##MultiboxSynchronizeStep"))
                                MultiboxUtility.Server.SendStepStart();
                        }
                        ImGui.Separator();
                    }
                }

                ImGui.Unindent();
            }
        }

        return;
    }

    public static bool MakeSliderOrInput(ref int configValue, string valString, int sliderMin, int sliderMax, int inputStep = 1, int inputStepFast = 2) =>
        Configuration.Meta.UseSliderInputs  && ImGui.SliderInt($"###{valString}Slider", ref configValue, sliderMin, sliderMax) ||
        !Configuration.Meta.UseSliderInputs && ImGui.InputInt($"###{valString}Input", ref configValue, step: inputStep, stepFast: inputStepFast);

    public static bool MakeSliderOrInputLong(ref long configValue, string valString, long sliderMin, long sliderMax, long inputStep = 1, long inputStepFast = 2) =>
        Configuration.Meta.UseSliderInputs  && ImGui.SliderLong($"###{valString}Slider", ref configValue, sliderMin, sliderMax) ||
        !Configuration.Meta.UseSliderInputs && ImGui.InputLong($"###{valString}Input", ref configValue, step: inputStep, stepFast: inputStepFast);
}
