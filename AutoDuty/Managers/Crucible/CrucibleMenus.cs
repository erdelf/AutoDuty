using AutoDuty.Configurations;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoDuty.Managers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Screens = CrucibleUi.Screens;

    internal sealed unsafe class CrucibleMenus
    {
        private const int   FightPicks = CrucibleTeam.FightSize;
        private const int   RestPicks  = 2;
        private const float RestBelow  = 0.6f;
        private const int   ItemCap    = 10;
        private const float FightLow   = 0.4f;
        private const float BoardLow   = 0.6f;

        private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan Retry         = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan PickInterval  = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan CommenceRetry = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ShopStep      = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan FeedRetry     = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan FeedTimeout   = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ItemGap       = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ItemMenuWait  = TimeSpan.FromMilliseconds(1500);

        private DateTime confirmFrom = DateTime.MinValue;
        private DateTime next;

        private int      fightStep = -1;
        private DateTime fightNext;

        private List<int>? restPicks;
        private int        restStep;

        private readonly HashSet<int> shopTried = [];
        private DateTime   shopNext;
        private DateTime   feedFrom = DateTime.MinValue;
        private List<int>? feedOrder;
        private int        feedTry;
        private bool       fedThisVisit;
        private bool       closedThisVisit;

        private DateTime itemNext;
        private DateTime itemLastUse = DateTime.MinValue;
        private DateTime itemMenuFrom = DateTime.MinValue;

        public string Status { get; private set; } = "";

        private static ConfigurationProfileV2.MetaConfig.CrucibleConfig Config => AutoDuty.Configuration.Meta.Crucible;

        public void Reset()
        {
            this.confirmFrom  = DateTime.MinValue;
            this.fightStep    = -1;
            this.restPicks    = null;
            this.feedFrom     = DateTime.MinValue;
            this.itemMenuFrom = DateTime.MinValue;
            this.ResetShopVisit();
            this.Status = "";
        }

        public void Update()
        {
            DateTime now = DateTime.UtcNow;

            this.UpdateItems(now);

            if (now - this.confirmFrom <= ConfirmWindow && !this.FeedPending(now) && CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* confirm))
            {
                Screens.Prompt.Yes(confirm);
                this.confirmFrom = DateTime.MinValue;
                return;
            }

            if (this.StartFight(now))
                return;

            this.UpdateShop(now);

            if (now < this.next || CrucibleUi.IsOpen(CrucibleUi.YesNo))
                return;

            if (Config.Rest && !this.FeedPending(now) && !CrucibleUi.IsOpen(CrucibleUi.ShopWindow) && !CrucibleUi.IsOpen(CrucibleUi.BoardLayout) &&
                CrucibleUi.TryReady(CrucibleUi.TeamWindow, out AtkUnitBase* party))
            {
                this.Rest(party, now);
                return;
            }

            this.restPicks = null;

            if (Config.Treasure && CrucibleUi.TryReady(CrucibleUi.TreasureWindow, out AtkUnitBase* treasure))
            {
                this.PickTreasure(treasure, now);
                return;
            }

            if (!Config.Loot)
                return;

            if (CrucibleUi.TryReady(CrucibleUi.LootWindow, out AtkUnitBase* loot))
            {
                if (Screens.Booty.TakeAll(loot))
                {
                    this.confirmFrom = now;
                    this.Status      = "Taking the loot";
                }

                this.next = now + Retry;
                return;
            }

            if (CrucibleUi.TryReady(CrucibleUi.ResultWindow, out AtkUnitBase* result))
            {
                this.Status = "Finishing the board";
                Screens.Result.Continue(result);
                this.next = now + Retry;
            }
        }

        private bool StartFight(DateTime now)
        {
            if (!Config.FightPicks)
                return false;

            AtkUnitBase* layout = CrucibleUi.Ready(CrucibleUi.BoardLayout);
            AtkUnitBase* party  = CrucibleUi.Ready(CrucibleUi.TeamWindow);
            if (layout == null || party == null)
            {
                this.fightStep = -1;
                return false;
            }

            if (CrucibleUi.IsOpen(CrucibleUi.YesNo) || now < this.fightNext)
                return true;

            List<CrucibleUi.TeamRow>? team = CrucibleUi.Team();
            if (team == null || team.Count == 0)
                return true;

            List<int> alive = CrucibleTeam.FightOrder(team).Take(FightPicks).ToList();
            if (alive.Count == 0)
            {
                this.Status = "Every familiar is knocked out";
                return true;
            }

            if (this.fightStep < 0)
                this.fightStep = 0;

            if (this.fightStep < alive.Count)
            {
                int row = alive[this.fightStep];
                Screens.PetParty.Pick(party, row);
                this.fightStep++;
                this.fightNext = now + PickInterval;
                this.Status    = $"Picking familiar {this.fightStep} of {alive.Count} ({team[row].Name})";
                return true;
            }

            Screens.StageDetail.Confirm(layout);
            this.fightNext = now + CommenceRetry;
            this.Status    = "Commencing the battle";
            return true;
        }

        private void PickTreasure(AtkUnitBase* treasure, DateTime now)
        {
            this.next = now + Retry;

            List<CrucibleUi.Choice> choices = CrucibleUi.Choices(treasure, Screens.Treasure.FirstItemParam);
            if (choices.Count == 0)
                return;

            var offered = choices.Select(x => (Choice: x, Item: CrucibleItemData.ItemIn(x.Text))).ToList();
            var best    = offered.OrderBy(x => CrucibleItemData.TreasureRank(x.Item)).ThenBy(x => x.Choice.Param).First();

            Svc.Log.Info($"[Crucible] Treasure: taking {Describe(best)} from {string.Join(" / ", offered.Select(Describe))}");

            if (Screens.Treasure.Take(treasure, best.Choice.NodeId))
            {
                this.confirmFrom = now;
                this.Status      = $"Taking {(best.Item != 0 ? CrucibleItemData.NameOf(best.Item) : best.Choice.Text)}";
            }

            static string Describe((CrucibleUi.Choice Choice, uint Item) x) =>
                x.Item != 0 ? $"{CrucibleItemData.NameOf(x.Item)} ({x.Item})" : $"unknown \"{x.Choice.Text}\"";
        }

        private void Rest(AtkUnitBase* party, DateTime now)
        {
            if (this.restPicks == null)
            {
                if (CrucibleUi.Team() is not { Count: > 0 } rows)
                    return;

                // Picking nobody gives a 90% heal
                this.restPicks = rows.Select((row, index) => (row, index))
                                     .Where(x => x.row.Hp > 0 && x.row.CurrentHp > 0 && (float)x.row.CurrentHp / x.row.Hp < RestBelow)
                                     .OrderBy(x => (float)x.row.CurrentHp / x.row.Hp)
                                     .Take(RestPicks)
                                     .Select(x => x.index)
                                     .ToList();
                this.restStep = 0;

                string names = string.Join(", ", this.restPicks.Select(i => $"{rows[i].Name} {rows[i].CurrentHp}/{rows[i].Hp}"));
                Svc.Log.Info($"[Crucible] Campsite: resting with {(names.Length > 0 ? names : "no familiars (90% self heal)")}");
            }

            if (this.restStep < this.restPicks.Count)
            {
                Screens.PetParty.Pick(party, this.restPicks[this.restStep]);
                this.restStep++;
                this.next = now + PickInterval;
                return;
            }

            if (Screens.PetParty.Rest(party))
            {
                this.confirmFrom = now;
                this.Status      = "Resting at the campsite";
            }

            this.next = now + Retry;
        }

        private bool FeedPending(DateTime now) =>
            now - this.feedFrom <= FeedTimeout;

        private void UpdateShop(DateTime now)
        {
            if (now < this.shopNext)
                return;

            this.shopNext = now + TimeSpan.FromMilliseconds(250);

            if (!Config.Shop)
            {
                this.ResetShopVisit();
                return;
            }

            if (this.FeedPending(now))
            {
                if (now - this.confirmFrom <= ConfirmWindow && CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* feedYes))
                {
                    Screens.Prompt.Yes(feedYes);
                    this.confirmFrom = DateTime.MinValue;
                    this.feedFrom    = DateTime.MinValue;
                    this.shopNext    = now + ShopStep;
                    return;
                }

                this.Feed(now);
                return;
            }

            AtkUnitBase* shop = CrucibleUi.Ready(CrucibleUi.ShopWindow);
            if (shop == null)
            {
                this.ResetShopVisit();
                return;
            }

            if (now - this.confirmFrom <= ConfirmWindow && CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* yes))
            {
                Screens.Prompt.Yes(yes);
                this.confirmFrom = DateTime.MinValue;
                this.shopNext    = now + ShopStep;
                return;
            }

            if (CrucibleUi.IsOpen(CrucibleUi.YesNo) || CrucibleUi.IsOpen(CrucibleUi.TeamWindow))
                return;

            int coins = CrucibleUi.ShopCoins(shop);
            List<CrucibleUi.ShopEntry> affordable = CrucibleUi.ShopStock(shop).Where(x => !x.Bought && x.Price <= coins && !this.shopTried.Contains(x.Index)).ToList();

            if (this.ChooseBuy(affordable, CrucibleUi.ShopHeldItems(shop), CrucibleUi.ShopOwnedGear(shop)) is not { } buy)
            {
                if (!this.closedThisVisit)
                {
                    this.closedThisVisit = true;
                    this.Status          = $"Shop done, {coins} coins left";
                    Svc.Log.Info($"[Crucible] Shop: done with {coins} coins left");

                    if (Screens.ItemShop.Close(shop))
                        this.confirmFrom = now;
                }

                return;
            }

            this.Status = $"Buying {CrucibleItemData.NameOf(buy.Row)} for {buy.Price}";
            Svc.Log.Info($"[Crucible] Shop: buying {CrucibleItemData.NameOf(buy.Row)} for {buy.Price} of {coins} coins");

            this.shopTried.Add(buy.Index);
            Screens.ItemShop.Buy(shop, buy.Index);
            this.confirmFrom = now;
            this.shopNext    = now + ShopStep;

            if (CrucibleItemData.ShopFeed.Contains(buy.Row))
            {
                this.fedThisVisit = true;
                this.feedFrom     = now;
                this.feedOrder    = null;
                this.feedTry      = 0;
            }
        }

        private CrucibleUi.ShopEntry? ChooseBuy(List<CrucibleUi.ShopEntry> stock, int held, HashSet<uint> ownedGear)
        {
            if (held < ItemCap && FirstInStock(stock, CrucibleItemData.ShopHealing) is { } healing)
                return healing;

            if (FirstInStock(stock.Where(x => !ownedGear.Contains(x.Row)), CrucibleItemData.ShopGear) is { } gear)
                return gear;

            return this.fedThisVisit ? null : FirstInStock(stock, CrucibleItemData.ShopFeed);
        }

        private static CrucibleUi.ShopEntry? FirstInStock(IEnumerable<CrucibleUi.ShopEntry> stock, uint[] priority)
        {
            Dictionary<uint, CrucibleUi.ShopEntry> byRow = stock.GroupBy(x => x.Row).ToDictionary(g => g.Key, g => g.First());
            foreach (uint row in priority)
                if (byRow.TryGetValue(row, out CrucibleUi.ShopEntry entry))
                    return entry;
            return null;
        }

        private void Feed(DateTime now)
        {
            AtkUnitBase* party = CrucibleUi.Ready(CrucibleUi.TeamWindow);
            if (party == null || CrucibleUi.IsOpen(CrucibleUi.YesNo))
                return;

            if (this.feedOrder == null)
            {
                if (CrucibleUi.Team() is not { Count: > 0 } team)
                    return;
                this.feedOrder = CrucibleTeam.FightOrder(team);
            }

            if (this.feedTry >= this.feedOrder.Count)
            {
                Svc.Log.Info("[Crucible] Shop: no familiar would take the feed");
                Screens.PetParty.Return(party);
                this.feedFrom = DateTime.MinValue;
                return;
            }

            Screens.PetParty.Pick(party, this.feedOrder[this.feedTry]);
            this.feedTry++;
            this.confirmFrom = now;
            this.shopNext    = now + FeedRetry;
        }

        private void ResetShopVisit()
        {
            this.shopTried.Clear();
            this.fedThisVisit    = false;
            this.closedThisVisit = false;
        }

        private void UpdateItems(DateTime now)
        {
            if (now < this.itemNext)
                return;

            this.itemNext = now + TimeSpan.FromMilliseconds(250);

            if (now - this.itemMenuFrom <= ItemMenuWait)
            {
                if (CrucibleUi.TryReady(CrucibleUi.ContextMenu, out AtkUnitBase* menu))
                {
                    Screens.Menu.ChooseFirst(menu);
                    this.itemMenuFrom = DateTime.MinValue;
                    this.itemLastUse  = now;
                }

                return;
            }

            if (!Config.Items || now - this.itemLastUse < ItemGap || Svc.Objects.LocalPlayer is not { IsDead: false, MaxHp: > 0 } me)
                return;

            AtkUnitBase* hud = CrucibleUi.Ready(CrucibleUi.MainHud);
            if (hud == null)
                return;

            bool  fighting = Svc.Condition[ConditionFlag.InCombat];
            float hp       = (float)me.CurrentHp / me.MaxHp;
            if (hp >= (fighting ? FightLow : BoardLow))
                return;

            List<CrucibleUi.ItemSlot> items = CrucibleUi.HudItems(hud);
            CrucibleUi.ItemSlot pick = (fighting ? CrucibleItemData.FightItems : CrucibleItemData.BoardItems).Select(row => items.FirstOrDefault(x => x.Row == row))
                                                                          .FirstOrDefault(x => x.Row != 0);
            if (pick.Row == 0)
                return;

            Screens.MainHud.OpenItemMenu(hud, pick.Slot);
            this.itemMenuFrom = now;
            this.Status       = $"Using {pick.Name} at {hp:P0}";
            Svc.Log.Info($"[Crucible] Items: using {pick.Name} (slot {pick.Slot}) at {hp:P0} {(fighting ? "in a fight" : "on the board")}");
        }
    }
}
