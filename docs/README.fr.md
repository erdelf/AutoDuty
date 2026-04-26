<a href="../README.md"><img src="https://flagcdn.com/24x18/gb.png" alt="EN"> English</a> · <a href="README.fr.md"><img src="https://flagcdn.com/24x18/fr.png" alt="FR"> Français</a> · <a href="README.ja.md"><img src="https://flagcdn.com/24x18/jp.png" alt="JA"> 日本語</a> · <a href="README.zh-CN.md"><img src="https://flagcdn.com/24x18/cn.png" alt="ZH-CN"> 简体中文</a> · <a href="README.zh-TW.md"><img src="https://flagcdn.com/24x18/tw.png" alt="ZH-TW"> 繁體中文</a>

# AutoDuty

<img align="right" width="150" height="150" src="../logo.png">

AutoDuty (AD) est un plugin Dalamud pour FFXIV qui sert d'outil pour créer et suivre des chemins à travers les donjons et missions. Il est conçu pour permettre le bouclage automatique de contenu instancié avec le Soutien, les Adelphes ou les Escouades.

Le support se trouve dans [ce canal](https://discord.com/channels/1001823907193552978/1236757595738476725) du [serveur Discord Puni.sh](https://discord.gg/punishxiv).

_Code non licencié, qui suit la [loi sur le droit d'auteur par défaut](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository#choosing-the-right-license)_

# Chemins de donjon

<img align="right" height="250" src="../Assets/paths.png">

Chaque donjon supporté par AutoDuty possède un chemin préconfiguré qui devrait compléter le donjon sans problème. Si vous rencontrez des soucis, n'hésitez pas à nous contacter sur le [Discord de support](https://discord.gg/punishxiv).

Les chemins passent également sur tous les coffres au trésor et, lorsque configuré, ramassent le butin et en font quelque chose entre les runs ou après la fin de vos runs programmées.

La création de chemins est aussi possible et facile à réaliser dans AutoDuty via l'onglet `Création` en haut. Pour l'état des chemins, consultez le [tableur épinglé](https://discord.com/channels/1001823907193552978/1236757595738476725/1243059104528994334) dans le [canal AutoDuty](https://discord.com/channels/1001823907193552978/1236757595738476725) du [Discord de support](https://discord.gg/punishxiv).

# Montée de niveau automatique

<img align="right" height="250" src="../Assets/leveling.png">

AutoDuty possède une fonctionnalité de montée de niveau qui vous fait parcourir en continu le "bon" donjon pour progresser. Le donjon est généralement déterminé par la qualité du chemin, le support des combats de boss, et évidemment le niveau du donjon.

Avec AutoDuty, vous pouvez monter une classe du niveau 15 au niveau max très rapidement et en totale autonomie, à condition d'avoir l'équipement adéquat et l'option dans la configuration AD pour équiper automatiquement le meilleur équipement.

Ce plugin peut aussi monter le niveau de vos Adelphes de la même manière. Il sélectionne intelligemment les membres et les monte tous au max, en totale autonomie.

# Automatisations

AutoDuty supporte un large éventail d'automatisations et d'intégrations avec d'autres plugins. Voici quelques-unes des possibilités qu'AD offre lors de l'automatisation de contenu instancié :

| Avant les runs | Pendant la run | Entre les runs | Après les runs |
| -------- | -------- | -------- | -------- |
| Se rendre à l'auberge, maison ou domaine de CL <br /> Réparation automatique (soi-même ou PNJ) <br /> Exécuter des commandes (ex: scripts SND) <br /> Consommation automatique d'objets | Ramasser les coffres au trésor <br /> Gérer l'état des plugins | Extraction automatique de matéria <br /> Recyclage automatique du butin <br /> Livraison automatique du butin à la CG <br /> Utiliser AutoRetainer <br /> Équiper automatiquement le meilleur équipement | Arrêter la boucle à un niveau <br /> Arrêter quand plus d'XP de repos <br /> Activer le mode multi d'AutoRetainer <br /> Éteindre votre ordinateur |

<hr />

# Installation

Ajoutez `https://puni.sh/api/repository/erdelf` à vos dépôts de plugins puis recherchez `AutoDuty` dans l'installeur de plugins pour installer AutoDuty.

Les paramètres sont accessibles via l'installeur de plugins ou en utilisant la commande `/ad`.

## Plugins requis

Les plugins suivants sont également requis. Faites un clic droit sur le nom du plugin et copiez l'URL pour l'ajouter à vos dépôts en jeu.

- [vnavmesh](https://puni.sh/api/repository/veyn) : Navigation automatique avec points de passage
- [Wrath Combo](https://github.com/PunishXIV/WrathCombo) ou [Rotation Solver Reborn](https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json) : Exécution automatique de rotations, toutes les classes supportées
- [Veyn's Boss Mod](https://puni.sh/api/repository/veyn) ou [BossmodReborn](https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json) : Exécution automatique des combats de boss

## Plugins optionnels

Les plugins suivants sont optionnels. Ils s'intègrent bien avec AutoDuty et dans certains cas peuvent être déclenchés par AD lui-même.

- [Gearsetter](https://plugins.carvel.li) : Trouve automatiquement les améliorations d'équipement dans votre inventaire
- [AutoRetainer](https://love.puni.sh/ment.json) : Gère automatiquement vos servants et sous-marins et les envoie en expédition
 
# Obtenir de l'aide

Si vous trouvez un bug ou pensez avoir un problème avec le plugin, demandez dans [ce canal](https://discord.com/channels/1001823907193552978/1236757595738476725) du [serveur Discord Puni.sh](https://discord.gg/punishxiv). C'est peut-être un problème connu ou les membres pourront vous aider rapidement. De plus, il peut parfois s'agir d'un problème avec l'un des plugins requis. Les utilisateurs sur Discord pourront aider à trier le problème et vous diriger au bon endroit.

La bonne pratique est de ne pas dire "Je suis mort dans ce donjon et je ne sais pas pourquoi." Incluez autant de détails que possible. Quel boss ? Étiez-vous bloqué à un endroit précis ?

Pour le support de Veyn's Boss Mod et vnavmesh, demandez dans [ce canal](https://discord.com/channels/1001823907193552978/1191076246860349450) du [serveur Discord Puni.sh](https://discord.gg/punishxiv),
ou pour Wrath Combo dans [ce canal](https://discord.com/channels/1001823907193552978/1271175781569003590) du serveur Puni.sh. Pour BossModReborn et 
Rotation Solver Reborn, demandez sur le [serveur Discord Combat Reborn](https://discord.gg/p54TZMPnC9).

N'hésitez pas à créer des tickets pour vos demandes de fonctionnalités et rapports de bugs.
