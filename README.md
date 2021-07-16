## Features

- Allows players to spawn large drones with computer stations attached to them
- Attaches cameras to the bottom for viewing directly below the drone
- Allows moving the drone while controlling the camera
- Allows quickly switching between the drone and camera view using the "swap seats" key
- API and server command allow integration with other plugins
- One mega drone per player
- Configurable cooldown

### How it works

- Spawning the drone will automatically cause the player to mount the attached computer station
- When a player mounts the computer station, they will automatically start controlling the drone
- The computer station automatically has saved identifiers for the drone and the attached camera

## Required plugins

- Entity Scale Manager
- Drone Scale Manager

## Permissions

- `megadrones.spawn` -- Allows players to spawn a personal mega drone with the `megadrone` command.
- `megadrones.fetch` -- Allows players to fetch their existing mega drone with the `megadrone fetch` command.
- `megadrones.destroy` -- Allows players to destroy their existing mega drone with the `megadrone destroy` command.
- `megadrones.give` -- Allows players to give unlimited mega drones with the `givemegadrone <player>` command.

## Commands

- `megadrone` (or `md`) -- Spawns a mega drone in front of the player.
  - Each player may only have one mega drone at a time.
- `megadrone fetch` -- Teleports the player's mega drone to in front of them.
- `megadrone destroy` -- Destroys the player's mega drone.
- `megadrone help` -- Prints help info about the commands the player is allowed to use.
- `givemegadrone <player>` (or `givemd`) -- Spawns a mega drone in front of the specified player.
  - Supported via the server console, so this can be used via other plugins such as GUI shop.
  - When no player is specified, the calling player will receive the mega drone (if run by a player).

## Configuration

```json
{
  "DroneIdentifierPrefix": "MD",
  "CamIdentifierPrefix": "MDCam",
  "CanSpawnWhileBuildingBlocked": false,
  "CanFetchWhileBuildingBlocked": false,
  "CanFetchWhileOccupied": false,
  "CanDespawnWhileOccupied": false,
  "DismountPlayersOnFetch": true,
  "Cooldowns": {
    "SpawnSeconds": 3600,
    "FetchSeconds": 600
  }
}
```

- `DroneIdentifierPrefix` -- Identifier prefix to give mega drones. A random number between 1 and 9999 will be appended.
- `CamIdentifierPrefix` -- Identifier prefix to give the camera attached to each mega drone. A random number between 1 and 9999 will be appended.
- `CanSpawnWhileBuildingBlocked` (`true` or `false`) -- While `true`, players can spawn mega drones while building blocked.
- `CanFetchWhileBuildingBlocked` (`true` or `false`) -- While `true`, players can fetch their mege drone while building blocked.
- `CanFetchWhileOccupied` (`true` or `false`) -- While `true`, players can fetch their mega drone while it is occupied.
- `CanDestroyWhileOccupied` (`true` or `false`) -- While `true`, players can destroy their existing mega drone while it is occupied.
- `DismountPlayersOnFetch` (`true` or `false`) -- While `true`, fetching a mega drone will dismount any players currently on it. Only applies if `CanFetchWhileOccupied` is `true`.

The definition of "occupied" is when a player is mounted on the computer station, or when any player is standing on the drone if using the Ridable Drones plugin.

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
  "Help.Spawn": "<color=#fb4>{0}</color> - Spawn a mega drone",
  "Help.Fetch": "<color=#fb4>{0} f | fetch</color> - Fetch your mega drone",
  "Help.Destroy": "<color=#fb4>{0} d | destroy</color> - Destroy your mega drone"
}
```

## FAQ

#### How do I remote-control a drone?

Controls are `W`/`A`/`S`/`D` to move, `shift` (sprint) to go up, `ctrl` (duck) to go down, and mouse to steer.

## Recommended compatible plugins

- [Drone Hover](https://umod.org/plugins/drone-hover) -- Allows RC drones to hover in place while not being controlled.
- [Drone Lights](https://umod.org/plugins/drone-lights) -- Adds controllable search lights to RC drones.
- [Drone Storage](https://umod.org/plugins/drone-storage) -- Allows players to deploy a small stash to RC drones.
- [Drone Turrets](https://umod.org/plugins/drone-turrets) -- Allows players to deploy auto turrets to RC drones.
- [Auto Flip Drones](https://umod.org/plugins/auto-flip-drones) -- Auto flips upside-down RC drones when a player takes control.
- [RC Identifier Fix](https://umod.org/plugins/rc-identifier-fix) -- Auto updates RC identifiers saved in computer stations to refer to the correct entity.

## Developer API

#### API_StopAnimating

Plugins can call this API to spawn a mega drone for the specified player.

```csharp
Drone API_SpawnMegaDrone(BasePlayer player)
```

## Developer Hooks

#### OnMegaDroneSpawn

- Called when a mega drone is about to be spawned for a player
- Returning `false` will prevent the drone from being spawned
- Returning `null` will result in the default behavior

```csharp
bool? OnMegaDroneSpawn(BasePlayer player)
```

#### OnMegaDroneSpawned

- Called after a mega drone has been spawned for a palyer
- No return behavior

```csharp
bool? OnMegaDroneSpawned(Drone drone, BasePlayer player)
```

#### OnMegaDroneFetch

- Called when a player tries to fetch their mega drone
- Returning `false` will prevent the drone from being fetched
- Returning `null` will result in the default behavior

```csharp
bool? OnMegaDroneFetch(BasePlayer player, Drone drone)
```

#### OnMegaDroneDestroy

- Called when a player tries to destroy their mega drone
- Returning `false` will prevent the drone from being destroyed
- Returning `null` will result in the default behavior

```csharp
bool? OnMegaDroneDestroy(BasePlayer player, Drone drone)
```
