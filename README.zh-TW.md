**EN** [English](README.md) | **FR** [Français](README.fr.md) | **JA** [日本語](README.ja.md) | **ZH** [简体中文](README.zh-CN.md) | **ZH** [繁體中文](README.zh-TW.md)

# AutoDuty

<img align="right" width="150" height="150" src="logo.png">

AutoDuty（AD）是一個 FFXIV 的 Dalamud 插件，用於建立和追蹤副本路徑。它可以使用副本支援、信賴或軍事小隊自動循環運行副本內容。

支援頻道位於 [Puni.sh Discord 伺服器](https://discord.gg/punishxiv) 的 [此頻道](https://discord.com/channels/1001823907193552978/1236757595738476725)。

_未授權代碼，遵循[預設著作權法](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository#choosing-the-right-license)_

# 副本路徑

<img align="right" height="250" src="Assets/paths.png">

AutoDuty 支援的每個副本都有預設路徑，可以順利完成副本。如遇問題，請在 [支援 Discord](https://discord.gg/punishxiv) 中回報。

路徑會經過所有寶箱，設定後會拾取戰利品並在循環之間或所有循環結束後進行處理。

也支援建立路徑，可在頂部的 `Build` 分頁中輕鬆完成。有關路徑狀態，請參閱 [支援 Discord](https://discord.gg/punishxiv) 的 [AutoDuty 頻道](https://discord.com/channels/1001823907193552978/1236757595738476725) 中的 [置頂表格](https://discord.com/channels/1001823907193552978/1236757595738476725/1243059104528994334)。

# 自動練等

<img align="right" height="250" src="Assets/leveling.png">

AutoDuty 具有練等功能，會持續運行「合適的」副本來升級。副本通常由路徑品質、Boss 戰支援和副本等級決定。

使用 AutoDuty，只要擁有合適的裝備並在 AD 設定中啟用自動裝備最佳裝備選項，就可以從 15 等快速全自動升到滿等。

此插件還可以用同樣的方式為信賴成員升級。它可以智慧選擇成員並將他們全部升到滿等，完全自動。

# 自動化功能

AutoDuty 支援廣泛的自動化功能和與其他插件的整合。以下是 AD 在自動化副本內容時可以提供的部分功能：

| 循環前 | 副本中 | 循環間 | 循環後 |
| -------- | -------- | -------- | -------- |
| 前往旅館、住宅或部隊房屋 <br /> 自動修理（自己或NPC） <br /> 執行指令（如 SND 腳本） <br /> 自動使用消耗品 | 拾取寶箱 <br /> 管理插件狀態 | 自動精煉魔晶石 <br /> 自動分解戰利品 <br /> 自動軍票繳交 <br /> 使用 AutoRetainer <br /> 自動裝備最佳裝備 | 達到指定等級時停止 <br /> 精力獎勵用完時停止 <br /> 啟動 AutoRetainer 多號模式 <br /> 關閉電腦 |

<hr />

# 安裝

將 `https://puni.sh/api/repository/erdelf` 新增到插件儲存庫，然後在插件安裝器中搜尋 `AutoDuty` 進行安裝。

可透過插件安裝器或使用聊天指令 `/ad` 存取設定。

## 必要插件

以下插件也是必要的。右鍵點擊插件名稱並複製 URL，新增到遊戲內的儲存庫中。

- [vnavmesh](https://puni.sh/api/repository/veyn)：帶路徑點的自動導航
- [Wrath Combo](https://github.com/PunishXIV/WrathCombo) 或 [Rotation Solver Reborn](https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json)：自動循環執行，支援所有職業
- [Veyn's Boss Mod](https://puni.sh/api/repository/veyn) 或 [BossmodReborn](https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json)：Boss 戰自動執行

## 選用插件

以下插件為選用。它們與 AutoDuty 整合良好，某些情況下可由 AD 直接觸發。

- [Gearsetter](https://plugins.carvel.li)：自動尋找背包中的裝備升級
- [AutoRetainer](https://love.puni.sh/ment.json)：自動管理僱員和潛水艇

# 取得幫助

如果發現 Bug 或遇到問題，請在 [Puni.sh Discord 伺服器](https://discord.gg/punishxiv) 的 [此頻道](https://discord.com/channels/1001823907193552978/1236757595738476725) 中提問。

Veyn's Boss Mod 和 vnavmesh 的支援請在 [此頻道](https://discord.com/channels/1001823907193552978/1191076246860349450)，Wrath Combo 的支援請在 [此頻道](https://discord.com/channels/1001823907193552978/1271175781569003590)。BossModReborn 和 Rotation Solver Reborn 請在 [Combat Reborn Discord 伺服器](https://discord.gg/p54TZMPnC9) 中提問。

歡迎建立 Issue 提交功能請求和 Bug 回報。
