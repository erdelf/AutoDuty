<a href="README.md"><img src="https://flagcdn.com/24x18/gb.png" alt="EN"> English</a> · <a href="README.fr.md"><img src="https://flagcdn.com/24x18/fr.png" alt="FR"> Français</a> · <a href="README.ja.md"><img src="https://flagcdn.com/24x18/jp.png" alt="JA"> 日本語</a> · <a href="README.zh-CN.md"><img src="https://flagcdn.com/24x18/cn.png" alt="ZH-CN"> 简体中文</a> · <a href="README.zh-TW.md"><img src="https://flagcdn.com/24x18/tw.png" alt="ZH-TW"> 繁體中文</a>

# AutoDuty

<img align="right" width="150" height="150" src="logo.png">

AutoDuty（AD）是一个 FFXIV 的 Dalamud 插件，用于创建和跟踪副本路径。它可以使用副本辅助、信赖或军事小队自动循环运行副本内容。

支持频道位于 [Puni.sh Discord 服务器](https://discord.gg/punishxiv) 的 [此频道](https://discord.com/channels/1001823907193552978/1236757595738476725)。

_未授权代码，遵循[默认版权法](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository#choosing-the-right-license)_

# 副本路径

<img align="right" height="250" src="Assets/paths.png">

AutoDuty 支持的每个副本都有预配置的路径，可以顺利完成副本。如遇问题，请在 [支持 Discord](https://discord.gg/punishxiv) 中反馈。

路径会经过所有宝箱，配置后会拾取战利品并在循环之间或所有循环结束后进行处理。

也支持创建路径，可在顶部的 `Build` 选项卡中轻松完成。有关路径状态，请参阅 [支持 Discord](https://discord.gg/punishxiv) 的 [AutoDuty 频道](https://discord.com/channels/1001823907193552978/1236757595738476725) 中的 [置顶表格](https://discord.com/channels/1001823907193552978/1236757595738476725/1243059104528994334)。

# 自动练级

<img align="right" height="250" src="Assets/leveling.png">

AutoDuty 具有练级功能，会持续运行"合适的"副本来升级。副本通常由路径质量、Boss 战支持和副本等级决定。

使用 AutoDuty，只要拥有合适的装备并在 AD 配置中启用自动装备最佳装备选项，就可以从 15 级快速全自动升到满级。

此插件还可以用同样的方式为信赖成员升级。它可以智能选择成员并将他们全部升到满级，完全自动。

# 自动化功能

AutoDuty 支持广泛的自动化功能和与其他插件的集成。以下是 AD 在自动化副本内容时可以提供的部分功能：

| 循环前 | 副本中 | 循环间 | 循环后 |
| -------- | -------- | -------- | -------- |
| 前往旅馆、住宅或部队房屋 <br /> 自动修理（自己或NPC） <br /> 执行命令（如 SND 脚本） <br /> 自动使用消耗品 | 拾取宝箱 <br /> 管理插件状态 | 自动精制魔晶石 <br /> 自动分解战利品 <br /> 自动军票上缴 <br /> 使用 AutoRetainer <br /> 自动装备最佳装备 | 达到指定等级时停止 <br /> 精力奖励用完时停止 <br /> 启动 AutoRetainer 多号模式 <br /> 关闭电脑 |

<hr />

# 安装

将 `https://puni.sh/api/repository/erdelf` 添加到插件仓库，然后在插件安装器中搜索 `AutoDuty` 进行安装。

可通过插件安装器或使用聊天命令 `/ad` 访问设置。

## 必需插件

以下插件也是必需的。右键点击插件名称并复制 URL，添加到游戏内的仓库中。

- [vnavmesh](https://puni.sh/api/repository/veyn)：带路径点的自动导航
- [Wrath Combo](https://github.com/PunishXIV/WrathCombo) 或 [Rotation Solver Reborn](https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json)：自动循环执行，支持所有职业
- [Veyn's Boss Mod](https://puni.sh/api/repository/veyn) 或 [BossmodReborn](https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json)：Boss 战自动执行

## 可选插件

以下插件为可选。它们与 AutoDuty 集成良好，某些情况下可由 AD 直接触发。

- [Gearsetter](https://plugins.carvel.li)：自动查找背包中的装备升级
- [AutoRetainer](https://love.puni.sh/ment.json)：自动管理雇员和潜水艇

# 获取帮助

如果发现 Bug 或遇到问题，请在 [Puni.sh Discord 服务器](https://discord.gg/punishxiv) 的 [此频道](https://discord.com/channels/1001823907193552978/1236757595738476725) 中提问。

Veyn's Boss Mod 和 vnavmesh 的支持请在 [此频道](https://discord.com/channels/1001823907193552978/1191076246860349450)，Wrath Combo 的支持请在 [此频道](https://discord.com/channels/1001823907193552978/1271175781569003590)。BossModReborn 和 Rotation Solver Reborn 请在 [Combat Reborn Discord 服务器](https://discord.gg/p54TZMPnC9) 中提问。

欢迎创建 Issue 提交功能请求和 Bug 报告。
