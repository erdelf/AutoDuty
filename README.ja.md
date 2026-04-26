🇬🇧 [English](README.md) | 🇫🇷 [Français](README.fr.md) | 🇯🇵 [日本語](README.ja.md) | 🇨🇳 [简体中文](README.zh-CN.md) | 🇹🇼 [繁體中文](README.zh-TW.md)

# AutoDuty

<img align="right" width="150" height="150" src="logo.png">

AutoDuty（AD）は、FFXIV用のDalamudプラグインで、ダンジョンやコンテンツのパスを作成・追跡するツールです。フェイス、小隊、またはコンテンツサポーターを使用して、インスタンスコンテンツの自動周回を可能にします。

サポートは [Puni.sh Discordサーバー](https://discord.gg/punishxiv) の [このチャンネル](https://discord.com/channels/1001823907193552978/1236757595738476725) で受けられます。

_ライセンスなしのコード、[デフォルトの著作権法](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository#choosing-the-right-license)に従います_

# ダンジョンパス

<img align="right" height="250" src="Assets/paths.png">

AutoDutyがサポートする各ダンジョンには、問題なくクリアできるパスが事前に設定されています。問題が発生した場合は、[サポートDiscord](https://discord.gg/punishxiv)でお問い合わせください。

パスはすべての宝箱を通過し、設定に応じて戦利品を拾い、周回の合間やすべての周回終了後に処理します。

パスの作成もサポートされており、上部の `Build` タブで簡単に作成できます。パスの状態については、[サポートDiscord](https://discord.gg/punishxiv) の [AutoDutyチャンネル](https://discord.com/channels/1001823907193552978/1236757595738476725) にある [固定スプレッドシート](https://discord.com/channels/1001823907193552978/1236757595738476725/1243059104528994334) を参照してください。

# 自動レベリング

<img align="right" height="250" src="Assets/leveling.png">

AutoDutyにはレベリング機能があり、適切なダンジョンを継続的に周回してレベルを上げます。ダンジョンはパスの品質、ボス戦のサポート、ダンジョンのレベルによって決定されます。

AutoDutyを使えば、適切な装備とAD設定での自動装備があれば、レベル15から最大レベルまで非常に速く、完全放置で上げることができます。

このプラグインはフェイスのレベルも同様に上げることができます。メンバーを自動的に選択し、全員を最大レベルまで放置で上げられます。

# 自動化機能

AutoDutyは幅広い自動化機能と他のプラグインとの連携をサポートしています。以下はADがインスタンスコンテンツの自動化中に提供できる機能の一部です：

| 周回前 | 周回中 | 周回間 | 周回後 |
| -------- | -------- | -------- | -------- |
| 宿屋・自宅・FC邸宅に移動 <br /> 自動修理（自己修理またはNPC） <br /> コマンド実行（SNDスクリプトなど） <br /> アイテム自動使用 | 宝箱の回収 <br /> プラグイン状態の管理 | マテリア自動精製 <br /> 戦利品の自動分解 <br /> GCへの自動納品 <br /> AutoRetainer使用 <br /> 最適装備の自動装着 | 指定レベルで停止 <br /> レストXPがなくなったら停止 <br /> AutoRetainerマルチモード起動 <br /> PCをシャットダウン |

<hr />

# インストール

`https://puni.sh/api/repository/erdelf` をプラグインリポジトリに追加し、プラグインインストーラーで `AutoDuty` を検索してインストールしてください。

設定はプラグインインストーラーまたはチャットコマンド `/ad` からアクセスできます。

## 必須プラグイン

以下のプラグインも必要です。プラグイン名を右クリックしてURLをコピーし、ゲーム内のリポジトリに追加してください。

- [vnavmesh](https://puni.sh/api/repository/veyn)：ウェイポイントによる自動ナビゲーション
- [Wrath Combo](https://github.com/PunishXIV/WrathCombo) または [Rotation Solver Reborn](https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json)：自動ローテーション実行、全ジョブ対応
- [Veyn's Boss Mod](https://puni.sh/api/repository/veyn) または [BossmodReborn](https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json)：ボス戦の自動実行

## オプションプラグイン

以下のプラグインはオプションです。AutoDutyとの連携が良く、場合によってはADから直接呼び出すことができます。

- [Gearsetter](https://plugins.carvel.li)：インベントリから装備アップグレードを自動検索
- [AutoRetainer](https://love.puni.sh/ment.json)：リテイナーとサブマリンの自動管理

# ヘルプ

バグを見つけたり問題があると思った場合は、[Puni.sh Discordサーバー](https://discord.gg/punishxiv) の [このチャンネル](https://discord.com/channels/1001823907193552978/1236757595738476725) でお問い合わせください。

Veyn's Boss Modとvnavmeshのサポートは [Puni.sh Discordサーバー](https://discord.gg/punishxiv) の [このチャンネル](https://discord.com/channels/1001823907193552978/1191076246860349450) で、
Wrath Comboのサポートは [このチャンネル](https://discord.com/channels/1001823907193552978/1271175781569003590) で受けられます。BossModRebornとRotation Solver Rebornについては [Combat Reborn Discordサーバー](https://discord.gg/p54TZMPnC9) でお問い合わせください。

機能リクエストやバグ報告のIssueもお気軽にどうぞ。
