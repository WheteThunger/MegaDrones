using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Rust;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Text;
using UnityEngine;
using VLB;

namespace Oxide.Plugins
{
    [Info("Mega Drones", "WhiteThunder", "0.2.10")]
    [Description("Allows players to spawn large drones with computer stations attached to them.")]
    internal class MegaDrones : CovalencePlugin
    {
        #region Fields

        [PluginReference]
        private readonly Plugin DroneScaleManager, EntityScaleManager, VehicleDeployedLocks;

        private Configuration _config;
        private StoredData _data;

        private const string PermissionSpawn = "megadrones.spawn";
        private const string PermissionFetch = "megadrones.fetch";
        private const string PermissionDestroy = "megadrones.destroy";
        private const string PermissionGive = "megadrones.give";
        private const string PermissionCooldownPrefix = "megadrones.cooldown";

        private const string CommandName_MegaDrone = "megadrone";
        private const string CommandName_GiveMegaDrone = "givemegadrone";
        private const string SubCommandName_Help = "help";
        private const string SubCommandName_Fetch = "fetch";
        private const string SubCommandName_Destroy = "destroy";

        private const float MegaDroneScale = 7f;

        private const int DroneItemId = 1588492232;
        private const int ComputerStationItemId = -1588628467;
        private const int CCTVItemId = 634478325;

        private const BaseEntity.Slot MegaDroneSlot = BaseEntity.Slot.UpperModifier;

        private const string DronePrefab = "assets/prefabs/deployable/drone/drone.deployed.prefab";
        private const string ComputerStationPrefab = "assets/prefabs/deployable/computerstation/computerstation.deployed.prefab";
        private const string ComputerStationDeployEffectPrefab = "assets/prefabs/deployable/table/effects/table-deploy.prefab";
        private const string CCTVPrefab = "assets/prefabs/deployable/cctvcamera/cctv_deployed.prefab";
        private const string CCTVDeployEffectPrefab = "assets/prefabs/deployable/tuna can wall lamp/effects/tuna-can-lamp-deploy.prefab";

        private static readonly Vector3 ComputerStationLocalPosition = new Vector3(0, 0.115f, 0);
        private static readonly Quaternion ComputerStationLocalRotation = Quaternion.Euler(0, 180, 0);
        private static readonly Vector3 CameraLocalPosition = new Vector3(0, -0.032f);
        private static readonly Quaternion CameraLocalRotation = Quaternion.Euler(90, 0, 0);

        private static readonly Vector3 LockPosition = new Vector3(-0.65f, 0.732f, 0.242f);
        private static readonly Quaternion LockRotation = Quaternion.Euler(0, 270, 90);

        private static readonly Vector3 DroneExtents = new Vector3(0.75f, 0.1f, 0.75f) * MegaDroneScale / 2;

        private readonly object True = true;
        private readonly object False = false;

        // These layers are used to preventing spawning inside walls or players.
        private const int BoxcastLayers = Layers.Mask.Default
            + Layers.Mask.Deployed
            + Layers.Mask.Player_Server
            + Layers.Mask.AI
            + Layers.Mask.Vehicle_Detailed
            + Layers.Mask.Vehicle_World
            + Layers.Mask.World
            + Layers.Mask.Construction
            + Layers.Mask.Tree;

        private DynamicHookHashSet<NetworkableId> _megaDroneTracker;
        private DynamicHookHashSet<ulong> _droneMounteeTracker;
        private DynamicHookHashSet<ulong> _droneControllerTracker;

        public MegaDrones()
        {
            _droneMounteeTracker = new DynamicHookHashSet<ulong>(this,
                nameof(OnEntityDismounted),
                nameof(OnPlayerDismountFailed)
            );

            _droneControllerTracker = new DynamicHookHashSet<ulong>(this,
                nameof(OnServerCommand)
            );
        }

        #endregion

        #region Hooks

        private void Init()
        {
            _config.Init(this);
            _data = StoredData.Load();

            permission.RegisterPermission(PermissionSpawn, this);
            permission.RegisterPermission(PermissionFetch, this);
            permission.RegisterPermission(PermissionDestroy, this);
            permission.RegisterPermission(PermissionGive, this);

            foreach (var entry in _config.CommandAliases)
            {
                if (entry.Key == CommandName_MegaDrone)
                {
                    AddCovalenceCommand(entry.Value, nameof(CommandMegaDrone));
                }
                else if (entry.Key == CommandName_GiveMegaDrone)
                {
                    AddCovalenceCommand(entry.Value, nameof(CommandGiveMegaDrone));
                }
            }

            var megaDroneDynamicHookNames = new List<string>()
            {
                nameof(OnEntityTakeDamage),
                nameof(canRemove),
                nameof(OnEntityKill),
                nameof(OnBookmarkControlStarted),
                nameof(OnBookmarkControlEnded),
                nameof(OnEntityMounted),
                nameof(OnCCTVDirectionChange),
                nameof(OnVehicleLockDeployed),
                nameof(OnCCTVMovableBecome),
                nameof(OnDroneRangeLimit),
            };

            if (_config.DestroyOnDisconnect)
            {
                megaDroneDynamicHookNames.Add(nameof(OnPlayerDisconnected));
            }

            _megaDroneTracker = new DynamicHookHashSet<NetworkableId>(this, megaDroneDynamicHookNames.ToArray());
            _megaDroneTracker.Unsubscribe();

            _droneControllerTracker.Unsubscribe();
            _droneMounteeTracker.Unsubscribe();
        }

        private void OnServerInitialized()
        {
            RegisterWithVehicleDeployedLocks();

            if (VerifyDependencies())
            {
                RefreshAllMegaDrones();
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                var computerStation = player.GetMounted() as ComputerStation;
                if (computerStation == null)
                    continue;

                OnEntityMounted(computerStation, player);

                var entity = computerStation.currentlyControllingEnt.Get(serverside: true);
                var drone = entity as Drone;
                if (drone != null)
                {
                    OnBookmarkControlStarted(computerStation, player, string.Empty, drone);
                    continue;
                }

                var camera = entity as CCTV_RC;
                if (camera != null)
                {
                    OnBookmarkControlStarted(computerStation, player, string.Empty, camera);
                    continue;
                }
            }
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CameraMovement.RemoveFromPlayer(player);
            }

            _data.Save();
        }

        private void OnServerSave()
        {
            _data.Save();
        }

        private void OnNewSave()
        {
            _data = StoredData.Reset();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == DroneScaleManager || plugin == EntityScaleManager)
            {
                if (DroneScaleManager != null && EntityScaleManager != null)
                {
                    RefreshAllMegaDrones();
                }
            }
            else if (plugin == VehicleDeployedLocks)
            {
                RegisterWithVehicleDeployedLocks();
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null)
                return;

            var drone = FindPlayerDrone(player.UserIDString);
            if (drone == null)
                return;

            NextTick(() =>
            {
                if (drone == null)
                    return;

                if (GetMountedPlayer(drone) == null)
                {
                    drone.Kill();
                }
            });
        }

        private void OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null || arg.cmd.FullName != "vehicle.swapseats")
                return;

            var basePlayer = arg.Player();
            if (basePlayer == null)
                return;

            var station = basePlayer.GetMounted() as ComputerStation;
            if (station == null)
                return;

            var controlledEntity = station.currentlyControllingEnt.Get(serverside: true);
            if (controlledEntity == null)
                return;

            CCTV_RC camera;

            Drone drone = controlledEntity as Drone;
            if (drone != null && IsMegaDrone(drone))
            {
                camera = GetCamera(drone);
                if (camera == null)
                    return;

                station.StopControl(basePlayer);
                StartControlling(basePlayer, station, camera);
                return;
            }

            camera = controlledEntity as CCTV_RC;
            if (camera != null)
            {
                drone = GetParentMegaDrone(camera);
                if (drone == null)
                    return;

                station.StopControl(basePlayer);
                StartControlling(basePlayer, station, drone);
                return;
            }
        }

        // Redirect damage from the computer station to the drone.
        private object OnEntityTakeDamage(ComputerStation station, HitInfo hitInfo)
        {
            return HandleOnEntityTakeDamage(station, hitInfo);
        }

        // Redirect damage from the camera to the drone.
        private object OnEntityTakeDamage(CCTV_RC camera, HitInfo hitInfo)
        {
            return HandleOnEntityTakeDamage(camera, hitInfo);
        }

        // Not a hook, just a helper.
        private object HandleOnEntityTakeDamage(BaseEntity entity, HitInfo hitInfo)
        {
            var drone = GetParentMegaDrone(entity);
            if (drone == null)
                return null;

            drone.Hurt(hitInfo);
            HitNotify(drone, hitInfo);

            // Return true (standard) to cancel default behavior (to prevent damage).
            return True;
        }

        // This hook is exposed by plugin: Remover Tool (RemoverTool).
        private object canRemove(BasePlayer player, Drone drone)
        {
            return IsMegaDrone(drone) ? False : null;
        }

        // This hook is exposed by plugin: Remover Tool (RemoverTool).
        private object canRemove(BasePlayer player, ComputerStation station)
        {
            return HandleCanRemove(station);
        }

        // This hook is exposed by plugin: Remover Tool (RemoverTool).
        private object canRemove(BasePlayer player, CCTV_RC camera)
        {
            return HandleCanRemove(camera);
        }

        // Not a hook, just a helper.
        private object HandleCanRemove(BaseEntity entity)
        {
            return GetParentMegaDrone(entity) != null ? False : null;
        }

        private void OnEntityKill(Drone drone)
        {
            string userIdString;
            if (!IsMegaDrone(drone, out userIdString))
                return;

            _megaDroneTracker.Remove(drone.net.ID);

            if (userIdString != null)
            {
                var player = BasePlayer.Find(userIdString);
                if (player != null)
                {
                    ChatMessage(player, Lang.InfoDroneDestroyed);
                }

                _data.UnregisterPlayerDrone(userIdString);
                return;
            }

            _data.UnregisterOtherDrone(drone);
        }

        private void OnBookmarkControlStarted(ComputerStation station, BasePlayer player, string bookmarkName, Drone drone)
        {
            if (!IsMegaDrone(drone))
                return;

            _droneControllerTracker.Add(player.userID);
        }

        private void OnBookmarkControlStarted(ComputerStation station, BasePlayer player, string bookmarkName, CCTV_RC camera)
        {
            var drone = GetParentMegaDrone(camera);
            if (drone == null)
                return;

            if (!RCUtils.CanControl(player, drone))
                return;

            RCUtils.AddViewer(drone, player);
            CameraMovement.AddToPlayer(player, drone);
            Interface.CallHook("OnBookmarkControlStarted", station, player, drone.GetIdentifier(), drone);
        }

        private void OnBookmarkControlEnded(ComputerStation station, BasePlayer player, Drone drone)
        {
            if (drone == null || !IsMegaDrone(drone))
                return;

            _droneControllerTracker.Remove(player.userID);
        }

        private void OnBookmarkControlEnded(ComputerStation station, BasePlayer player, CCTV_RC camera)
        {
            if (camera == null)
                return;

            var drone = GetParentMegaDrone(camera);
            if (drone == null)
                return;

            if (!RCUtils.HasController(drone, player))
                return;

            RCUtils.RemoveController(drone);
            CameraMovement.RemoveFromPlayer(player);
            Interface.CallHook("OnBookmarkControlEnded", station, player, drone);
        }

        private void OnEntityMounted(ComputerStation station, BasePlayer player)
        {
            var drone = GetParentMegaDrone(station);
            if (drone == null)
                return;

            StartControlling(player, station, drone);
        }

        private void OnEntityDismounted(ComputerStation station, BasePlayer player)
        {
            var drone = GetParentMegaDrone(station);
            if (drone == null)
                return;

            _droneMounteeTracker.Remove(player.userID);

            if (_config.DestroyOnDisconnect
                // Can skip if the owner dismounted since the disconnect hook will handle that case.
                && player.userID != drone.OwnerID
                // Make sure it's the player's personal drone, not one given by givemegadrone or via API.
                && IsPlayerMegaDrone(drone))
            {
                var ownerPlayer = BasePlayer.FindByID(drone.OwnerID);
                if (ownerPlayer == null || !ownerPlayer.IsConnected)
                {
                    drone.Kill();
                }
            }
        }

        private void OnPlayerDismountFailed(BasePlayer player, ComputerStation station)
        {
            if (station == null)
                return;

            BaseEntity rootEntity;
            var drone = GetParentMegaDrone(station, out rootEntity);
            if (drone == null)
                return;

            var rootTransform = rootEntity.transform;
            if (Vector3.Dot(Vector3.up, rootTransform.up) > 0.1f)
                return;

            // Player failed to dismount, and drone is at a bad angle, flip it upright.
            rootTransform.rotation = Quaternion.Euler(0, rootTransform.rotation.eulerAngles.y, 0);
        }

        private object OnCCTVDirectionChange(CCTV_RC camera)
        {
            return GetParentMegaDrone(camera) != null ? False : null;
        }

        // This hook is exposed by plugin: Vehicle Deployed Locks (VehicleDeployedLocks).
        private void OnVehicleLockDeployed(ComputerStation computerStation, BaseLock baseLock)
        {
            BaseEntity rootEntity;
            var drone = GetParentMegaDrone(computerStation, out rootEntity);
            if (drone == null)
                return;

            // Reference the lock from the other entities to make things easier for other plugins.
            rootEntity.SetSlot(BaseEntity.Slot.Lock, baseLock);
            drone.SetSlot(BaseEntity.Slot.Lock, baseLock);
        }

        // This hook is exposed by plugin: Drone Settings (DroneSettings).
        private string OnDroneTypeDetermine(Drone drone)
        {
            return IsMegaDrone(drone) ? Name : null;
        }

        // This hook is exposed by plugin: Movable CCTV (MovableCCTV)
        private object OnCCTVMovableBecome(CCTV_RC camera)
        {
            return GetParentMegaDrone(camera) != null ? False : null;
        }

        // This hook is exposed by plugin: Limited Drone Range (LimitedDroneRange).
        private object OnDroneRangeLimit(Drone drone)
        {
            return IsMegaDrone(drone) ? False : null;
        }

        #endregion

        #region API

        [HookMethod(nameof(API_SpawnMegaDrone))]
        public Drone API_SpawnMegaDrone(BasePlayer player)
        {
            if (SpawnMegaDroneWasBlocked(player))
                return null;

            return SpawnMegaDrone(player, shouldTrack: false);
        }

        #endregion

        #region Commands

        [Command(CommandName_MegaDrone)]
        private void CommandMegaDrone(IPlayer player, string cmd, string[] args)
        {
            if (player.IsServer)
                return;

            // Key binds automatically pass the "True" argument.
            if (args.Length == 0 || args[0] == "True")
            {
                SubCommand_Spawn(player, cmd);
                return;
            }

            switch (DetermineSubCommand(args[0].ToLower()))
            {
                case SubCommandName_Help:
                    SubCommand_Help(player, cmd);
                    return;

                case SubCommandName_Fetch:
                    SubCommand_Fetch(player);
                    return;

                case SubCommandName_Destroy:
                    SubCommand_Destroy(player);
                    return;

                default:
                    ReplyToPlayer(player, Lang.ErrorUnknownCommand, cmd + " " + String.Join(" ", args));
                    return;
            }
        }

        private void SubCommand_Help(IPlayer player, string cmd)
        {
            var canSpawn = player.HasPermission(PermissionSpawn);
            var canFetch = player.HasPermission(PermissionFetch);
            var canDestroy = player.HasPermission(PermissionDestroy);

            if (!canSpawn && !canFetch && !canDestroy)
            {
                ReplyToPlayer(player, Lang.ErrorNoPermission);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(GetMessage(player, Lang.Help));

            if (canSpawn)
            {
                var spawnSecondsRemaining = GetRemainingCooldownSeconds(player.Id, CooldownType.Spawn);
                var cooldownMessage = spawnSecondsRemaining > 0
                    ? GetMessage(player, Lang.HelpRemainingCooldown, FormatTime(spawnSecondsRemaining))
                    : string.Empty;

                sb.AppendLine(GetMessage(player, Lang.HelpSpawn, cmd, cooldownMessage));
            }

            if (canFetch)
            {
                var fetchSecondsRemaining = GetRemainingCooldownSeconds(player.Id, CooldownType.Fetch);
                var cooldownMessage = fetchSecondsRemaining > 0
                    ? GetMessage(player, Lang.HelpRemainingCooldown, FormatTime(fetchSecondsRemaining))
                    : string.Empty;

                sb.AppendLine(GetMessage(player, Lang.HelpFetch, cmd, cooldownMessage));
            }

            if (canDestroy)
            {
                sb.AppendLine(GetMessage(player, Lang.HelpDestroy, cmd));
            }

            player.Reply(sb.ToString());
        }

        private void SubCommand_Spawn(IPlayer player, string cmd)
        {
            if (!VerifyPermission(player, PermissionSpawn))
                return;

            var basePlayer = player.Object as BasePlayer;
            Vector3 spawnPosition;
            Quaternion spawnRotation;

            if (!VerifyCanInteract(player)
                || !VerifyNotMounted(player))
                return;

            var drone = FindPlayerDrone(player.Id);
            if (drone != null)
            {
                var hasFetchPermission = permission.UserHasPermission(player.Id, PermissionFetch);

                if (_config.AutoFetch && hasFetchPermission)
                {
                    FetchInternal(player, drone);
                }
                else
                {
                    var messages = new List<string> { GetMessage(player, Lang.SpawnErrorDroneAlreadyExists) };
                    if (hasFetchPermission)
                    {
                        messages.Add(GetMessage(player, Lang.SpawnErrorDroneAlreadyExistsHelp, cmd));
                    }

                    player.Reply(string.Join(" ", messages));
                }

                return;
            }

            if (!VerifyOffCooldown(player, CooldownType.Spawn)
                || !_config.CanSpawnBuildingBlocked && !VerifyCanBuild(player)
                || !VerifySufficientSpace(player, out spawnPosition, out spawnRotation)
                || SpawnMegaDroneWasBlocked(basePlayer))
                return;

            drone = SpawnMegaDrone(basePlayer);
            if (drone != null)
            {
                ReplyToPlayer(player, Lang.SpawnSuccess);

                if (_config.AutoMount)
                {
                    TryMountPlayer(drone, basePlayer);
                }
            }
        }

        private void FetchInternal(IPlayer player, Drone drone)
        {
            var basePlayer = player.Object as BasePlayer;
            Vector3 fetchPosition;
            Quaternion fetchRotation;

            if (!VerifyOffCooldown(player, CooldownType.Fetch)
                || !_config.CanFetchOccupied && !VerifyDroneNotOccupied(player, drone)
                || !_config.CanFetchBuildingBlocked && !VerifyCanBuild(player)
                || !VerifySufficientSpace(player, out fetchPosition, out fetchRotation)
                || FetchMegaDroneWasBlocked(basePlayer, drone))
                return;

            if (_config.DismountPlayersOnFetch)
            {
                DismountAllPlayersFromDrone(drone);
            }

            var rootEntity = GetRootEntity(drone);

            // The root entity rotation may not match the drone rotation.
            fetchRotation *= Quaternion.Inverse(drone.transform.localRotation);

            rootEntity.transform.SetPositionAndRotation(fetchPosition, fetchRotation);
            rootEntity.UpdateNetworkGroup();
            rootEntity.SendNetworkUpdateImmediate();

            StartCooldown(player.Id, CooldownType.Fetch);
            ReplyToPlayer(player, Lang.SpawnSuccess);

            if (_config.AutoMount)
            {
                TryMountPlayer(drone, basePlayer);
            }
        }

        private void SubCommand_Fetch(IPlayer player)
        {
            if (!VerifyPermission(player, PermissionFetch))
                return;

            Drone drone;

            if (!VerifyCanInteract(player)
                || !VerifyNotMounted(player)
                || !VerifyHasDrone(player, out drone))
                return;

            FetchInternal(player, drone);
        }

        private void SubCommand_Destroy(IPlayer player)
        {
            if (!VerifyPermission(player, PermissionDestroy))
                return;

            var basePlayer = player.Object as BasePlayer;
            Drone drone;

            if (!VerifyHasDrone(player, out drone)
                || !_config.CanDestroyWhileOccupied && !VerifyDroneNotOccupied(player, drone)
                || DestroyMegaDroneWasBlocked(basePlayer, drone))
                return;

            drone.Kill();
        }

        [Command(CommandName_GiveMegaDrone)]
        private void CommandGiveMegaDrone(IPlayer player, string cmd, string[] args)
        {
            if (!player.IsServer && !player.HasPermission(PermissionGive))
            {
                ReplyToPlayer(player, Lang.ErrorNoPermission);
                return;
            }

            BasePlayer targetPlayer;

            if (args.Length > 0)
            {
                var playerNameOrIdArg = args[0];

                targetPlayer = BasePlayer.Find(playerNameOrIdArg);
                if (targetPlayer == null)
                {
                    ReplyToPlayer(player, Lang.GiveErrorPlayerNotFound, playerNameOrIdArg);
                    return;
                }
            }
            else if (player.IsServer)
            {
                ReplyToPlayer(player, Lang.GiveErrorSyntax, cmd);
                return;
            }
            else
            {
                targetPlayer = player.Object as BasePlayer;
            }

            var drone = SpawnMegaDrone(targetPlayer, shouldTrack: false);
            if (drone != null)
            {
                ReplyToPlayer(player, Lang.GiveSuccess, targetPlayer.displayName);
            }
        }

        #endregion

        #region Utilities

        private static class RCUtils
        {
            public static bool HasController(IRemoteControllable controllable, BasePlayer player)
            {
                return controllable.ControllingViewerId?.SteamId == player.userID;
            }

            public static bool HasRealController(IRemoteControllable controllable)
            {
                return controllable.ControllingViewerId.GetValueOrDefault().SteamId != 0;
            }

            public static bool CanControl(BasePlayer player, IRemoteControllable controllable)
            {
                return !HasRealController(controllable) || HasController(controllable, player);
            }

            public static void RemoveController(IRemoteControllable controllable)
            {
                var controllerId = controllable.ControllingViewerId;
                if (controllerId.HasValue)
                {
                    controllable.StopControl(controllerId.Value);
                }
            }

            public static bool AddViewer(IRemoteControllable controllable, BasePlayer player)
            {
                return controllable.InitializeControl(new CameraViewerId(player.userID, 0));
            }
        }

        #endregion

        #region Helper Methods - Command Checks

        private bool VerifyPermission(IPlayer player, string perm)
        {
            if (permission.UserHasPermission(player.Id, perm))
                return true;

            ReplyToPlayer(player, Lang.ErrorNoPermission);
            return false;
        }

        private bool VerifyHasNoDrone(IPlayer player, string cmd)
        {
            if (FindPlayerDrone(player.Id) == null)
                return true;

            var messages = new List<string> { GetMessage(player, Lang.SpawnErrorDroneAlreadyExists) };
            if (permission.UserHasPermission(player.Id, PermissionFetch))
            {
                messages.Add(GetMessage(player, Lang.SpawnErrorDroneAlreadyExistsHelp, cmd));
            }

            player.Reply(string.Join(" ", messages));
            return false;
        }

        private bool VerifyHasDrone(IPlayer player, out Drone drone)
        {
            drone = FindPlayerDrone(player.Id);
            if (drone != null)
                return true;

            ReplyToPlayer(player, Lang.ErrorDroneNotFound);
            return false;
        }

        private bool VerifyCanBuild(IPlayer player)
        {
            if ((player.Object as BasePlayer).CanBuild())
                return true;

            ReplyToPlayer(player, Lang.ErrorBuildingBlocked);
            return false;
        }

        private bool VerifyDroneNotOccupied(IPlayer player, Drone drone)
        {
            if (GetMountedPlayer(drone) == null && !HasChildPlayer(drone))
                return true;

            ReplyToPlayer(player, Lang.ErrorDroneOccupied);
            return false;
        }

        private bool VerifyOffCooldown(IPlayer player, CooldownType cooldownType)
        {
            var secondsRemaining = GetRemainingCooldownSeconds(player.Id, cooldownType);
            if (secondsRemaining <= 0)
                return true;

            ReplyToPlayer(player, Lang.ErrorCooldown, FormatTime(secondsRemaining));
            return false;
        }

        private bool VerifySufficientSpace(IPlayer player, out Vector3 determinedPosition, out Quaternion determinedRotation)
        {
            var basePlayer = player.Object as BasePlayer;
            determinedPosition = GetPlayerRelativeSpawnPosition(basePlayer);
            determinedRotation = GetPlayerRelativeSpawnRotation(basePlayer);

            if (!Physics.CheckBox(determinedPosition, DroneExtents, determinedRotation, BoxcastLayers, QueryTriggerInteraction.Ignore))
                return true;

            ReplyToPlayer(player, Lang.ErrorInsufficientSpace);
            return false;
        }

        private bool VerifyCanInteract(IPlayer player)
        {
            if ((player.Object as BasePlayer).CanInteract())
                return true;

            ReplyToPlayer(player, Lang.ErrorGenericRestricted);
            return false;
        }

        private bool VerifyNotMounted(IPlayer player)
        {
            if ((player.Object as BasePlayer).isMounted)
            {
                ReplyToPlayer(player, Lang.ErrorMounted);
                return false;
            }

            return true;
        }

        #endregion

        #region Helper Methods

        public static void LogInfo(string message) => Interface.Oxide.LogInfo($"[Mega Drones] {message}");
        public static void LogError(string message) => Interface.Oxide.LogError($"[Mega Drones] {message}");
        public static void LogWarning(string message) => Interface.Oxide.LogWarning($"[Mega Drones] {message}");

        private bool VerifyDependencies()
        {
            if (DroneScaleManager == null)
            {
                LogError("DroneScaleManager is not loaded, get it at https://umod.org");
                return false;
            }

            if (EntityScaleManager == null)
            {
                LogError("EntityScaleManager is not loaded, get it at https://umod.org");
                return false;
            }

            return true;
        }

        private void RegisterWithVehicleDeployedLocks()
        {
            if (VehicleDeployedLocks == null)
                return;

            // Locks will be attached to the computer station.
            // A reference to the lock is also added to the root entity after it's deployed.
            Func<BaseEntity, BaseEntity> determineLockParent = (entity) =>
            {
                var computerStation = entity as ComputerStation;
                if (computerStation != null && GetParentMegaDrone(computerStation) != null)
                    return computerStation;

                var drone = entity as Drone;
                if (drone != null)
                    return GetComputerStation(drone);

                // Returning null indicates that this is not a mega drone.
                return null;
            };

            VehicleDeployedLocks.Call("API_RegisterCustomVehicleType", "megadrone", LockPosition, LockRotation, null, determineLockParent);
        }

        private static bool SpawnMegaDroneWasBlocked(BasePlayer player)
        {
            var hookResult = Interface.CallHook("OnMegaDroneSpawn", player);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static bool FetchMegaDroneWasBlocked(BasePlayer player, Drone drone)
        {
            var hookResult = Interface.CallHook("OnMegaDroneFetch", player, drone);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static bool DestroyMegaDroneWasBlocked(BasePlayer player, Drone drone)
        {
            var hookResult = Interface.CallHook("OnMegaDroneDestroy", player, drone);
            return hookResult is bool && (bool)hookResult == false;
        }

        private BaseEntity GetRootEntity(Drone drone)
        {
            return DroneScaleManager?.Call("API_GetRootEntity", drone) as BaseEntity;
        }

        private static string GetCooldownPermission(string permissionSuffix)
        {
            return $"{PermissionCooldownPrefix}.{permissionSuffix}";
        }

        public bool IsMegaDrone(Drone drone)
        {
            return _data.IsMegaDrone(drone);
        }

        public bool IsMegaDrone(Drone drone, out string userIdString)
        {
            return _data.IsMegaDrone(drone, out userIdString);
        }

        public bool IsPlayerMegaDrone(Drone drone)
        {
            string userIdString;
            return _data.IsMegaDrone(drone, out userIdString) && userIdString != null;
        }

        private Drone GetParentMegaDrone(BaseEntity entity, out BaseEntity rootEntity)
        {
            rootEntity = entity.GetParentEntity();
            if (rootEntity == null)
                return null;

            var drone = DroneScaleManager?.Call("API_GetParentDrone", entity) as Drone;
            if (drone == null || !IsMegaDrone(drone))
                return null;

            return drone;
        }

        private Drone GetParentMegaDrone(BaseEntity entity)
        {
            BaseEntity rootEntity;
            return GetParentMegaDrone(entity, out rootEntity);
        }

        private bool ParentEntityToDrone(Drone drone, BaseEntity entity)
        {
            var result = DroneScaleManager?.Call("API_ParentEntity", drone, entity);
            return result is bool && (bool)result;
        }

        private static T GetChildOfType<T>(BaseEntity entity) where T : BaseEntity
        {
            foreach (var child in entity.children)
            {
                var childOfType = child as T;
                if (childOfType != null)
                    return childOfType;
            }

            return null;
        }

        private ComputerStation GetComputerStation(Drone drone)
        {
            var rootEntity = GetRootEntity(drone);
            if (rootEntity == null)
                return null;

            return GetChildOfType<ComputerStation>(rootEntity);
        }

        private CCTV_RC GetCamera(Drone drone)
        {
            var rootEntity = GetRootEntity(drone);
            if (rootEntity == null)
                return null;

            return GetChildOfType<CCTV_RC>(rootEntity);
        }

        private static void StartControlling(BasePlayer player, ComputerStation station, IRemoteControllable controllable)
        {
            var entity = controllable.GetEnt();
            if (entity == null)
                return;

            station.currentlyControllingEnt.uid = entity.net.ID;
            station.currentPlayerID = player.userID;
            var isControlling = RCUtils.AddViewer(controllable, player);
            station.SetFlag(ComputerStation.Flag_HasFullControl, isControlling, networkupdate: false);
            station.SendNetworkUpdateImmediate();
            station.InvokeRepeating(station.ControlCheck, 0, 0);
            Interface.CallHook("OnBookmarkControlStarted", station, player, controllable.GetIdentifier(), entity);
        }

        private bool TryMountPlayer(Drone drone, BasePlayer player)
        {
            var station = GetComputerStation(drone);
            if (station == null)
                return false;

            station.AttemptMount(player, doMountChecks: false);
            return true;
        }

        private static bool IsDroneEligible(Drone drone)
        {
            return !(drone is DeliveryDrone);
        }

        private static void HitNotify(BaseEntity entity, HitInfo info)
        {
            var player = info.Initiator as BasePlayer;
            if (player == null)
                return;

            entity.ClientRPCPlayer(null, player, "HitNotify");
        }

        private static void SetupComputerStation(Drone drone, ComputerStation station)
        {
            // Damage will be processed by the drone.
            station.baseProtection = null;

            RemoveGroundWatch(station);
            station.pickup.enabled = false;
            station.OwnerID = drone.OwnerID;
            station.isMobile = true;

            if (station.IsFullySpawned() && !BaseMountable.AllMountables.Contains(station))
            {
                BaseMountable.AllMountables.Add(station);
            }

            foreach (var collider in station.GetComponents<BoxCollider>())
            {
                // Removing the box collider helps with mounting and dismounting at an angle
                UnityEngine.Object.Destroy(collider);
            }
        }

        private ComputerStation DeployComputerStation(Drone drone, BasePlayer player)
        {
            var station = GameManager.server.CreateEntity(ComputerStationPrefab, ComputerStationLocalPosition, ComputerStationLocalRotation) as ComputerStation;
            if (station == null)
                return null;

            SetupComputerStation(drone, station);

            if (!ParentEntityToDrone(drone, station))
            {
                station.Spawn();
                station.Kill();
                return null;
            }

            drone.SetSlot(MegaDroneSlot, station);

            Effect.server.Run(ComputerStationDeployEffectPrefab, station.transform.position, station.transform.up);
            RunOnEntityBuilt(player, station, ComputerStationItemId);

            return station;
        }

        private static void SetupCamera(Drone drone, CCTV_RC camera)
        {
            // Damage will be processed by the drone.
            camera.baseProtection = null;

            RemoveGroundWatch(camera);
            camera.pickup.enabled = false;
            camera.OwnerID = drone.OwnerID;
            camera.UpdateFromInput(5, 0);
        }

        private CCTV_RC DeployCamera(Drone drone, BasePlayer player, int idNumber)
        {
            var camera = GameManager.server.CreateEntity(CCTVPrefab, CameraLocalPosition, CameraLocalRotation) as CCTV_RC;
            if (camera == null)
                return null;

            camera.UpdateIdentifier($"{_config.CamIdentifierPrefix}{idNumber}");
            SetupCamera(drone, camera);

            if (!ParentEntityToDrone(drone, camera))
            {
                camera.Spawn();
                camera.Kill();
                return null;
            }

            Effect.server.Run(CCTVDeployEffectPrefab, camera.transform.position, camera.transform.up);
            RunOnEntityBuilt(player, camera, CCTVItemId);

            return camera;
        }

        private static Quaternion GetPlayerWorldRotation(BasePlayer player)
        {
            var rotation = player.GetNetworkRotation();
            var parent = player.GetParentEntity();

            if (parent != null)
            {
                rotation *= parent.transform.rotation;
            }

            return rotation;
        }

        private static Vector3 GetPlayerForwardPosition(BasePlayer player)
        {
            Vector3 forward = GetPlayerWorldRotation(player) * Vector3.forward;
            forward.y = 0;
            return forward.normalized;
        }

        // Directly in front of the player.
        private static Vector3 GetPlayerRelativeSpawnPosition(BasePlayer player)
        {
            Vector3 forward = GetPlayerForwardPosition(player);
            Vector3 position = player.transform.position + forward * 3.5f;
            position.y = player.transform.position.y + 1f;
            return position;
        }

        private static Quaternion GetPlayerRelativeSpawnRotation(BasePlayer player)
        {
            var rotation = player.GetNetworkRotation();
            var parent = player.GetParentEntity();
            if (parent != null)
            {
                rotation *= parent.transform.rotation;
            }

            return Quaternion.Euler(0, rotation.eulerAngles.y, 0);
        }

        private void SetupDrone(Drone drone)
        {
            drone.pickup.enabled = false;
            _megaDroneTracker.Add(drone.net.ID);
        }

        private static int SetRandomIdentifier(IRemoteControllable controllable, string prefix)
        {
            var idNumber = UnityEngine.Random.Range(1, 9999);
            controllable.UpdateIdentifier($"{prefix}{idNumber}");
            return idNumber;
        }

        private static void RegisterIdentifier(ComputerStation station, IRemoteControllable controllable)
        {
            var identifier = controllable.GetIdentifier();
            if (string.IsNullOrEmpty(identifier))
                return;

            var entity = controllable.GetEnt();
            if (entity == null)
                return;

            if (!station.controlBookmarks.Contains(identifier))
            {
                station.controlBookmarks.Add(identifier);
            }
        }

        private Drone SpawnMegaDrone(BasePlayer player, bool shouldTrack = true)
        {
            if (!VerifyDependencies())
                return null;

            var drone = GameManager.server.CreateEntity(DronePrefab, GetPlayerRelativeSpawnPosition(player), GetPlayerRelativeSpawnRotation(player)) as Drone;
            if (drone == null)
                return null;

            drone.OwnerID = player.userID;
            var idNumber = SetRandomIdentifier(drone, _config.DroneIdentifierPrefix);
            drone.Spawn();
            SetupDrone(drone);

            DroneScaleManager.Call("API_ScaleDrone", drone, MegaDroneScale);
            RunOnEntityBuilt(player, drone, DroneItemId);

            var computerStation = DeployComputerStation(drone, player);
            var camera = DeployCamera(drone, player, idNumber);

            if (computerStation != null)
            {
                RegisterIdentifier(computerStation, drone);

                if (camera != null)
                {
                    RegisterIdentifier(computerStation, camera);
                }
            }

            if (shouldTrack)
            {
                _data.RegisterPlayerDrone(player.UserIDString, drone);
                StartCooldown(player.UserIDString, CooldownType.Spawn);
            }
            else
            {
                _data.RegisterOtherDrone(drone);
            }

            Interface.CallHook("OnMegaDroneSpawned", drone, player);
            return drone;
        }

        private static void RemoveGroundWatch(BaseEntity entity)
        {
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<GroundWatch>());
        }

        private static void RunOnEntityBuilt(Item item, BaseEntity entity)
        {
            Interface.CallHook("OnEntityBuilt", item.GetHeldEntity(), entity.gameObject);
        }

        private static void RunOnEntityBuilt(BasePlayer basePlayer, BaseEntity entity, int itemid)
        {
            // Allow other plugins to detect the entity being deployed.
            var turretItem = basePlayer.inventory.FindItemByItemID(itemid);
            if (turretItem != null)
            {
                RunOnEntityBuilt(turretItem, entity);
            }
            else
            {
                // Temporarily increase the player inventory capacity to ensure there is enough space.
                basePlayer.inventory.containerMain.capacity++;
                var temporaryItem = ItemManager.CreateByItemID(itemid);
                if (basePlayer.inventory.GiveItem(temporaryItem))
                {
                    RunOnEntityBuilt(temporaryItem, entity);
                    temporaryItem.RemoveFromContainer();
                }

                temporaryItem.Remove();
                basePlayer.inventory.containerMain.capacity--;
            }
        }

        private static string FormatTime(double seconds)
        {
            return TimeSpan.FromSeconds(seconds).ToString("g");
        }

        private void DismountAllPlayersFromDrone(Drone drone)
        {
            var rootEntity = GetRootEntity(drone);
            var station = GetComputerStation(drone);

            if (station.AnyMounted())
            {
                station.DismountAllPlayers();
            }

            if (rootEntity != null)
            {
                foreach (var child in rootEntity.children.ToList())
                {
                    var childPlayer = child as BasePlayer;
                    if (childPlayer != null)
                    {
                        childPlayer.SetParent(null, worldPositionStays: true);
                    }
                }
            }
        }

        private Drone FindPlayerDrone(string userId)
        {
            ulong droneId;
            if (!_data.PlayerDrones.TryGetValue(userId, out droneId))
                return null;

            var drone = BaseNetworkable.serverEntities.Find(new NetworkableId(droneId)) as Drone;
            if (drone == null)
            {
                _data.UnregisterPlayerDrone(userId);
            }

            return drone;
        }

        private BasePlayer GetMountedPlayer(Drone drone)
        {
            var station = GetComputerStation(drone);
            if (station == null)
                return null;

            return station.GetMounted();
        }

        private bool HasChildPlayer(Drone drone)
        {
            var rootEntity = GetRootEntity(drone);
            if (rootEntity == null)
                return false;

            foreach (var child in rootEntity.children)
            {
                if (child is BasePlayer)
                    return true;
            }

            return false;
        }

        private void RefreshMegaDrone(Drone drone)
        {
            SetupDrone(drone);

            var rootEntity = GetRootEntity(drone);
            if (rootEntity == null)
                return;

            foreach (var child in rootEntity.children)
            {
                var station = child as ComputerStation;
                if (station != null)
                {
                    SetupComputerStation(drone, station);
                    continue;
                }

                var camera  = child as CCTV_RC;
                if (camera != null)
                {
                    SetupCamera(drone, camera);
                    continue;
                }
            }
        }

        private void RefreshAllMegaDrones()
        {
            var megaDroneIds = _data.GetAllMegaDroneIds();

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                var drone = entity as Drone;
                if (drone == null || !IsDroneEligible(drone))
                    continue;

                if (!megaDroneIds.Contains(drone.net.ID.Value))
                    continue;

                RefreshMegaDrone(drone);
            }
        }

        private string DetermineSubCommand(string argLower)
        {
            foreach (var entry in _config.SubcommandAliases)
            {
                var commandLower = entry.Key.ToLowerInvariant();
                if (commandLower == argLower || entry.Value.Contains(argLower))
                    return commandLower;
            }

            return argLower;
        }

        private long GetRemainingCooldownSeconds(string userId, CooldownType cooldownType)
        {
            long cooldownStart;
            if (!_data.Cooldowns.GetCooldownMap(cooldownType).TryGetValue(userId, out cooldownStart))
                return 0;

            var cooldownSeconds = _config.GetCooldownConfigForPlayer(this, userId).GetSeconds(cooldownType);
            return cooldownStart + cooldownSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private void StartCooldown(string userId, CooldownType cooldownType)
        {
            if (_config.GetCooldownConfigForPlayer(this, userId).GetSeconds(cooldownType) <= 0)
                return;

            _data.Cooldowns.GetCooldownMap(cooldownType)[userId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        #endregion

        #region Dynamic Hooks

        private class HookCollection
        {
            public bool IsSubscribed { get; private set; } = true;
            private readonly MegaDrones _plugin;
            private readonly string[] _hookNames;
            private readonly Func<bool> _shouldSubscribe;

            public HookCollection(MegaDrones plugin, Func<bool> shouldSubscribe, params string[] hookNames)
            {
                _plugin = plugin;
                _hookNames = hookNames;
                _shouldSubscribe = shouldSubscribe;
            }

            public void Subscribe()
            {
                foreach (var hookName in _hookNames)
                {
                    _plugin.Subscribe(hookName);
                }

                IsSubscribed = true;
            }

            public void Unsubscribe()
            {
                foreach (var hookName in _hookNames)
                {
                    _plugin.Unsubscribe(hookName);
                }

                IsSubscribed = false;
            }

            public void Refresh()
            {
                if (_shouldSubscribe())
                {
                    if (!IsSubscribed)
                    {
                        Subscribe();
                    }
                }
                else if (IsSubscribed)
                {
                    Unsubscribe();
                }
            }
        }

        private class DynamicHookHashSet<T> : HashSet<T>
        {
            private readonly HookCollection _hookCollection;

            public DynamicHookHashSet(MegaDrones plugin, params string[] hookNames)
            {
                _hookCollection = new HookCollection(plugin, () => Count > 0, hookNames);
            }

            public new bool Add(T item)
            {
                var result = base.Add(item);
                if (result)
                {
                    _hookCollection.Refresh();
                }
                return result;
            }

            public new bool Remove(T item)
            {
                var result = base.Remove(item);
                if (result)
                {
                    _hookCollection.Refresh();
                }
                return result;
            }

            public void Unsubscribe() => _hookCollection.Unsubscribe();
        }

        #endregion

        #region Camera Movement

        private class CameraMovement : EntityComponent<BasePlayer>
        {
            public static CameraMovement AddToPlayer(BasePlayer player, Drone drone)
            {
                return player.GetOrAddComponent<CameraMovement>().SetDrone(drone);
            }

            public static void RemoveFromPlayer(BasePlayer player)
            {
                DestroyImmediate(player.GetComponent<CameraMovement>());
            }

            private Drone _drone;

            private CameraMovement SetDrone(Drone drone)
            {
                _drone = drone;
                _drone.InitializeControl(new CameraViewerId(baseEntity.userID, 0));
                return this;
            }

            private void Update()
            {
                if (_drone == null)
                    return;

                // Optimization: Skip if there was no user input this frame.
                if (baseEntity.lastTickTime < Time.time)
                    return;

                _drone.UserInput(baseEntity.serverInput, new CameraViewerId(baseEntity.userID, 0));
            }
        }

        #endregion

        #region Data

        private class StoredData
        {
            [JsonProperty("PlayerDrones")]
            public Dictionary<string, ulong> PlayerDrones = new Dictionary<string, ulong>();

            [JsonProperty("OtherDrones")]
            public HashSet<ulong> OtherDrones = new HashSet<ulong>();

            [JsonProperty("Cooldowns")]
            public CooldownManager Cooldowns = new CooldownManager();

            public static StoredData Load()
            {
                return Interface.Oxide.DataFileSystem.ReadObject<StoredData>(nameof(MegaDrones)) ?? new StoredData();
            }

            public static StoredData Reset() => new StoredData().Save();

            public StoredData Save()
            {
                Interface.Oxide.DataFileSystem.WriteObject(nameof(MegaDrones), this);
                return this;
            }

            public HashSet<ulong> GetAllMegaDroneIds()
            {
                var droneIds = new HashSet<ulong>(PlayerDrones.Values);
                droneIds.UnionWith(OtherDrones);
                return droneIds;
            }

            public bool IsMegaDrone(Drone drone, out string userIdString)
            {
                var droneId = drone.net.ID.Value;

                foreach (var entry in PlayerDrones)
                {
                    if (entry.Value == droneId)
                    {
                        userIdString = entry.Key;
                        return true;
                    }
                }

                userIdString = null;
                return OtherDrones.Contains(droneId);
            }

            public bool IsMegaDrone(Drone drone)
            {
                string userIdString;
                return IsMegaDrone(drone, out userIdString);
            }

            public void RegisterPlayerDrone(string userId, Drone drone)
            {
                PlayerDrones[userId] = drone.net.ID.Value;
            }

            public void UnregisterPlayerDrone(string userId)
            {
                PlayerDrones.Remove(userId);
            }

            public void RegisterOtherDrone(Drone drone)
            {
                OtherDrones.Add(drone.net.ID.Value);
            }

            public void UnregisterOtherDrone(Drone drone)
            {
                OtherDrones.Remove(drone.net.ID.Value);
            }
        }

        private enum CooldownType { Spawn, Fetch }

        private class CooldownManager
        {
            [JsonProperty("Spawn")]
            private Dictionary<string, long> Spawn = new Dictionary<string, long>();

            [JsonProperty("Fetch")]
            private Dictionary<string, long> Fetch = new Dictionary<string, long>();

            public Dictionary<string, long> GetCooldownMap(CooldownType cooldownType)
            {
                switch (cooldownType)
                {
                    case CooldownType.Spawn:
                        return Spawn;
                    case CooldownType.Fetch:
                        return Fetch;
                    default:
                        LogWarning($"Cooldown not implemented for {cooldownType}");
                        return null;
                }
            }

            public void ClearAll()
            {
                Spawn.Clear();
                Fetch.Clear();
            }
        }

        #endregion

        #region Configuration

        private class Configuration : BaseConfiguration
        {
            [JsonProperty("AutoMount")]
            public bool AutoMount = true;

            [JsonProperty("AutoFetch")]
            public bool AutoFetch = false;

            [JsonProperty("DroneIdentifierPrefix")]
            public string DroneIdentifierPrefix = "MD";

            [JsonProperty("CamIdentifierPrefix")]
            public string CamIdentifierPrefix = "MDCam";

            [JsonProperty("CanSpawnWhileBuildingBlocked")]
            public bool CanSpawnBuildingBlocked = false;

            [JsonProperty("CanFetchWhileBuildingBlocked")]
            public bool CanFetchBuildingBlocked = false;

            [JsonProperty("CanFetchWhileOccupied")]
            public bool CanFetchOccupied = false;

            [JsonProperty("CanDestroyWhileOccupied")]
            public bool CanDestroyWhileOccupied = false;

            [JsonProperty("DismountPlayersOnFetch")]
            public bool DismountPlayersOnFetch = true;

            [JsonProperty("DestroyOnDisconnect")]
            public bool DestroyOnDisconnect = false;

            [JsonProperty("DefaultCooldowns")]
            public CooldownConfig DefaultCooldowns = new CooldownConfig()
            {
                SpawnSeconds = 3600,
                FetchSeconds = 600,
            };

            [JsonProperty("CooldownsRequiringPermission")]
            public CooldownConfig[] CooldownsRequiringPermission =
            {
                new CooldownConfig
                {
                    PermissionSuffix = "long",
                    SpawnSeconds = 86400,
                    FetchSeconds = 3600,
                },
                new CooldownConfig
                {
                    PermissionSuffix = "medium",
                    SpawnSeconds = 3600,
                    FetchSeconds = 600,
                },
                new CooldownConfig
                {
                    PermissionSuffix = "short",
                    SpawnSeconds = 600,
                    FetchSeconds = 60,
                },
                new CooldownConfig
                {
                    PermissionSuffix = "none",
                    SpawnSeconds = 0,
                    FetchSeconds = 0,
                },
            };

            [JsonProperty("CommandAliases")]
            public Dictionary<string, string[]> CommandAliases = new Dictionary<string, string[]>()
            {
                [CommandName_MegaDrone] = new[] { "md" },
                [CommandName_GiveMegaDrone] = new[] { "givemd" },
            };

            [JsonProperty("SubcommandAliases")]
            public Dictionary<string, string[]> SubcommandAliases = new Dictionary<string, string[]>()
            {
                [SubCommandName_Help] = new[] { "h" },
                [SubCommandName_Fetch] = new[] { "f" },
                [SubCommandName_Destroy] = new[] { "d" },
            };

            public void Init(MegaDrones pluginInstance)
            {
                foreach (var cooldownConfig in CooldownsRequiringPermission)
                {
                    cooldownConfig.Init(pluginInstance);
                }
            }

            public CooldownConfig GetCooldownConfigForPlayer(MegaDrones plugin, string userId)
            {
                if (CooldownsRequiringPermission.Length == 0)
                    return DefaultCooldowns;

                for (var i = CooldownsRequiringPermission.Length - 1; i >= 0; i--)
                {
                    var config = CooldownsRequiringPermission[i];
                    if (config.Permission != null && plugin.permission.UserHasPermission(userId, config.Permission))
                        return config;
                }

                return DefaultCooldowns;
            }
        }

        private class CooldownConfig
        {
            [JsonProperty("PermissionSuffix", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string PermissionSuffix;

            [JsonProperty("SpawnSeconds")]
            public long SpawnSeconds;

            [JsonProperty("FetchSeconds")]
            public long FetchSeconds;

            [JsonIgnore]
            public string Permission;

            public void Init(MegaDrones plugin)
            {
                if (string.IsNullOrWhiteSpace(PermissionSuffix))
                    return;

                Permission = GetCooldownPermission(PermissionSuffix);
                plugin.permission.RegisterPermission(Permission, plugin);
            }

            public long GetSeconds(CooldownType cooldownType)
            {
                switch (cooldownType)
                {
                    case CooldownType.Spawn:
                        return SpawnSeconds;
                    case CooldownType.Fetch:
                        return FetchSeconds;
                    default:
                        LogWarning($"Cooldown not implemented for {cooldownType}");
                        return 0;
                }
            }
        }

        private Configuration GetDefaultConfig() => new Configuration();

        #endregion

        #region Configuration Helpers

        private class BaseConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(BaseConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                object currentRawValue;
                if (currentRaw.TryGetValue(key, out currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                        {
                            changed = true;
                        }
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion

        #region Localization

        private string GetMessage(string playerId, string messageName, params object[] args)
        {
            var message = lang.GetMessage(messageName, this, playerId);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        private string GetMessage(IPlayer player, string messageName, params object[] args) =>
            GetMessage(player.Id, messageName, args);

        private void ReplyToPlayer(IPlayer player, string messageName, params object[] args) =>
            player.Reply(string.Format(GetMessage(player, messageName), args));

        private void ChatMessage(BasePlayer player, string messageName, params object[] args) =>
            player.ChatMessage(string.Format(GetMessage(player.UserIDString, messageName), args));

        private class Lang
        {
            public const string ErrorNoPermission = "Error.NoPermission";
            public const string ErrorBuildingBlocked = "Error.BuildingBlocked";
            public const string ErrorDroneNotFound = "Error.DroneNotFound";
            public const string ErrorDroneOccupied = "Error.DroneOccupied";
            public const string ErrorCooldown = "Error.Cooldown";
            public const string ErrorGenericRestricted = "Error.GenericRestricted";
            public const string ErrorUnknownCommand = "Error.UnknownCommand";
            public const string ErrorMounted = "Error.Mounted";
            public const string ErrorInsufficientSpace = "Error.InsufficientSpace";

            public const string SpawnSuccess = "Spawn.Success";
            public const string SpawnErrorDroneAlreadyExists = "Spawn.Error.DroneAlreadyExists";
            public const string SpawnErrorDroneAlreadyExistsHelp = "Spawn.Error.DroneAlreadyExists.Help";

            public const string GiveErrorSyntax = "Give.Error.Syntax";
            public const string GiveErrorPlayerNotFound = "Give.Error.PlayerNotFound";
            public const string GiveSuccess = "Give.Success";

            public const string InfoDroneDestroyed = "Info.DroneDestroyed";

            public const string Help = "Help";
            public const string HelpSpawn = "Help.Spawn";
            public const string HelpFetch = "Help.Fetch";
            public const string HelpDestroy = "Help.Destroy";
            public const string HelpRemainingCooldown = "Help.RemainingCooldown";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.ErrorNoPermission] = "You don't have permission to do that.",
                [Lang.ErrorBuildingBlocked] = "Error: Cannot do that while building blocked.",
                [Lang.ErrorDroneNotFound] = "Error: You need a mega drone to do that.",
                [Lang.ErrorDroneOccupied] = "Error: Cannot do that while your mega drone is occupied.",
                [Lang.ErrorCooldown] = "Please wait <color=#f44>{0}</color> and try again.",
                [Lang.ErrorGenericRestricted] = "Error: You cannot do that right now.",
                [Lang.ErrorUnknownCommand] = "Error: Unrecognized command <color=#fb4>{0}</color>.",
                [Lang.ErrorMounted] = "Error: Cannot do that while mounted.",
                [Lang.ErrorInsufficientSpace] = "Error: Not enough space.",

                [Lang.SpawnSuccess] = "Here is your mega drone.",
                [Lang.SpawnErrorDroneAlreadyExists] = "Error: You already have a mega drone.",
                [Lang.SpawnErrorDroneAlreadyExistsHelp] = "Try <color=#fb4>{0} fetch</color> or <color=#fb4>{0} help</color>.",

                [Lang.GiveErrorSyntax] = "Syntax: {0} <player>",
                [Lang.GiveErrorPlayerNotFound] = "Error: Player '{0}' not found.",
                [Lang.GiveSuccess] = "Player '{0}' has been given a mega drone.",

                [Lang.InfoDroneDestroyed] = "Your mega drone was destroyed.",

                [Lang.Help] = "<color=#fb4>Mega Drone Commands</color>",
                [Lang.HelpSpawn] = "<color=#fb4>{0}</color> - Spawn a mega drone{1}",
                [Lang.HelpFetch] = "<color=#fb4>{0} f | fetch</color> - Fetch your mega drone{1}",
                [Lang.HelpDestroy] = "<color=#fb4>{0} d | destroy</color> - Destroy your mega drone",
                [Lang.HelpRemainingCooldown] = " - <color=#f44>{0}</color>",
            }, this, "en");
            //Added Brazilian Portuguese translation
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.ErrorNoPermission] = "Você não tem permissão para fazer isso.",
                [Lang.ErrorBuildingBlocked] = "Erro: Não é possível fazer isso enquanto o prédio está bloqueado.",
                [Lang.ErrorDroneNotFound] = "Erro: você precisa de um mega drone para fazer isso.",
                [Lang.ErrorDroneOccupied] = "Erro: Não é possível fazer isso enquanto o mega drone está ocupado.",
                [Lang.ErrorCooldown] = "Aguarde <color=#f44>{0}</color> e tente novamente.",
                [Lang.ErrorGenericRestricted] = "Erro: você não pode fazer isso agora.",
                [Lang.ErrorUnknownCommand] = "Erro: comando não reconhecido <color=#fb4>{0}</color>.",
                [Lang.ErrorMounted] = "Erro: não é possível fazer isso enquanto está montado.",
                [Lang.ErrorInsufficientSpace] = "Erro: espaço insuficiente.",

                [Lang.SpawnSuccess] = "Aqui está o seu mega drone.",
                [Lang.SpawnErrorDroneAlreadyExists] = "Erro: você já tem um mega drone.",
                [Lang.SpawnErrorDroneAlreadyExistsHelp] = "Experimente <color=#fb4>{0} fetch</color> ou <color=#fb4>{0} help</color>.",

                [Lang.GiveErrorSyntax] = "Syntax: {0} <player>",
                [Lang.GiveErrorPlayerNotFound] = "Erro: Player '{0}' não encontrado.",
                [Lang.GiveSuccess] = "O jogador '{0}' recebeu um mega drone.",

                [Lang.InfoDroneDestroyed] = "Seu mega drone foi destruído.",

                [Lang.Help] = "<color=#fb4>Comandos Mega Drone</color>",
                [Lang.HelpSpawn] = "<color=#fb4>{0}</color> - Gerar um mega drone{1}",
                [Lang.HelpFetch] = "<color=#fb4>{0} f | fetch</color> - Pegue seu mega drone{1}",
                [Lang.HelpDestroy] = "<color=#fb4>{0} d | destroy</color> - Destrua seu mega drone",
                [Lang.HelpRemainingCooldown] = " - <color=#f44>{0}</color>",
            }, this, "pt-BR");
        }

        #endregion
    }
}
