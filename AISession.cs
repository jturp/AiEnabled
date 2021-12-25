using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;

using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.ObjectBuilders;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using AiEnabled.Utilities;
using AiEnabled.Bots;
using AiEnabled.Ai.Support;
using AiEnabled.Support;
using AiEnabled.Networking;
using Sandbox.Game;
using VRage.ModAPI;
using VRage.Game.Entity;
using AiEnabled.Bots.Roles;
using VRage.Utils;
using Sandbox.Game.Weapons;
using VRage.Game.Entity.UseObject;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Particles;
using Sandbox.Game.Components;
using AiEnabled.ConfigData;
using Sandbox.Game.Entities.Character.Components;
using AiEnabled.API;
using AiEnabled.Graphics.Support;
using AiEnabled.Graphics;
using VRage.Input;
using AiEnabled.Input;
using Sandbox.ModAPI.Weapons;
using AiEnabled.Projectiles;

namespace AiEnabled
{
  [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
  public partial class AiSession : MySessionComponentBase
  {
    public class ControlInfo
    {
      public IMyIdentity Identity;
      public IMyEntityController Controller;
      public long EntityId;

      public ControlInfo(IMyPlayer player, long entityId)
      {
        Identity = player.Identity;
        Controller = player.Controller;
        EntityId = entityId;
      }
    }

    public struct FutureBot
    {
      public string Subtype, DisplayName;
      public long OwnerId, GridId, BotId;
      public int Role;
      public MyPositionAndOrientation PositionAndOrientation;

      public FutureBot(string subtype, string name, long ownerId, long botId, long gridEntityId, BotType botType, MatrixD matrix)
      {
        DisplayName = name;
        Subtype = subtype;
        OwnerId = ownerId;
        GridId = gridEntityId;
        BotId = botId;
        Role = (int)botType;
        PositionAndOrientation = new MyPositionAndOrientation(matrix);
      }
    }

    public static int MainThreadId = 1;
    public static AiSession Instance;
    public string VERSION = "v0.9b";

    public int MaxBots = 100;
    public int MaxHelpers = 2;
    public int ControllerCacheNum = 20;
    public uint GlobalSpawnTimer, GlobalSpeakTimer, GlobalMapInitTimer;

    public int BotNumber => _robots?.Count ?? 0;
    public Logger Logger { get; protected set; }
    public bool Registered { get; protected set; }
    public bool CanSpawn => Registered && _controllerInfo?.Count > 10;

    public bool FactoryControlsHooked;
    public bool FactoryControlsCreated;
    public bool FactoryActionsCreated;
    public bool IsServer, IsClient;
    public bool DrawDebug;

    public List<HelperInfo> MyHelperInfo;
    public CommandMenu CommandMenu;
    public PlayerMenu PlayerMenu;
    public HudAPIv2 HudAPI;
    public NetworkHandler Network;
    public SaveData ModSaveData;
    public PlayerData PlayerData;
    public Inputs Input;
    public ProjectileInfo Projectiles = new ProjectileInfo();
    LocalBotAPI _localBotAPI;
    IMyHudNotification _hudMsg;
    bool _isControlBot;
    Vector3D _starterPosition;

    public GridBase GetNewGraph(MyCubeGrid grid, Vector3D newGraphPosition, MatrixD worldMatrix)
    {
      if (grid != null)
        return GetGridGraph(grid, worldMatrix);

      return GetVoxelGraph(newGraphPosition);
    }

    public CubeGridMap GetGridGraph(MyCubeGrid grid, MatrixD worldMatrix)
    {
      if (grid == null || grid.MarkedForClose)
        return null;

      if (grid.GridSizeEnum != MyCubeSize.Large)
      {
        Logger.Log($"GetGridGraph: Attempted to get a graph for a small grid!", MessageType.WARNING);
        return null;
      }

      CubeGridMap gridBase;
      if (!GridGraphDict.TryGetValue(grid.EntityId, out gridBase) || gridBase == null || !gridBase.IsValid)
      {
        gridBase = new CubeGridMap(grid, worldMatrix);
        GridGraphDict.TryAdd(grid.EntityId, gridBase);
      }

      return gridBase;
    }

    ulong _lastVoxelId;
    public VoxelGridMap GetVoxelGraph(Vector3D worldPosition, bool forceRefresh = false)
    {
      foreach (var voxelGraph in VoxelGraphDict.Values)
      {
        if (voxelGraph == null || !voxelGraph.IsValid)
        {
          VoxelGridMap _;
          VoxelGraphDict.TryRemove(voxelGraph.Key, out _);
          continue;
        }

        if (voxelGraph.OBB.Contains(ref worldPosition))
        {
          if (forceRefresh)
            voxelGraph.Refresh();

          return voxelGraph;
        }
      }

      var graph = new VoxelGridMap(worldPosition)
      {
        Key = _lastVoxelId
      };

      VoxelGraphDict[_lastVoxelId] = graph;
      _lastVoxelId++;
      return graph;
    }

    public void StartWeaponFire(long botId, long targetId, float damage, List<float> rand, int ticksBetweenAttacks, int ammoRemaining, bool isGrinder, bool isWelder)
    {
      var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
      if (bot == null || bot.IsDead)
        return;

      var tgt = MyEntities.GetEntityById(targetId);
      if (!isWelder && (tgt == null || tgt.MarkedForClose))
        return;

      var info = GetWeaponInfo();
      info.Set(bot, tgt, damage, rand, ticksBetweenAttacks, ammoRemaining, isGrinder, isWelder);

      _botEntityIds[botId] = new byte();
      _weaponFireList.Add(info);
    }

    public PathCollection GetCollection()
    {
      var pc = _pathCollections.Count > 0 ? _pathCollections.Pop() : new PathCollection();
      return pc;
    }

    public WeaponInfo GetWeaponInfo()
    {
      var info = _weaponInfoStack.Count > 0 ? _weaponInfoStack.Pop() : new WeaponInfo();
      return info;
    }

    public IconInfo GetIconInfo()
    {
      var info = _iconInfoStack.Count > 0 ? _iconInfoStack.Pop() : new IconInfo();
      return info;
    }

    public HealthInfo GetHealthInfo()
    {
      var info = _healthInfoStack.Count > 0 ? _healthInfoStack.Pop() : new HealthInfo();
      return info;
    }

    public void ReturnCollection(PathCollection pc)
    {
      if (pc == null)
        return;

      pc.Locked = false;
      pc.CleanUp(true);

      pc.Bot = null;
      pc.Graph = null;

      if (_pathCollections != null)
        _pathCollections.Push(pc);
    }

    public void ReturnWeaponInfo(WeaponInfo info)
    {
      info.Clear();
      _weaponInfoStack.Push(info);
    }

    public void ReturnIconInfo(IconInfo info)
    {
      info.Clear();
      _iconInfoStack.Push(info);
    }

    public void ReturnHealthInfo(HealthInfo info)
    {
      info.Clear();
      _healthInfoStack.Push(info);
    }

    protected override void UnloadData()
    {
      try
      {
        UnloadModData();        
      }
      catch
      {
        // pass
      }
      finally
      {
        Instance = null;
        base.UnloadData();
      }
    }

    void UnloadModData()
    {
      Logger?.Log($"AiSession: Unloading mod data. Registered = {Registered}");
      Registered = false;

      MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
      MyEntities.OnEntityAdd -= MyEntities_OnEntityAdd;
      MyVisualScriptLogicProvider.PlayerConnected -= PlayerConnected;
      MyVisualScriptLogicProvider.PlayerDisconnected -= PlayerDisconnected;
      MyVisualScriptLogicProvider.PlayerEnteredCockpit -= PlayerEnteredCockpit;
      MyVisualScriptLogicProvider.PlayerLeftCockpit -= PlayerLeftCockpit;
      MyVisualScriptLogicProvider.PlayerSpawned -= PlayerSpawned;
      MyVisualScriptLogicProvider.PlayerDied -= PlayerDied;

      if (MyAPIGateway.Session?.Factions != null)
      {
        MyAPIGateway.Session.Factions.FactionCreated -= Factions_FactionCreated;
        MyAPIGateway.Session.Factions.FactionEdited -= Factions_FactionEdited;
        MyAPIGateway.Session.Factions.FactionStateChanged -= Factions_FactionStateChanged;
        MyAPIGateway.Session.Factions.FactionAutoAcceptChanged -= Factions_FactionAutoAcceptChanged;
      }

      if (_selectedBot != null)
        MyVisualScriptLogicProvider.SetHighlight(_selectedBot.Name, false);

      if (ModSaveData != null)
      {
        if (ModSaveData.PlayerHelperData != null)
        {
          foreach (var item in ModSaveData.PlayerHelperData)
            item?.Close();

          ModSaveData.PlayerHelperData.Clear();
        }

        ModSaveData.FactionPairings?.Clear();
      }

      if (BlockFaceDictionary != null)
      {
        foreach (var kvp in BlockFaceDictionary)
        {
          if (kvp.Value == null)
            continue;

          foreach (var kvp2 in kvp.Value)
            kvp2.Value?.Clear();

          kvp.Value?.Clear();
        }

        BlockFaceDictionary.Clear();
      }

      if (PlayerToHelperIdentity != null)
      {
        foreach (var kvp in PlayerToHelperIdentity)
          kvp.Value?.Clear();

        PlayerToHelperIdentity.Clear();
      }

      if (PlayerToActiveHelperIds != null)
      {
        foreach (var kvp in PlayerToActiveHelperIds)
          kvp.Value?.Clear();

        PlayerToActiveHelperIds.Clear();
      }

      if (GridGraphDict != null)
      {
        foreach (var kvp in GridGraphDict)
        {
          var map = kvp.Value;
          if (map != null)
          {
            map.Close();
          }
        }

        GridGraphDict.Clear();
      }

      if (VoxelGraphDict != null)
      {
        foreach (var graph in VoxelGraphDict.Values)
        {
          if (graph != null)
          {
            graph.Close();
          }
        }

        VoxelGraphDict.Clear();
      }

      if (_pathCollections != null)
      {
        foreach (var collection in _pathCollections)
          collection?.Close();

        _pathCollections.Clear();
      }

      if (InvCacheStack != null)
      {
        while (InvCacheStack.Count > 0)
        {
          InventoryCache cache;
          InvCacheStack.TryPop(out cache);
          cache?.Close();
        }
      }

      CommandMenu?.Close();
      PlayerMenu?.Close();
      Input?.Close();
      HudAPI?.Close();
      Projectiles?.Close();
      Network?.Unregister();

      InvCacheStack?.Clear();
      SlimListStack?.Clear();
      GridMapListStack?.Clear();
      OverlapResultListStack?.Clear();
      GridCheckHashStack?.Clear();
      StringListStack?.Clear();
      SoundListStack?.Clear();
      EntListStack?.Clear();
      HitListStack?.Clear();
      LineListStack?.Clear();
      GridGroupListStack?.Clear();
      Players?.Clear();
      Bots?.Clear();
      CatwalkRailDirections?.Clear();
      CatwalkBlockDefinitions?.Clear();
      SlopeBlockDefinitions?.Clear();
      SlopedHalfBlockDefinitions?.Clear();
      RampBlockDefinitions?.Clear();
      HalfStairBlockDefinitions?.Clear();
      HalfStairMirroredDefinitions?.Clear();
      LadderBlockDefinitions?.Clear();
      PassageBlockDefinitions?.Clear();
      ArmorPanelMiscDefinitions?.Clear();
      ArmorPanelAllDefinitions?.Clear();
      FactoryBotInfoDict?.Clear();
      SoundPairDict?.Clear();
      SoundEmitters?.Clear();
      FutureBotQueue?.Clear();
      RobotSubtypes?.Clear();
      UseObjectsAPI?.Clear();
      BotToSeatRelativePosition?.Clear();
      ComponentInfoDict?.Clear();
      StorageStack?.Clear();

      _gpsAddIDs?.Clear();
      _gpsOwnerIDs?.Clear();
      _gpsRemovals?.Clear();
      _localGpsBotIds?.Clear();
      _graphRemovals?.Clear();
      _tempPlayers?.Clear();
      _tempPlayersAsync?.Clear();
      _newPlayerIds?.Clear();
      _robots?.Clear();
      _cli?.Clear();
      _controllerInfo?.ClearImmediate();
      _pendingControllerInfo?.ClearImmediate();
      _weaponFireList?.Clear();
      _weaponInfoStack?.Clear();
      _controlBotIds?.Clear();
      _botsToClose?.Clear();
      _botEntityIds?.Clear();
      _useObjList?.Clear();
      _gridSeats?.Clear();
      _gridSeatGroupEnter?.Clear();

      MapInitQueue = null;
      InvCacheStack = null;
      CornerArrayStack = null;
      SlimListStack = null;
      GridMapListStack = null;
      OverlapResultListStack = null;
      GridCheckHashStack = null;
      StringListStack = null;
      SoundListStack = null;
      EntListStack = null;
      HitListStack = null;
      LineListStack = null;
      GridGroupListStack = null;
      Players = null;
      Bots = null;
      DiagonalDirections = null;
      CardinalDirections = null;
      BtnPanelDefinitions = null;
      VoxelMovementDirections = null;
      CatwalkRailDirections = null;
      CatwalkBlockDefinitions = null;
      SlopeBlockDefinitions = null;
      SlopedHalfBlockDefinitions = null;
      RampBlockDefinitions = null;
      HalfWallDefinitions = null;
      FreightBlockDefinitions = null;
      RailingBlockDefinitions = null;
      HalfStairBlockDefinitions = null;
      HalfStairMirroredDefinitions = null;
      LadderBlockDefinitions = null;
      BlockFaceDictionary = null;
      PassageBlockDefinitions = null;
      ArmorPanelFullDefinitions = null;
      ArmorPanelSlopeDefinitions = null;
      ArmorPanelHalfDefinitions = null;
      ArmorPanelMiscDefinitions = null;
      ArmorPanelAllDefinitions = null;
      PipeBlockDefinitions = null;
      LockerDefinitions = null;
      BeamBlockDefinitions = null;
      FlatWindowDefinitions = null;
      AngledWindowDefinitions = null;
      VanillaTurretDefinitions = null;
      FactoryBotInfoDict = null;
      SoundPairDict = null;
      SoundEmitters = null;
      ModSaveData = null;
      FutureBotQueue = null;
      RobotSubtypes = null;
      Projectiles = null;
      PlayerToHelperIdentity = null;
      PlayerToActiveHelperIds = null;
      UseObjectsAPI = null;
      Input = null;
      CommandMenu = null;
      PlayerMenu = null;
      BotToSeatRelativePosition = null;
      ComponentInfoDict = null;
      StorageStack = null;
      VoxelGraphDict = null;
      GridGraphDict = null;

      _gpsAddIDs = null;
      _gpsOwnerIDs = null;
      _gpsRemovals = null;
      _localGpsBotIds = null;
      _graphRemovals = null;
      _localBotAPI = null;
      _validSlopedBlockDefs = null;
      _pathCollections = null;
      _tempPlayers = null;
      _tempPlayersAsync = null;
      _newPlayerIds = null;
      _robots = null;
      _ignoreTypes = null;
      _cli = null;
      _controllerInfo = null;
      _pendingControllerInfo = null;
      _weaponFireList = null;
      _weaponInfoStack = null;
      _controlBotIds = null;
      _botsToClose = null;
      _botEntityIds = null;
      _useObjList = null;
      _gridSeats = null;
      _gridSeatGroupEnter = null;

      Logger?.Close();
    }

    public override void BeforeStart()
    {
      try
      {
        Instance = this;
        Logger = new Logger("AiEnabled.log");

        Network = new NetworkHandler(55387, this);
        Network.Register();

        IsServer = MyAPIGateway.Multiplayer.IsServer;
        IsClient = !IsServer;
        MainThreadId = Environment.CurrentManagedThreadId;

        foreach (var def in MyDefinitionManager.Static.Characters)
        {
          var subtype = def.Id.SubtypeName;
          AnimationControllerDictionary[subtype] = def.AnimationController;
          RobotSubtypes.Add(subtype);
        }

        //Logger.Log($"Sounds:");
        //foreach (var soundDef in MyDefinitionManager.Static.GetSoundDefinitions())
        //{
        //  Logger.Log(soundDef.Id.SubtypeName);
        //}

        //HashSet<MyDefinitionId> ammoIds = new HashSet<MyDefinitionId>();
        //Logger.Log($"Ammo Definition info");
        //foreach (var def in MyDefinitionManager.Static.GetWeaponDefinitions())
        //{
        //  var weaponItemDef = def as MyWeaponItemDefinition;
        //  if (weaponItemDef == null)
        //  {
        //    Logger.AddLine($"{def.Id} was not a MyWeaponItemDefinition\n");
        //    continue;
        //  }

        //  var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponItemDef.WeaponDefinitionId);
        //  if (weaponDef?.AmmoMagazinesId != null)
        //  {
        //    for (int i = 0; i < weaponDef.AmmoMagazinesId.Length; i++)
        //    {
        //      var magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(weaponDef.AmmoMagazinesId[i]);
        //      if (magDef != null)
        //      {
        //        var ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(magDef.AmmoDefinitionId);
        //        var projDef = ammoDef as MyProjectileAmmoDefinition;
        //        var mslDef = ammoDef as MyMissileAmmoDefinition;
        //        if (projDef != null)
        //        {
        //          if (!ammoIds.Add(projDef.Id))
        //            continue;

        //          Logger.AddLine($"{projDef.Id}");
        //          Logger.AddLine($" -> AmmoType: {projDef.AmmoType}");
        //          Logger.AddLine($" -> IsExplosive {projDef.IsExplosive}");
        //          Logger.AddLine($" -> Speed: {projDef.DesiredSpeed}");
        //          Logger.AddLine($" -> SpeedVar: {projDef.SpeedVar}");
        //          Logger.AddLine($" -> Max Trajectory: {projDef.MaxTrajectory}");
        //          Logger.AddLine($" -> HeadShot Allowed: {projDef.HeadShot}");
        //          Logger.AddLine($" -> HeadShot Damage: {projDef.ProjectileHeadShotDamage}");
        //          Logger.AddLine($" -> Health Damage: {projDef.ProjectileHealthDamage}");
        //          Logger.AddLine($" -> Mass Damage: {projDef.ProjectileMassDamage}");
        //          Logger.AddLine($" -> Mechanical Damage: {projDef.GetDamageForMechanicalObjects()}");
        //          Logger.AddLine($" -> Damage Multiplier = {weaponDef.DamageMultiplier}");
        //          Logger.AddLine($" -> OnHit Effect: {projDef.ProjectileOnHitEffectName}");
        //          Logger.AddLine($" -> Trail Material: {projDef.ProjectileTrailMaterial}");
        //          Logger.AddLine($" -> Trail Probability: {projDef.ProjectileTrailProbability}");
        //          Logger.AddLine($" -> Trail Color: {projDef.ProjectileTrailColor}");
        //          Logger.AddLine($" -> Trail Scale: {projDef.ProjectileTrailScale}\n");
        //        }
        //        else if (mslDef != null)
        //        {
        //          if (!ammoIds.Add(mslDef.Id))
        //            continue;

        //          Logger.AddLine($"{mslDef.Id}");
        //          Logger.AddLine($" -> AmmoType: {mslDef.AmmoType}");
        //          Logger.AddLine($" -> IsExplosive {mslDef.IsExplosive}");
        //          Logger.AddLine($" -> Initial Speed = {mslDef.MissileInitialSpeed}");
        //          Logger.AddLine($" -> Desired Speed: {mslDef.DesiredSpeed}");
        //          Logger.AddLine($" -> Acceleration: {mslDef.MissileAcceleration}");
        //          Logger.AddLine($" -> SpeedVar: {mslDef.SpeedVar}");
        //          Logger.AddLine($" -> Max Trajectory: {mslDef.MaxTrajectory}");
        //          Logger.AddLine($" -> Missile Mass: {mslDef.MissileMass}");
        //          Logger.AddLine($" -> Damage: {mslDef.MissileExplosionDamage}");
        //          Logger.AddLine($" -> Mechanical Damage: {mslDef.GetDamageForMechanicalObjects()}");
        //          Logger.AddLine($" -> Damage Radius = {mslDef.MissileExplosionRadius}");
        //        }
        //        else
        //        {
        //          Logger.AddLine($"{magDef.Id} returned null as MyProjectileAmmoDefinition and MyMissileAmmoDefinition\n");
        //        }
        //      }
        //      else
        //      {
        //        Logger.AddLine($"{weaponDef.AmmoMagazinesId[i]} returned null as MyAmmoMagazineDefinition\n");
        //      }
        //    }
        //  }
        //  else
        //  {
        //    Logger.AddLine($"{weaponItemDef.WeaponDefinitionId} returned null as MyWeaponDefinition\n");
        //  }
        //}

        //Logger.LogAll();

        //Logger.Log($"\nAnimations:");
        //foreach (var thing in MyDefinitionManager.Static.GetAllDefinitions())
        //{
        //  if (thing is MyCubeBlockDefinition)
        //    continue;

        //  try
        //  {
        //    var ob = thing.GetObjectBuilder() as MyObjectBuilder_AnimationDefinition;

        //    if (ob != null)
        //    {
        //      Logger.Log($"{ob.Id}");
        //    }
        //  }
        //  catch (Exception ex)
        //  {
        //    Logger.Log($"Attempted to get {thing.Id} as MyObjectBuilder_AnimationDefinition. Error = {ex.Message}");
        //  }
        //}

        _localBotAPI = new LocalBotAPI();
        ControllerCacheNum = Math.Max(20, MyAPIGateway.Session.MaxPlayers * 2);

        if (MyAPIGateway.Session?.SessionSettings != null)
        {
          MyAPIGateway.Session.SessionSettings.EnableWolfs = false;
          MyAPIGateway.Session.SessionSettings.EnableSpiders = false;
          MyAPIGateway.Session.SessionSettings.MaxFactionsCount = 100;
        }
        else
          Logger.Log($"AiSession.BeforeStart: Unable to disable wolves and spiders, or adjust max factions - SessionSettings was null!", MessageType.WARNING);

        if (IsServer)
        {
          VoxelGraphDict = new ConcurrentDictionary<ulong, VoxelGridMap>();

          ModSaveData = Config.ReadFileFromWorldStorage<SaveData>("AiEnabled.cfg", typeof(SaveData), Logger);
          if (ModSaveData == null)
          {
            ModSaveData = new SaveData()
            {
              MaxBotsInWorld = MaxBots,
              MaxHelpersPerPlayer = MaxHelpers
            };
          }

          if (ModSaveData.PlayerHelperData == null)
            ModSaveData.PlayerHelperData = new List<HelperData>();

          if (ModSaveData.FactionPairings == null)
            ModSaveData.FactionPairings = new List<FactionData>();

          if (ModSaveData.MaxBotsInWorld >= 0)
            MaxBots = ModSaveData.MaxBotsInWorld;

          if (ModSaveData.MaxHelpersPerPlayer >= 0)
            MaxHelpers = ModSaveData.MaxHelpersPerPlayer;

          if (!MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            foreach (var data in ModSaveData.PlayerHelperData)
            {
              for (int i = data.Helpers.Count - 1; i >= 0; i--)
              {
                var helper = data.Helpers[i];
                if (!helper.IsActiveHelper)
                  continue;

                data.Helpers.RemoveAtFast(i);
                var matrix = MatrixD.CreateFromQuaternion(helper.Orientation);
                matrix.Translation = helper.Position;

                var future = new FutureBot(helper.Subtype, helper.DisplayName, data.OwnerIdentityId, helper.HelperId, helper.GridEntityId, (BotType)helper.Role, matrix);
                FutureBotQueue.Enqueue(future);
              }
            }
          }

          foreach (var factionInfo in ModSaveData.FactionPairings)
          {
            if (!BotFactions.ContainsKey(factionInfo.PlayerFactionId))
            {
              var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionInfo.BotFactionId);
              if (faction != null)
              {
                BotFactionTags.Remove(faction.Tag);
                BotFactions.TryAdd(factionInfo.PlayerFactionId, faction);
              }
            }
          }

          HashSet<long> factionMembers = new HashSet<long>();

          foreach (var kvp in MyAPIGateway.Session.Factions.Factions)
          {
            var faction = kvp.Value;
            var factionId = kvp.Key;

            if (!faction.AcceptHumans)
            {
              if (!faction.AutoAcceptMember)
                MyAPIGateway.Session.Factions.ChangeAutoAccept(faction.FactionId, 0L, true, faction.AutoAcceptPeace);

              var joinRequests = faction.JoinRequests;
              if (joinRequests.Count > 0)
              {
                factionMembers.Clear();
                factionMembers.UnionWith(joinRequests.Keys);

                foreach (var member in factionMembers)
                {
                  MyAPIGateway.Session.Factions.CancelJoinRequest(faction.FactionId, member);
                }
              }

              if (faction.Members.Count > 1)
              {
                factionMembers.Clear();
                bool founderOK = false;
                foreach (var kvpMember in faction.Members)
                {
                  if (!founderOK && (kvpMember.Value.IsFounder || kvpMember.Value.IsLeader))
                  {
                    founderOK = true;
                    continue;
                  }

                  factionMembers.Add(kvpMember.Key);
                }

                if (factionMembers.Count > 0)
                  Logger.Log($"Faction cleanup: Removing {factionMembers.Count} members from {faction.Name}");

                foreach (var memberId in factionMembers)
                  MyAPIGateway.Session.Factions.KickMember(factionId, memberId);
              }

              continue;
            }
            else if (BotFactions.ContainsKey(factionId))
              continue;

            Logger.Log($"Attempting to pair bot faction with {faction.Name}");
            bool good = true;
            IMyFaction botFaction = null;
            while (botFaction == null)
            {
              if (BotFactionTags.Count == 0)
              {
                good = false;
                Logger.Log($"AiSession.BeforeStart: BotFactionTags found empty during faction pairing!", MessageType.WARNING);
                break;
              }

              var rand = MyUtils.GetRandomInt(0, BotFactionTags.Count);
              var botFactionTag = BotFactionTags[rand];
              BotFactionTags.RemoveAtFast(rand);

              botFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag(botFactionTag);
            }

            if (!good)
              break;

            if (!BotFactions.TryAdd(factionId, botFaction))
              Logger.Log($"Aisession.BeforeStart: Failed to add faction pair - ID: {factionId}, BotFactionTag: {botFaction.Tag}", MessageType.WARNING);
            else
              Logger.Log($" -> Paired {faction.Name} with {botFaction.Name}");
          }

          MyAPIGateway.Session.Factions.FactionCreated += Factions_FactionCreated;
          MyAPIGateway.Session.Factions.FactionEdited += Factions_FactionEdited;
          MyAPIGateway.Session.Factions.FactionStateChanged += Factions_FactionStateChanged;
          MyAPIGateway.Session.Factions.FactionAutoAcceptChanged += Factions_FactionAutoAcceptChanged;

          foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
          {
            var cubeDef = def as MyCubeBlockDefinition;
            if (cubeDef == null || _ignoreTypes.ContainsItem(cubeDef.Id.TypeId))
              continue;

            var blockDef = cubeDef.Id;
            if (cubeDef.IsCubePressurized != null && !BlockFaceDictionary.ContainsKey(blockDef))
            {
              var cubeDict = new Dictionary<Vector3I, HashSet<Vector3I>>();

              foreach (var kvp in cubeDef.IsCubePressurized)
              {
                HashSet<Vector3I> faceHash;
                if (!cubeDict.TryGetValue(kvp.Key, out faceHash))
                {
                  faceHash = new HashSet<Vector3I>();
                  cubeDict[kvp.Key] = faceHash;
                }

                foreach (var kvp2 in kvp.Value)
                {
                  if (kvp2.Value == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways)
                    faceHash.Add(kvp2.Key);
                }
              }

              BlockFaceDictionary[blockDef] = cubeDict;
            }

            if (cubeDef.CubeSize != MyCubeSize.Large)
              continue;

            var blockSubtype = blockDef.SubtypeName;
            bool isSlopedBlock = _validSlopedBlockDefs.ContainsItem(blockDef) || blockSubtype.EndsWith("HalfSlopeArmorBlock");
            bool isStairBlock = !isSlopedBlock && blockSubtype != "LargeStairs" && blockSubtype.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isStairBlock || isSlopedBlock)
            {
              SlopeBlockDefinitions.Add(blockDef);

              var isHalf = blockSubtype.IndexOf("half", StringComparison.OrdinalIgnoreCase) >= 0;

              if (isStairBlock)
              {
                if (isHalf)
                {
                  HalfStairBlockDefinitions.Add(blockDef);

                  if (blockSubtype.IndexOf("mirrored", StringComparison.OrdinalIgnoreCase) >= 0)
                  {
                    HalfStairMirroredDefinitions.Add(blockDef);
                  }
                }
              }
              else if (isHalf || blockSubtype.IndexOf("tip", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                SlopedHalfBlockDefinitions.Add(blockDef);
              }
            }
            else if (blockDef.TypeId == typeof(MyObjectBuilder_Passage) || blockSubtype.StartsWith("Passage"))
            {
              PassageBlockDefinitions.Add(blockDef);
            }
            else if (blockSubtype == "LargeStairs" || blockSubtype.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              RampBlockDefinitions.Add(blockDef);
            }
            else if (blockSubtype.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              CatwalkBlockDefinitions.Add(blockDef);
            }
            else if (blockDef.TypeId == typeof(MyObjectBuilder_Ladder2))
            {
              LadderBlockDefinitions.Add(blockDef);
            }
            else if (blockDef.TypeId == typeof(MyObjectBuilder_CubeBlock) && blockSubtype.IndexOf("panel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              ArmorPanelAllDefinitions.Add(blockDef);

              if (!ArmorPanelFullDefinitions.ContainsItem(blockDef) && !ArmorPanelSlopeDefinitions.ContainsItem(blockDef)
                && !ArmorPanelHalfSlopeDefinitions.ContainsItem(blockDef) && !ArmorPanelHalfDefinitions.ContainsItem(blockDef))
              {
                ArmorPanelMiscDefinitions.Add(blockDef);
              }
            }
          }

          bool planetFound = false;
          var hash = new HashSet<VRage.ModAPI.IMyEntity>();
          MyAPIGateway.Entities.GetEntities(hash);

          foreach (var entity in hash)
          {
            if (IsServer)
            {
              var character = entity as IMyCharacter;
              if (character != null)
              {
                var charPlayer = MyAPIGateway.Players.GetPlayerControllingEntity(character);

                bool isBot = charPlayer != null && (charPlayer.IsBot || !charPlayer.IsValidPlayer());
                if (!isBot)
                  isBot = string.IsNullOrWhiteSpace(character.DisplayName)
                    && (character.IsBot || character.ControllerInfo?.Controller == null || RobotSubtypes.Contains(character.Definition.Id.SubtypeName));

                if (isBot)
                {
                  MyGamePruningStructure.Remove((MyEntity)entity);
                  character.Close();
                  continue;
                }
              }

              var grid = entity as MyCubeGrid;
              if (grid != null)
              {
                foreach (var block in grid.GetFatBlocks())
                {
                  var seat = block as IMyCockpit;
                  if (seat == null)
                    continue;

                  var pilot = seat?.Pilot;
                  if (pilot != null)
                  {
                    var charPlayer = MyAPIGateway.Players.GetPlayerControllingEntity(pilot);

                    bool isBot = charPlayer != null && (charPlayer.IsBot || !charPlayer.IsValidPlayer());
                    if (!isBot)
                      isBot = string.IsNullOrWhiteSpace(pilot.DisplayName)
                        && (pilot.IsBot || pilot.ControllerInfo?.Controller == null || RobotSubtypes.Contains(pilot.Definition.Id.SubtypeName));

                    if (isBot)
                    {
                      seat.RemovePilot();
                      MyGamePruningStructure.Remove((MyEntity)pilot);
                      pilot.Close();
                    }
                  }
                }

                continue;
              }

              if (!planetFound)
              {
                var planet = entity as MyPlanet;
                if (planet != null)
                {
                  planetFound = true;
                  _starterPosition = planet.PositionComp.WorldVolume.Center;
                }
              }
            }

            MyEntities_OnEntityAdd((MyEntity)entity);
          }

          hash.Clear();
          hash = null;

          if (!planetFound)
          {
            var rand = MyUtils.GetRandomDouble(5000000, 10000000);
            _starterPosition = new Vector3D(rand);
          }
        }

        if (!MyAPIGateway.Utilities.IsDedicated)
        {
          var player = MyAPIGateway.Session?.Player;
          var playerId = player?.IdentityId ?? MyAPIGateway.Players.TryGetIdentityId(MyAPIGateway.Multiplayer.MyId);
          if (playerId > 0)
            _newPlayerIds[playerId] = player?.Character?.EntityId ?? 0L;

          MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
          HudAPI = new HudAPIv2(HudAPICallback);
          CommandMenu = new CommandMenu();
          PlayerData = Config.ReadFileFromLocalStorage<PlayerData>("AiEnabledPlayerConfig.cfg", typeof(PlayerData), Logger);

          if (PlayerData == null)
          {
            PlayerData = new PlayerData();
          }
          else
          {
            if (PlayerData.Keybinds == null)
              PlayerData.Keybinds = new List<Input.Support.SerializableKeybind>();

            if (playerId > 0)
            {
              var pkt = new AdminPacket(playerId, PlayerData.ShowHealthBars);
              Network.SendToServer(pkt);
            }
            else
              Logger.Log($"Player was null in BeforeStart!", MessageType.WARNING);
          }
        }

        MyEntities.OnEntityAdd += MyEntities_OnEntityAdd;
        MyVisualScriptLogicProvider.PlayerConnected += PlayerConnected;
        MyVisualScriptLogicProvider.PlayerDisconnected += PlayerDisconnected;
        MyVisualScriptLogicProvider.PlayerEnteredCockpit += PlayerEnteredCockpit;
        MyVisualScriptLogicProvider.PlayerLeftCockpit += PlayerLeftCockpit;
        MyVisualScriptLogicProvider.PlayerSpawned += PlayerSpawned;
        MyVisualScriptLogicProvider.PlayerDied += PlayerDied;
        MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(int.MaxValue, BeforeDamageHandler);

        Registered = true;
      }
      catch (Exception ex)
      {
        Logger?.Log($"Exception in AiSession.BeforeStart: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        UnloadModData();
      }
      finally
      {
        base.BeforeStart();
      }
    }

    bool _needsUpdate, _needsAdminUpdate;
    int _updateCounter, _updateCounterAdmin;
    const int UpdateTime = 300;

    public void StartUpdateCounter()
    {
      if (MyAPIGateway.Utilities.IsDedicated)
        return;

      _needsUpdate = true;
      _updateCounter = 0;
    }

    public void StartAdminUpdateCounter()
    {
      if (!IsServer)
        return;

      _needsAdminUpdate = true;
      _updateCounterAdmin = 0;
    }

    public void UpdateConfig()
    {
      try
      {
        if (!_needsUpdate || ++_updateCounter < UpdateTime)
          return;

        if (PlayerData == null)
          PlayerData = new PlayerData();

        Config.WriteFileToLocalStorage($"AiEnabledPlayerConfig.cfg", typeof(PlayerData), PlayerData, Logger);

        _updateCounter = 0;
        _needsUpdate = false;
      }
      catch (Exception e)
      {
        Logger?.Log($"Exception in UpdateConfig: {e.Message}\n{e.StackTrace}\n", MessageType.ERROR);
      }
    }

    public void UpdateAdminConfig()
    {
      if (!_needsAdminUpdate || ++_updateCounterAdmin < UpdateTime)
        return;

      _needsAdminUpdate = false;
      ModSaveData.MaxBotsInWorld = MaxBots;
      ModSaveData.MaxHelpersPerPlayer = MaxHelpers;

      Config.WriteFileToWorldStorage("AiEnabled.cfg", typeof(SaveData), ModSaveData, Logger);
    }

    private void HudAPICallback()
    {
      CommandMenu.Register();
      PlayerMenu = new PlayerMenu(PlayerData);
      Input = new Inputs(PlayerData.Keybinds);

      var pkt = new SettingRequestPacket();
      Network.SendToServer(pkt);
    }

    private void Factions_FactionAutoAcceptChanged(long factionId, bool autoAcceptMember, bool autoAcceptPeace)
    {
      try
      {
        var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
        if (faction != null && !faction.AcceptHumans && !faction.AutoAcceptMember)
        {
          MyAPIGateway.Session.Factions.ChangeAutoAccept(factionId, 0L, true, faction.AutoAcceptPeace);
        }
      }
      catch { }
    }

    private void Factions_FactionStateChanged(MyFactionStateChange action, long fromFactionId, long toFactionId, long playerId, long senderId)
    {
      try
      {
        if (action == MyFactionStateChange.RemoveFaction)
        {
          long factionToRemove = 0;
          foreach (var kvp in BotFactions)
          {
            var faction = MyAPIGateway.Session.Factions.TryGetFactionById(kvp.Key);
            if (faction == null)
            {
              factionToRemove = kvp.Key;
              break;
            }
          }

          if (factionToRemove > 0)
          {
            IMyFaction faction;
            if (BotFactions.TryRemove(factionToRemove, out faction))
              BotFactionTags.Add(faction.Tag);

            SaveModData();
          }
        }
      }
      catch { }
    }

    private void Factions_FactionEdited(long factionId)
    {
      try
      {
        IMyFaction faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
        if (faction != null)
        {
          if (!faction.AcceptHumans)
          {
            IMyFaction f;
            if (BotFactions.TryRemove(factionId, out f))
              BotFactionTags.Add(f.Tag);

            return;
          }

          if (BotFactions.ContainsKey(factionId))
            return;

          bool good = true;
          IMyFaction botFaction = null;
          while (botFaction == null)
          {
            if (BotFactionTags.Count == 0)
            {
              good = false;
              Logger.Log($"AiSession.FactionEdited: BotFactionTags found empty during faction pairing!", MessageType.WARNING);
              break;
            }

            var rand = MyUtils.GetRandomInt(0, BotFactionTags.Count);
            var botFactionTag = BotFactionTags[rand];
            BotFactionTags.RemoveAtFast(rand);

            botFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag(botFactionTag);
          }

          if (!good)
            return;

          if (!BotFactions.TryAdd(factionId, botFaction))
            Logger.Log($"Aisession.FactionEdited: Failed to add faction pair - ID: {factionId}, BotFactionTag: {botFaction.Tag}", MessageType.WARNING);
          else
            Logger.Log($"AiSession.FactionEdited: Human faction '{faction.Tag}' paired with bot faction '{botFaction.Tag}' successfully!");
        }
      }
      catch { }
    }

    private void Factions_FactionCreated(long factionId)
    {
      try
      {
        IMyFaction faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
        if (faction != null && faction.AcceptHumans && !BotFactions.ContainsKey(factionId))
        {
          bool good = true;
          IMyFaction botFaction = null;
          while (botFaction == null)
          {
            if (BotFactionTags.Count == 0)
            {
              good = false;
              Logger.Log($"AiSession.FactionCreated: BotFactionTags found empty during faction pairing!", MessageType.WARNING);
              break;
            }

            var rand = MyUtils.GetRandomInt(0, BotFactionTags.Count);
            var botFactionTag = BotFactionTags[rand];
            BotFactionTags.RemoveAtFast(rand);

            botFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag(botFactionTag);
          }

          if (!good)
            return;

          if (!BotFactions.TryAdd(factionId, botFaction))
            Logger.Log($"Aisession.FactionCreated: Failed to add faction pair - ID: {factionId}, BotFactionTag: {botFaction.Tag}", MessageType.WARNING);
          else
            Logger.Log($"AiSession.FactionCreated: Human faction '{faction.Tag}' paired with bot faction '{botFaction.Tag}' successfully!");
        }
      }
      catch { }
    }

    public void ClearBotControllers()
    {
      //Logger.Log($"Clearing bot controllers");
      _controlSpawnTimer = 0;
      _checkControlTimer = true;
      _controllerSet = false;
      _controllerInfo.ClearImmediate();
      _pendingControllerInfo.ClearImmediate();
    }

    public void CheckControllerForPlayer(long playerId, long botEntId)
    {
      //Logger.Log($"CheckControllerForPlayer: {playerId}, EntityId = {botEntId}");
      _newPlayerIds[playerId] = botEntId;
      MyAPIGateway.Utilities.InvokeOnGameThread(GetBotControllerClient, "AiEnabled");
    }

    public void UpdateControllerForPlayer(long playerId, long botId, long? ownerId = null)
    {
      for (int i = 0; i < _controllerInfo.Count; i++)
      {
        var info = _controllerInfo[i];
        if (info.Identity.IdentityId == playerId)
        {
          info.EntityId = botId;
          if (ownerId.HasValue)
          {
            List<long> botIDs;
            if (!PlayerToHelperIdentity.TryGetValue(ownerId.Value, out botIDs))
            {
              botIDs = new List<long>();
              PlayerToHelperIdentity[ownerId.Value] = botIDs;
            }

            if (!botIDs.Contains(botId))
              botIDs.Add(botId);
          }

          break;
        }
      }

      //ControlInfo info;
      //if (_controllerInfo.TryGetValue(botId, out info))
      //{
      //  info.EntityId = botId;
      //  if (ownerId.HasValue)
      //  {
      //    List<long> botIDs;
      //    if (!PlayerToHelperIdentity.TryGetValue(ownerId.Value, out botIDs))
      //    {
      //      botIDs = new List<long>();
      //      PlayerToHelperIdentity[ownerId.Value] = botIDs;
      //    }

      //    if (!botIDs.Contains(botId))
      //      botIDs.Add(botId);
      //  }
      //}
    }

    private void PlayerSpawned(long playerId)
    {
      if (!MyAPIGateway.Multiplayer.MultiplayerActive || IsClient)
        return;

      _tempPlayersAsync.Clear();
      MyAPIGateway.Players?.GetPlayers(_tempPlayersAsync);

      for (int i = 0; i < _tempPlayersAsync.Count; i++)
      {
        var player = _tempPlayersAsync[i];
        if (player?.IdentityId == playerId)
        {
          if (player.IsBot)
          {
            var packet = new AdminPacket(playerId, null, null);
            Network.RelayToClients(packet);
          }
          break;
        }
      }
    }

    private void PlayerDied(long playerId)
    {
      List<BotBase> playerHelpers;
      if (PlayerToHelperDict.TryGetValue(playerId, out playerHelpers))
      {
        for (int i = playerHelpers.Count - 1; i >= 0; i--)
        {
          var bot = playerHelpers[i];
          if (bot?.Character?.IsDead != false)
          {
            playerHelpers.RemoveAtFast(i);
            continue;
          }

          bot.ReturnHome();
        }
      }
    }

    internal void PlayerLeftCockpit(string entityName, long playerId, string gridName)
    {
      List<BotBase> playerHelpers;
      if (!Registered || !PlayerToHelperDict.TryGetValue(playerId, out playerHelpers) || playerHelpers == null || playerHelpers.Count == 0)
      {
        return;
      }

      bool recallBots = gridName == "AiEnabled_RecallBots";

      for (int i = playerHelpers.Count - 1; i >= 0; i--)
      {
        var bot = playerHelpers[i];
        var character = bot?.Character;
        if (character == null || character.IsDead)
        {
          playerHelpers.RemoveAtFast(i);
          continue;
        }

        if (!bot.CanUseSeats)
          continue;

        if (recallBots)
          bot.UseAPITargets = false;
        else if (bot.UseAPITargets)
          continue;

        var seat = character.Parent as IMyCockpit;
        if (seat != null)
        {
          seat.RemovePilot();

          var jetpack = character.Components?.Get<MyCharacterJetpackComponent>();
          if (jetpack != null)
          {
            if (bot.RequiresJetpack)
            {
              if (!jetpack.TurnedOn)
              {
                var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
                MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
                jetpack.TurnOnJetpack(true);
                MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
              }
            }
            else if (jetpack.TurnedOn)
              jetpack.SwitchThrusts();
          }

          bot.Target.RemoveTarget();
          bot._pathCollection?.CleanUp(true);

          if (!bot.UseAPITargets)
            bot.SetTarget();

          Vector3D gotoPosition, actualPosition;
          bot.Target.GetTargetPosition(out gotoPosition, out actualPosition);
          bot.StartCheckGraph(ref actualPosition, true);

          Vector3D relPosition;
          BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out relPosition);
          var position = seat.GetPosition() + Vector3D.Rotate(relPosition, seat.WorldMatrix) + bot.WorldMatrix.Down;

          var voxelGraph = bot._currentGraph as VoxelGridMap;
          var gridGraph = bot._currentGraph as CubeGridMap;
          MatrixD? newMatrix = null;

          if (voxelGraph != null)
          {
            Vector3D up = bot.WorldMatrix.Up;

            float interference;
            var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out interference);
            if (gravity.LengthSquared() == 0)
              gravity = MyAPIGateway.Physics.CalculateArtificialGravityAt(position, interference);

            if (gravity.LengthSquared() > 0)
              up = Vector3D.Normalize(-gravity);

            if (relPosition.LengthSquared() < 2)
              position += Vector3D.CalculatePerpendicularVector(up) * (seat.CubeGrid.WorldAABB.HalfExtents.AbsMax() + 5);

            position = voxelGraph.GetClosestSurfacePointFast(bot, position, up) + up;

            if (bot.WorldMatrix.Up.Dot(up) < 0)
            {
              var forward = Vector3D.CalculatePerpendicularVector(up);
              newMatrix = MatrixD.CreateWorld(position, forward, up);
            }
          }
          else if (gridGraph != null && seat.CubeGrid.GridSize > 1)
          {
            var local = gridGraph.WorldToLocal(position);
            Vector3I openNode;
            if (gridGraph.GetClosestValidNode(bot, local, out openNode, seat.WorldMatrix.Up))
            {
              position = gridGraph.LocalToWorld(openNode) + bot.WorldMatrix.Down;
            }
            else
            {
              Logger.Log($"{GetType().FullName}: Unable to find valid position upon exit from seat! Grid = {gridGraph.Grid?.DisplayName ?? "NULL"}", MessageType.WARNING);
            }
          }
          else if (relPosition.LengthSquared() < 2)
          {
            Vector3D up = bot.WorldMatrix.Up;
            MyPlanet planet = null;

            float interference;
            var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out interference);
            if (gravity.LengthSquared() == 0)
              gravity = MyAPIGateway.Physics.CalculateArtificialGravityAt(position, interference);
            else
              planet = MyGamePruningStructure.GetClosestPlanet(position);

            if (gravity.LengthSquared() > 0)
              up = Vector3D.Normalize(-gravity);

            position += Vector3D.CalculatePerpendicularVector(up) * (seat.CubeGrid.WorldAABB.HalfExtents.AbsMax() + 5);

            if (planet != null)
              position = planet.GetClosestSurfacePointGlobal(position) + up * 5;
          }

          if (newMatrix.HasValue)
            character.SetWorldMatrix(newMatrix.Value);
          else
            character.SetPosition(position);
        }
        else if (bot.Target.Override.HasValue)
        {
          bot.Target.RemoveOverride(false);
          bot.Target.RemoveTarget();
        }
      }
    }

    internal void PlayerEnteredCockpit(string entityName, long playerId, string gridName)
    {
      IMyPlayer player;
      if (!Players.TryGetValue(playerId, out player) || player?.Character == null || player.Character.IsDead)
      {
        return;
      }

      var parent = player.Character.Parent as IMyShipController;
      var playerGrid = parent?.CubeGrid;
      if (playerGrid == null || playerGrid.MarkedForClose)
      {
        return;
      }

      var blockDef = parent.BlockDefinition.SubtypeName;
      if (blockDef.IndexOf("toilet", StringComparison.OrdinalIgnoreCase) >= 0
        || blockDef.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0
        || blockDef.IndexOf("bathroom", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        return;
      }

      List<BotBase> playerHelpers;
      if (!PlayerToHelperDict.TryGetValue(playerId, out playerHelpers) || playerHelpers == null || playerHelpers.Count == 0)
      {
        return;
      }

      _gridSeatGroupEnter.Clear();
      _gridSeats.Clear();
      MyAPIGateway.GridGroups.GetGroup(playerGrid, GridLinkTypeEnum.Logical, _gridSeatGroupEnter);

      foreach (var grid in _gridSeatGroupEnter)
      {
        grid.GetBlocks(_gridSeats, b =>
        {
          var seat = b.FatBlock as IMyCockpit;
          if (seat == null || seat.Pilot != null || !seat.IsFunctional
            || seat.BlockDefinition.SubtypeId.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0
            || seat.BlockDefinition.SubtypeId.IndexOf("toilet", StringComparison.OrdinalIgnoreCase) >= 0
            || seat.BlockDefinition.SubtypeId.IndexOf("bathroom", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            return false;
          }

          return true;
        });
      }

      for (int i = playerHelpers.Count - 1; i >= 0; i--)
      {
        var bot = playerHelpers[i];
        if (bot?.Character == null || bot.Character.IsDead)
        {
          playerHelpers.RemoveAtFast(i);
          continue;
        }

        if (!bot.CanUseSeats)
          continue;

        if (_gridSeats.Count == 0)
          break;

        if (bot.UseAPITargets || bot.Character.Parent is IMyCockpit)
          continue;

        _gridSeats.ShellSort(bot.Position, reverse: true);

        for (int j = _gridSeats.Count - 1; j >= 0; j--)
        {
          var seat = _gridSeats[j]?.FatBlock as IMyCockpit;
          if (seat == null || seat.Pilot != null || !seat.IsFunctional || !seat.HasPlayerAccess(bot.Character.ControllerInfo.ControllingIdentityId))
          {
            _gridSeats.RemoveAtFast(j);
            continue;
          }

          CubeGridMap gridGraph;
          if (seat.CubeGrid.GridSize > 1 && GridGraphDict.TryGetValue(seat.CubeGrid.EntityId, out gridGraph) && gridGraph != null)
          {
            if (!gridGraph.TempBlockedNodes.ContainsKey(seat.Position))
            {
              _gridSeats.RemoveAtFast(i);
              var seatPos = seat.GetPosition();
              bot.Target.SetOverride(seatPos);
              break;
            }
          }

          // TODO: Save bot position relative to the seat and return bot to relative position when player exits
          // Also need to ensure that the position is valid on exit (in case ship took off) and if not, find valid tile!

          _useObjList.Clear();
          var useComp = seat.Components.Get<MyUseObjectsComponentBase>();
          useComp?.GetInteractiveObjects(_useObjList);
          if (_useObjList.Count > 0)
          {
            var relativePosition = Vector3D.Rotate(bot.Position - seat.GetPosition(), MatrixD.Transpose(seat.WorldMatrix));
            BotToSeatRelativePosition[bot.Character.EntityId] = relativePosition;

            var useObj = _useObjList[0];
            useObj.Use(UseActionEnum.Manipulate, bot.Character);

            bot._pathCollection?.CleanUp(true);
            bot.Target?.RemoveTarget();

            _gridSeats.RemoveAtFast(j);
            break;
          }
        }
      }
    }

    private void BeforeDamageHandler(object target, ref MyDamageInformation info)
    {
      if (!Registered || !IsServer || info.IsDeformation || info.AttackerId == 0 || info.Amount == 0)
        return;

      var slim = target as IMySlimBlock;
      if (slim?.FatBlock is IMyDoor && info.Type == MyDamageType.Grind)
      {
        var grinder = MyEntities.GetEntityById(info.AttackerId) as IMyAngleGrinder;
        if (grinder != null && Bots.ContainsKey(grinder.OwnerId))
        {
          info.Amount = 0.005f;
          return;
        }
      }

      var character = target as IMyCharacter;
      if (character == null || character.IsDead)
        return;

      BotBase bot;
      bool targetIsBot = false;
      if (Bots.TryGetValue(character.EntityId, out bot))
      {
        targetIsBot = true;
        if (bot?.Behavior?.PainSounds?.Count > 0)
          bot.Behavior.ApplyPain();
      }

      long ownerId, ownerIdentityId = 0;
      float damageAmount;
      switch (info.Type.String)
      {
        case "Grind":
          ownerId = info.AttackerId;

          if (Bots.ContainsKey(ownerId))
          {
            if (targetIsBot)
            {
              SetNomadHostility(character.EntityId, ownerId);
            }

            info.Amount = 0;
            return;
          }

          damageAmount = 0.2f;

          var ch = MyEntities.GetEntityById(ownerId) as IMyCharacter;
          if (ch != null)
            ownerIdentityId = ch.ControllerInfo.ControllingIdentityId;

          break;
        default:
          var ent = MyEntities.GetEntityById(info.AttackerId);

          var turret = ent as IMyLargeTurretBase;
          if (turret != null)
          {
            if (targetIsBot)
            {
              var amount = info.Amount * 0.1f;
              info.Amount = 0;
              DamageCharacter(turret.EntityId, character, info.Type, amount);
            }

            return;
          }

          var attackerChar = ent as IMyCharacter;
          if (attackerChar != null)
          {
            if (targetIsBot)
            {
              var subtype = attackerChar.Definition.Id.SubtypeName;
              if (subtype.StartsWith("space_spider", StringComparison.OrdinalIgnoreCase) || subtype.StartsWith("space_wolf", StringComparison.OrdinalIgnoreCase))
              {
                info.Amount = 0;
                DamageCharacter(info.AttackerId, character, info.Type, 1.5f);
              }

              SetNomadHostility(character.EntityId, attackerChar.EntityId);
            }

            return;
          }

          var gun = ent as IMyHandheldGunObject<MyGunBase>;
          if (gun == null)
          {
            return;
          }

          ownerId = gun.OwnerId;
          ownerIdentityId = gun.OwnerIdentityId;
          damageAmount = targetIsBot ? 10f : 1.5f;
          break;
      }

      if (targetIsBot && ownerId > 0)
      {
        SetNomadHostility(character.EntityId, ownerId);
      }

      if (!Bots.ContainsKey(ownerId))
      {
        if (targetIsBot && Players.ContainsKey(ownerIdentityId))
        {
          HealthInfoStat infoStat;
          if (!PlayerToHealthBars.TryGetValue(ownerIdentityId, out infoStat))
          {
            infoStat = new HealthInfoStat();
            PlayerToHealthBars[ownerIdentityId] = infoStat;
          }
          else if (infoStat.ShowHealthBars)
          {
            infoStat.BotEntityIds.Add(character.EntityId);
          }
        }

        return;
      }

      var owner = MyEntities.GetEntityById(ownerId) as IMyCharacter;
      if (owner != null)
      {
        info.Amount = 0;
        DamageCharacter(owner.EntityId, character, info.Type, damageAmount);
      }
    }

    void SetNomadHostility(long botId, long attackerId)
    {
      BotBase bot;
      if (Bots.TryGetValue(botId, out bot))
      {
        var nomad = bot as NomadBot;
        if (nomad != null && !nomad.Target.HasTarget)
        {
          nomad.SetHostile(attackerId);
        }
      }
    }

    public void DamageCharacter(long shooterEntityId, IMyCharacter target, MyStringHash damageType, float damageAmount)
    {
      BotBase bot;
      if (Bots.TryGetValue(shooterEntityId, out bot) && bot != null)
      {
        damageAmount *= bot.DamageModifier;

        if (bot.Owner != null && string.IsNullOrWhiteSpace(target.DisplayName))
        {
          HealthInfoStat infoStat;
          if (!PlayerToHealthBars.TryGetValue(bot.Owner.IdentityId, out infoStat))
          {
            infoStat = new HealthInfoStat();
            PlayerToHealthBars[bot.Owner.IdentityId] = infoStat;
          }
          else if (infoStat.ShowHealthBars)
          {
            infoStat.BotEntityIds.Add(target.EntityId);
          }
        }
      }

      target.DoDamage(damageAmount, damageType, true);
    }

    private void PlayerConnected(long playerId)
    {
      _newPlayerIds[playerId] = 0L;
    }

    private void PlayerDisconnected(long playerId)
    {
      try
      {
        if (!MyAPIGateway.Multiplayer.MultiplayerActive)
          return;

        List<BotBase> botList;
        if (PlayerToHelperDict.TryGetValue(playerId, out botList))
        {
          bool playerFound = false;
          for (int i = ModSaveData.PlayerHelperData.Count - 1; i >= 0; i--)
          {
            var playerData = ModSaveData.PlayerHelperData[i];
            if (playerData.OwnerIdentityId == playerId)
            {
              playerFound = true;
              var helperList = playerData.Helpers;

              for (int j = botList.Count - 1; j >= 0; j--)
              {
                var bot = botList[j];
                var id = bot.Character.EntityId;
                bool botFound = false;

                for (int k = helperList.Count - 1; k >= 0; k--)
                {
                  var helper = helperList[k];
                  if (helper.HelperId == id)
                  {
                    var matrix = bot.WorldMatrix;
                    helper.Orientation = Quaternion.CreateFromRotationMatrix(matrix);
                    helper.Position = matrix.Translation;

                    var gridGraph = bot._currentGraph as CubeGridMap;
                    var grid = gridGraph?.Grid ?? null;
                    helper.GridEntityId = grid?.EntityId ?? 0L;

                    botFound = true;
                    break;
                  }
                }

                if (!botFound)
                {
                  var gridGraph = bot._currentGraph as CubeGridMap;
                  var grid = gridGraph?.Grid ?? null;
                  var botType = bot is RepairBot ? BotType.Repair : BotType.Combat;

                  playerData.AddHelper(bot.Character, botType, grid);
                }

                bot.Close();
              }

              break;
            }
          }

          if (!playerFound)
          {
            var playerData = new HelperData(playerId, null);
            foreach (var bot in botList)
            {
              var gridGraph = bot._currentGraph as CubeGridMap;
              var grid = gridGraph?.Grid ?? null;
              var botType = bot is RepairBot ? BotType.Repair : BotType.Combat;

              playerData.AddHelper(bot.Character, botType, grid);
              bot.Close();
            }

            ModSaveData.PlayerHelperData.Add(playerData);
          }

          SaveModData(true);
          botList.Clear();
        }

        IMyPlayer _;
        List<long> __;
        Players.TryRemove(playerId, out _);
        PlayerToHelperDict.TryRemove(playerId, out botList);
        PlayerToHelperIdentity.TryRemove(playerId, out __);
      }
      catch(Exception ex)
      {
        Logger.Log($"Exception in AiSession.PlayerDisconnected: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    public void FireWeaponForBot(WeaponInfo info, bool enable)
    {
      var bot = info.Bot;
      var tgt = info.Target;

      if (bot?.IsDead != false)
        return;

      if (!info.IsWelder && tgt?.MarkedForClose != false)
        return;

      var gun = bot.EquippedTool as IMyGunObject<MyDeviceBase>;
      if (gun == null)
        return;

      if (enable)
      {
        if (!info.IsWelder && !info.IsGrinder)
        {
          Projectiles.Add(bot, tgt, info.GetRandom(), info.Damage, info);
        }
        else
        {
          gun.Shoot(MyShootActionEnum.PrimaryAction, (Vector3)bot.WorldMatrix.Forward, null);
        }
      }
      else
        gun.EndShoot(MyShootActionEnum.PrimaryAction);
    }

    public void PlaySoundForEntity(long entityId, string sound, bool stop, bool includeIcon)
    {
      var ent = MyEntities.GetEntityById(entityId);
      if (ent == null)
        return;

      var bot = ent as IMyCharacter;
      if (bot == null)
      {
        PlaySoundForEntity(ent, sound, stop);
        return;
      }

      var soundComp = bot.Components?.Get<MyCharacterSoundComponent>();

      if (stop)
      {
        // figure out how to stop them...
      }
      else
      {
        if (string.IsNullOrWhiteSpace(sound))
        {
          return;
        }

        MySoundPair sp;
        if (!SoundPairDict.TryGetValue(sound, out sp))
        {
          sp = new MySoundPair(sound);
          SoundPairDict[sound] = sp;
        }

        soundComp?.PlayActionSound(sp);

        if (includeIcon && !_botSpeakers.ContainsKey(entityId))
        {
          var info = GetIconInfo();
          info.Set(bot, 120);
          if (!_botSpeakers.TryAdd(entityId, info))
            ReturnIconInfo(info);
        }
      }
    }

    void PlaySoundForEntity(MyEntity entity, string sound, bool stop)
    {
      var emitter = GetEmitter();
      emitter.SetPosition(entity.PositionComp.WorldAABB.Center);

      MySoundPair soundPair;
      if (!SoundPairDict.TryGetValue(sound, out soundPair))
      {
        soundPair = new MySoundPair(sound);
        SoundPairDict[sound] = soundPair;
      }

      emitter.PlaySound(soundPair);
      ReturnEmitter(emitter);
    }

    bool _checkControlTimer;
    int _controlSpawnTimer;
    private void MyEntities_OnEntityRemove(IMyEntity obj)
    {
      try
      {
        if (obj == null)
          return;

        obj.OnClose -= MyEntities_OnEntityRemove;

        if (_botsToClose.Remove(obj.EntityId))
        {
          if (IsServer)
          {
            bool found = false;
            for (int i = 0; i < _pendingControllerInfo.Count; i++)
            {
              var info = _pendingControllerInfo[i];
              if (info.EntityId == obj.EntityId)
              {
                found = true;
                _pendingControllerInfo.RemoveAtImmediately(i);
                _controllerInfo.Add(info);

                _pendingControllerInfo.ApplyChanges();
                _controllerInfo.ApplyChanges();
                break;
              }
            }

            //ControlInfo info;
            //if (_pendingControllerInfo.TryRemove(obj.EntityId, out info) && info != null)
            //{
            //  _controllerInfo[info.EntityId] = info;
            //}
            //else

            if (!found)
            {
              Logger.Log($"AiSession.OnEntityRemove: Control bot removed, but did not find its control info in the dictionary!", MessageType.WARNING);
            }
          }

          if (_botsToClose.Count == 0)
          {
            _controllerSet = true;
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"Exception in AiSession.OnEntityRemove: {ex.Message}\n{ex.StackTrace}");
      }
    }

    private void MyEntities_OnEntityAdd(MyEntity obj)
    {
      try
      {
        var character = obj as IMyCharacter;
        if (character?.Definition == null || !string.IsNullOrWhiteSpace(character.DisplayName) || !RobotSubtypes.Contains(character.Definition.Id.SubtypeName))
          return;

        if (Bots.ContainsKey(character.EntityId))
          return;

        if (!_isControlBot && !MyAPIGateway.Utilities.IsDedicated) // IsClient)
        {
          if (character.ControllerInfo?.Controller == null && _controllerInfo.Count > 0)
          {
            for (int i = 0; i < _controllerInfo.Count; i++)
            {
              var info = _controllerInfo[i];
              if (info.EntityId == character.EntityId)
              {
                _controllerInfo.RemoveAtImmediately(i);
                _controllerInfo.ApplyChanges();

                info.Controller.TakeControl(character);
                break;
              }
            }

            //ControlInfo info;
            //if (_controllerInfo.TryRemove(character.EntityId, out info) && info != null)
            //{
            //  info.Controller.TakeControl(character);
            //}
          }

          if (character.Definition.Id.SubtypeName == "Drone_Bot")
          {
            var jetpack = character.Components?.Get<MyCharacterJetpackComponent>();
            if (jetpack == null)
            {
              Logger.Log($"AiSession.OnEntityAdded: Drone_Bot was added with null jetpack component!", MessageType.WARNING);
            }
            else if (!jetpack.TurnedOn)
            {
              var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
              MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
              jetpack.TurnOnJetpack(true);
              MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
            }
          }

          if (!IsServer)
            return;
        }

        if (_isControlBot)
        {
          character.OnClose += MyEntities_OnEntityRemove;
          _isControlBot = false;
          return;
        }

        if (_robots.Count >= MaxBots)
        {
          character.Close();
        }
        else
        {
          character.OnClose += MyEntities_OnEntityRemove;
        }
      }
      catch (Exception e)
      {
        Logger?.Log($"Exception occurred in AiEnabled.AiSession.MyEntities_OnEntityAdd:\n{e.Message}\n{e.StackTrace}", MessageType.ERROR);
      }
    }

    public bool AllowMusic = true;
    MyCommandLine _cli = new MyCommandLine();
    private void OnMessageEntered(string messageText, ref bool sendToOthers)
    {
      if (!messageText.StartsWith("botai", StringComparison.OrdinalIgnoreCase) || !_cli.TryParse(messageText) || _cli.ArgumentCount < 2)
      {
        return;
      }

      sendToOthers = false;

      var player = MyAPIGateway.Session.LocalHumanPlayer;
      var character = player?.Character;
      if (character == null || character.IsDead)
      {
        ShowMessage("Respawn and try again.", timeToLive: 5000);
        return;
      }

      var cmd = _cli.Argument(1);
      if (cmd.Equals("fix", StringComparison.OrdinalIgnoreCase))
      {
        List<BotBase> helpers;
        PlayerToHelperDict.TryGetValue(player.IdentityId, out helpers);
        ShowMessage($"Attempting to fix {helpers?.Count ?? 0} bots", timeToLive: 5000);

        var pkt = new FixBotPacket(player.IdentityId);
        Network.SendToServer(pkt);
      }
      else if (player.PromoteLevel < MyPromoteLevel.Admin)
      {
        ShowMessage("You must have admin privileges to use that command.", timeToLive: 5000);
        return;
      }
      else if (cmd.Equals("maxbots", StringComparison.OrdinalIgnoreCase))
      {
        int num = MaxBots;
        if (_cli.ArgumentCount > 2)
          int.TryParse(_cli.Argument(2), out num);

        ShowMessage($"Max bots set to {num}");

        if (num != MaxBots)
        {
          var packet = new AdminPacket(num, MaxHelpers);
          Network.SendToServer(packet);
        }
      }
      else if (cmd.Equals("music", StringComparison.OrdinalIgnoreCase))
      {
        bool on = !AllowMusic;
        if (_cli.ArgumentCount > 2)
          bool.TryParse(_cli.Argument(2), out on);

        if (AllowMusic != on)
        {
          AllowMusic = on;
          var pkt = new AdminPacket(AllowMusic);
          Network.SendToServer(pkt);
        }

        ShowMessage($"AllowMusic set to {on}");
      }
      else if (cmd.Equals("debug", StringComparison.OrdinalIgnoreCase))
      {
        if (MyAPIGateway.Utilities.IsDedicated)
        {
          ShowMessage("Debug draw is not available on DS.", timeToLive: 5000);
          return;
        }

        bool b;
        if (_cli.ArgumentCount < 3 || !bool.TryParse(_cli.Argument(2), out b))
          b = !DrawDebug;

        MyAPIGateway.Utilities.ShowNotification($"DrawDebug set to {b}", 5000);
        DrawDebug = b;
      }
      else if (cmd.Equals("spawn", StringComparison.OrdinalIgnoreCase))
      {
        if (IsServer)
        {
          if (!CanSpawn)
          {
            MyAPIGateway.Utilities.ShowNotification($"Unable to spawn bot. Try again in a moment...");
            return;
          }

          var ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);
          if (ownerFaction == null)
          {
            MyAPIGateway.Utilities.ShowNotification($"Unable to spawn bot. Owner is not in a faction!");
            return;
          }

          IMyFaction botFaction;
          if (!BotFactions.TryGetValue(ownerFaction.FactionId, out botFaction))
          {
            MyAPIGateway.Utilities.ShowNotification($"Unable to spawn bot. There was no bot faction paired with owner's faction!");
            return;
          }
        }

        float num;
        var grav = MyAPIGateway.Physics.CalculateNaturalGravityAt(character.WorldAABB.Center, out num);
        var artGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(character.WorldAABB.Center, num);
        grav += artGrav;

        Vector3D forward, up;
        if (grav.LengthSquared() > 0)
        {
          up = Vector3D.Normalize(-grav);
          forward = Vector3D.CalculatePerpendicularVector(up);
        }
        else
        {
          up = character.WorldMatrix.Up;
          forward = character.WorldMatrix.Forward;
        }
        
        var position = character.WorldAABB.Center + character.WorldMatrix.Forward * 50;
        var planet = MyGamePruningStructure.GetClosestPlanet(position);
        if (planet != null)
        {
          var surfacePoint = planet.GetClosestSurfacePointGlobal(position);
          var lineTo = surfacePoint - planet.PositionComp.GetPosition();
          position = surfacePoint + Vector3D.Normalize(lineTo) * 5;
        }

        var packet = new SpawnPacket(position, forward, up, player.IdentityId);
        Network.SendToServer(packet);
      }
      else if (cmd.Equals("pet", StringComparison.OrdinalIgnoreCase))
      {
        if (IsServer)
        {
          if (!CanSpawn)
          {
            MyAPIGateway.Utilities.ShowNotification($"Unable to spawn bot. Try again in a moment...");
            return;
          }

          var ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);
          if (ownerFaction == null)
          {
            MyAPIGateway.Utilities.ShowNotification($"Unable to spawn bot. Owner is not in a faction!");
            return;
          }

          IMyFaction botFaction;
          if (!BotFactions.TryGetValue(ownerFaction.FactionId, out botFaction))
          {
            MyAPIGateway.Utilities.ShowNotification($"Unable to spawn bot. There was no bot faction paired with owner's faction!");
            return;
          }
        }

        float num;
        var grav = MyAPIGateway.Physics.CalculateNaturalGravityAt(character.WorldAABB.Center, out num);
        var artGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(character.WorldAABB.Center, num);
        grav += artGrav;

        Vector3D forward, up;
        if (grav.LengthSquared() > 0)
        {
          up = Vector3D.Normalize(-grav);
          forward = Vector3D.CalculatePerpendicularVector(up);
        }
        else
        {
          up = character.WorldMatrix.Up;
          forward = character.WorldMatrix.Forward;
        }

        var position = character.WorldAABB.Center + character.WorldMatrix.Forward * 50;
        var planet = MyGamePruningStructure.GetClosestPlanet(position);
        if (planet != null)
        {
          var surfacePoint = planet.GetClosestSurfacePointGlobal(position);
          var lineTo = surfacePoint - planet.PositionComp.GetPosition();
          position = surfacePoint + Vector3D.Normalize(lineTo) * 5;
        }

        var matrix = MatrixD.CreateWorld(position, forward, up);
        var posAndOr = new MyPositionAndOrientation(ref matrix);

        //var pet = BotFactory.SpawnHelper("Space_Wolf", "Wolfy", player.IdentityId, posAndOr, null, "Soldier");
        var faction = MyAPIGateway.Session.Factions.TryGetFactionByTag("SPID");
        BotFactory.SpawnBotFromAPI("Space_spider_black", "SPID_1", posAndOr, null, "Creature", faction?.FounderId);
        BotFactory.SpawnBotFromAPI("Space_spider_black", "SPID_2", posAndOr, null, "Creature", faction?.FounderId);

        //var packet = new SpawnPacket(position, forward, up, player.IdentityId);
        //Network.SendToServer(packet);
      }
      else if (cmd.Equals("nomad", StringComparison.OrdinalIgnoreCase))
      {
        if (IsServer)
        {
          if (!CanSpawn)
          {
            MyAPIGateway.Utilities.ShowNotification($"Unable to spawn bot. Try again in a moment...");
            return;
          }
        }

        Vector3D forward, up;
        up = character.WorldMatrix.Up;
        forward = character.WorldMatrix.Forward;
        var position = character.WorldAABB.Center + character.WorldMatrix.Forward * 10;

        var nomad = BotFactory.SpawnNPC("Default_Astronaut", null, new MyPositionAndOrientation(position, forward, up), null, "Nomad", Color.Tan);

        //var packet = new SpawnPacket(position, forward, up);
        //Network.SendToServer(packet);
      }
    }

    bool _drawMenu;
    TimeSpan _lastClickTime = new TimeSpan(DateTime.Now.Ticks);
    public override void HandleInput()
    {
      try
      {
        if (MyAPIGateway.Utilities.IsDedicated || !Registered)
          return;

        Input?.CheckKeys();

        var player = MyAPIGateway.Session?.Player?.Character;
        if (MyAPIGateway.Gui.IsCursorVisible || CommandMenu?.Registered != true || player == null)
          return;

        bool uiDrawn = CommandMenu.ShowInventory || _drawMenu;

        if (MyParticlesManager.Paused)
        {
          if (uiDrawn && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Escape))
          {
            CommandMenu.ResetCommands();
            _drawMenu = false;
          }

          return;
        }

        if (uiDrawn)
          player.MoveAndRotate(player.LastMotionIndicator, Vector2.Zero, 0);

        if (CommandMenu.UseControl.IsNewPressed())
        {
          if (CommandMenu.SendTo || CommandMenu.ShowInventory)
            CommandMenu.ResetCommands();

          if (_drawMenu)
            _drawMenu = false;
          else if (_selectedBot?.IsDead == false)
            _drawMenu = true;
        }
        else if (CommandMenu.ShowInventory || CommandMenu.SendTo)
        {
          if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Escape)
            || MyAPIGateway.Input.IsNewKeyPressed(MyKeys.OemTilde)
            || (MyAPIGateway.Gui.ChatEntryVisible && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F2))
            || MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.None)
          {
            CommandMenu.ResetCommands();
            _drawMenu = false;
          }
          else
          {
            var newLMBPress = MyAPIGateway.Input.IsNewLeftMousePressed();
            if (CommandMenu.SendTo)
            {
              if (newLMBPress)
                CommandMenu.Activate(null);
            }
            else // show inventory stuffs
            {
              var delta = MyAPIGateway.Input.GetCursorPositionDelta();
              if (!Vector2.IsZero(ref delta))
                CommandMenu.UpdateCursorPosition(delta);

              var deltaWheel = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
              if (deltaWheel != 0)
                CommandMenu.ApplyMouseWheelMovement(Math.Sign(deltaWheel));

              var doubleClick = false;
              if (newLMBPress)
              {
                var currentTime = TimeSpan.FromTicks(DateTime.Now.Ticks);
                var elapsed = currentTime - _lastClickTime;
                if (elapsed.TotalMilliseconds < 200)
                  doubleClick = true;

                _lastClickTime = currentTime;
              }

              var leftPress = MyAPIGateway.Input.IsLeftMousePressed();
              var rightPress = MyAPIGateway.Input.IsRightMousePressed();
              var leftRelease = !leftPress && MyAPIGateway.Input.IsNewLeftMouseReleased();
              var rightRelease = !leftPress && !rightPress && MyAPIGateway.Input.IsNewRightMouseReleased();
              var anyPress = leftPress || rightPress;


              if (anyPress || leftRelease || rightRelease)
                CommandMenu.ActivateInventory(ref leftPress, ref rightPress, ref leftRelease, ref rightRelease, ref doubleClick);
            }
          }
        }
        else if (_drawMenu)
        {
          if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Escape)
            || MyAPIGateway.Input.IsNewKeyPressed(MyKeys.OemTilde)
            || (MyAPIGateway.Gui.ChatEntryVisible && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F2))
            || MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.None)
          {
            _drawMenu = false;
          }
          else if (MyAPIGateway.Input.IsNewLeftMousePressed())
          {
            CommandMenu.Activate(_selectedBot);
            _drawMenu = false;
          }
          else
          {
            var delta = MyAPIGateway.Input.GetCursorPositionDelta();
            if (!Vector2.IsZero(ref delta))
              CommandMenu.UpdateCursorDirection(delta);
          }
        }
      }
      catch(Exception ex)
      {
        Logger.Log($"Exception in AiSession.HandleInput: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
      finally
      {
        base.HandleInput();
      }
    }

    public override void Draw()
    {
      try
      {
        if (MyAPIGateway.Utilities.IsDedicated || MyParticlesManager.Paused || MyAPIGateway.Gui.IsCursorVisible || MyAPIGateway.Gui.ChatEntryVisible)
        {
          return;
        }

        var player = MyAPIGateway.Session?.Player?.Character;
        if (!Registered || CommandMenu?.Registered != true || player == null)
          return;

        if (CommandMenu.ShowInventory || CommandMenu.SendTo)
        {
          if (CommandMenu.RadialVisible)
            CommandMenu.CloseMenu();
          else if (CommandMenu.InteractVisible)
            CommandMenu.CloseInteractMessage();

          if (CommandMenu.ShowInventory)
            CommandMenu.DrawInventoryScreen(player);
          else
            CommandMenu.DrawSendTo(player);
        }
        else if (_drawMenu)
        {
          if (CommandMenu.InteractVisible)
            CommandMenu.CloseInteractMessage();

          CommandMenu.DrawRadialMenu();
        }
        else
        {
          if (CommandMenu.RadialVisible)
            CommandMenu.CloseMenu();

          if (_selectedBot?.IsDead == false)
            CommandMenu.DrawInteractMessage(_selectedBot);
          else if (CommandMenu.InteractVisible)
            CommandMenu.CloseInteractMessage();
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"Exception in AiSession.Draw: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
      finally
      {
        base.Draw();
      }
    }

    public bool IsBotAllowedToUse(BotBase bot, string toolSubtype, out string reason)
    {
      reason = null;
      if (bot is CombatBot)
      {
        bool allowed = toolSubtype.IndexOf("rifle", StringComparison.OrdinalIgnoreCase) >= 0 
          || toolSubtype.IndexOf("pistol", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!allowed)
          reason = "The CombatBot can only use rifles and pistols.";

        return allowed;
      }
      
      if (bot is RepairBot)
      {
        bool allowed = toolSubtype.IndexOf("welder", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!allowed)
          reason = "The RepairBot can only use welders";

        return allowed;
      }

      return false;
    }

    public override void SaveData()
    {
      try
      {
        SaveModData(true);
        _needsUpdate = true;
        _updateCounter = UpdateTime - 2;
      }
      finally
      {
        base.SaveData();
      }
    }

    public void SaveModData(bool writeConfig = false)
    {
      ModSaveData.MaxBotsInWorld = MaxBots;
      ModSaveData.MaxHelpersPerPlayer = MaxHelpers;

      for (int i = ModSaveData.PlayerHelperData.Count - 1; i >= 0; i--)
      {
        var playerData = ModSaveData.PlayerHelperData[i];

        for (int j = playerData.Helpers.Count - 1; j >= 0; j--)
        {
          var helperData = playerData.Helpers[j];
          if (!helperData.IsActiveHelper)
            continue;

          BotBase bot;
          var botChar = MyEntities.GetEntityById(helperData.HelperId) as IMyCharacter;
          if (botChar == null || !Bots.TryGetValue(botChar.EntityId, out bot))
            continue;

          var gridGraph = bot._currentGraph as CubeGridMap;
          var grid = gridGraph?.Grid ?? null;

          helperData.GridEntityId = grid?.EntityId ?? 0L;
          helperData.Position = botChar.GetPosition();
          helperData.Orientation = Quaternion.CreateFromRotationMatrix(botChar.WorldMatrix);
        }
      }

      ModSaveData.FactionPairings.Clear();
      foreach (var kvp in BotFactions)
        ModSaveData.FactionPairings.Add(new FactionData(kvp.Key, kvp.Value.FactionId));

      if (writeConfig)
        Config.WriteFileToWorldStorage("AiEnabled.cfg", typeof(SaveData), ModSaveData, Logger);
    }

    bool _isTick10, _isTick100;
    int _ticks;

    public override void UpdateBeforeSimulation()
    {
      try
      {
        if (!Registered)
          return;

        ++GlobalSpawnTimer;
        ++GlobalSpeakTimer;
        ++GlobalMapInitTimer;
        ++_ticks;
        _isTick10 = _ticks % 10 == 0;
        _isTick100 = _isTick10 && _ticks % 100 == 0;

        var player = MyAPIGateway.Session?.Player;
        if (player != null)
        {
          if (_botAnalyzers.Count > 0 || _botSpeakers.Count > 0 || _healthBars.Count > 0)
            DrawOverHeadIcons();

          UpdateGPSLocal(player);
        }

        DoTickNow();

        if (_isTick10)
        {
          if (IsServer)
            DoTick10();

          if (player?.Character?.IsDead == false)
            CheckForSelectedBot(player);
        }

        if (_isTick100)
          DoTick100();

        if (_needsUpdate)
          UpdateConfig();

        if (_needsAdminUpdate)
          UpdateAdminConfig();
      }
      catch (Exception ex)
      {
        ShowMessage($"Error in UpdateBeforeSim: {ex.Message}", timeToLive: 5000);
        Logger.Log($"Exception in AiSession.UpdateBeforeSim: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        //UnloadModData();
      }
      finally
      {
        base.UpdateBeforeSimulation();
      }
    }

    int _controlSpawnTimeCheck = 120;
    IMyCharacter _selectedBot;

    void CheckForSelectedBot(IMyPlayer player)
    {
      bool currentNull = _selectedBot == null;
      List<long> helperIds;
      if (!PlayerToHelperIdentity.TryGetValue(player.IdentityId, out helperIds) || helperIds == null || helperIds.Count == 0)
      {
        if (!currentNull)
        {
          MyVisualScriptLogicProvider.SetHighlight(_selectedBot.Name, false);
          _selectedBot = null;
        }

        return;
      }

      IHitInfo hit;
      var character = player.Character;

      if (character.Parent is IMyShipController)
      {
        if (!currentNull)
        {
          MyVisualScriptLogicProvider.SetHighlight(_selectedBot.Name, false);
          _selectedBot = null;
        }

        return;
      }

      var matrix = character.GetHeadMatrix(true);
      MyAPIGateway.Physics.CastRay(matrix.Translation + matrix.Forward * 0.25, matrix.Translation + matrix.Forward * 5, out hit, CollisionLayers.CharacterCollisionLayer);

      var bot = hit?.HitEntity as IMyCharacter;
      if (bot == null || bot.EntityId == character.EntityId)
      {
        bool valid = false;
        var grid = hit?.HitEntity as IMyCubeGrid;
        if (grid != null)
        {
          var localPos = grid.WorldToGridInteger(hit.Position - hit.Normal * grid.GridSize * 0.1f);
          var seat = grid.GetCubeBlock(localPos)?.FatBlock as IMyCockpit;
          if (seat?.Pilot != null && seat.Pilot.EntityId != character.EntityId)
          {
            valid = true;
            bot = seat.Pilot;
          }
        }

        if (!valid)
        {
          if (!currentNull)
          {
            MyVisualScriptLogicProvider.SetHighlight(_selectedBot.Name, false);
            _selectedBot = null;
          }

          return;
        }
      }

      if (!currentNull && _selectedBot.EntityId != bot.EntityId)
      {
        MyVisualScriptLogicProvider.SetHighlight(_selectedBot.Name, false);
        _selectedBot = null;
        currentNull = true;
      }

      if (helperIds.Contains(bot.EntityId))
      {
        if (PlayerData.ShowHealthBars)
        {
          HealthInfo healthInfo;
          if (_healthBars.TryGetValue(bot.EntityId, out healthInfo))
          {
            healthInfo.Renew();
          }
          else
          {
            healthInfo = GetHealthInfo();
            healthInfo.Set(bot);

            if (!_healthBars.TryAdd(bot.EntityId, healthInfo))
              ReturnHealthInfo(healthInfo);
          }
        }

        _selectedBot = bot;
        MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name);
      }
      else if (!currentNull)
      {
        MyVisualScriptLogicProvider.SetHighlight(_selectedBot.Name, false);
        _selectedBot = null;
      }
    }

    public void AddHealthBarIcons(List<long> bots)
    {
      for (int i = 0; i < bots.Count; i++)
      {
        var botId = bots[i];
        HealthInfo info;
        if (_healthBars.TryGetValue(botId, out info))
        {
          info.Renew();
        }
        else
        {
          var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
          if (bot == null || bot.MarkedForClose)
            continue;

          info = GetHealthInfo();
          info.Set(bot);
          if (!_healthBars.TryAdd(botId, info))
            ReturnHealthInfo(info);
        }
      }
    }

    public void AddOverHeadIcons(List<long> analyzers)
    {
      for (int i = 0; i < analyzers.Count; i++)
      {
        var botId = analyzers[i];
        if (_botAnalyzers.ContainsKey(botId))
          continue;

        var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
        if (bot == null || bot.MarkedForClose)
          continue;

        var info = GetIconInfo();
        info.Set(bot, 90);
        if (!_botAnalyzers.TryAdd(botId, info))
          ReturnIconInfo(info);
      }
    }

    MyStringId _material_Analyze = MyStringId.GetOrCompute("AiEnabled_Analyze");
    MyStringId _material_Chat = MyStringId.GetOrCompute("AiEnabled_Chat");
    Vector4 _iconColor = Color.LightCyan.ToVector4();
    void DrawOverHeadIcons()
    {
      var matrix = MyAPIGateway.Session.Camera.WorldMatrix;

      if (_botAnalyzers.Count > 0)
      {
        _iconRemovals.Clear();

        foreach (var kvp in _botAnalyzers)
        {
          var info = kvp.Value;
          if (info.Update(ref matrix, ref _material_Analyze, ref _iconColor))
            _iconRemovals.Add(kvp.Key);
        }

        foreach (var botId in _iconRemovals)
        {
          IconInfo info;
          if (_botAnalyzers.TryRemove(botId, out info))
            ReturnIconInfo(info);
        }
      }

      if (_botSpeakers.Count > 0)
      {
        _iconRemovals.Clear();

        foreach (var kvp in _botSpeakers)
        {
          var info = kvp.Value;
          if (info.Update(ref matrix, ref _material_Chat, ref _iconColor))
            _iconRemovals.Add(kvp.Key);
        }

        foreach (var botId in _iconRemovals)
        {
          IconInfo info;
          if (_botSpeakers.TryRemove(botId, out info))
            ReturnIconInfo(info);
        }
      }

      if (_healthBars.Count > 0)
      {
        _hBarRemovals.Clear();

        foreach (var kvp in _healthBars)
        {
          var info = kvp.Value;
          if (info.Update(ref matrix))
            _hBarRemovals.Add(kvp.Key);
        }

        foreach (var botId in _hBarRemovals)
        {
          HealthInfo info;
          if (_healthBars.TryRemove(botId, out info))
            ReturnHealthInfo(info);
        }
      }      

      _hBarRemovals.Clear();
      _iconRemovals.Clear();
    }

    void DoTickNow()
    {
      try
      {
        if (!Registered)
          return;

        Projectiles.Update();

        for (int i = _weaponFireList.Count - 1; i >= 0; i--)
        {
          var info = _weaponFireList[i];
          if (info == null)
          {
            _weaponFireList.RemoveAtFast(i);
            continue;
          }

          bool fire = info.Update();
          if (info.Finished)
          {
            _weaponFireList.RemoveAtFast(i);

            bool stop = true;
            for (int j = 0; j < _weaponFireList.Count; j++)
            {
              if (_weaponFireList[j].Bot.EntityId == info.Bot.EntityId)
              {
                stop = false;
                break;
              }
            }
            
            if (stop)
              FireWeaponForBot(info, false);

            ReturnWeaponInfo(info);
          }
          else if (fire)
          {
            FireWeaponForBot(info, true);
          }
        }

        UpdateParticles();

        if (IsClient)
        {
          for (int i = _robots.Count - 1; i >= 0; i--)
          {
            var bot = _robots[i];
            if (bot?.Character == null || bot.Character.MarkedForClose || bot.Character.IsDead)
              _robots.RemoveAtFast(i);
          }

          return;
        }
      }
      catch(Exception ex)
      {
        Logger.Log($"Exception in DoTickNow.IsClient: {ex.Message}\n{ex.StackTrace}");
        return;
      }

      try
      {
        if (Players.Count == 0)
        {
          return;
        }

        if (_checkControlTimer)
        {
          ++_controlSpawnTimer;
          if (_controlSpawnTimer > _controlSpawnTimeCheck)
          {
            _controlSpawnTimer = 0;
            _controlSpawnTimeCheck = 120;
            _checkControlTimer = false;
            _controllerSet = true;
          }
        }

        if (_botCharsToClose.Count > 0)
        {
          var checkTime = MyAPIGateway.Multiplayer.MultiplayerActive ? 300 : 60;
          ++_controlSpawnTimer;
          if (_controlSpawnTimer > checkTime)
          {
            foreach (var bot in _botCharsToClose)
              bot?.Close();

            _botCharsToClose.Clear();
          }
        }

        if (_controllerSet && _controllerInfo.Count + _pendingControllerInfo.Count < ControllerCacheNum)
        {
          bool spawnOkay = false;
          try
          {
            _isControlBot = true;
            var botId = MyVisualScriptLogicProvider.SpawnBot("Police_Bot", _starterPosition, Vector3D.Forward, Vector3D.Up, "");
            spawnOkay = true;

            var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
            if (bot != null)
            {
              bot.Flags &= ~EntityFlags.NeedsUpdate100;

              _controllerSet = false;
              _controlBotIds.Add(botId);
              MyAPIGateway.Utilities.InvokeOnGameThread(GetBotController, "AiEnabled");
              //MyAPIGateway.Utilities.ShowMessage("AiEnabled", "Spawned bot");
            }
          }
          catch
          {
            if (spawnOkay)
              throw;
            else
            {
              Logger.Log($"Spawn issue detected. Waiting to try again...", MessageType.WARNING);
              _controlSpawnTimeCheck = 600;
              _controllerSet = false;
              _isControlBot = false;
              _checkControlTimer = true;
              _controlSpawnTimer = 0;
            }
          }
        }

        if (_robots.Count == 0)
          return;

        foreach (var kvp in GridGraphDict)
        {
          var gridGraph = kvp.Value;
          if (gridGraph?.Ready == true && gridGraph.Grid?.IsStatic == false)
            gridGraph.RecalculateOBB();
        }

        for (int i = _robots.Count - 1; i >= 0; i--)
        {
          var robot = _robots[i];
          if (robot?.IsDead != false || robot.Character.MarkedForClose)
          {
            _robots.RemoveAtFast(i);
            continue;
          }

          if (!robot.Update() || robot.Character.Parent is IMyCockpit)
            continue;

          if (robot.Owner != null)
            robot.CheckPathCounter();
  
          robot.MoveToTarget();
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"Exception in DoTickNow.ServerStuff: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    void UpdateParticles()
    {
      foreach (var item in ParticleDictionary)
      {
        if (ParticleInfoDict.ContainsKey(item.Key))
        {
          continue;
        }

        var val = item.Value;
        var bot = MyEntities.GetEntityById(val.BotEntityId) as IMyCharacter;
        if (bot == null)
        {
          continue;
        }

        switch (val.ParticleType)
        {
          case ParticleInfoBase.ParticleType.Factory:
            if (val.BlockEntityId > 0)
            {
              var block = MyEntities.GetEntityById(val.BlockEntityId) as IMyTerminalBlock;
              if (block != null)
                ParticleInfoDict.TryAdd(val.BotEntityId, new FactoryParticleInfo(bot, block));
            }
            break;
          case ParticleInfoBase.ParticleType.Zombie:
            ParticleInfoDict.TryAdd(val.BotEntityId, new EnemyParticleInfo(bot, "ZombieGas"));
            break;
          case ParticleInfoBase.ParticleType.Ghost:
            ParticleInfoDict.TryAdd(val.BotEntityId, new EnemyParticleInfo(bot, "GhostIce"));
            break;
          case ParticleInfoBase.ParticleType.Builder:
            if (val.BlockEntityId > 0)
            {
              var block = MyEntities.GetEntityById(val.BlockEntityId) as IMyTerminalBlock;
              if (block != null)
                ParticleInfoDict.TryAdd(val.BotEntityId, new BuilderParticleInfo(bot, block.SlimBlock, val.IsWelderParticle));
            }
            else if (val.GridEntityId > 0 && val.BlockPosition != null)
            {
              var grid = MyEntities.GetEntityById(val.GridEntityId) as MyCubeGrid;
              if (grid != null)
              {
                var slim = grid.GetCubeBlock(val.BlockPosition.Value) as IMySlimBlock;
                if (slim != null)
                  ParticleInfoDict.TryAdd(val.BotEntityId, new BuilderParticleInfo(bot, slim, val.IsWelderParticle));
              }
            }
            break;
        }
      }

      foreach (var item in ParticleInfoDict)
      {
        if (item.Value?.Bot?.MarkedForClose != false)
        {
          ParticleInfoBase pBase;
          ParticleInfoDict.TryRemove(item.Key, out pBase);
          continue;
        }

        item.Value.Update();
      }
    }

    int _tempObstacleTimer;
    HashSet<long> _graphRemovals = new HashSet<long>();
    void DoTick10()
    {
      ++_tempObstacleTimer;
      bool clearObstacles = _tempObstacleTimer % 360 == 0;

      ++GlobalMapInitTimer;
      if (GlobalMapInitTimer > 6 && MapInitQueue.Count > 0)
      {
        GridBase map;
        if (MapInitQueue.TryDequeue(out map))
        {
          GlobalMapInitTimer = 0;
          map.Init();
        }
      }

      foreach (var kvp in GridGraphDict)
      {
        var graph = kvp.Value;
        if (graph?.Grid == null || graph.Grid.MarkedForClose)
        {
          _graphRemovals.Add(kvp.Key);
          continue;
        }

        bool updateInventory = false;

        if (graph.Ready)
        {
          graph.LastActiveTicks++;
          if (graph.LastActiveTicks > 6)
          {
            graph.IsActive = false;
            continue;
          }
        }

        if (graph.Dirty)
        {
          graph.Refresh();
          graph.InventoryCache._needsUpdate = true;
          updateInventory = true;
        }
        else
        {
          if (clearObstacles)
          {
            graph.TempBlockedNodes.Clear();
            updateInventory = graph.InventoryCache.AccessibleInventoryCount == 0;
          }

          if (graph.HasMoved)
          {
            if (!graph.PlanetTilesRemoved)
              graph.RemovePlanetTiles();

            var grid = graph.Grid;
            if (grid.Physics.LinearVelocity.LengthSquared() < 0.01)
              graph.ResetMovement();
          }
        }

        graph.InventoryCache.Update(updateInventory);
      }

      if (_graphRemovals.Count > 0)
      {
        foreach (var key in _graphRemovals)
        {
          CubeGridMap _;
          GridGraphDict.TryRemove(key, out _);
        }

        _graphRemovals.Clear();
      }


      foreach (var kvp in VoxelGraphDict)
      {
        var graph = kvp.Value;

        if (graph.Ready)
        {
          graph.LastActiveTicks++;
          if (graph.LastActiveTicks > 6)
          {
            graph.IsActive = false;
            continue;
          }
        }

        if (graph.Dirty)
        {
          graph.Refresh();
        }
        else
        {
          if (clearObstacles)
            graph.TempBlockedNodes.Clear();
        }
      }

      var localPlayer = MyAPIGateway.Session.Player;
      var mpActive = MyAPIGateway.Multiplayer.MultiplayerActive;

      if (AnalyzeHash.Count > 0)
      {
        _analyzeList.AddRange(AnalyzeHash);
        if (mpActive)
        {
          var pkt = new OverHeadIconPacket(_analyzeList);
          Network.RelayToClients(pkt);
        }

        if (localPlayer != null)
          AddOverHeadIcons(_analyzeList);

        _analyzeList.Clear();
        AnalyzeHash.Clear();
      }

      foreach (var kvp in PlayerToHealthBars)
      {
        var infoStat = kvp.Value;
        if (infoStat == null || !infoStat.ShowHealthBars)
          continue;

        var list = infoStat.BotEntityIds;
        if (list.Count == 0)
          continue;

        if (kvp.Key == localPlayer?.IdentityId)
        {
          AddHealthBarIcons(list);
        }
        else if (mpActive)
        {
          var steamId = MyAPIGateway.Players.TryGetSteamId(kvp.Key);
          if (steamId > 0)
          {
            var pkt = new HealthBarPacket(list);
            Network.SendToPlayer(pkt, steamId);
          }
        }

        list.Clear();
      }

      if (DoorsToClose.Count > 0)
      {
        var span = MyAPIGateway.Session.ElapsedPlayTime;

        foreach (var kvp in DoorsToClose)
        {
          var door = MyEntities.GetEntityById(kvp.Key) as IMyDoor;
          if (door == null || door.MarkedForClose || !door.IsWorking || door.Status > Sandbox.ModAPI.Ingame.DoorStatus.Open)
          {
            _graphRemovals.Add(kvp.Key);
            continue;
          }

          var elapsed = span - kvp.Value;
          if (elapsed.TotalSeconds >= 1.5 && door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
          {
            door.CloseDoor();
            _graphRemovals.Add(kvp.Key);
          }
        }

        foreach (var id in _graphRemovals)
          DoorsToClose.TryRemove(id, out span);

        _graphRemovals.Clear();
      }
    }

    bool _gpsUpdatesAvailable;
    int _planetCheckTimer;
    void DoTick100()
    {
      if (IsServer)
      {
        if (_gpsUpdatesAvailable)
          SendGPSEntriesToPlayers();

        if (_newPlayerIds.Count > 0)
          UpdatePlayers();

        if (_controllerSet && FutureBotQueue.Count > 0 && _controllerInfo.Count >= ControllerCacheNum)
        {
          while (FutureBotQueue.Count > 0)
          {
            if (_controllerInfo.Count < 10)
              break;

            var future = FutureBotQueue.Dequeue();

            MyCubeGrid grid = null;
            if (future.GridId > 0)
            {
              grid = MyEntities.GetEntityById(future.GridId) as MyCubeGrid;
            }

            var bot = BotFactory.SpawnHelper(future.Subtype, future.DisplayName ?? "", future.OwnerId, future.PositionAndOrientation, grid, ((BotType)future.Role).ToString());
            if (bot == null)
            {
              Logger.Log($"{GetType().FullName}: FutureBot returned null from spawn event", MessageType.WARNING);
            }
            else
            {
              IMyPlayer player;
              if (Players.TryGetValue(future.OwnerId, out player) && player != null) 
              {
                //List<long> helperIds;
                //if (!PlayerToActiveHelperIds.TryGetValue(future.OwnerId, out helperIds))
                //{
                //  helperIds = new List<long>();
                //  PlayerToActiveHelperIds[future.OwnerId] = helperIds;
                //}

                //if (!helperIds.Contains(bot.EntityId))
                //  helperIds.Add(bot.EntityId);

                var pkt = new SpawnPacketClient(bot.EntityId, false);
                Network.SendToPlayer(pkt, player.SteamUserId);

                var saveData = ModSaveData.PlayerHelperData;
                for (int i = 0; i < saveData.Count; i++)
                {
                  var playerData = saveData[i];
                  if (playerData.OwnerIdentityId == player.IdentityId)
                  {
                    var helperData = playerData.Helpers;
                    for (int j = 0; j < helperData.Count; j++)
                    {
                      var helper = helperData[j];
                      if (helper.HelperId == future.BotId)
                      {
                        helperData.RemoveAt(j);
                        break;
                      }
                    }

                    var pkt2 = new ClientHelperPacket(helperData);
                    Network.SendToPlayer(pkt2, player.SteamUserId);

                    break;
                  }
                }
              }
            }
          }

          if (FutureBotQueue.Count == 0)
            StartAdminUpdateCounter();
        }

        ++_planetCheckTimer;
        var checkPlanets = _planetCheckTimer % 10 == 0;

        foreach (var graph in GridGraphDict.Values)
        {
          if (graph._locked || !graph.IsActive)
            continue;

          if (checkPlanets)
            graph.CheckPlanet();

          if (graph.NeedsVoxelUpate)
            graph.UpdateVoxels();

          if (graph.Ready)
            graph.UpdateTempObstacles();
        }

        bool removedGraphThisTick = false;

        foreach (var kvp in VoxelGraphDict)
        {
          var graph = kvp.Value;
          if (graph._locked || !graph.IsActive)
          {
            if (!graph._locked && graph.LastActiveTicks > 100 && !removedGraphThisTick)
            {
              removedGraphThisTick = true;

              graph.Close();
              VoxelGraphDict.TryRemove(kvp.Key, out graph);
              //MyAPIGateway.Utilities.ShowNotification($"Removing voxel graph {kvp.Key}, {VoxelGraphDict.Count} VMs remaining");
              //Logger.Log($"Removing voxel graph {kvp.Key} (last active = {graph.LastActiveTicks}), {VoxelGraphDict.Count} VMs remaining");
            }

            continue;
          }

          if (graph.NeedsVoxelUpate)
            graph.UpdateVoxels();

          if (graph.Ready)
            graph.UpdateTempObstacles();
        }

        for (int i = _robots.Count - 1; i >= 0; i--)
        {
          var bot = _robots[i];
          var botChar = bot?.Character;
          if (botChar == null || botChar.IsDead)
            continue;

          //var ent = bot as MyEntity;
          //if ((ent.NeedsUpdate & MyEntityUpdateEnum.EACH_100TH_FRAME) != 0)
          //  ent.GameLogic?.UpdateAfterSimulation100();

          var soundComp = botChar.Components?.Get<MyCharacterSoundComponent>();
          soundComp?.UpdateAfterSimulation100();

          bot.CheckDeniedDoors();
        }
      }
    }

    public ControlInfo GetBotIdentity()
    {
      if (_controllerInfo.Count == 0)
        return null;

      var info = _controllerInfo[0];
      _controllerInfo.RemoveAtImmediately(0);
      _controllerInfo.ApplyChanges();

      return info;
    }

    bool _controllerSet = true;
    void GetBotController()
    {
      _tempPlayers.Clear();
      MyAPIGateway.Players.GetPlayers(_tempPlayers);
      for (int i = 0; i < _tempPlayers.Count; i++)
      {
        var player = _tempPlayers[i];
        var entityId = player?.Character?.EntityId ?? -1;
        if (player?.IsBot == true && player.Controller != null && _controlBotIds.Remove(entityId))
        {
          _pendingControllerInfo.Add(new ControlInfo(player, entityId));
          _pendingControllerInfo.ApplyChanges();

          //_pendingControllerInfo[entityId] = new ControlInfo(player, entityId);
          _botsToClose.Add(entityId);
          _botCharsToClose.Add(player.Character);
          _controlSpawnTimer = 0;

          break;
        }
      }

      _controllerSet = _botsToClose.Count < 5;
    }

    void GetBotControllerClient()
    {
      _tempPlayers.Clear();
      MyAPIGateway.Players.GetPlayers(_tempPlayers);

      for (int i = 0; i < _tempPlayers.Count; i++)
      {
        var player = _tempPlayers[i];
        if (player == null)
        {
          continue;
        }

        if (!player.IsBot || !_newPlayerIds.ContainsKey(player.IdentityId))
        {
          _newPlayerIds.Remove(player.IdentityId);
          continue;
        }

        var entityId = _newPlayerIds[player.IdentityId];

        if (player.Controller != null && _newPlayerIds.Remove(player.IdentityId))
        {
          _controllerInfo.Add(new ControlInfo(player, entityId));
          _controllerInfo.ApplyChanges();

          //_controllerInfo[entityId] = new ControlInfo(player, entityId);
          break;
        }
      }
    }

    public void RemoveBot(long botId, long ownerId = 0L, bool cleanConfig = false)
    {
      if (!Registered)
        return;

      BotBase _;
      Bots.TryRemove(botId, out _);
      _botEntityIds.Remove(botId);

      for (int i = _robots.Count - 1; i >= 0; i--)
      {
        if (_robots[i]?.Character?.EntityId == botId)
        {
          _robots.RemoveAtFast(i);
          break;
        }
      }

      if (ownerId > 0)
      {
        RemoveGPSForBot(botId);

        if (cleanConfig)
        {
          for (int i = 0; i < ModSaveData.PlayerHelperData.Count; i++)
          {
            var helperData = ModSaveData.PlayerHelperData[i];
            if (helperData?.OwnerIdentityId == ownerId)
            {
              helperData.RemoveHelper(botId);

              var steamId = MyAPIGateway.Players.TryGetSteamId(ownerId);
              if (steamId > 0)
              {
                if (steamId == MyAPIGateway.Multiplayer.MyId)
                {
                  MyHelperInfo = helperData.Helpers;
                }
                else
                {
                  var pkt = new ClientHelperPacket(helperData.Helpers);
                  Network.SendToPlayer(pkt, steamId);
                }
              }

              break;
            }
          }
        }

        List<BotBase> playerHelpers;
        if (!PlayerToHelperDict.TryGetValue(ownerId, out playerHelpers))
          return;

        for (int i = 0; i < playerHelpers.Count; i++)
        {
          var helper = playerHelpers[i];
          if (helper?.Character?.EntityId == botId)
          {
            playerHelpers.RemoveAtFast(i);
            break;
          }
        }
      }
    }

    List<long> _gpsAddIDs = new List<long>();
    List<long> _gpsOwnerIDs = new List<long>();
    List<long> _gpsRemovals = new List<long>();
    List<long> _localGpsBotIds = new List<long>(10);

    void SendGPSEntriesToPlayers()
    {
      if (!IsServer || MyAPIGateway.Players.Count == 0)
        return;

      _gpsAddIDs.Clear();
      _gpsOwnerIDs.Clear();
      _gpsRemovals.Clear();

      foreach (var kvp in BotGPSDictionary)
      {
        BotBase bb;
        if (!Bots.TryGetValue(kvp.Key, out bb) || bb?.Owner == null)
        {
          _gpsRemovals.Add(kvp.Key);
          continue;
        }

        var bot = bb?.Character;
        if (bot == null || bot.IsDead || bot.MarkedForClose)
        {
          _gpsRemovals.Add(kvp.Key);
          continue;
        }

        _gpsAddIDs.Add(kvp.Key);
        _gpsOwnerIDs.Add(bb.Owner.IdentityId);
      }

      var idDedicated = MyAPIGateway.Utilities.IsDedicated;
      var gpsCollectionValid = MyAPIGateway.Session?.GPS != null;

      foreach (var botId in _gpsRemovals)
      {
        IMyGps gps;
        if (BotGPSDictionary.TryRemove(botId, out gps) && !idDedicated && gpsCollectionValid)
          MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
      }

      if (_gpsAddIDs.Count > 0)
      {
        var packet = new GpsUpdatePacket(_gpsAddIDs, _gpsOwnerIDs);
        Network.RelayToClients(packet);
      }
    }

    void UpdateGPSLocal(IMyPlayer player)
    {
      var character = player?.Character;
      var gpsList = MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId);

      for (int i = 0; i < _localGpsBotIds.Count; i++)
      {
        var botId = _localGpsBotIds[i];
        if (BotGPSDictionary.ContainsKey(botId))
          continue;

        var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
        if (bot != null)
          AddGPSForBot(bot);
      }

      foreach (var kvp in BotGPSDictionary)
      {
        var gps = kvp.Value;
        var contains = gpsList.Contains(gps);

        var bot = MyEntities.GetEntityById(kvp.Key) as IMyCharacter;
        if (bot == null || bot.IsDead || character == null || character.IsDead)
        {
          if (contains)
            MyAPIGateway.Session.GPS.RemoveLocalGps(gps.Hash);

          continue;
        }

        var botPosition = bot.PositionComp.WorldAABB.Center + bot.WorldMatrix.Up * 0.25;
        var distanceSq = Vector3D.DistanceSquared(character.WorldAABB.Center, botPosition);

        if (distanceSq > 62500)
        {
          if (contains)
            MyAPIGateway.Session.GPS.RemoveLocalGps(gps);

          continue;
        }

        if (contains)
          MyAPIGateway.Session.GPS.RemoveLocalGps(gps.Hash);

        gps.Coords = botPosition;
        gps.ShowOnHud = true;
        gps.UpdateHash();

        MyAPIGateway.Session.GPS.AddLocalGps(gps);
      }
    }

    internal void UpdateGPSCollection(List<long> toAdd, List<long> owners)
    {
      if (toAdd == null || toAdd.Count != owners?.Count)
        return;

      _localGpsBotIds.Clear();
      _localGpsBotIds.AddList(toAdd);

      var player = MyAPIGateway.Session?.Player;
      if (player == null)
        return;

      _gpsRemovals.Clear();
      foreach (var kvp in BotGPSDictionary)
      {
        var bot = MyEntities.GetEntityById(kvp.Key) as IMyCharacter;
        if (bot == null || bot.IsDead || bot.MarkedForClose)
        {
          _gpsRemovals.Add(kvp.Key);
          continue;
        }

        if (_localGpsBotIds.Contains(kvp.Key))
          continue;

        MyAPIGateway.Session.GPS.RemoveLocalGps(kvp.Value);
        _gpsRemovals.Add(kvp.Key);
      }

      foreach (var botId in _gpsRemovals)
      {
        IMyGps _;
        BotGPSDictionary.TryRemove(botId, out _);
      }

      for (int i = 0; i < _localGpsBotIds.Count; i++)
      {
        var botId = toAdd[i];
        var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
        if (bot == null || bot.IsDead || bot.MarkedForClose)
          continue;

        var ownerId = owners[i];
        AddGPSForBot(bot, ownerId);
      }
    }

    internal void AddGPSForBot(IMyCharacter bot, long? ownerId = null)
    {
      if (bot == null || BotGPSDictionary.ContainsKey(bot.EntityId))
        return;

      var position = bot.PositionComp.WorldAABB.Center + bot.WorldMatrix.Up * 0.25;
      var name = bot.DisplayName;
      if (string.IsNullOrWhiteSpace(name))
        name = string.IsNullOrWhiteSpace(bot.Name) ? bot.Definition.Id.SubtypeName : bot.Name;

      var gpsColor = Color.White;
      var player = MyAPIGateway.Session?.Player;
      if (player != null && ownerId.HasValue && ownerId > 0)
      {
        var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);
        if (playerFaction != null)
        {
          var botFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId.Value);
          if (botFaction != null)
          {
            name = $"{botFaction.Tag}.{name}";
            if (playerFaction.FactionId == botFaction.FactionId)
            {
              gpsColor = new Color(117, 201, 241);
            }
            else
            {
              var rep = MyAPIGateway.Session.Factions.GetRelationBetweenFactions(playerFaction.FactionId, botFaction.FactionId);

              if (rep == MyRelationsBetweenFactions.Friends)
                gpsColor = new Color(101, 178, 90);
              else if (rep == MyRelationsBetweenFactions.Enemies)
                gpsColor = new Color(227, 62, 63);
            }
          }
        }
      }

      var gps = MyAPIGateway.Session.GPS.Create(name, "", position, false);
      gps.GPSColor = gpsColor;

      if (BotGPSDictionary.TryAdd(bot.EntityId, gps) && IsServer)
        _gpsUpdatesAvailable = true;
    }

    internal void RemoveGPSForBot(long botEntityId)
    {
      IMyGps gps;
      if (BotGPSDictionary.TryRemove(botEntityId, out gps) && !MyAPIGateway.Utilities.IsDedicated)
        MyAPIGateway.Session?.GPS?.RemoveLocalGps(gps);

      _gpsUpdatesAvailable = true;
    }

    public void AddBot(BotBase bot, long ownerId = 0L)
    {
      var botId = bot.Character.EntityId;
      Bots[botId] = bot;

      byte b;
      if (!_botEntityIds.TryGetValue(botId, out b))
      {
        _botEntityIds[botId] = b;
        _robots.Add(bot);
      }

      IMyPlayer player;
      if (ownerId > 0 && Players.TryGetValue(ownerId, out player))
      {
        if (MyAPIGateway.Session?.Player?.IdentityId == ownerId)
        {
          List<long> helperIds;
          if (!PlayerToHelperIdentity.TryGetValue(ownerId, out helperIds))
          {
            helperIds = new List<long>();
            PlayerToHelperIdentity[ownerId] = helperIds;
          }

          if (!helperIds.Contains(botId))
            helperIds.Add(botId);

          List<long> activeHelpers;
          if (!PlayerToActiveHelperIds.TryGetValue(ownerId, out activeHelpers))
          {
            activeHelpers = new List<long>();
            PlayerToActiveHelperIds[ownerId] = activeHelpers;
          }

          if (!activeHelpers.Contains(botId))
            activeHelpers.Add(botId);
        }

        AddGPSForBot(bot.Character, ownerId);

        bool found = false;
        for (int i = 0; i < ModSaveData.PlayerHelperData.Count; i++)
        {
          var helperData = ModSaveData.PlayerHelperData[i];
          if (helperData?.OwnerIdentityId == ownerId)
          {
            found = true;
            bool add = true;

            for (int j = 0; j < helperData.Helpers.Count; j++)
            {
              var helper = helperData.Helpers[j];
              if (helper.HelperId == botId)
              {
                var matrix = bot.WorldMatrix;
                var gridGraph = bot._currentGraph as CubeGridMap;
                var grid = gridGraph?.Grid ?? null;

                helper.Orientation = Quaternion.CreateFromRotationMatrix(matrix);
                helper.Position = matrix.Translation;
                helper.GridEntityId = grid?.EntityId ?? 0L;
                add = false;
                break;
              }
            }

            if (add)
            {
              var gridGraph = bot._currentGraph as CubeGridMap;
              var grid = gridGraph?.Grid ?? null;
              var botType = bot is RepairBot ? BotType.Repair : BotType.Combat;

              helperData.AddHelper(bot.Character, botType, grid);
            }

            var pkt = new ClientHelperPacket(helperData.Helpers);
            Network.SendToPlayer(pkt, player.SteamUserId);

            break;
          }
        }

        if (!found)
        {
          var data = new HelperData(ownerId, null);
          var gridGraph = bot._currentGraph as CubeGridMap;
          var grid = gridGraph?.Grid ?? null;
          var botType = bot is RepairBot ? BotType.Repair : BotType.Combat;

          data.AddHelper(bot.Character, botType, grid);
          ModSaveData.PlayerHelperData.Add(data);

          var pkt = new ClientHelperPacket(data.Helpers);
          Network.SendToPlayer(pkt, player.SteamUserId);
        }

        List<BotBase> playerHelpers;
        if (!PlayerToHelperDict.TryGetValue(ownerId, out playerHelpers))
        {
          playerHelpers = new List<BotBase>();
          PlayerToHelperDict[ownerId] = playerHelpers;
        }

        foreach (var helper in playerHelpers)
        {
          if (helper?.Character?.EntityId == botId)
            return;
        }

        playerHelpers.Add(bot);
      }
    }

    void UpdatePlayers(long playerRemoved = 0L)
    {
      _tempPlayers.Clear();
      MyAPIGateway.Players.GetPlayers(_tempPlayers);
      bool clearControllers = false;

      foreach (var p in _tempPlayers)
      {
        if (p?.Character == null)
          continue;

        _newPlayerIds.Remove(p.IdentityId);
        if (p.IdentityId == playerRemoved)
          continue;

        if (p.IsValidPlayer() && !Players.ContainsKey(p.IdentityId))
        {
          Players[p.IdentityId] = p;
          clearControllers = true;
          SpawnPlayerHelpers(p);
        }
      }

      if (clearControllers && IsServer)
        ClearBotControllers();
    }

    void SpawnPlayerHelpers(IMyPlayer p)
    {
      foreach (var data in ModSaveData.PlayerHelperData)
      {
        if (data.OwnerIdentityId == p.IdentityId)
        {
          var steamId = MyAPIGateway.Players.TryGetSteamId(data.OwnerIdentityId);
          if (steamId > 0)
          {
            if (steamId == MyAPIGateway.Multiplayer.MyId)
            {
              MyHelperInfo = data.Helpers;
            }
            else
            {
              var pkt = new ClientHelperPacket(data.Helpers);
              Network.SendToPlayer(pkt, steamId);
            }
          }
          
          foreach (var helper in data.Helpers)
          {
            if (!helper.IsActiveHelper)
              continue;

            var matrix = MatrixD.CreateFromQuaternion(helper.Orientation);
            matrix.Translation = helper.Position;

            var future = new FutureBot(helper.Subtype, helper.DisplayName, data.OwnerIdentityId, helper.HelperId, helper.GridEntityId, (BotType)helper.Role, matrix);
            FutureBotQueue.Enqueue(future);
          }

          break;
        }
      }
    }

    public void ShowMessage(string text, string font = MyFontEnum.Red, int timeToLive = 2000)
    {
      if (_hudMsg == null)
        _hudMsg = MyAPIGateway.Utilities.CreateNotification(string.Empty);

      _hudMsg.Hide();
      _hudMsg.Font = font;
      _hudMsg.AliveTime = timeToLive;
      _hudMsg.Text = text;
      _hudMsg.Show();
    }

    internal MyEntity3DSoundEmitter GetEmitter(IMyEntity ent)
    {
      MyEntity3DSoundEmitter emitter;
      if (SoundEmitters.TryPop(out emitter))
      {
        emitter.Entity = ent as MyEntity;
        return emitter;
      }

      return new MyEntity3DSoundEmitter(ent as MyEntity);
    }

    internal MyEntity3DSoundEmitter GetEmitter()
    {
      MyEntity3DSoundEmitter emitter;
      if (SoundEmitters.TryPop(out emitter))
      {
        emitter.Entity = null;
        return emitter;
      }

      return new MyEntity3DSoundEmitter(null);
    }

    internal void ReturnEmitter(MyEntity3DSoundEmitter emitter)
    {
      if (emitter == null)
        return;

      emitter.Entity = null;
      SoundEmitters.Push(emitter);
    }
  }
}
