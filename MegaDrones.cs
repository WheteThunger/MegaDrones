﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Text;
using UnityEngine;
using VLB;

namespace Oxide.Plugins
{
    [Info("Mega Drones", "WhiteThunder", "0.1.0")]
    [Description("Allows players to spawn large drones with computer stations attached to them.")]
    internal class MegaDrones : CovalencePlugin
    {
        #region Fields

        [PluginReference]
        Plugin DroneScaleManager, EntityScaleManager, VehicleDeployedLocks;

        private static MegaDrones _pluginInstance;
        private static StoredData _pluginData;
        private static Configuration _pluginConfig;

        private const string PermissionSpawn = "megadrones.spawn";
        private const string PermissionFetch = "megadrones.fetch";
        private const string PermissionDestroy = "megadrones.destroy";
        private const string PermissionGive = "megadrones.give";

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

        #endregion

        #region Hooks

        private void Init()
        {
            _pluginInstance = this;
            _pluginData = StoredData.Load();

            permission.RegisterPermission(PermissionSpawn, this);
            permission.RegisterPermission(PermissionFetch, this);
            permission.RegisterPermission(PermissionDestroy, this);
            permission.RegisterPermission(PermissionGive, this);
        }

        private void OnServerInitialized()
        {
            RegisterWithVehicleDeployedLocks();

            if (VerifyDependencies())
                RefreshAllMegaDrones();
        }

        private void Unload()
        {
            CameraMovement.RemoveAll();

            _pluginData.Save();

            _pluginData = null;
            _pluginConfig = null;
            _pluginInstance = null;
        }

        private void OnServerSave()
        {
            _pluginData.Save();
        }

        private void OnNewSave()
        {
            _pluginData = StoredData.Reset();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == DroneScaleManager || plugin == EntityScaleManager)
            {
                if (DroneScaleManager != null && EntityScaleManager != null)
                    RefreshAllMegaDrones();
            }
            else if (plugin == VehicleDeployedLocks)
            {
                RegisterWithVehicleDeployedLocks();
            }
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
        private bool? OnEntityTakeDamage(ComputerStation station, HitInfo hitInfo) =>
            HandleOnEntityTakeDamage(station, hitInfo);

        // Redirect damage from the camera to the drone.
        private bool? OnEntityTakeDamage(CCTV_RC camera, HitInfo hitInfo) =>
            HandleOnEntityTakeDamage(camera, hitInfo);

        private bool? HandleOnEntityTakeDamage(BaseEntity entity, HitInfo hitInfo)
        {
            var drone = GetParentMegaDrone(entity);
            if (drone == null)
                return null;

            drone.Hurt(hitInfo);
            HitNotify(drone, hitInfo);

            // Return true (standard) to cancel default behavior (to prevent damage).
            return true;
        }

        // This hook is exposed by plugin: Remover Tool (RemoverTool).
        private bool? canRemove(BasePlayer player, Drone drone)
        {
            if (IsMegaDrone(drone))
                return false;

            return null;
        }

        // This hook is exposed by plugin: Remover Tool (RemoverTool).
        private bool? canRemove(BasePlayer player, ComputerStation station) =>
            HandleCanRemove(station);

        // This hook is exposed by plugin: Remover Tool (RemoverTool).
        private bool? canRemove(BasePlayer player, CCTV_RC camera) =>
            HandleCanRemove(camera);

        // Not a hook, just a helper.
        private bool? HandleCanRemove(BaseEntity entity)
        {
            if (GetParentMegaDrone(entity) != null)
                return false;

            return null;
        }

        private void OnEntityKill(Drone drone)
        {
            string userIdString;
            if (!IsMegaDrone(drone, out userIdString))
                return;

            if (userIdString != null)
            {
                var player = BasePlayer.Find(userIdString);
                if (player != null)
                    ChatMessage(player, Lang.InfoDroneDestroyed);

                _pluginData.UnregisterPlayerDrone(userIdString);
                return;
            }

            _pluginData.UnregisterOtherDrone(drone);
        }

        private void OnBookmarkControlStarted(ComputerStation station, BasePlayer player, string bookmarkName, CCTV_RC camera)
        {
            var drone = GetParentMegaDrone(camera);
            if (drone == null)
                return;

            CameraMovement.AddToPlayer(player, drone);
        }

        private void OnBookmarkControlEnded(ComputerStation station, BasePlayer player, CCTV_RC camera)
        {
            if (camera == null)
                return;

            var drone = GetParentMegaDrone(camera);
            if (drone == null)
                return;

            drone.StopControl();
            Interface.CallHook("OnBookmarkControlEnded", station, player, drone);
            CameraMovement.RemoveFromPlayer(player);
        }

        private void OnEntityMounted(ComputerStation station, BasePlayer player)
        {
            var drone = GetParentMegaDrone(station);
            if (drone == null)
                return;

            StartControlling(player, station, drone);
        }

        private bool? OnCCTVDirectionChange(CCTV_RC camera)
        {
            if (GetParentMegaDrone(camera) != null)
                return false;

            return null;
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

        // This hook is exposed by plugin: Movable CCTV (MovableCCTCV)
        private bool? OnCCTVMovableBecome(CCTV_RC camera)
        {
            if (GetParentMegaDrone(camera) != null)
                return false;

            return null;
        }

        private bool? OnDroneRangeLimit(Drone drone, ComputerStation station, BasePlayer player)
        {
            if (IsMegaDrone(drone))
                return false;

            return null;
        }

        #endregion

        #region API

        private Drone API_SpawnMegaDrone(BasePlayer player)
        {
            if (SpawnMegaDroneWasBlocked(player))
                return null;

            return SpawnMegaDrone(player, shouldTrack: false);
        }

        #endregion

        #region Commands

        [Command("megadrone", "md")]
        private void CommandMegdaDrone(IPlayer player, string cmd, string[] args)
        {
            if (player.IsServer)
                return;

            // Key binds automatically pass the "True" argument.
            if (args.Length == 0 || args[0] == "True")
            {
                SubCommand_Spawn(player, cmd);
                return;
            }

            switch (args[0].ToLower())
            {
                case "h":
                case "help":
                    SubCommand_Help(player, cmd);
                    return;

                case "f":
                case "fetch":
                    SubCommand_Fetch(player);
                    return;

                case "d":
                case "destroy":
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
                sb.AppendLine(GetMessage(player, Lang.HelpSpawn, cmd));
            if (canFetch)
                sb.AppendLine(GetMessage(player, Lang.HelpFetch, cmd));
            if (canDestroy)
                sb.AppendLine(GetMessage(player, Lang.HelpDestroy, cmd));

            player.Reply(sb.ToString());
        }

        private void SubCommand_Spawn(IPlayer player, string cmd)
        {
            if (!VerifyPermissionAny(player, PermissionSpawn))
                return;

            var basePlayer = player.Object as BasePlayer;
            Vector3 spawnPosition;
            Quaternion spawnRotation;

            if (!VerifyCanInteract(player)
                || !VerifyHasNoDrone(player, cmd)
                || !VerifyOffCooldown(player, CooldownType.Spawn)
                || !_pluginConfig.CanSpawnBuildingBlocked && !VerifyNotBuildingBlocked(player)
                || !VerifySufficientSpace(player, out spawnPosition, out spawnRotation)
                || SpawnMegaDroneWasBlocked(basePlayer))
                return;

            var drone = SpawnMegaDrone(basePlayer);
            if (drone != null)
            {
                ReplyToPlayer(player, Lang.SpawnSuccess);
                TryMountPlayer(drone, basePlayer);
            }
        }

        private void SubCommand_Fetch(IPlayer player)
        {
            if (!VerifyPermissionAny(player, PermissionFetch))
                return;

            Drone drone;

            var basePlayer = player.Object as BasePlayer;
            Vector3 fetchPosition;
            Quaternion fetchRotation;

            if (!VerifyCanInteract(player)
                || !VerifyHasDrone(player, out drone)
                || !VerifyOffCooldown(player, CooldownType.Fetch)
                || !_pluginConfig.CanFetchOccupied && !VerifyDroneNotOccupied(player, drone)
                || !_pluginConfig.CanFetchBuildingBlocked && !VerifyNotBuildingBlocked(player)
                || !VerifySufficientSpace(player, out fetchPosition, out fetchRotation)
                || FetchMegaDroneWasBlocked(basePlayer, drone))
                return;

            if (_pluginConfig.DismountPlayersOnFetch)
                DismountAllPlayersFromDrone(drone);

            var rootEntity = GetRootEntity(drone);

            // The root entity rotation may not match the drone rotation.
            fetchRotation *= Quaternion.Inverse(drone.transform.localRotation);

            rootEntity.transform.SetPositionAndRotation(fetchPosition, fetchRotation);
            rootEntity.UpdateNetworkGroup();
            rootEntity.SendNetworkUpdateImmediate();

            _pluginData.StartCooldown(player.Id, CooldownType.Fetch);
            ReplyToPlayer(player, Lang.SpawnSuccess);
        }

        private void SubCommand_Destroy(IPlayer player)
        {
            if (!VerifyPermissionAny(player, PermissionDestroy))
                return;

            var basePlayer = player.Object as BasePlayer;
            Drone drone;

            if (!VerifyHasDrone(player, out drone)
                || !_pluginConfig.CanDestroyWhileOccupied && !VerifyDroneNotOccupied(player, drone)
                || DestroyMegaDroneWasBlocked(basePlayer, drone))
                return;

            drone.Kill();
        }

        [Command("givemegadrone", "givemd")]
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
                targetPlayer = player.Object as BasePlayer;

            var drone = SpawnMegaDrone(targetPlayer, shouldTrack: false);
            if (drone != null)
                ReplyToPlayer(player, Lang.GiveSuccess, targetPlayer.displayName);
        }

        #endregion

        #region Helper Methods - Command Checks

        private bool VerifyPermissionAny(IPlayer player, params string[] permissionNames)
        {
            foreach (var perm in permissionNames)
            {
                if (!permission.UserHasPermission(player.Id, perm))
                {
                    ReplyToPlayer(player, Lang.ErrorNoPermission);
                    return false;
                }
            }
            return true;
        }

        private bool VerifyHasNoDrone(IPlayer player, string cmd)
        {
            if (FindPlayerDrone(player) == null)
                return true;

            var messages = new List<string> { GetMessage(player, Lang.SpawnErrorDroneAlreadyExists) };
            if (permission.UserHasPermission(player.Id, PermissionFetch))
                messages.Add(GetMessage(player, Lang.SpawnErrorDroneAlreadyExistsHelp, cmd));

            player.Reply(string.Join(" ", messages));
            return false;
        }

        private bool VerifyHasDrone(IPlayer player, out Drone drone)
        {
            drone = FindPlayerDrone(player);
            if (drone == null)
            {
                ReplyToPlayer(player, Lang.ErrorDroneNotFound);
                return false;
            }
            return true;
        }

        private bool VerifyNotBuildingBlocked(IPlayer player)
        {
            if ((player.Object as BasePlayer).IsBuildingBlocked())
            {
                ReplyToPlayer(player, Lang.ErrorBuildingBlocked);
                return false;
            }
            return true;
        }

        private bool VerifyDroneNotOccupied(IPlayer player, Drone drone)
        {
            if (GetMountedPlayer(drone) != null || HasChildPlayer(drone))
            {
                ReplyToPlayer(player, Lang.ErrorDroneOccupied);
                return false;
            }
            return true;
        }

        private bool VerifyOffCooldown(IPlayer player, CooldownType cooldownType)
        {
            var secondsRemaining = _pluginData.GetRemainingCooldownSeconds(player.Id, cooldownType);
            if (secondsRemaining > 0)
            {
                ReplyToPlayer(player, Lang.ErrorCooldown, FormatTime(secondsRemaining));
                return false;
            }
            return true;
        }

        private bool VerifySufficientSpace(IPlayer player, out Vector3 determinedPosition, out Quaternion determinedRotation)
        {
            var basePlayer = player.Object as BasePlayer;

            // TODO: Check space

            determinedPosition = GetPlayerRelativeSpawnPosition(basePlayer);
            determinedRotation = GetPlayerRelativeSpawnRotation(basePlayer);

            return true;
        }

        private bool VerifyCanInteract(IPlayer player)
        {
            var basePlayer = player.Object as BasePlayer;
            if (!basePlayer.CanInteract())
            {
                ReplyToPlayer(player, Lang.ErrorGenericRestricted);
                return false;
            }
            return true;
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

        private static bool VerifyDependencies()
        {
            if (_pluginInstance.DroneScaleManager == null)
            {
                _pluginInstance.LogError("DroneScaleManager is not loaded, get it at https://umod.org");
                return false;
            }

            if (_pluginInstance.EntityScaleManager == null)
            {
                _pluginInstance.LogError("EntityScaleManager is not loaded, get it at https://umod.org");
                return false;
            }

            return true;
        }

        private static void RegisterWithVehicleDeployedLocks()
        {
            if (_pluginInstance.VehicleDeployedLocks == null)
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

            _pluginInstance.VehicleDeployedLocks.Call("API_RegisterCustomVehicleType", "megadrone", LockPosition, LockRotation, null, determineLockParent);
        }

        private static bool SpawnMegaDroneWasBlocked(BasePlayer player)
        {
            object hookResult = Interface.CallHook("OnMegaDroneSpawn", player);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static bool FetchMegaDroneWasBlocked(BasePlayer player, Drone drone)
        {
            object hookResult = Interface.CallHook("OnMegaDroneFetch", player, drone);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static bool DestroyMegaDroneWasBlocked(BasePlayer player, Drone drone)
        {
            object hookResult = Interface.CallHook("OnMegaDroneDestroy", player, drone);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static Drone GetControlledDrone(ComputerStation station) =>
            station.currentlyControllingEnt.Get(serverside: true) as Drone;

        private static Drone GetControlledDrone(BasePlayer player)
        {
            var computerStation = player.GetMounted() as ComputerStation;
            if (computerStation == null)
                return null;

            return GetControlledDrone(computerStation);
        }

        public static bool IsMegaDrone(Drone drone) =>
            _pluginData.IsMegaDrone(drone);

        public static bool IsMegaDrone(Drone drone, out string userIdString) =>
            _pluginData.IsMegaDrone(drone, out userIdString);

        private static BaseEntity GetRootEntity(Drone drone) =>
            _pluginInstance.DroneScaleManager?.Call("API_GetRootEntity", drone) as BaseEntity;

        private static Drone GetParentMegaDrone(BaseEntity entity, out BaseEntity rootEntity)
        {
            rootEntity = entity.GetParentEntity();
            if (rootEntity == null)
                return null;

            var drone = _pluginInstance.DroneScaleManager?.Call("API_GetParentDrone", entity) as Drone;
            if (drone == null || !IsMegaDrone(drone))
                return null;

            return drone;
        }

        private static Drone GetParentMegaDrone(BaseEntity entity)
        {
            BaseEntity rootEntity;
            return GetParentMegaDrone(entity, out rootEntity);
        }

        private static bool ParentEntityToDrone(Drone drone, BaseEntity entity)
        {
            var result = _pluginInstance.DroneScaleManager?.Call("API_ParentEntity", drone, entity);
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

        private static ComputerStation GetComputerStation(Drone drone)
        {
            var rootEntity = GetRootEntity(drone);
            if (rootEntity == null)
                return null;

            return GetChildOfType<ComputerStation>(rootEntity);
        }

        private static CCTV_RC GetCamera(Drone drone)
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
            station.SendNetworkUpdateImmediate();
            station.SendControlBookmarks(player);
            controllable.InitializeControl(player);
            station.InvokeRepeating(station.ControlCheck, 0, 0);
            Interface.CallHook("OnBookmarkControlStarted", station, player, controllable.GetIdentifier(), entity);
        }

        private static bool TryMountPlayer(Drone drone, BasePlayer player)
        {
            var station = GetComputerStation(drone);
            if (station == null)
                return false;

            station.AttemptMount(player, doMountChecks: false);
            return true;
        }

        private static bool IsDroneEligible(Drone drone) =>
            !(drone is DeliveryDrone);

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

            // computerStation.transform.localScale = new Vector3(0, 0, 0);
            foreach (var collider in station.GetComponents<BoxCollider>())
            {
                // Removing the box collider helps with mounting and dismounting at an angle
                UnityEngine.Object.Destroy(collider);
            }
        }

        private static ComputerStation DeployComputerStation(Drone drone, BasePlayer player)
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

        private static CCTV_RC DeployCamera(Drone drone, BasePlayer player, int idNumber)
        {
            var camera = GameManager.server.CreateEntity(CCTVPrefab, CameraLocalPosition, CameraLocalRotation) as CCTV_RC;
            if (camera == null)
                return null;

            camera.UpdateIdentifier($"{_pluginConfig.CamIdentifierPrefix}{idNumber}");
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
                rotation *= parent.transform.rotation;

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
            Vector3 position = player.transform.position + forward * 3f;
            position.y = player.transform.position.y + 1f;
            return position;
        }

        private static Quaternion GetPlayerRelativeSpawnRotation(BasePlayer player)
        {
            var rotation = player.GetNetworkRotation();
            var parent = player.GetParentEntity();
            if (parent != null)
                rotation *= parent.transform.rotation;

            return Quaternion.Euler(0, rotation.eulerAngles.y, 0);
        }

        private static void SetupDrone(Drone drone)
        {
            drone.pickup.enabled = false;
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

            station.controlBookmarks[identifier] = entity.net.ID;
        }

        private static Drone SpawnMegaDrone(BasePlayer player, bool shouldTrack = true)
        {
            if (!VerifyDependencies())
                return null;

            var drone = GameManager.server.CreateEntity(DronePrefab, GetPlayerRelativeSpawnPosition(player), GetPlayerRelativeSpawnRotation(player)) as Drone;
            if (drone == null)
                return null;

            drone.OwnerID = player.userID;
            var idNumber = SetRandomIdentifier(drone, _pluginConfig.DroneIdentifierPrefix);
            SetupDrone(drone);
            drone.Spawn();

            _pluginInstance.DroneScaleManager.Call("API_ScaleDrone", drone, MegaDroneScale);
            RunOnEntityBuilt(player, drone, DroneItemId);

            var computerStation = DeployComputerStation(drone, player);
            var camera = DeployCamera(drone, player, idNumber);

            if (computerStation != null)
            {
                RegisterIdentifier(computerStation, drone);

                if (camera != null)
                    RegisterIdentifier(computerStation, camera);
            }

            if (shouldTrack)
            {
                _pluginData.RegisterPlayerDrone(player.UserIDString, drone);
                _pluginData.StartCooldown(player.UserIDString, CooldownType.Spawn);
            }
            else
                _pluginData.RegisterOtherDrone(drone);

            Interface.CallHook("OnMegaDroneSpawned", drone, player);
            return drone;
        }

        private static void RemoveGroundWatch(BaseEntity entity)
        {
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<GroundWatch>());
        }

        private static void RunOnEntityBuilt(Item item, BaseEntity entity) =>
            Interface.CallHook("OnEntityBuilt", item.GetHeldEntity(), entity.gameObject);

        private static void RunOnEntityBuilt(BasePlayer basePlayer, BaseEntity entity, int itemid)
        {
            // Allow other plugins to detect the entity being deployed.
            var turretItem = basePlayer.inventory.FindItemID(itemid);
            if (turretItem != null)
            {
                RunOnEntityBuilt(turretItem, entity);
            }
            else
            {
                // Temporarily increase the player inventory capacity to ensure there is enough space.
                basePlayer.inventory.containerMain.capacity++;
                var temporaryTurretItem = ItemManager.CreateByItemID(itemid);
                if (basePlayer.inventory.GiveItem(temporaryTurretItem))
                {
                    RunOnEntityBuilt(temporaryTurretItem, entity);
                    temporaryTurretItem.RemoveFromContainer();
                }
                temporaryTurretItem.Remove();
                basePlayer.inventory.containerMain.capacity--;
            }
        }

        private static string FormatTime(double seconds) =>
            TimeSpan.FromSeconds(seconds).ToString("g");

        private static void DismountAllPlayersFromDrone(Drone drone)
        {
            var rootEntity = GetRootEntity(drone);
            var station = GetComputerStation(drone);

            if (station.IsMounted())
                station.DismountAllPlayers();

            foreach (var child in drone.children.ToList())
            {
                var childPlayer = child as BasePlayer;
                if (childPlayer != null)
                    (child as BasePlayer).SetParent(null, worldPositionStays: true);
            }
        }

        private static Drone FindPlayerDrone(IPlayer player)
        {
            uint droneId;
            if (!_pluginData.PlayerDrones.TryGetValue(player.Id, out droneId))
                return null;

            var drone = BaseNetworkable.serverEntities.Find(droneId) as Drone;
            if (drone == null)
                _pluginData.UnregisterPlayerDrone(player.Id);

            return drone;
        }

        private static BasePlayer GetMountedPlayer(Drone drone)
        {
            var station = GetComputerStation(drone);
            if (station == null)
                return null;

            return station.GetMounted();
        }

        private static bool HasChildPlayer(Drone drone)
        {
            var rootEntity = GetRootEntity(drone);
            if (rootEntity == null)
                return false;

            if (rootEntity != null)
            {
                foreach (var child in drone.children)
                {
                    if (child is BasePlayer)
                        return true;
                }
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
            var megaDroneIds = _pluginData.GetAllMegaDroneIds();

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                var drone = entity as Drone;
                if (drone == null || !IsDroneEligible(drone))
                    continue;

                if (!megaDroneIds.Contains(drone.net.ID))
                    continue;

                RefreshMegaDrone(drone);
            }
        }

        #endregion

        #region Camera Movement

        private class CameraMovement : EntityComponent<BasePlayer>
        {
            public static CameraMovement AddToPlayer(BasePlayer player, Drone drone) =>
                player.GetOrAddComponent<CameraMovement>().SetDrone(drone);

            public static void RemoveFromPlayer(BasePlayer player) =>
                DestroyImmediate(player.GetComponent<CameraMovement>());

            public static void RemoveAll()
            {
                foreach (var player in BasePlayer.activePlayerList)
                    RemoveFromPlayer(player);
            }

            private Drone _drone;

            private CameraMovement SetDrone(Drone drone)
            {
                _drone = drone;
                _drone.InitializeControl(baseEntity);
                return this;
            }

            private void Update()
            {
                if (_drone == null)
                    return;

                // Optimization: Skip if there was no user input this frame.
                if (baseEntity.lastTickTime < Time.time)
                    return;

                _drone.UserInput(baseEntity.serverInput, baseEntity);
            }
        }

        #endregion

        #region Data

        private class StoredData
        {
            [JsonProperty("PlayerDrones")]
            public Dictionary<string, uint> PlayerDrones = new Dictionary<string, uint>();

            [JsonProperty("OtherDrones")]
            public HashSet<uint> OtherDrones = new HashSet<uint>();

            [JsonProperty("Cooldowns")]
            public CooldownManager Cooldowns = new CooldownManager();

            public static StoredData Load() =>
                Interface.Oxide.DataFileSystem.ReadObject<StoredData>(_pluginInstance.Name) ?? new StoredData();

            public static StoredData Reset() => new StoredData().Save();

            public StoredData Save()
            {
                Interface.Oxide.DataFileSystem.WriteObject(_pluginInstance.Name, this);
                return this;
            }

            public HashSet<uint> GetAllMegaDroneIds()
            {
                var droneIds = new HashSet<uint>(PlayerDrones.Values);
                droneIds.UnionWith(OtherDrones);
                return droneIds;
            }

            public bool IsMegaDrone(Drone drone, out string userIdString)
            {
                var droneId = drone.net.ID;

                foreach (var entry in _pluginData.PlayerDrones)
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

            public void RegisterPlayerDrone(string userId, Drone drone) =>
                PlayerDrones[userId] = drone.net.ID;

            public void UnregisterPlayerDrone(string userId) =>
                PlayerDrones.Remove(userId);

            public void RegisterOtherDrone(Drone drone) =>
                OtherDrones.Add(drone.net.ID);

            public void UnregisterOtherDrone(Drone drone) =>
                OtherDrones.Remove(drone.net.ID);

            public long GetRemainingCooldownSeconds(string userId, CooldownType cooldownType)
            {
                long cooldownStart;
                if (!Cooldowns.GetCooldownMap(cooldownType).TryGetValue(userId, out cooldownStart))
                    return 0;

                var cooldownSeconds = _pluginConfig.Cooldowns.GetSeconds(cooldownType);
                return cooldownStart + cooldownSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            public void StartCooldown(string userId, CooldownType cooldownType)
            {
                if (_pluginConfig.Cooldowns.GetSeconds(cooldownType) <= 0)
                    return;

                Cooldowns.GetCooldownMap(cooldownType)[userId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
                        _pluginInstance.LogWarning($"Cooldown not implemented for {cooldownType}");
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

        private class Configuration : SerializableConfiguration
        {
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

            [JsonProperty("Cooldowns")]
            public CooldownConfig Cooldowns = new CooldownConfig();
        }

        private class CooldownConfig
        {
            [JsonProperty("SpawnSeconds")]
            public long SpawnSeconds = 3600;

            [JsonProperty("FetchSeconds")]
            public long FetchSeconds = 600;

            public long GetSeconds(CooldownType cooldownType)
            {
                switch (cooldownType)
                {
                    case CooldownType.Spawn:
                        return SpawnSeconds;
                    case CooldownType.Fetch:
                        return FetchSeconds;
                    default:
                        _pluginInstance.LogWarning($"Cooldown not implemented for {cooldownType}");
                        return 0;
                }
            }
        }

        private Configuration GetDefaultConfig() => new Configuration();

        #endregion

        #region Configuration Boilerplate

        private class SerializableConfiguration
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

        private bool MaybeUpdateConfig(SerializableConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            bool changed = false;

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
                            changed = true;
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

        protected override void LoadDefaultConfig() => _pluginConfig = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _pluginConfig = Config.ReadObject<Configuration>();
                if (_pluginConfig == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_pluginConfig))
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
            Config.WriteObject(_pluginConfig, true);
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

                [Lang.SpawnSuccess] = "Here is your mega drone.",
                [Lang.SpawnErrorDroneAlreadyExists] = "Error: You already have a mega drone.",
                [Lang.SpawnErrorDroneAlreadyExistsHelp] = "Try <color=#fb4>{0} fetch</color> or <color=#fb4>{0} help</color>.",

                [Lang.GiveErrorSyntax] = "Syntax: {0} <player>",
                [Lang.GiveErrorPlayerNotFound] = "Error: Player '{0}' not found.",
                [Lang.GiveSuccess] = "Player '{0}' has been given a mega drone.",

                [Lang.InfoDroneDestroyed] = "Your mega drone was destroyed.",

                [Lang.Help] = "<color=#fb4>Mega Drone Commands</color>",
                [Lang.HelpSpawn] = "<color=#fb4>{0}</color> - Spawn a mega drone",
                [Lang.HelpFetch] = "<color=#fb4>{0} f | fetch</color> - Fetch your mega drone",
                [Lang.HelpDestroy] = "<color=#fb4>{0} d | destroy</color> - Destroy your mega drone",
            }, this, "en");
        }

        #endregion
    }
}