## Video Tutorial

[![Video Tutorial](https://img.youtube.com/vi/1qwImfmknbM/maxresdefault.jpg)](https://www.youtube.com/watch?v=1qwImfmknbM)

## Features

- Allows players to spawn large drones with computer stations attached to them
- Attaches a camera to the bottom for viewing directly below the drone
- Allows moving the drone while controlling the camera
- Allows quickly switching between the drone and camera view using the "swap seats" key
- API and server command allow integration with other plugins
- One mega drone per player
- Configurable cooldowns based on permission

## Known issues

Since the March 2023 Rust update, drones now sway in the wind, but attached entities do not sway. This causes undesirable visuals to players observing a drone that is being controlled. There is no known fix at this time.

Additionally, it's no longer feasible to stand on drones, which means you should not attempt to dismount a Mega Drone in the air.

### How it works

- Spawning the drone will automatically cause the player to mount the attached computer station
- When a player mounts the computer station, they will automatically start controlling the drone
- The computer station automatically has saved identifiers for the drone and the attached camera

## Plugin compatibility

Compatible with all drone plugins by WhiteThunder. Note that attachments such as stashes, turrets, boomboxes and seats **cannot** be deployed onto mega drones specifically.

**Not** compatible with [Drone Pilot](https://umod.org/plugins/drone-pilot).

## Required plugins

The following plugins are required, but they work out-of-the-box, so no permissions or configuration are needed.

- [Entity Scale Manager](https://umod.org/plugins/entity-scale-manager) -- Allows entities to be resized.
- [Drone Scale Manager](https://umod.org/plugins/drone-scale-manager) -- Solves various problems with resizing drones.

## Highly recommended plugins

- [Drone Settings](https://umod.org/plugins/drone-settings) -- Allows changing speed, toughness and other properties of RC drones.
- [Targetable Drones](https://umod.org/plugins/targetable-drones) -- Allows RC drones to be targeted by Auto Turrets and SAM Sites.
- [Better Drone Collision](https://umod.org/plugins/better-drone-collision) -- Fixes collision issues with large drones.
- [Limited Drone Height](https://umod.org/plugins/limited-drone-height) -- Limits how high RC drones can be flown above terrain.

## Other synergistic plugins

- [Ridable Drones](https://umod.org/plugins/ridable-drones) -- Allows players to ride RC drones by standing on them. This essentially allows mega drones to have passengers.
- [Vehicle Deployed Locks](https://umod.org/plugins/vehicle-deployed-locks) -- Allows players with permissions to deploy code locks and key locks to mega drones.
- [Drone Hover](https://umod.org/plugins/drone-hover) -- Allows RC drones to hover in place while not being controlled. This allows you to dismount a mega drone computer station in the air without it falling to the ground. Combined with Ridable Drones, this allows you to dismount the computer station while the drone is moving, without falling off, while the drone glides to a stop midair.
- [Drone Lights](https://umod.org/plugins/drone-lights) -- Adds controllable search lights to RC drones.

## Permissions

- `megadrones.spawn` -- Allows players to spawn a personal mega drone with the `megadrone` command.
- `megadrones.fetch` -- Allows players to fetch their existing mega drone with the `megadrone fetch` command.
- `megadrones.destroy` -- Allows players to destroy their existing mega drone with the `megadrone destroy` command.
- `megadrones.give` -- Allows players to give unlimited mega drones with the `givemegadrone <player>` command.

### Cooldown permissions

The following permissions come with the plugin's **default configuration**. Granting one to a player determines their cooldowns for spawning and fetching their mega drone, overriding the default. Granting multiple to a player will cause only the last one to apply, based on the order in the config.

- `megadrones.cooldown.long` -- 1 day to spawn, 1 hour to fetch
- `megadrones.cooldown.medium` -- 1 hour to spawn, 10 minutes to fetch
- `megadrones.cooldown.short` -- 10 minutes to spawn, 1 minute to fetch
- `megadrones.cooldown.none` -- no cooldown

You can add more cooldown profiles in the plugin configuration (`CooldownsRequiringPermission`), and the plugin will automatically generate permissions of the format `megadrones.cooldown.<suffix>` when reloaded.

## Commands

- `megadrone` -- Spawns a mega drone in front of the player.
  - Each player may only have one mega drone at a time.
- `megadrone fetch` -- Teleports the player's mega drone to in front of them.
- `megadrone destroy` -- Destroys the player's mega drone.
- `megadrone help` -- Prints help info about the commands the player is allowed to use.
- `givemegadrone <player>` -- Spawns a mega drone in front of the specified player.
  - This command can be run from the server console, allowing this to be used by other plugins such as GUI shop.
  - When this command is run by a player (who has permission), if no target player is specified, the mega drone will be spawned for the player who ran the command.
  - Note: This command does not check for sufficient space, so it can potentially spawn the mega drone inside other objects.

## Configuration

```json
{
  "AutoMount": true,
  "AutoFetch": false,
  "DroneIdentifierPrefix": "MD",
  "CamIdentifierPrefix": "MDCam",
  "CanSpawnWhileBuildingBlocked": false,
  "CanFetchWhileBuildingBlocked": false,
  "CanFetchWhileOccupied": false,
  "CanDestroyWhileOccupied": false,
  "DismountPlayersOnFetch": true,
  "DestroyOnDisconnect": false,
  "DefaultCooldowns": {
    "SpawnSeconds": 3600,
    "FetchSeconds": 600
  },
  "CooldownsRequiringPermission": [
    {
      "PermissionSuffix": "long",
      "SpawnSeconds": 86400,
      "FetchSeconds": 3600
    },
    {
      "PermissionSuffix": "medium",
      "SpawnSeconds": 3600,
      "FetchSeconds": 600
    },
    {
      "PermissionSuffix": "short",
      "SpawnSeconds": 600,
      "FetchSeconds": 60
    },
    {
      "PermissionSuffix": "none",
      "SpawnSeconds": 0,
      "FetchSeconds": 0
    }
  ],
  "CommandAliases": {
    "megadrone": [
      "md"
    ],
    "givemegadrone": [
      "givemd"
    ]
  },
  "SubcommandAliases": {
    "help": [
      "h"
    ],
    "fetch": [
      "f"
    ],
    "destroy": [
      "d"
    ]
  }
}
```

- `AutoMount` (`true` or `false`) -- While `true`, spawning or fetching a mega drone will automatically mount the player. Note: This does not apply for drones spawned via the `givemegadrone` command which are never auto mounted.
- `AutoFetch` (`true` or `false`) -- While `true`, running the `megadrone` command will automatically fetch your existing mega drone if you already have one. This will only happen if you have the `megadrones.fetch` permission and do not have an active `Fetch` cooldown.
- `DroneIdentifierPrefix` -- Identifier prefix to give mega drones. A random number between 1 and 9999 will be appended to this.
- `CamIdentifierPrefix` -- Identifier prefix to give the camera attached to each mega drone. A random number between 1 and 9999 will be appended to this.
  - The same random number will be used for both the drone and camera.
- `CanSpawnWhileBuildingBlocked` (`true` or `false`) -- While `true`, players can spawn mega drones while building blocked.
- `CanFetchWhileBuildingBlocked` (`true` or `false`) -- While `true`, players can fetch their mega drone while building blocked.
- `CanFetchWhileOccupied` (`true` or `false`) -- While `true`, players can fetch their mega drone while it is occupied.
- `CanDestroyWhileOccupied` (`true` or `false`) -- While `true`, players can destroy their existing mega drone while it is occupied.
- `DismountPlayersOnFetch` (`true` or `false`) -- While `true`, fetching a mega drone will dismount any players currently on it. Only applies if `CanFetchWhileOccupied` is `true`.
- `DestroyOnDisconnect` (`true` or `false`) -- While `true`, disconnecting from the server will automatically destroy your mega drone. If the mega drone is mounted at that time, it will be destroyed when fully dismounted.
- `DefaultCooldowns` -- Default cooldowns, for players who do not have permission to any entries in `CooldownsRequiringPermission`.
  - `SpawnSeconds` -- The number of seconds the player must wait after spawning a mega drone, before they can spawn one again.
    - Note: Each player can have only one mega drone at a time, so reducing a player's cooldown won't allow them to have multiple at once.
  - `FetchSeconds` -- The number of seconds the player must wait after fetching their mega drone, before they can fetch it again.
- `CooldownsRequiringPermission` -- List of cooldown configs requiring permission. Each one will generate a permission of the format `megadrones.cooldown.<suffix>`. Granting one to a player determines their cooldowns.
  - `PermissionSuffix` -- Determines the generated permission of format `megadrones.cooldown.<suffix>`.
  - `SpawnSeconds` -- Works like the option of the same name in `DefaultCooldowns`.
  - `FetchSeconds` -- Works like the option of the same name in `DefaultCooldowns`.
- `CommandAliases` -- Determines aliases of each command. Each command can have multiple aliases if you want, such as for other languages.
  - For example, `md` can be used in place of `megadrone`.
  - To remove all aliases, change the value for a given command to `[]`, like `"megadrone": []`.
- `SubcommandAliases` -- Determines alises of each subcommand. Each subcommand can have multiple aliases if you want, such as for other languages.
  - For example `megadrone d` or `md d` can be used in place of `megadrone destroy`.
  - To remove all aliases, change the value for a given subcommand to `[]`, like `"destroy": []`.

The definition of "occupied" is when a player is mounted on the computer station, or when any player is standing on the drone if using the [Ridable Drones](https://umod.org/plugins/ridable-drones) plugin.

## Localization

```json
{
  "Error.NoPermission": "You don't have permission to do that.",
  "Error.BuildingBlocked": "Error: Cannot do that while building blocked.",
  "Error.DroneNotFound": "Error: You need a mega drone to do that.",
  "Error.DroneOccupied": "Error: Cannot do that while your mega drone is occupied.",
  "Error.Cooldown": "Please wait <color=#f44>{0}</color> and try again.",
  "Error.GenericRestricted": "Error: You cannot do that right now.",
  "Error.UnknownCommand": "Error: Unrecognized command <color=#fb4>{0}</color>.",
  "Error.Mounted": "Error: Cannot do that while mounted.",
  "Error.InsufficientSpace": "Error: Not enough space.",
  "Spawn.Success": "Here is your mega drone.",
  "Spawn.Error.DroneAlreadyExists": "Error: You already have a mega drone.",
  "Spawn.Error.DroneAlreadyExists.Help": "Try <color=#fb4>{0} fetch</color> or <color=#fb4>{0} help</color>.",
  "Give.Error.Syntax": "Syntax: {0} <player>",
  "Give.Error.PlayerNotFound": "Error: Player '{0}' not found.",
  "Give.Success": "Player '{0}' has been given a mega drone.",
  "Info.DroneDestroyed": "Your mega drone was destroyed.",
  "Help": "<color=#fb4>Mega Drone Commands</color>",
  "Help.Spawn": "<color=#fb4>{0}</color> - Spawn a mega drone{1}",
  "Help.Fetch": "<color=#fb4>{0} f | fetch</color> - Fetch your mega drone{1}",
  "Help.Destroy": "<color=#fb4>{0} d | destroy</color> - Destroy your mega drone",
  "Help.RemainingCooldown": " - <color=#f44>{0}</color>"
}
```

## Developer API

#### API_SpawnMegaDrone

Plugins can call this API to spawn a mega drone for the specified player.

```csharp
Drone API_SpawnMegaDrone(BasePlayer player)
```

## Developer Hooks

#### OnMegaDroneSpawn

```csharp
bool? OnMegaDroneSpawn(BasePlayer player)
```

- Called when a mega drone is about to be spawned for a player
- Returning `false` will prevent the drone from being spawned
- Returning `null` will result in the default behavior

#### OnMegaDroneSpawned

```csharp
bool? OnMegaDroneSpawned(Drone drone, BasePlayer player)
```

- Called after a mega drone has been spawned for a palyer
- No return behavior

#### OnMegaDroneFetch

```csharp
bool? OnMegaDroneFetch(BasePlayer player, Drone drone)
```

- Called when a player tries to fetch their mega drone
- Returning `false` will prevent the drone from being fetched
- Returning `null` will result in the default behavior

#### OnMegaDroneDestroy

```csharp
bool? OnMegaDroneDestroy(BasePlayer player, Drone drone)
```

- Called when a player tries to destroy their mega drone
- Returning `false` will prevent the drone from being destroyed
- Returning `null` will result in the default behavior
