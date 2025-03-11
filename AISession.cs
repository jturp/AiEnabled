using AiEnabled.Ai.Support;
using AiEnabled.API;
using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.ConfigData;
using AiEnabled.Graphics;
using AiEnabled.Input;
using AiEnabled.Networking;
using AiEnabled.Networking.Packets;
using AiEnabled.Particles;
using AiEnabled.Projectiles;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.AI.Pathfinding.Obsolete;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

using SpaceEngineers.Game.ModAPI;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Scripting;
using VRage.Utils;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

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

    public readonly MyStringHash RoboDogSubtype = MyStringHash.GetOrCompute("RoboDog");
    public readonly MyStringHash PlushieSubtype = MyStringHash.GetOrCompute("Plushie_Astronaut");
    public readonly MyStringHash RoboPlushieSubtype = MyStringHash.GetOrCompute("Robo_Plushie");
    public readonly MyDefinitionId HandGrenade_ConsumableId = new MyDefinitionId(typeof(MyObjectBuilder_ConsumableItem), "JT_HandGrenade");
    // TODO: Add other grenades to this and let bots use them (randomly or targeted??)

    public static int MainThreadId = 1;
    public static AiSession Instance;
    public const string VERSION = "v1.9.5";
    const int MIN_SPAWN_COUNT = 3;
    public static KVPComparer IgnoreListComparer = new KVPComparer();

    public uint GlobalSpawnTimer, GlobalSpeakTimer, GlobalMapInitTimer;

    public int BotNumber => _robots?.Count ?? 0;
    public Logger Logger { get; protected set; }
    public bool Registered { get; protected set; }
    public bool CanSpawn
    {
      get
      {
        if (!Registered || _controllerInfo == null || BotNumber >= ModSaveData.MaxBotsInWorld)
          return false;

        var queueCount = FutureBotQueue?.Count ?? 0;
        var queueCountAPI = FutureBotAPIQueue?.Count ?? 0;

        if (_controllerInfo.Count - queueCount - queueCountAPI < MIN_SPAWN_COUNT)
          return false;

        if (!MyAPIGateway.Utilities.IsDedicated)
        {
          var info = _controllerInfo[0];
          if (info?.Identity == null)
            return false;

          return MyAPIGateway.Players.TryGetSteamId(info.Identity.IdentityId) == 0;
        }

        return true;
      }
    }

    // for path testing, only works with 1 bot active
    public bool InPathFinder;

    public bool FactoryControlsHooked;
    public bool FactoryControlsCreated;
    public bool FactoryActionsCreated;
    public bool IsServer, IsClient;
    public bool DrawDebug, DrawDebug2, DrawObstacles;
    public bool ShieldAPILoaded, WcAPILoaded, IndOverhaulLoaded, EemLoaded, GrenadesEnabled;
    public bool InfiniteAmmoEnabled;
    public double SyncRange = 3000;
    public long LastSpawnId = 1000;
    public readonly MyStringHash FactorySorterHash = MyStringHash.GetOrCompute("RoboFactory");

    public List<HelperInfo> MyHelperInfo;
    public CommandMenu CommandMenu;
    public PlayerMenu PlayerMenu;
    public NetworkHandler Network;
    public SaveData ModSaveData;
    public MovementCostData MovementCostData;
    public BotPricing ModPriceData;
    public PlayerData PlayerData;
    public Inputs Input;
    public AiScheduler Scheduler = new AiScheduler();
    public ProjectileInfo Projectiles = new ProjectileInfo();
    public RepairDelay BlockRepairDelays = new RepairDelay();
    public MyCubeGrid.MyCubeGridHitInfo CubeGridHitInfo = new MyCubeGrid.MyCubeGridHitInfo();
    public BlockInfo BlockInfo;

    // APIs
    public HudAPIv2 HudAPI;
    public DefenseShieldsAPI ShieldAPI = new DefenseShieldsAPI();
    public WcApi WcAPI = new WcApi();
    public LocalBotAPI LocalBotAPI;

    IMyHudNotification _hudMsg;
    Vector3D _starterPosition;
    bool _isControlBot, _firstCacheComplete;
    int _controllerCacheNum = 20;

    TimeSpan _firstFrameTime;
    FastResourceLock _voxelMapResourceLock = new FastResourceLock();
    FastResourceLock _gridMapResourceLock = new FastResourceLock();

    public GridBase GetNewGraph(MyCubeGrid grid, Vector3D newGraphPosition, MatrixD worldMatrix)
    {
      if (grid != null)
      {
        var gridGraph = GetGridGraph(grid, worldMatrix);
        if (gridGraph != null)
          return gridGraph;
      }

      return GetVoxelGraph(newGraphPosition, worldMatrix);
    }

    public CubeGridMap GetGridGraph(MyCubeGrid grid, MatrixD worldMatrix)
    {
      grid = GridBase.GetLargestGridForMap(grid) as MyCubeGrid;
      if (grid == null || grid.MarkedForClose)
        return null;

      if (grid.GridSizeEnum == MyCubeSize.Small)
      {
        Logger.Warning($"GetGridGraph: Attempted to get a graph for a small grid ({grid.DisplayName})!");
        return null;
      }

      using (_gridMapResourceLock.AcquireExclusiveUsing())
      {
        CubeGridMap gridBase;
        if (!GridGraphDict.TryGetValue(grid.EntityId, out gridBase) || gridBase == null || !gridBase.IsValid)
        {
          gridBase = new CubeGridMap(grid, worldMatrix);
          GridGraphDict.TryAdd(grid.EntityId, gridBase);
        }

        return gridBase;
      }
    }

    ulong _lastVoxelId;
    public VoxelGridMap GetVoxelGraph(Vector3D worldPosition, MatrixD worldMatrix, bool forceRefresh = false, bool returnFirstFound = true)
    {
      using (_voxelMapResourceLock.AcquireExclusiveUsing())
      {
        double minDistance = double.MaxValue;
        double checkDistance = VoxelGridMap.DefaultHalfSize * 0.5;
        checkDistance *= checkDistance;

        VoxelGridMap closestMap = null;
        foreach (var voxelGraph in VoxelGraphDict.Values)
        {
          if (voxelGraph == null || !voxelGraph.IsValid)
          {
            VoxelGridMap _;
            VoxelGraphDict.TryRemove(voxelGraph.Key, out _);
          }
          else if (voxelGraph.OBB.Contains(ref worldPosition))
          {
            if (returnFirstFound)
            {
              if (forceRefresh)
                voxelGraph.Refresh();

              return voxelGraph;
            }

            var graphDistance = Vector3D.DistanceSquared(worldPosition, voxelGraph.OBB.Center);
            if (graphDistance < minDistance)
            {
              closestMap = voxelGraph;
              minDistance = graphDistance;
            }

            if (graphDistance < checkDistance)
            {
              if (forceRefresh)
                voxelGraph.Refresh();

              return voxelGraph;
            }
          }
        }

        var graph = new VoxelGridMap(worldPosition, worldMatrix)
        {
          Key = _lastVoxelId
        };

        VoxelGraphDict[_lastVoxelId] = graph;
        _lastVoxelId++;
        return graph;
      }
    }

    public void StartWeaponFire(long botId, long targetId, float damage, float angleDeviationTan, List<float> rand, int ticksBetweenAttacks, int ammoRemaining, bool isGrinder, bool isWelder, bool leadTargets, Vector3I? position = null)
    {
      var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
      if (bot == null || bot.IsDead)
        return;

      var tgt = MyEntities.GetEntityById(targetId); // this should be the grid if the target is a block, in which case position must be provided!
      if (!isWelder && (tgt == null || tgt.MarkedForClose))
        return;

      var info = GetWeaponInfo();
      info.Set(bot, tgt, damage, angleDeviationTan, rand, ticksBetweenAttacks, ammoRemaining, isGrinder, isWelder, leadTargets, position);

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
      pc.CleanUp(true, true);
      pc.Bot = null;

      _pathCollections?.Push(pc);
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

    public override void LoadData()
    {
      try
      {
        MyVisualScriptLogicProvider.PrefabSpawnedDetailed += OnPrefabSpawned;
      }
      catch (Exception ex)
      {
        MyLog.Default.WriteLine(ex.ToString());
      }
      finally
      {
        base.LoadData();
      }
    }

    private void MyEntities_OnCloseAll()
    {
      try
      {
        if (Environment.CurrentManagedThreadId != MainThreadId)
        {
          Logger.Warning($"Calling MyEntities.CloseAll from a parallel thread! ThreadId = {Environment.CurrentManagedThreadId}");
          MyAPIGateway.Utilities.InvokeOnGameThread(MyEntities_OnCloseAll);
        }

        if (Bots?.Count > 0)
        {
          Logger.Log($"Closing {Bots.Count} bot(s) before unload");

          foreach (var kvp in Bots)
          {
            BotBase bot;
            if (Bots.TryRemove(kvp.Key, out bot) && bot != null)
            {
              bot.Close();
            }
          }

          Bots.Clear();
          Bots = null;
        }
      }
      catch (Exception ex)
      {
        Logger?.Log(ex.ToString());
      }
    }


    protected override void UnloadData()
    {
      try
      {
        UnloadModData();        
      }
      finally
      {
        Instance = null;
        IgnoreListComparer = null;
        base.UnloadData();
      }
    }

    void UnloadModData()
    {
      Logger?.Log($"Unloading mod data. Registered = {Registered}");
      Registered = false;

      MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
      MyEntities.OnEntityAdd -= MyEntities_OnEntityAdd;
      MyEntities.OnCloseAll -= MyEntities_OnCloseAll;
      MyVisualScriptLogicProvider.PlayerConnected -= PlayerConnected;
      MyVisualScriptLogicProvider.PlayerDisconnected -= PlayerDisconnected;
      MyVisualScriptLogicProvider.PlayerEnteredCockpit -= PlayerEnteredCockpit;
      MyVisualScriptLogicProvider.PlayerLeftCockpit -= PlayerLeftCockpit;
      MyVisualScriptLogicProvider.PlayerSpawned -= PlayerSpawned;
      MyVisualScriptLogicProvider.PlayerDied -= PlayerDied;
      MyVisualScriptLogicProvider.PrefabSpawnedDetailed -= OnPrefabSpawned;

      if (MyAPIGateway.Session?.Factions != null)
      {
        MyAPIGateway.Session.Factions.FactionCreated -= Factions_FactionCreated;
        MyAPIGateway.Session.Factions.FactionEdited -= Factions_FactionEdited;
        MyAPIGateway.Session.Factions.FactionStateChanged -= Factions_FactionStateChanged;
        MyAPIGateway.Session.Factions.FactionAutoAcceptChanged -= Factions_FactionAutoAcceptChanged;
      }

      if (_selectedBot != null)
        MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);

      if (ModSaveData?.PlayerHelperData != null)
      { 
        if (FutureBotQueue?.Count > 0)
        {
          while (FutureBotQueue.Count > 0)
          {
            var future = FutureBotQueue.Dequeue();

            if (future.OwnerId > 0)
            {
              foreach (var data in ModSaveData.PlayerHelperData)
              {
                if (data.OwnerIdentityId == future.OwnerId)
                {
                  var helpers = data.Helpers;
                  bool found = false;

                  foreach (var helper in helpers)
                  {
                    if (helper.HelperId == future.HelperInfo.HelperId)
                    {
                      found = true;
                      break;
                    }
                  }

                  if (!found)
                    helpers.Add(future.HelperInfo);
  
                  break;
                }
              }
            }
          }

          UpdateAdminConfig(true);
        }

        foreach (var item in ModSaveData.PlayerHelperData)
          item?.Close();

        ModSaveData.PlayerHelperData.Clear();
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

      if (Bots != null)
      {
        foreach (var bot in Bots.Values)
        {
          bot?.Close();
        }

        Bots.Clear();
      }

      if (_pathCollections != null)
      {
        foreach (var collection in _pathCollections)
          collection?.Close();

        _pathCollections.Clear();
      }

      if (GridGraphDict != null)
      {
        foreach (var graph in GridGraphDict.Values)
        {
          graph?.Close();
        }

        GridGraphDict.Clear();
      }

      if (VoxelGraphDict != null)
      {
        foreach (var graph in VoxelGraphDict.Values)
        {
          graph?.Close();
        }

        VoxelGraphDict.Clear();
      }

      if (BotComponents != null)
      {
        foreach (var kvp in BotComponents)
        {
          kvp.Value?.Clear();
        }

        BotComponents.Clear();
      }

      if (ShieldAPILoaded)
      {
        ShieldAPILoaded = false;
        ShieldAPI?.Unload();
      }

      if (WcAPILoaded)
      {
        WcAPILoaded = false;
        WcAPI.Unload();
      }

      Scheduler?.Close();
      BlockRepairDelays?.Close();
      CommandMenu?.Close();
      PlayerMenu?.Close();
      Input?.Close();
      HudAPI?.Close();
      Projectiles?.Close();
      MovementCostData?.Close();
      BlockInfo?.Close();
      Network?.Unregister();
      ProjectileConstants.Close();

      AllCoreWeaponDefinitions?.Clear();
      InvCachePool?.Clean();
      GridMapListPool?.Clean();
      OverlapResultListPool?.Clean();
      GridCheckHashPool?.Clean();
      StringListPool?.Clean();
      SoundListPool?.Clean();
      EntListPool?.Clean();
      HitListPool?.Clean();
      NodeQueuePool?.Clean();
      Players?.Clear();
      Bots?.Clear();
      CatwalkBlockDefinitions?.Clear();
      SlopeBlockDefinitions?.Clear();
      SlopedHalfBlockDefinitions?.Clear();
      RampBlockDefinitions?.Clear();
      HalfStairBlockDefinitions?.Clear();
      HalfStairMirroredDefinitions?.Clear();
      LadderBlockDefinitions?.Clear();
      PassageBlockDefinitions?.Clear();
      PassageIntersectionDefinitions?.Clear();
      ArmorPanelMiscDefinitions?.Clear();
      ArmorPanelAllDefinitions?.Clear();
      FactoryBotInfoDict?.Clear();
      SoundPairDict?.Clear();
      SoundEmitters?.Clear();
      FutureBotQueue?.Clear();
      FutureBotAPIStack?.Clear();
      FutureBotAPIQueue?.Clear();
      SpawnDataStack?.Clear();
      RobotSubtypes?.Clear();
      UseObjectsAPI?.Clear();
      BotToSeatRelativePosition?.Clear();
      BotToSeatShareMode?.Clear();
      ItemInfoDict?.Clear();
      AllowedBotRoles?.Clear();
      StorageStack?.Clear();
      AcceptedItemDict?.Clear();
      ItemOBDict?.Clear();
      AllGameDefinitions?.Clear();
      ScavengerItemList?.Clear();
      MissingCompsDictPool?.Clean();
      EmptySorterCache?.Clear();
      FactorySorterCache?.Clear();
      ApiWorkDataPool?.Clean();
      LocalVectorHashPool?.Clean();
      ConsumableItemList?.Clear();
      CrewAnimations?.Clear();
      BotStatusPool?.Clean();
      BotStatusListPool?.Clean();
      PendingBotRespawns?.Clear();
      ScaffoldBlockDefinitions?.Clear();
      GratedCatwalkExpansionBlocks?.Clear();
      ObstacleWorkDataPool?.Clean();
      VoxelUpdateListPool?.Clean();
      VoxelUpdateItemPool?.Clean();
      VoxelUpdateQueuePool?.Clean();
      BotToControllerInfoDict?.Clear();
      CharacterListPool?.Clean();
      KnownLootContainerIds?.Clear();
      BotModelDict?.Clear();
      BotModelList?.Clear();
      AnimationControllerDictionary?.Clear();
      SubtypeToSkeletonDictionary?.Clear();
      PlayerToRepairRadius?.Clear();
      EconomyGrids?.Clear();
      ColorDictionary?.Clear();
      CubeGridHitInfo?.Reset();
      HelperAnimations?.Clear();
      PlayerFollowDistanceDict?.Clear();
      GridsToDraw?.Clear();
      BotUpkeepPrices?.Clear();
      HealingHash?.Clear();
      AnalyzeHash?.Clear();
      LocalVectorQueuePool?.Clean();
      TempNodePool?.Clean(); 
      NodePool?.Clean();
      GraphWorkPool?.Clean();
      PathWorkPool?.Clean();
      RepairWorkPool?.Clean();
      SlimListPool?.Clean();
      GridGroupListPool?.Clean();
      VoxelMapListPool?.Clean();
      LineListPool?.Clean();
      PositionListPool?.Clean();
      IgnoreTypeDictionary?.Clear();
      MESBlockIds?.Clear();
      TransparentMaterialDefinitions?.Clear();

      _nameSB?.Clear();
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
      _healthInfoStack?.Clear();
      _iconInfoStack?.Clear();
      _controlBotIds?.Clear();
      _botsToClose?.Clear();
      _botEntityIds?.Clear();
      _useObjList?.Clear();
      _keyPresses?.Clear();
      _iconRemovals?.Clear();
      _hBarRemovals?.Clear();
      _iconAddList?.Clear();
      _botCharsToClose?.Clear();
      _botSpeakers?.Clear();
      _botAnalyzers?.Clear();
      _healthBars?.Clear();
      _botHealings?.Clear();
      _prefabsToCheck?.Clear();
      _commandInfo?.Clear();
      _activeHelpersToUpkeep?.Clear();

      MESBlockIds = null;
      IgnoreTypeDictionary = null;
      Scheduler = null;
      AllCoreWeaponDefinitions = null;
      RepairWorkPool = null;
      GraphWorkPool = null;
      PathWorkPool = null;
      BlockRepairDelays = null;
      MapInitQueue = null;
      InvCachePool = null;
      CornerArrayStack = null;
      SlimListPool = null;
      GridMapListPool = null;
      OverlapResultListPool = null;
      GridCheckHashPool = null;
      StringListPool = null;
      SoundListPool = null;
      EntListPool = null;
      HitListPool = null;
      LineListPool = null;
      PositionListPool = null;
      NodeQueuePool = null;
      GridGroupListPool = null;
      Players = null;
      Bots = null;
      DiagonalDirections = null;
      CardinalDirections = null;
      ButtonPanelDefinitions = null;
      VoxelMovementDirections = null;
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
      PassageBlockDefinitions = null;
      PassageIntersectionDefinitions = null;
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
      SpawnDataStack = null;
      FutureBotQueue = null;
      FutureBotAPIStack = null;
      FutureBotAPIQueue = null;
      RobotSubtypes = null;
      Projectiles = null;
      PlayerToHelperIdentity = null;
      PlayerToActiveHelperIds = null;
      UseObjectsAPI = null;
      Input = null;
      CommandMenu = null;
      PlayerMenu = null;
      BotToSeatRelativePosition = null;
      BotToSeatShareMode = null;
      ItemInfoDict = null;
      AllowedBotRoles = null;
      StorageStack = null;
      VoxelGraphDict = null;
      GridGraphDict = null;
      AcceptedItemDict = null;
      ItemOBDict = null;
      ShieldAPI = null;
      VoxelMapListPool = null;
      BotComponents = null;
      AllGameDefinitions = null;
      ScavengerItemList = null;
      MissingCompsDictPool = null;
      EmptySorterCache = null;
      FactorySorterCache = null;
      ApiWorkDataPool = null;
      LocalVectorHashPool = null;
      DirArray = null;
      ConsumableItemList = null;
      CrewAnimations = null;
      BotStatusPool = null;
      BotStatusListPool = null;
      PendingBotRespawns = null;
      ScaffoldBlockDefinitions = null;
      GratedCatwalkExpansionBlocks = null;
      ObstacleWorkDataPool = null;
      VoxelUpdateItemPool = null;
      VoxelUpdateListPool = null;
      VoxelUpdateQueuePool = null;
      NodePool = null;
      TempNodePool = null;
      BotToControllerInfoDict = null;
      CharacterListPool = null;
      KnownLootContainerIds = null;
      BotModelDict = null;
      BotModelList = null;
      AnimationControllerDictionary = null;
      SubtypeToSkeletonDictionary = null;
      PlayerToRepairRadius = null;
      EconomyGrids = null;
      LocalBotAPI = null;
      ColorDictionary = null;
      CubeGridHitInfo = null;
      HelperAnimations = null;
      PlayerFollowDistanceDict = null;
      GridsToDraw = null;
      BotUpkeepPrices = null;
      HealingHash = null;
      AnalyzeHash = null;
      LocalVectorQueuePool = null;
      IgnoreListComparer = null;
      TransparentMaterialDefinitions = null;
      BlockInfo = null;

      _nameArray = null;
      _nameSB = null;
      _gpsAddIDs = null;
      _gpsOwnerIDs = null;
      _gpsRemovals = null;
      _localGpsBotIds = null;
      _graphRemovals = null;
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
      _iconInfoStack = null;
      _healthInfoStack = null;
      _controlBotIds = null;
      _botsToClose = null;
      _botEntityIds = null;
      _useObjList = null;
      _keyPresses = null;
      _iconRemovals = null;
      _hBarRemovals = null;
      _iconAddList = null;
      _botCharsToClose = null;
      _botSpeakers = null;
      _botAnalyzers = null;
      _healthBars = null;
      _botHealings = null;
      _voxelMapResourceLock = null;
      _gridMapResourceLock = null;
      _prefabsToCheck = null;
      _commandInfo = null;
      _activeHelpersToUpkeep = null;

      if (Logger != null)
      {
        Logger.Log($"Mod unloaded successfully");
        Logger.Close();
      }
    }

    public override void BeforeStart()
    {
      try
      {
        Instance = this;
        Logger = new Logger("AiEnabled.log");
        BlockInfo = new BlockInfo(Logger);

        Network = new NetworkHandler(55387, this);
        Network.Register();

        IsServer = MyAPIGateway.Multiplayer.IsServer;
        IsClient = !IsServer;
        MainThreadId = Environment.CurrentManagedThreadId;
        _controllerCacheNum = Math.Max(20, Math.Min(50, MyAPIGateway.Session.MaxPlayers * 2));

        Scheduler.Schedule(() => _firstFrameTime = MyAPIGateway.Session.ElapsedPlayTime);

        foreach (var tpd in MyDefinitionManager.Static.GetTransparentMaterialDefinitions())
        {
          if (!TransparentMaterialDefinitions.Add(tpd.Id.SubtypeId))
            Logger.Log($"Tried to add duplicate Transparent Material for {tpd.Id.SubtypeName}");
        }

        foreach (var def in MyDefinitionManager.Static.GetInventoryItemDefinitions())
        {
          var subtype = def.Id.SubtypeName;
          if (def?.DisplayNameText != null && def.Public /*&& def.Context.ModId != "1521905890"*/
            && !subtype.StartsWith("MES", StringComparison.OrdinalIgnoreCase)
            && subtype.IndexOf("Admin", StringComparison.OrdinalIgnoreCase) < 0
            && subtype.IndexOf("Inhibitor", StringComparison.OrdinalIgnoreCase) < 0
            && subtype.IndexOf("Proprietary", StringComparison.OrdinalIgnoreCase) < 0
            && subtype.IndexOf("UraniumB", StringComparison.OrdinalIgnoreCase) < 0
            && subtype.IndexOf("EEMPilotSoul", StringComparison.OrdinalIgnoreCase) < 0
            && subtype.IndexOf("NPC_Component", StringComparison.OrdinalIgnoreCase) < 0
            && subtype.IndexOf("NPC_Token", StringComparison.OrdinalIgnoreCase) < 0)
          {
            IgnoreTypeDictionary[MyStringId.GetOrCompute(def.DisplayNameText)] = new KeyValuePair<string, bool>(def.DisplayNameText, false);
          }
        }

        bool sessionOK = MyAPIGateway.Session != null;
        if (sessionOK)
        {
          SyncRange = MyAPIGateway.Session.SessionSettings?.SyncDistance ?? 3000;

          if (MyAPIGateway.Session.Mods?.Count > 0)
          {
            foreach (var mod in MyAPIGateway.Session.Mods)
            {
              if (mod.PublishedFileId == 1365616918 || mod.PublishedFileId == 2372872458 || mod.PublishedFileId == 3154379105
                || mod.GetPath().Contains("AppData\\Roaming\\SpaceEngineers\\Mods\\DefenseShields"))
              {
                ShieldAPILoaded = ShieldAPI.Load();
                Logger.Log($"Defense Shields Mod found. API loaded successfully = {ShieldAPILoaded}");
              }
              else if (mod.PublishedFileId == 2861285936 || mod.PublishedFileId == 2861675418
                || mod.GetPath().Contains("AppData\\Roaming\\SpaceEngineers\\Mods\\HandGrenade"))
              {
                GrenadesEnabled = true;
              }
              else if (mod.PublishedFileId == 2200451495)
              {
                Logger.Log($"Water Mod v{WaterAPI.ModAPIVersion} found");
              }
              else if (mod.PublishedFileId == 1918681825 || mod.PublishedFileId == 3154371364)
              {
                try
                {
                  if (WcAPI.IsRegistered)
                    WcAPI.Unload();

                  WcAPI.Load(WeaponCoreRegistered);
                }
                catch(Exception ex)
                {
                  Logger.Log($"Failed attempt to load WC API: {ex}");
                }
              }
              else if (mod.PublishedFileId == 2344068716)
              {
                IndOverhaulLoaded = true;
              }
              else if (mod.PublishedFileId == 531659576)
              {
                EemLoaded = true;

                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                  Logger.Log($"EEM Mod found. Spawns will be delayed until EEM faction validation passes.");
                }
              }
            }
          }
          else
          {
            Logger.Warning($"Unable to check for mods in BeforeStart. Session OK = {sessionOK}");
          }
        }
        else
        {
          Logger.Warning($"Unable to check for mods in BeforeStart. Session OK = {sessionOK}");
        }

        foreach (var botDef in MyDefinitionManager.Static.GetBotDefinitions())
        {
          var agentDef = botDef as MyAgentDefinition;
          if (agentDef != null)
          {
            KnownLootContainerIds.Add(agentDef.InventoryContainerTypeId.SubtypeName);
          }
        }

        // Easter Egg block :)
        var hiddenBlock = new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeDeadAstronaut");
        var hiddenDef = MyDefinitionManager.Static.GetCubeBlockDefinition(hiddenBlock);
        if (hiddenDef != null)
        {
          hiddenDef.Public = true;
          hiddenDef.HasPhysics = false; // the block doesn't have physics, but this was still set to true which messes with pathing
        }

        foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
        {
          if (def == null || !def.Public || def.Id.SubtypeName == "ZoneChip")
            continue;

          if (def is MyGasTankDefinition || def is MyOxygenGeneratorDefinition)
            continue;

          var consumable = def as MyConsumableItemDefinition;
          if (consumable != null)
          {
            ConsumableItemList.Add(consumable);
            continue;
          }

          var cubeDef = def as MyCubeBlockDefinition;
          if (cubeDef != null && !cubeDef.Context.IsBaseGame && cubeDef.Context.ModId == "1521905890")
          { 
            MESBlockIds.Add(cubeDef.Id);
          }

          // Thanks to Digi for showing me how to figure out what is craftable :)
          var prodDef = def as MyProductionBlockDefinition;
          if (prodDef != null)
          {
            foreach (MyBlueprintClassDefinition bpClass in prodDef.BlueprintClasses)
            {
              if (bpClass != null)
              {
                foreach (MyBlueprintDefinitionBase bp in bpClass)
                {
                  if (bp != null)
                  {
                    foreach (MyBlueprintDefinitionBase.Item result in bp.Results)
                    {
                      var compDef = MyDefinitionManager.Static.GetDefinition(result.Id) as MyComponentDefinition;
                      if (compDef != null && compDef.Public && compDef.Id.SubtypeName != "ZoneChip" && !AllGameDefinitions.ContainsKey(compDef.Id))
                        AllGameDefinitions[compDef.Id] = compDef;
                    }
                  }
                }
              }
            }
          }
        }

        foreach (var charDef in MyDefinitionManager.Static.Characters)
        {
          if (charDef == null)
            continue;

          var subtype = charDef.Id.SubtypeId;

          if (charDef.Id.SubtypeId == PlushieSubtype || charDef.Id.SubtypeId == RoboPlushieSubtype
            || subtype.String.IndexOf("SmallSpider", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            // Fix for turrets shooting over their heads
            charDef.HeadServerOffset = -1.25f;
          }
          else if (charDef.Id.SubtypeId == RoboDogSubtype)
          {
            charDef.HeadServerOffset = -0.15f;
          }
          else if (subtype.String.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0
            || subtype.String.IndexOf("hound", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            charDef.MaxCrouchWalkSpeed = 1;
            charDef.MaxWalkSpeed = 1;
            charDef.MaxWalkStrafingSpeed = 1;
            charDef.HeadServerOffset = 0.15f;
          }
          else if (subtype.String.IndexOf("space_spider", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            charDef.HeadServerOffset = 0.25f;
          }

          if (charDef.Name != subtype.String && charDef.Name != null)
            subtype = MyStringHash.GetOrCompute(charDef.Name);

          AnimationControllerDictionary[subtype] = charDef.AnimationController;
          SubtypeToSkeletonDictionary[subtype] = charDef.Skeleton;  
          RobotSubtypes.Add(subtype.String);
        }

        if (IsServer)
        {
          LocalBotAPI = new LocalBotAPI();

          if (sessionOK)
          {
            if (MyAPIGateway.Session.SessionSettings != null)
            {
              InfiniteAmmoEnabled = MyAPIGateway.Session.SessionSettings.InfiniteAmmo;

              MyAPIGateway.Session.SessionSettings.EnableWolfs = false;
              MyAPIGateway.Session.SessionSettings.EnableSpiders = false;
              MyAPIGateway.Session.SessionSettings.MaxFactionsCount = Math.Max(MyAPIGateway.Session.SessionSettings.MaxFactionsCount, 100);
            }
            else
              Logger.Warning($"AiSession.BeforeStart: Unable to disable wolves and spiders, or adjust max factions - SessionSettings was null!");
          }
          else
            Logger.Warning($"APIGateway.Session was null in BeforeStart");

          VoxelGraphDict = new ConcurrentDictionary<ulong, VoxelGridMap>();

          MovementCostData = Config.ReadFileFromWorldStorage<MovementCostData>("MovementCosts.cfg", typeof(MovementCostData), Logger);
          if (MovementCostData == null)
          {
            MovementCostData = new MovementCostData();
          }

          MovementCostData.Update();
          Config.WriteFileToWorldStorage("MovementCosts.cfg", typeof(MovementCostData), MovementCostData, Logger);

          ModPriceData = Config.ReadFileFromWorldStorage<BotPricing>("BotPricing.cfg", typeof(BotPricing), Logger);
          if (ModPriceData == null)
          {
            ModPriceData = new BotPricing();
          }

          if (ModPriceData.BotPrices == null)
          {
            ModPriceData.BotPrices = new List<BotPrice>();

            foreach (var kvp in BotPrices)
            {
              var bType = kvp.Key;
              var credits = kvp.Value;
              var upkeep = BotUpkeepPrices[bType];

              List<SerialId> comps;
              if (!BotComponents.TryGetValue(bType, out comps))
              {
                Logger.Warning($"BeforeStart: BotComponents did not contain an entry for {bType}, initializing to empty list");
                comps = new List<SerialId>()
                {
                  new SerialId()
                };
              }
              else if (comps == null)
              {
                Logger.Warning($"BeforeStart: Component list for {bType} was null, initializing to empty list");
                comps = new List<SerialId>()
                {
                  new SerialId()
                };
              }

              var bp = new BotPrice
              {
                BotType = bType,
                SpaceCredits = credits,
                UpkeepCredits = upkeep,
                Components = new List<SerialId>(comps)
              };

              ModPriceData.BotPrices.Add(bp);
            }
          }
          else
          {
            foreach (var kvp in BotPrices)
            {
              var bType = kvp.Key;
              var upkeep = BotUpkeepPrices[bType];

              bool found = false;
              for (int i = 0; i < ModPriceData.BotPrices.Count; i++)
              {
                var modPrice = ModPriceData.BotPrices[i];
                if (modPrice.BotType == bType)
                {
                  found = true;
                  break;
                }
              }

              if (!found)
              {
                List<SerialId> comps;
                if (!BotComponents.TryGetValue(bType, out comps))
                {
                  Logger.Warning($"BeforeStart: BotComponents did not contain an entry for {bType}, initializing to empty list");
                  comps = new List<SerialId>()
                  {
                    new SerialId()
                  };
                }
                else if (comps == null)
                {
                  Logger.Warning($"BeforeStart: Component list for {bType} was null, initializing to empty list");
                  comps = new List<SerialId>()
                  {
                    new SerialId()
                  };
                }

                var priceData = new BotPrice()
                {
                  BotType = bType,
                  SpaceCredits = kvp.Value,
                  UpkeepCredits = upkeep,
                  Components = new List<SerialId>(comps)
                };

                ModPriceData.BotPrices.Add(priceData);
              }
            }

            foreach (var botPrice in ModPriceData.BotPrices)
            {
              if (botPrice?.BotType != null && BotPrices.ContainsKey(botPrice.BotType.Value))
              {
                if (botPrice.Components?.Count > 0)
                {
                  var comps = BotComponents.GetValueOrDefault(botPrice.BotType.Value, new List<SerialId>());
                  comps.Clear();

                  foreach (var c in botPrice.Components)
                  {
                    var def = c.DefinitionId;

                    if (!def.TypeId.IsNull && AllGameDefinitions.ContainsKey(def))
                    {
                      comps.Add(c);
                    }
                  }

                  BotComponents[botPrice.BotType.Value] = comps;
                }
                else
                {
                  BotComponents[botPrice.BotType.Value]?.Clear();
                }

                BotPrices[botPrice.BotType.Value] = botPrice.SpaceCredits;
                BotUpkeepPrices[botPrice.BotType.Value] = botPrice.UpkeepCredits;
              }
            }
          }

          var efficiency = MyAPIGateway.Session.AssemblerEfficiencyMultiplier;
          var fixedPoint = (MyFixedPoint)(1f / efficiency);
          var remainder = 1 - (fixedPoint * efficiency);
          var componentReqs = new Dictionary<MyDefinitionId, float>(MyDefinitionId.Comparer);

          foreach (var kvp in BotPrices)
          {
            var bType = kvp.Key;
            var subtype = $"AiEnabled_Comp_{bType}BotMaterial";
            var comp = new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype);
            var bpDef = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(comp);

            if (bpDef != null)
            {
              var items = BotComponents[bType];
              if (items.Count == 0)
                items.Add(new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"), 1));

              componentReqs.Clear();
              for (int i = 0; i < items.Count; i++)
              {
                var item = items[i];
                var amount = item.Amount;

                var compBp = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(item.DefinitionId);
                if (compBp != null)
                {
                  var compReqs = compBp.Prerequisites;
                  if (compReqs?.Length > 0)
                  {
                    for (int j = 0; j < compReqs.Length; j++)
                    {
                      var compReq = compReqs[j];

                      float num;
                      componentReqs.TryGetValue(compReq.Id, out num);
                      componentReqs[compReq.Id] = num + (float)compReq.Amount * amount;
                    }
                  }
                }
              }

              if (componentReqs.Count == 0)
                componentReqs[new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Iron")] = 100 * efficiency;

              var reqs = new MyBlueprintDefinitionBase.Item[componentReqs.Count];
              int k = 0;

              foreach (var item in componentReqs)
              {
                var req = new MyBlueprintDefinitionBase.Item
                {
                  Amount = (MyFixedPoint)item.Value,
                  Id = item.Key
                };

                req.Amount *= efficiency;

                if (remainder > 0)
                  req.Amount += req.Amount * remainder + remainder;

                reqs[k] = req;
                k++;
              }

              bpDef.Atomic = true;
              bpDef.Prerequisites = reqs;
            }
          }

          componentReqs.Clear();
          componentReqs = null;

          Config.WriteFileToWorldStorage("BotPricing.cfg", typeof(BotPricing), ModPriceData, Logger);

          ModSaveData = Config.ReadFileFromWorldStorage<SaveData>("AiEnabled.cfg", typeof(SaveData), Logger);
          if (ModSaveData == null)
          {
            ModSaveData = new SaveData()
            {
              MaxBotsInWorld = 100,
              MaxHelpersPerPlayer = 2,
              MaxBotProjectileDistance = 150,
              MaxBotHuntingDistanceEnemy = 300,
              MaxBotHuntingDistanceFriendly = 150,               
            };
          }

          if (ModSaveData.BotUpkeepTimeInMinutes <= 0)
          {
            ModSaveData.BotUpkeepTimeInMinutes = 0;
            ModSaveData.ChargePlayersForBotUpkeep = false;
          }

          if (ModSaveData.InventoryItemsToKeep == null)
          {
            ModSaveData.InventoryItemsToKeep = new List<string>()
            {
              "Medkit",
              "Powerkit",
              "EngineerShield",
              "PocketShield"
            };
          }

          if (ModSaveData.AllowedHelperSubtypes == null || ModSaveData.AllowedHelperSubtypes.Count == 0)
          {
            ModSaveData.AllowedHelperSubtypes = new List<string>()
            {
              "Police_Bot",
              "Drone_Bot",
              "Target_Dummy",
              "RoboDog",
              "Default_Astronaut",
              "Default_Astronaut_Female"
            };
          }

          if (ModSaveData.AllowedBotSubtypes == null || ModSaveData.AllowedBotSubtypes.Count == 0)
          {
            if (ModSaveData.AllowedBotSubtypes == null)
              ModSaveData.AllowedBotSubtypes = new List<string>();

            foreach (var charDef in MyDefinitionManager.Static.Characters)
            {
              if (charDef != null)
                ModSaveData.AllowedBotSubtypes.Add(charDef.Name ?? charDef.Id.SubtypeName);
            }
          }

          if (ModSaveData.AllHumanSubtypes == null)
            ModSaveData.AllHumanSubtypes = new List<string>();
          else
            ModSaveData.AllHumanSubtypes.Clear();

          if (ModSaveData.AllNonHumanSubtypes == null)
            ModSaveData.AllNonHumanSubtypes = new List<string>();
          else
            ModSaveData.AllNonHumanSubtypes.Clear();

          foreach (var charDef in MyDefinitionManager.Static.Characters)
          {
            if (charDef != null)
            {
              if (charDef.Skeleton == "Humanoid")
                ModSaveData.AllHumanSubtypes.Add(charDef.Name ?? charDef.Id.SubtypeName);
              else
                ModSaveData.AllNonHumanSubtypes.Add(charDef.Name ?? charDef.Id.SubtypeName);
            }
          }

          if (ModSaveData.AllowedBotRoles == null || ModSaveData.AllowedBotRoles.Count == 0)
          {
            if (ModSaveData.AllowedBotRoles == null)
              ModSaveData.AllowedBotRoles = new List<string>();

            var friends = Enum.GetNames(typeof(BotFactory.BotRoleFriendly));
            var enemies = Enum.GetNames(typeof(BotFactory.BotRoleEnemy));
            var neutrals = Enum.GetNames(typeof(BotFactory.BotRoleNeutral));

            ModSaveData.AllowedBotRoles.AddRange(friends);
            ModSaveData.AllowedBotRoles.AddRange(enemies);
            ModSaveData.AllowedBotRoles.AddRange(neutrals);

            if (!ModSaveData.AllowCombatBot)
              ModSaveData.AllowedBotRoles.Remove("COMBAT");

            if (!ModSaveData.AllowRepairBot)
              ModSaveData.AllowedBotRoles.Remove("REPAIR");

            if (!ModSaveData.AllowCrewBot)
              ModSaveData.AllowedBotRoles.Remove("CREW");

            if (!ModSaveData.AllowScavengerBot)
              ModSaveData.AllowedBotRoles.Remove("SCAVENGER");
          }
          else
          {
            bool combatFound = false;
            bool repairFound = false;
            bool scavFound = false;
            bool crewFound = false;

            for (int i = 0; i < ModSaveData.AllowedBotRoles.Count; i++)
            {
              var item = ModSaveData.AllowedBotRoles[i].ToUpperInvariant();

              if (item == "COMBAT")
                combatFound = true;
              else if (item == "REPAIR")
                repairFound = true;
              else if (item == "CREW")
                crewFound = true;
              else if (item == "SCAVENGER")
                scavFound = true;

              ModSaveData.AllowedBotRoles[i] = item;
            }

            if (!ModSaveData.AllowCombatBot)
              ModSaveData.AllowedBotRoles.Remove("COMBAT");
            else if (!combatFound)
              ModSaveData.AllowedBotRoles.Add("COMBAT");

            if (!ModSaveData.AllowRepairBot)
              ModSaveData.AllowedBotRoles.Remove("REPAIR");
            else if (!repairFound)
              ModSaveData.AllowedBotRoles.Add("REPAIR");

            if (!ModSaveData.AllowCrewBot)
              ModSaveData.AllowedBotRoles.Remove("CREW");
            else if (!crewFound)
              ModSaveData.AllowedBotRoles.Add("CREW");

            if (!ModSaveData.AllowScavengerBot)
              ModSaveData.AllowedBotRoles.Remove("SCAVENGER");
            else if (!scavFound)
              ModSaveData.AllowedBotRoles.Add("SCAVENGER");
          }

          var factoryDef = new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "RoboFactory");
          var factoryBlockDef = MyDefinitionManager.Static.GetCubeBlockDefinition(factoryDef);
          if (factoryBlockDef != null)
          {
            factoryBlockDef.Public = ModSaveData.MaxHelpersPerPlayer > 0 && (ModSaveData.AllowCombatBot || ModSaveData.AllowRepairBot || ModSaveData.AllowCrewBot || ModSaveData.AllowScavengerBot);
          }

          var component = new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_CombatBotMaterial");
          var compDef = MyDefinitionManager.Static.GetComponentDefinition(component);
          if (compDef != null)
          {
            compDef.Public = ModSaveData.AllowHelperTokenBuilding && ModSaveData.MaxHelpersPerPlayer > 0 && ModSaveData.AllowCombatBot;

            var compBP = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
            if (compBP != null)
              compBP.Public = compDef.Public;

            if (!compDef.Public)
              AllGameDefinitions.Remove(component);
            else if (!AllGameDefinitions.ContainsKey(component))
              AllGameDefinitions[component] = compDef;
          }

          component = new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_RepairBotMaterial");
          compDef = MyDefinitionManager.Static.GetComponentDefinition(component);
          if (compDef != null)
          {
            compDef.Public = ModSaveData.AllowHelperTokenBuilding && ModSaveData.MaxHelpersPerPlayer > 0 && ModSaveData.AllowRepairBot;

            var compBP = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
            if (compBP != null)
              compBP.Public = compDef.Public;

            if (!compDef.Public)
              AllGameDefinitions.Remove(component);
            else if (!AllGameDefinitions.ContainsKey(component))
              AllGameDefinitions[component] = compDef;
          }

          component = new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_ScavengerBotMaterial");
          compDef = MyDefinitionManager.Static.GetComponentDefinition(component);
          if (compDef != null)
          {
            compDef.Public = ModSaveData.AllowHelperTokenBuilding && ModSaveData.MaxHelpersPerPlayer > 0 && ModSaveData.AllowScavengerBot;

            var compBP = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
            if (compBP != null)
              compBP.Public = compDef.Public;

            if (!compDef.Public)
              AllGameDefinitions.Remove(component);
            else if (!AllGameDefinitions.ContainsKey(component))
              AllGameDefinitions[component] = compDef;
          }

          component = new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_CrewBotMaterial");
          compDef = MyDefinitionManager.Static.GetComponentDefinition(component);
          if (compDef != null)
          {
            compDef.Public = ModSaveData.AllowHelperTokenBuilding && ModSaveData.MaxHelpersPerPlayer > 0 && ModSaveData.AllowCrewBot;

            var compBP = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
            if (compBP != null)
              compBP.Public = compDef.Public;

            if (!compDef.Public)
              AllGameDefinitions.Remove(component);
            else if (!AllGameDefinitions.ContainsKey(component))
              AllGameDefinitions[component] = compDef;
          }

          foreach (var def in AllGameDefinitions)
          {
            ScavengerItemList.Add(def.Key);
          }

          if (ModSaveData.EnforceGroundPathingFirst)
            Logger.Warning($"EnforceGroundNodesFirst is enabled. This is a use-at-your-own-risk option that may result in lag.");

          if (ModSaveData.IncreaseNodeWeightsNearWeapons)
            Logger.Warning($"IncreaseNodeWeightsNearWeapons is enabled. This increases the time required to find a path - increase Pathfinding Timeout accordingly! This is a use-at-your-own-risk option that may result in lag.");

          ModSaveData.MaxBotHuntingDistanceEnemy = Math.Max(50, Math.Min(1000, ModSaveData.MaxBotHuntingDistanceEnemy));
          ModSaveData.MaxBotHuntingDistanceFriendly = Math.Max(50, Math.Min(1000, ModSaveData.MaxBotHuntingDistanceFriendly));
          ModSaveData.MaxBotProjectileDistance = Math.Max(50, Math.Min(500, ModSaveData.MaxBotProjectileDistance));

          if (ModSaveData.PlayerHelperData == null)
            ModSaveData.PlayerHelperData = new List<HelperData>();

          if (ModSaveData.AllowedHelperSubtypes.Count > 0)
          {
            var nameSB = new StringBuilder(32);
            for (int i = ModSaveData.AllowedHelperSubtypes.Count - 1; i >= 0; i--)
            {
              var newSubtype = ModSaveData.AllowedHelperSubtypes[i];
              if (!RobotSubtypes.Contains(newSubtype))
                ModSaveData.AllowedHelperSubtypes.RemoveAtFast(i);
              else
              {
                string nameToUse;

                if (newSubtype == "Default_Astronaut")
                  nameToUse = "Male Engineer";
                else if (newSubtype == "Default_Astronaut_Female")
                  nameToUse = "Female Engineer";
                else if (newSubtype == "RoboDog")
                  nameToUse = "Robo Dog";
                else
                {
                  nameSB.Clear();
                  foreach (var ch in newSubtype)
                  {
                    if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                      nameSB.Append(ch);
                    else if (ch == '_')
                      nameSB.Append(' ');
                  }

                  nameToUse = nameSB.ToString();
                }

                var hashId = MyStringId.GetOrCompute(nameToUse);
                if (!BotModelDict.ContainsKey(hashId))
                  BotModelDict[hashId] = newSubtype;
              }
            }

            nameSB.Clear();
          }

          BotModelList.Clear();
          foreach (var kvp in BotModelDict)
          {
            BotModelList.Add(kvp.Key);
          }

          if (!MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            foreach (var data in ModSaveData.PlayerHelperData)
            {
              for (int i = data.Helpers.Count - 1; i >= 0; i--)
              {
                var helper = data.Helpers[i];
                if (!helper.IsActiveHelper)
                  continue;

                var future = new FutureBot(helper, data.OwnerIdentityId);
                FutureBotQueue.Enqueue(future);
                data.Helpers.RemoveAtFast(i);
              }
            }
          }

          try
          {
            Logger.Log($"Attempting faction member cleanup for AiE Bots...");
            HashSet<long> factionMembers = new HashSet<long>();

            foreach (var kvp in MyAPIGateway.Session.Factions.Factions)
            {
              var faction = kvp.Value;
              var factionId = kvp.Key;

              if (!faction.AcceptHumans)
              {
                if (!faction.AutoAcceptMember)
                  MyAPIGateway.Session.Factions.ChangeAutoAccept(faction.FactionId, faction.FounderId, true, faction.AutoAcceptPeace);

                var joinRequests = faction.JoinRequests;
                if (joinRequests.Count > 0)
                {
                  factionMembers.Clear();
                  factionMembers.UnionWith(joinRequests.Keys);

                  foreach (var member in factionMembers)
                  {
                    MyFactionMember fm;
                    if (faction.Members.TryGetValue(member, out fm) && (fm.IsFounder || fm.IsLeader))
                      continue;

                    MyAPIGateway.Session.Factions.CancelJoinRequest(faction.FactionId, member);
                  }
                }

                if (faction.Members.Count > 2)
                {
                  factionMembers.Clear();
                  bool leaderOK = false;
                  foreach (var kvpMember in faction.Members)
                  {
                    if (kvpMember.Value.IsFounder)
                      continue;

                    if (!leaderOK && kvpMember.Value.IsLeader && !faction.Name.StartsWith("[AiE]"))
                    {
                      leaderOK = true;
                      continue;
                    }

                    factionMembers.Add(kvpMember.Key);
                  }

                  if (factionMembers.Count > 0)
                    Logger.Log($"Faction cleanup: Removing {factionMembers.Count}/{faction.Members.Count} members from {faction.Name}");

                  var tooMany = factionMembers.Count == faction.Members.Count;
                  int count = 0;
                  foreach (var memberId in factionMembers)
                  {
                    count++;
                    if (tooMany && count < 3)
                      continue;

                    MyAPIGateway.Session.Factions.KickMember(factionId, memberId);
                  }
                }
              }
            }

            factionMembers.Clear();
            factionMembers = null;
          }
          catch (NullReferenceException nre)
          {
            Logger.Log($"Unable to clean up faction members at this time...");
          }

          var player = MyAPIGateway.Session.Player;
          foreach (var tag in BotFactionTags)
          {
            var botFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag(tag);
            if (botFaction != null)
            {
              foreach (var faction in MyAPIGateway.Session.Factions.Factions)
              {
                if (faction.Key != botFaction.FactionId)
                {
                  var rep = MyAPIGateway.Session.Factions.GetReputationBetweenFactions(faction.Key, botFaction.FactionId);
                  if (rep != 0)
                    MyAPIGateway.Session.Factions.SetReputation(faction.Key, botFaction.FactionId, 0);

                  if (player != null)
                  {
                    rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(player.IdentityId, botFaction.FactionId);
                    if (rep != 0)
                      MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(player.IdentityId, botFaction.FactionId, 0);
                  }
                }
              }
            }
          }

          MyAPIGateway.Session.Factions.FactionCreated += Factions_FactionCreated;
          MyAPIGateway.Session.Factions.FactionEdited += Factions_FactionEdited;
          MyAPIGateway.Session.Factions.FactionStateChanged += Factions_FactionStateChanged;
          MyAPIGateway.Session.Factions.FactionAutoAcceptChanged += Factions_FactionAutoAcceptChanged;

          //Logger.AddLine($"Block Definitions:");
          foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
          {
            var cubeDef = def as MyCubeBlockDefinition;
            if (cubeDef == null || cubeDef.CubeSize != MyCubeSize.Large || _ignoreTypes.ContainsItem(cubeDef.Id.TypeId))
              continue;

            //Logger.AddLine($"{cubeDef.Id}, Airtight: {cubeDef.IsAirTight ?? false}, Base: {cubeDef.Context.IsBaseGame}");

            var blockDef = cubeDef.Id;
            var blockSubtype = blockDef.SubtypeName;
            bool isSlopedBlock = _validSlopedBlockDefs.ContainsItem(blockDef) || blockSubtype.EndsWith("HalfSlopeArmorBlock");
            bool isStairBlock = !isSlopedBlock && blockSubtype != "LargeStairs" && blockSubtype.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isGratedModBlock = cubeDef.Context.ModName == "Grated Catwalks Expansion";
            //bool isDigiLadder = blockSubtype.StartsWith("LargeShipUsableLadder");
            bool scaffoldHalfStair = false;
            if (blockSubtype.StartsWith("ven_scaffold", StringComparison.OrdinalIgnoreCase))
            {
              if (isStairBlock)
              {
                scaffoldHalfStair = true;
              }
              else if (blockSubtype.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                isStairBlock = true;
              }
              else if (!blockSubtype.EndsWith("Extension") && !blockSubtype.EndsWith("Balcony")
                && blockSubtype.IndexOf("rail", StringComparison.OrdinalIgnoreCase) < 0)
              {
                ScaffoldBlockDefinitions.Add(blockDef);
              }
            }
            else if (isGratedModBlock)
            {
              GratedCatwalkExpansionBlocks.Add(blockDef);
            }

            if (isStairBlock || isSlopedBlock)
            {
              SlopeBlockDefinitions.Add(blockDef);

              var isHalf = scaffoldHalfStair || blockSubtype.IndexOf("half", StringComparison.OrdinalIgnoreCase) >= 0;

              if (isStairBlock)
              {
                if (isHalf)
                {
                  HalfStairBlockDefinitions.Add(blockDef);

                  if (scaffoldHalfStair || blockSubtype.IndexOf("mirrored", StringComparison.OrdinalIgnoreCase) >= 0)
                  {
                    HalfStairMirroredDefinitions.Add(blockDef);
                  }
                }
                else if (isGratedModBlock && blockSubtype.StartsWith("GCMGratedStairsWithGratedSides1x2"))
                {
                  RampBlockDefinitions.Add(blockDef);
                }
              }
              else if (isHalf || blockSubtype.IndexOf("tip", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                SlopedHalfBlockDefinitions.Add(blockDef);
              }
            }
            else if (blockSubtype.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              CatwalkBlockDefinitions.Add(blockDef);

              if (isGratedModBlock && blockDef.TypeId == typeof(MyObjectBuilder_Ladder2))
                LadderBlockDefinitions.Add(blockDef);
            }
            else if (blockDef.TypeId == typeof(MyObjectBuilder_Passage)
              || blockDef.SubtypeName.IndexOf("passage", StringComparison.OrdinalIgnoreCase) >= 0
              || blockDef.SubtypeName.IndexOf("corridor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              bool isPassageIntersection = def.Context.ModName?.IndexOf("PassageIntersections", StringComparison.OrdinalIgnoreCase) >= 0;

              if (isPassageIntersection || isGratedModBlock || def.Context.IsBaseGame)
              {
                PassageBlockDefinitions.Add(blockDef);

                if (isPassageIntersection)
                  PassageIntersectionDefinitions.Add(blockDef);
              }
            }
            else if (blockSubtype == "LargeStairs" || blockSubtype.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              RampBlockDefinitions.Add(blockDef);
            }
            else if (/*isDigiLadder ||*/ blockDef.TypeId == typeof(MyObjectBuilder_Ladder2))
            {
              LadderBlockDefinitions.Add(blockDef);
            }
            else if (blockDef.TypeId == typeof(MyObjectBuilder_CubeBlock) && blockSubtype.IndexOf("panel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              if (blockSubtype.IndexOf("centerpanel", StringComparison.OrdinalIgnoreCase) < 0) // TODO: excluding the new centered panels for now, maybe add support ??
              {
                ArmorPanelAllDefinitions.Add(blockDef);

                if (!ArmorPanelFullDefinitions.ContainsItem(blockDef) && !ArmorPanelSlopeDefinitions.ContainsItem(blockDef)
                  && !ArmorPanelHalfSlopeDefinitions.ContainsItem(blockDef) && !ArmorPanelHalfDefinitions.ContainsItem(blockDef))
                {
                  ArmorPanelMiscDefinitions.Add(blockDef);
                }
              }
            }
          }

          //Logger.LogAll();

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
                  character.Delete();
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
                      pilot.Delete();
                    }
                  }
                }
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
        else
          ModSaveData = new SaveData();

        if (!MyAPIGateway.Utilities.IsDedicated)
        {
          var player = MyAPIGateway.Session?.Player;
          var playerId = player?.IdentityId ?? MyAPIGateway.Players.TryGetIdentityId(MyAPIGateway.Multiplayer.MyId);
          if (playerId > 0)
            _newPlayerIds[playerId] = player?.Character?.EntityId ?? 0L;

          MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
          PlayerData = Config.ReadFileFromLocalStorage<PlayerData>("AiEnabledPlayerConfig.cfg", typeof(PlayerData), Logger);

          if (PlayerData == null)
          {
            PlayerData = new PlayerData();
          }
          else
          {
            if (PlayerData.Keybinds == null)
              PlayerData.Keybinds = new List<Input.Support.SerializableKeybind>();

            if (PlayerData.PatrolRoutes == null)
              PlayerData.PatrolRoutes = new List<SerializableRoute>();

            if (playerId > 0)
            {
              var pkt = new AdminPacket(playerId, PlayerData.ShowHealthBars, PlayerData.RepairBotSearchRadius);
              Network.SendToServer(pkt);
            }
            else
              Logger.Warning($"Player was null in BeforeStart!");
          }

          CommandMenu = new CommandMenu();
          PlayerMenu = new PlayerMenu(PlayerData);
          HudAPI = new HudAPIv2(HudAPICallback);
        }

        // This should be set to FALSE for production
        bool assign = false;
        if (assign)
        {
          // These are only used when generating the files to be added to the mod
          BlockInfo.InitBlockInfo();
          BlockInfo.GenerateMissingBlockList(this);
          BlockInfo.SerializeToDisk();
          BlockInfo.Deserialize_Debug();

          Logger.Debug("Finished configuring Block Info");
        }
        else
        {
          // This is what the mod needs to ingest the block pathing data
          BlockInfo.DeserializeFromDisk(ModContext.ModItem);
        }

        BlockInfo.UpdateKnownBlocks();

        var blockInfo = BlockInfo.BlockDirInfo;
        var alwaysBlocked = BlockInfo.NoPathBlocks;

        if (blockInfo == null || blockInfo.Count == 0 || alwaysBlocked == null || alwaysBlocked.Count == 0)
        {
          Logger.Error("Block Info or Path collection found null or empty!");
        }

        MyEntities.OnEntityAdd += MyEntities_OnEntityAdd;
        MyEntities.OnCloseAll += MyEntities_OnCloseAll;
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
        Logger?.Error($"Exception in AiSession.BeforeStart: {ex}");
        UnloadModData();
      }
      finally
      {
        base.BeforeStart();
      }
    }

    void WeaponCoreRegistered()
    {
      WcAPILoaded = WcAPI.IsReady;
      Logger.Log($"WeaponCore Mod found. API loaded successfully = {WcAPILoaded}");

      WcAPI.GetAllCoreWeapons(AllCoreWeaponDefinitions);
      WcAPI.GetNpcSafeWeapons(NpcSafeCoreWeaponDefinitions);
      if (NpcSafeCoreWeaponDefinitions.Count == 0)
      {
        Logger.Log($" -> No NPC-Safe WeaponCore Weapons found.");
      }

      WcAPI.GetAllNpcSafeWeaponMagazines(NpcSafeCoreWeaponMagazines);
      if (NpcSafeCoreWeaponMagazines.Count == 0)
      {
        Logger.Log($" -> No NPC-Safe WeaponCore Weapon Magazines found.");
        WcAPI.GetAllWeaponMagazines(NpcSafeCoreWeaponMagazines);
      }
    }

    bool _needsUpdate, _needsAdminUpdate, _needsAdminSettingSync;
    int _updateCounter, _updateCounterAdmin, _updateCounterSettingSync;
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

    public void StartSettingSyncCounter()
    {
      if (!MyAPIGateway.Multiplayer.MultiplayerActive)
        return;

      _needsAdminSettingSync = true;
      _updateCounterSettingSync = 0;
    }

    public void UpdatePlayerConfig(bool force = false)
    {
      try
      {
        if (MyAPIGateway.Session?.Player == null)
          return;

        if (!force && (!_needsUpdate || ++_updateCounter < UpdateTime))
          return;

        if (PlayerData == null)
          PlayerData = new PlayerData();

        if (PlayerData.PatrolRoutes == null)
          PlayerData.PatrolRoutes = new List<SerializableRoute>();

        CommandMenu.GetPatrolRoutes(PlayerData.PatrolRoutes);

        Config.WriteFileToLocalStorage($"AiEnabledPlayerConfig.cfg", typeof(PlayerData), PlayerData, Logger);

        PlayerMenu.ResetKeyPresses();
        _updateCounter = 0;
        _needsUpdate = false;
      }
      catch (Exception e)
      {
        Logger?.Error($"Exception in UpdateConfig: {e}\n");
      }
    }

    public void UpdateAdminConfig(bool force = false)
    {
      if (!force && ++_updateCounterAdmin < UpdateTime)
        return;

      _updateCounterAdmin = 0;
      _needsAdminUpdate = false;

      Config.WriteFileToWorldStorage("AiEnabled.cfg", typeof(SaveData), ModSaveData, Logger);
    }

    public void UpdateAdminSettingSync(bool force = false)
    {
      if (!force && ++_updateCounterSettingSync < UpdateTime * 0.5)
        return;

      _needsAdminSettingSync = false;

      var pkt = new SettingSyncPacket(ModSaveData);
      Network.SendToServer(pkt);
    }

    private void HudAPICallback()
    {
      try
      {
        CommandMenu.Register();
        PlayerMenu.Register();
        Input = new Inputs(PlayerData.Keybinds);

        if (CommandMenu.Registered && PlayerData.PatrolRoutes.Count > 0)
          CommandMenu.AddPatrolRoutes(PlayerData.PatrolRoutes);

        var pkt = new SettingRequestPacket();
        Network.SendToServer(pkt);
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in HudAPICallback: {ex}");
      }
    }

    private void Factions_FactionAutoAcceptChanged(long factionId, bool autoAcceptMember, bool autoAcceptPeace)
    {
      try
      {
        var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
        if (faction != null && !faction.AcceptHumans && !faction.AutoAcceptMember)
        {
          MyAPIGateway.Session.Factions.ChangeAutoAccept(factionId, faction.FounderId, true, faction.AutoAcceptPeace);
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in Factions_FactionAutoAcceptChanged: {ex}");
      }
    }

    private void Factions_FactionStateChanged(MyFactionStateChange action, long fromFactionId, long toFactionId, long playerId, long senderId)
    {
      try
      {
        var fromFaction = MyAPIGateway.Session.Factions.TryGetFactionById(fromFactionId);
        var toFaction = MyAPIGateway.Session.Factions.TryGetFactionById(toFactionId);

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
      catch (Exception ex)
      {
        Logger.Error($"Exception in Factions_FactionStateChanged: {ex}");
      }
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
              Logger.Warning($"AiSession.FactionEdited: BotFactionTags found empty during faction pairing!");
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
            Logger.Warning($"Aisession.FactionEdited: Failed to add faction pair - ID: {factionId}, BotFactionTag: {botFaction.Tag}");
          else
            Logger.Log($"AiSession.FactionEdited: Human faction '{faction.Tag}' paired with bot faction '{botFaction.Tag}' successfully!");
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in Factions_FactionEdited: {ex}");
      }
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
              Logger.Warning($"AiSession.FactionCreated: BotFactionTags found empty during faction pairing!");
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
            Logger.Warning($"Aisession.FactionCreated: Failed to add faction pair - ID: {factionId}, BotFactionTag: {botFaction.Tag}");
          else
            Logger.Log($"AiSession.FactionCreated: Human faction '{faction.Tag}' paired with bot faction '{botFaction.Tag}' successfully!");
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in Factions_FactionCreated: {ex}");
      }
    }

    public void ClearBotControllers()
    {
      _controlSpawnTimer = 0;
      _checkControlTimer = true;
      _controllerSet = false;
      _controllerInfo.ClearImmediate();
      _pendingControllerInfo.ClearImmediate();
    }

    public void CheckControllerForPlayer(long playerId, long botEntId)
    {
      _newPlayerIds[playerId] = botEntId;
      Scheduler.Schedule(GetBotControllerClient);
      //MyAPIGateway.Utilities.InvokeOnGameThread(GetBotControllerClient, "AiEnabled");
    }

    public void UpdateControllerAfterResync(long oldBotId, long newBotId)
    {
      for (int i = 0; i < _controllerInfo.Count; i++)
      {
        var info = _controllerInfo[i];
        if (info.EntityId == oldBotId)
        {
          info.EntityId = newBotId;
          break;
        }
      }
    }

    public void UpdateControllerForPlayer(long playerId, long botId, long? ownerId = null)
    {
      for (int i = 0; i < _controllerInfo.Count; i++)
      {
        var info = _controllerInfo[i];
        if (info.Identity.IdentityId == playerId)
        {
          BotToControllerInfoDict[botId] = info;
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
      //Logger.Log($"PlayerSpawned: Id = {playerId}");

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

    void PlayerLeftCockpitDelayed(string entityname, long playerId, string gridName)
    {
      try
      {
        List<BotBase> playerHelpers;
        if (!Registered || !PlayerToHelperDict.TryGetValue(playerId, out playerHelpers) || playerHelpers == null || playerHelpers.Count == 0)
        {
          return;
        }

        bool recallBots = gridName.StartsWith("AiEnabled_RecallBots");
        int recallDistance = 0;

        if (recallBots)
        {
          var split = gridName.Split('.');
          if (split.Length > 1)
          {
            int num;
            if (int.TryParse(split[1], out num) && num >= 0)
              recallDistance = num;
          }
        }

        for (int i = playerHelpers.Count - 1; i >= 0; i--)
        {
          var bot = playerHelpers[i];
          var character = bot?.Character;
          if (character == null || character.IsDead)
          {
            playerHelpers.RemoveAtFast(i);
            continue;
          }

          if (recallBots)
          {
            if (recallDistance > 0 && bot.Owner?.Character != null)
            {
              var ownerPos = bot.Owner.Character.WorldAABB.Center;
              var botPos = bot.GetPosition();

              if (Vector3D.DistanceSquared(ownerPos, botPos) > recallDistance * recallDistance)
                continue;
            }

            bot.PatrolMode = false;
            bot.FollowMode = false;
            bot.UseAPITargets = false;

            if (bot.Target != null)
            {
              bot.Target.RemoveOverride(false);
              bot.Target.RemoveTarget();
            }
          }
          else if (bot.UseAPITargets)
            continue;

          var seat = character.Parent as IMyCockpit;
          if (seat != null)
          {
            seat.RemovePilot();

            MyOwnershipShareModeEnum shareMode;
            if (BotToSeatShareMode.TryRemove(character.EntityId, out shareMode))
            {
              var seatCube = seat as MyCubeBlock;
              if (seatCube?.IDModule != null)
                seatCube.IDModule.ShareMode = shareMode;
            }

            var jetpack = character.Components?.Get<MyCharacterJetpackComponent>();
            if (jetpack != null && !jetpack.TurnedOn)
            {
              if (bot.RequiresJetpack)
              {
                var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
                MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
                jetpack.TurnOnJetpack(true);
                MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
              }
              else if (bot.CanUseAirNodes && MyAPIGateway.Session.SessionSettings.EnableJetpack)
              {
                jetpack.TurnOnJetpack(true);
              }
            }

            bot.Target.RemoveTarget();
            bot.CleanPath();

            if (!bot.UseAPITargets)
            {
              bot.SetTarget();
              bot.Target.Update();
            }

            Vector3D actualPosition = bot.Target.CurrentActualPosition;
            bot.StartCheckGraph(ref actualPosition, true);

            var botMatrix = bot.WorldMatrix;

            Vector3D relPosition;
            BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out relPosition);
            var rotatedPosition = Vector3D.Rotate(relPosition, seat.WorldMatrix) + botMatrix.Down;
            var testPosition = seat.GetPosition() + rotatedPosition;
            Vector3D? goodPosition = null;

            MyVoxelBase voxel;
            var up = botMatrix.Up;
            if (GridBase.GetClosestPointAboveGround(ref testPosition, ref up, out voxel))
            {
              if (!bot.CanUseAirNodes && voxel != null)
              {
                bool onGround;
                var surfacePoint = GridBase.GetClosestSurfacePointFast(testPosition, up, voxel, out onGround);

                if (Vector3D.DistanceSquared(surfacePoint, testPosition) < 250)
                  goodPosition = testPosition;
              }
              else
              {
                goodPosition = testPosition;
              }
            }

            if (!goodPosition.HasValue)
            {
              testPosition = seat.GetPosition() - rotatedPosition;
              if (GridBase.GetClosestPointAboveGround(ref testPosition, ref up, out voxel))
              {
                if (!bot.CanUseAirNodes && voxel != null)
                {
                  bool onGround;
                  var surfacePoint = GridBase.GetClosestSurfacePointFast(testPosition, up, voxel, out onGround);

                  if (Vector3D.DistanceSquared(surfacePoint, testPosition) < 250)
                    goodPosition = testPosition;
                }
                else
                {
                  goodPosition = testPosition;
                }
              }
            }

            Vector3D position;
            if (goodPosition.HasValue)
            {
              position = goodPosition.Value;
            }
            else
            {
              position = seat.GetPosition() + seat.WorldMatrix.Up;
              relPosition = seat.WorldMatrix.Up;
            }

            var voxelGraph = bot._currentGraph as VoxelGridMap;
            var gridGraph = bot._currentGraph as CubeGridMap;
            MatrixD? newMatrix = null;

            if (voxelGraph != null)
            {
              if (relPosition.LengthSquared() < 2)
              {
                position += Vector3D.CalculatePerpendicularVector(up) * (seat.CubeGrid.WorldAABB.HalfExtents.AbsMax() + 5);

                while (GridBase.PointInsideVoxel(position, voxel))
                {
                  position += up * 2;
                }
              }

              if (botMatrix.Up.Dot(up) < 0)
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
                position = gridGraph.LocalToWorld(openNode);

                while (GridBase.PointInsideVoxel(position, voxel))
                {
                  position += up * 2;
                }
              }
              else
              {
                Logger.Warning($"{GetType().FullName}: Unable to find valid position upon exit from seat! Grid = {gridGraph.MainGrid?.DisplayName ?? "NULL"}");
              }
            }
            else if (relPosition.LengthSquared() < 2)
            {
              position += Vector3D.CalculatePerpendicularVector(up) * (seat.CubeGrid.WorldAABB.HalfExtents.AbsMax() + 5);

              while (GridBase.PointInsideVoxel(position, voxel))
              {
                position += up * 2;
              }
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
      catch (Exception ex)
      {
        Logger.Error($"Exception in PlayerLeftCockpitDelayed: {ex}");
      }
    }

    internal void PlayerLeftCockpit(string entityName, long playerId, string gridName)
    {
      try
      {
        List<BotBase> playerHelpers;
        if (!Registered || !PlayerToHelperDict.TryGetValue(playerId, out playerHelpers) || playerHelpers == null || playerHelpers.Count == 0)
        {
          return;
        }

        Scheduler.Schedule(() => PlayerLeftCockpitDelayed(entityName, playerId, gridName), 10);
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in PlayerLeftCockpit: {ex}" );
      }
    }

    void PlayerEnteredCockpitDelayed(string entityname, long playerId, string gridName)
    {
      try
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

        List<IMyCubeGrid> gridGroup = GridGroupListPool.Get();
        List<IMySlimBlock> gridSeats = SlimListPool.Get();

        playerGrid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(gridGroup);

        foreach (var grid in gridGroup)
        {
          if (grid == null || grid.MarkedForClose)
            continue;

          grid.GetBlocks(gridSeats, b =>
          {
            var seat = b.FatBlock as IMyCockpit;
            var seatDef = seat?.BlockDefinition.SubtypeName;

            if (seat == null || seat.Pilot != null || !seat.IsFunctional
              || seatDef.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0
              || seatDef.IndexOf("toilet", StringComparison.OrdinalIgnoreCase) >= 0
              || seatDef.IndexOf("bathroom", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              return false;
            }

            return true;
          });
        }

        GridGroupListPool?.Return(ref gridGroup);

        if (gridSeats.Count > 0)
        {
          var ownerPos = player.Character.WorldAABB.Center;

          for (int i = playerHelpers.Count - 1; i >= 0; i--)
          {
            var bot = playerHelpers[i];
            if (bot?.Character == null || bot.Character.IsDead)
            {
              playerHelpers.RemoveAtFast(i);
              continue;
            }

            var botPosition = bot.BotInfo.CurrentBotPositionActual;

            if (!bot.CanUseSeats || Vector3D.DistanceSquared(ownerPos, botPosition) > 10000)
              continue;

            if (gridSeats.Count == 0)
              break;

            if (bot.UseAPITargets || bot.PatrolMode || bot.Character.Parent is IMyCockpit || (bot is CrewBot && !bot.FollowMode))
              continue;

            if (bot.BotInfo.IsOnLadder)
              bot.Character.Use();

            gridSeats.ShellSort(botPosition, reverse: true);

            for (int j = gridSeats.Count - 1; j >= 0; j--)
            {
              var seat = gridSeats[j]?.FatBlock as IMyCockpit;
              if (seat == null || seat.Pilot != null || !seat.IsFunctional)
              {
                gridSeats.RemoveAtFast(j);
                continue;
              }

              var seatPosition = seat.GetPosition();
              CubeGridMap gridGraph;
              if (seat.CubeGrid.GridSize > 1 && GridGraphDict.TryGetValue(seat.CubeGrid.EntityId, out gridGraph) && gridGraph != null)
              {
                if (!gridGraph.TempBlockedNodes.ContainsKey(seat.Position))
                {
                  gridSeats.RemoveAtFast(j);
                  var seatPos = seatPosition;
                  bot.Target.SetOverride(seatPos);
                  break;
                }
              }

              var relativePosition = Vector3D.Rotate(botPosition - seatPosition, MatrixD.Transpose(seat.PositionComp.WorldMatrixRef));
              BotToSeatRelativePosition[bot.Character.EntityId] = relativePosition;

              var cpit = seat as MyCockpit;
              if (cpit != null)
              {
                var seatCube = seat as MyCubeBlock;
                var shareMode = seatCube.IDModule?.ShareMode ?? MyOwnershipShareModeEnum.All;
                bool changeBack = false;

                if (shareMode != MyOwnershipShareModeEnum.All)
                {
                  var owner = bot.Owner?.IdentityId ?? bot.BotIdentityId;
                  long gridOwner;
                  try
                  {
                    // because sometimes even though you check that there are owners, there are not (when used in a thread)

                    gridOwner = seat.CubeGrid.BigOwners?.Count > 0 ? seat.CubeGrid.BigOwners[0] : seat.CubeGrid.SmallOwners?.Count > 0 ? seat.CubeGrid.SmallOwners[0] : seat.OwnerId;
                  }
                  catch
                  {
                    gridOwner = seat.OwnerId;
                  }

                  var relation = MyIDModule.GetRelationPlayerPlayer(owner, gridOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
                  if (relation != MyRelationsBetweenPlayers.Enemies)
                  {
                    changeBack = true;
                    seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
                  }
                }

                if (seatCube.IDModule == null || seatCube.IDModule.ShareMode == MyOwnershipShareModeEnum.All)
                {
                  seat.AttachPilot(bot.Character);
                }

                if (changeBack)
                {
                  BotToSeatShareMode[bot.Character.EntityId] = shareMode;
                }

                bot.CleanPath();
                bot.Target?.RemoveTarget();

                gridSeats.RemoveAtFast(j);
                break;
              }
            }
          }
        }

        SlimListPool?.Return(ref gridSeats);
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in PlayerEnteredCockpitDelayed: {ex}");
      }
    }

    internal void PlayerEnteredCockpit(string entityName, long playerId, string gridName)
    {
      try
      {
        IMyPlayer player;
        if (!Players.TryGetValue(playerId, out player) || player?.Character == null || player.Character.IsDead)
        {
          return;
        }

        Scheduler.Schedule(() => PlayerEnteredCockpitDelayed(entityName, playerId, gridName), 10);
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in PlayerEnteredCockpit: {ex}");
      }
    }

    private void BeforeDamageHandler(object target, ref MyDamageInformation info)
    {
      try
      {
        if (!Registered || !IsServer || info.IsDeformation || info.AttackerId == 0 || info.Amount == 0)
          return;

        var slim = target as IMySlimBlock;
        if (slim != null && info.Type == MyDamageType.Grind)
        {
          var grinder = MyEntities.GetEntityById(info.AttackerId) as IMyAngleGrinder;

          if (grinder != null)
          {
            if (!Bots.ContainsKey(grinder.OwnerId))
            {
              if (!slim.IsDestroyed && slim.CubeGrid != null && !slim.CubeGrid.MarkedForClose)
                CheckGrindTarget(slim, grinder.OwnerId);
            }
            else if (slim.FatBlock is IMyDoor)
            {
              info.Amount = 0.005f;
            }
          }

          return;
        }

        var character = target as IMyCharacter;
        var charNull = character == null;
        if (charNull || character.IsDead || character.MarkedForClose)
        {
          return;
        }

        var checkFriendlyFire = !MyAPIGateway.Utilities.IsDedicated || !MyAPIGateway.Session.SessionSettings.EnableFriendlyFire;

        BotBase bot;
        bool targetIsBot = false;
        bool targetIsPlayer = false;
        bool targetisWildLife = false;
        if (Bots.TryGetValue(character.EntityId, out bot))
        {
          // bots may not affected by water or space 
          if ((info.Type == MyDamageType.Asphyxia && ModSaveData.DisableAsphyxiaDamageForBots)
            || (info.Type == MyDamageType.Temperature && ModSaveData.DisableTemperatureDamageForBots)
            || (info.Type == MyDamageType.LowPressure && ModSaveData.DisableLowPressureDamageForBots))
          {
            info.Amount = 0;
            return;
          }

          if (bot == null || bot.IsDead)
          {
            return;
          }
          else
          {
            targetIsBot = true;

            if (bot.Owner != null && ModSaveData.DisableEnvironmentDamageForHelpers && info.Type == MyDamageType.Environment)
            {
              info.Amount = 0;
              return;
            }

            if (bot.Behavior?.PainSounds?.Count > 0)
              bot.Behavior.ApplyPain();
          }
        }
        else if (Players.ContainsKey(character.ControllerInfo.ControllingIdentityId))
        {
          targetIsPlayer = true;
        }
        else // not a bot, not a player, must be wildlife
        {
          targetisWildLife = true;
        }

        long ownerId = -1, ownerIdentityId = 0;
        float damageAmount;

        if (targetIsBot && checkFriendlyFire)
        {
          var ent = MyEntities.GetEntityById(info.AttackerId, true);
          var grid = ent as MyCubeGrid;
          if (grid == null)
          {
            var block = ent as MyCubeBlock;
            grid = block?.CubeGrid;
          }

          if (grid != null)
          {
            ownerId = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : 0L;

            ValidateDamageForTarget(bot, ownerId, ref info);

            if (info.Amount == 0)
              return;
          }
        }

        switch (info.Type.String)
        {
          case "Asphyxia":
            if (targetIsBot || targetisWildLife)
            {
              if (ModSaveData.DisableAsphyxiaDamageForBots)
              {
                info.Amount = 0;
                return;
              }
              else
              {
                damageAmount = Math.Min(info.Amount, 10);
              }
            }
            else
            {
              return;
            }
            break;
          case "Temperature":
            if (targetIsBot || targetisWildLife)
            {
              if (ModSaveData.DisableTemperatureDamageForBots)
              {
                info.Amount = 0;
                return;
              }
              else
              {
                damageAmount = Math.Min(info.Amount, 10);
              }
            }
            else
            {
              return;
            }
            break;
          case "LowPressure":
            if (targetIsBot || targetisWildLife)
            {
              if (ModSaveData.DisableLowPressureDamageForBots)
              {
                info.Amount = 0;
                return;
              }
              else
              {
                damageAmount = Math.Min(info.Amount, 10);
              }
            }
            else
            {
              return;
            }
            break;
          case "Environment":
            ownerId = info.AttackerId;

            var ownerEnt = MyEntities.GetEntityById(ownerId, true);
            if (ownerEnt?.DefinitionId != null && ownerEnt.DefinitionId.Value.TypeId == typeof(MyObjectBuilder_Missile))
            {
              var ob = ownerEnt.GetObjectBuilder() as MyObjectBuilder_Missile;
              if (ob != null)
                ownerId = ob.Owner;
            }

            if (Bots.ContainsKey(ownerId))
            {
              damageAmount = info.Amount * 0.0001f;

              if (targetIsPlayer)
                damageAmount *= ModSaveData.BotWeaponDamageModifier;
            }
            else if (targetIsBot && (character.Definition.Id.SubtypeName == "Default_Astronaut" || character.Definition.Id.SubtypeName == "Default_Astronaut_Female"))
            {
              damageAmount = info.Amount * 0.001f;
            }
            else
            {
              damageAmount = info.Amount;
            }

            break;
          case "Rocket":
          case "Explosion":
            ownerId = info.AttackerId;

            if (Bots.ContainsKey(ownerId))
            {
              damageAmount = info.Amount * 0.04f;

              if (targetIsPlayer)
                damageAmount *= ModSaveData.BotWeaponDamageModifier;
            }
            else if (targetIsBot && (character.Definition.Id.SubtypeName == "Default_Astronaut" || character.Definition.Id.SubtypeName == "Default_Astronaut_Female"))
            {
              var ajustedAmount = info.Amount * 0.4f;
              damageAmount = Math.Max(ajustedAmount, Math.Min(1.5f * ModSaveData.BotWeaponDamageModifier, info.Amount));
            }
            else
            {
              damageAmount = info.Amount;
            }

            var entity = MyEntities.GetEntityById(ownerId, true) as IMyCharacter;
            if (entity != null)
            {
              BotBase botEntity;
              if (Bots.TryGetValue(entity.EntityId, out botEntity) && botEntity != null)
              {
                ownerIdentityId = botEntity.BotIdentityId;
              }
              else if (entity.IsPlayer && entity.Parent is IMyShipController)
              {
                var player = MyAPIGateway.Players.GetPlayerControllingEntity(entity.Parent);
                ownerIdentityId = player?.IdentityId ?? entity.ControllerInfo.ControllingIdentityId;
              }
              else
              {
                ownerIdentityId = entity.ControllerInfo.ControllingIdentityId;
              }
            }

            if (entity != null && Players.ContainsKey(ownerIdentityId) && targetIsBot)
              damageAmount *= ModSaveData.PlayerWeaponDamageModifier;

            break;
          case "Grind":
            var grinder = MyEntities.GetEntityById(info.AttackerId) as IMyAngleGrinder;
            ownerId = grinder?.OwnerId ?? info.AttackerId;

            var ch = MyEntities.GetEntityById(ownerId) as IMyCharacter;
            if (ch != null)
            {
              BotBase botEntity;
              if (Bots.TryGetValue(ch.EntityId, out botEntity) && botEntity != null)
              {
                ownerIdentityId = botEntity.BotIdentityId;
              }
              else if (ch.IsPlayer && ch.Parent is IMyShipController)
              {
                var player = MyAPIGateway.Players.GetPlayerControllingEntity(ch.Parent);
                ownerIdentityId = player?.IdentityId ?? ch.ControllerInfo.ControllingIdentityId;
              }
              else
              {
                ownerIdentityId = ch.ControllerInfo.ControllingIdentityId;
              }
            }

            damageAmount = 0.2f;

            if (ch != null && Players.ContainsKey(ownerIdentityId))
            {
              damageAmount = info.Amount;

              if (targetIsBot)
                damageAmount *= ModSaveData.PlayerWeaponDamageModifier;
            }
            else if (Bots.ContainsKey(ownerId))
            {
              if (targetIsBot)
              {
                SetNeutralHostility(character.EntityId, ownerId);
              }

              info.Amount = 0;
              return;
            }
            else if (targetisWildLife)
            {
              damageAmount = info.Amount;
            }

            break;
          default:
            var ent = MyEntities.GetEntityById(info.AttackerId);

            var turret = ent as IMyLargeTurretBase;
            if (turret != null)
            {
              if (targetIsBot)
              {
                var modifier = turret is IMyLargeInteriorTurret ? 0.5f : 0.1f;
                var amount = info.Amount * modifier;
                info.Amount = 0;
                DamageCharacter(turret.EntityId, character, info.Type, amount, targetisWildLife);
              }

              return;
            }

            var attackerChar = ent as IMyCharacter;
            if (attackerChar != null)
            {
              var isPlayerAttacker = Players.ContainsKey(attackerChar.ControllerInfo.ControllingIdentityId);
              if (targetIsBot)
              {
                var subtype = attackerChar.Definition.Id.SubtypeName;
                if (subtype.StartsWith("space_spider", StringComparison.OrdinalIgnoreCase) || subtype.StartsWith("space_wolf", StringComparison.OrdinalIgnoreCase))
                {
                  info.Amount = 0;
                  damageAmount = 1.5f;

                  if (isPlayerAttacker)
                    damageAmount *= ModSaveData.PlayerWeaponDamageModifier;

                  DamageCharacter(info.AttackerId, character, info.Type, damageAmount);
                }

                SetNeutralHostility(character.EntityId, attackerChar.EntityId);
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
            var ownerIsPlayer = Players.ContainsKey(ownerIdentityId);

            if (targetIsBot)
            {
              if (ownerIsPlayer)
              {
                var subtype = character.Definition.Id.SubtypeName;
                if (subtype.StartsWith("Default_Astronaut", StringComparison.OrdinalIgnoreCase))
                  damageAmount = Math.Max(10f, Math.Min(info.Amount, 1.5f * ModSaveData.BotWeaponDamageModifier));
                else
                  damageAmount = info.Amount;
              }
              else
              {
                damageAmount = 10f;
              }
            }
            else if (targetisWildLife)
            {
              damageAmount = info.Amount;
            }
            else
            {
              damageAmount = 1.5f;
            }

            if (targetIsBot && ownerIsPlayer)
            {
              damageAmount *= ModSaveData.PlayerWeaponDamageModifier;
            }
            else if (targetIsPlayer && Bots.ContainsKey(ownerId))
            {
              damageAmount *= ModSaveData.BotWeaponDamageModifier;
              damageAmount = Math.Max(damageAmount, Math.Min(info.Amount, 1.5f * ModSaveData.BotWeaponDamageModifier));
            }

            break;
        }

        if (targetIsBot && ownerId > 0)
        {
          SetNeutralHostility(character.EntityId, ownerId);
        }

        if (!Bots.ContainsKey(ownerId))
        {
          if ((targetIsBot || targetisWildLife) && Players.ContainsKey(ownerIdentityId))
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

            info.Amount = 0;
            DamageCharacter(-1L, character, info.Type, damageAmount, targetisWildLife);

            //var subtype = character.Definition.Id.SubtypeName;
            //if (subtype == "Default_Astronaut" || subtype == "Default_Astronaut_Female")
            //{
            //  info.Amount = 0;
            //  DamageCharacter(-1L, character, info.Type, damageAmount, targetisWildLife);
            //}
          }

          return;
        }

        var owner = MyEntities.GetEntityById(ownerId) as IMyCharacter;
        if (owner != null)
        {
          info.Amount = 0;
          DamageCharacter(owner.EntityId, character, info.Type, damageAmount, targetisWildLife);
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in BeforeDamageHandler: Target = {target?.GetType().FullName ?? "NULL"}, Info = {info.Type}x{info.Amount} by {info.AttackerId}\nExeption: {ex}");
      }
    }

    void ValidateDamageForTarget(BotBase bot, long attackerId, ref MyDamageInformation info)
    {
      var botOwner = bot.Owner?.IdentityId ?? bot.BotIdentityId;
      var relation = MyIDModule.GetRelationPlayerPlayer(botOwner, attackerId);
      if (relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self)
        info.Amount = 0;
    }

    void CheckGrindTarget(IMySlimBlock block, long attackerId)
    {
      var entity = MyEntities.GetEntityById(attackerId) as IMyCharacter;
      if (entity != null)
      {
        List<BotBase> helpers;
        var player = MyAPIGateway.Players.GetPlayerControllingEntity(entity);
        if (player != null && PlayerToHelperDict.TryGetValue(player.IdentityId, out helpers) && helpers?.Count > 0)
        {
          BlockRepairDelays.AddDelay(block.CubeGrid.EntityId, block.Position);
        }
      }
    }

    void SetNeutralHostility(long botId, long attackerId)
    {
      BotBase bot;
      if (Bots.TryGetValue(botId, out bot))
      {
        var neutralBot = bot as NeutralBotBase;
        if (neutralBot != null && !neutralBot.Target.HasTarget)
        {
          neutralBot.SetHostile(attackerId);
        }
      }
    }

    public void DamageCharacter(long shooterEntityId, IMyCharacter target, MyStringHash damageType, float damageAmount, bool isWildLife = false)
    {
      BotBase bot;
      if (Bots.TryGetValue(shooterEntityId, out bot) && bot != null)
      {
        //damageAmount *= bot.DamageModifier;

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
      //Logger.Log($"PlayerConnected: Id = {playerId}");
      _newPlayerIds[playerId] = 0L;
    }

    private void PlayerDisconnected(long playerId)
    {
      try
      {
        //Logger.Log($"PlayerDisconnected: Id = {playerId}");
        if (!MyAPIGateway.Multiplayer.MultiplayerActive)
          return;

        IMyPlayer player;
        List<BotBase> botList;
        if (Players.TryGetValue(playerId, out player))
        {
          IMyFaction botFaction;
          var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
          if (faction != null && BotFactions.TryGetValue(faction.FactionId, out botFaction))
          {
            bool atLeastOne = false;
            foreach (var fm in faction.Members)
            {
              if (fm.Key != player.IdentityId && Players.ContainsKey(fm.Key))
              {
                atLeastOne = true;
                break;
              }
            }

            if (!atLeastOne)
            {
              foreach (var kvp in MyAPIGateway.Session.Factions.Factions)
              {
                var rep = MyAPIGateway.Session.Factions.GetReputationBetweenFactions(kvp.Key, botFaction.FactionId);
                if (rep != 0)
                  MyAPIGateway.Session.Factions.SetReputation(kvp.Key, botFaction.FactionId, 0);
              }

              foreach (var p in Players)
              {
                MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(p.Key, botFaction.FactionId, 0);
              }
            }
          }

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
                  var botChar = bot.Character;
                  var id = botChar.EntityId;
                  bool botFound = false;

                  for (int k = helperList.Count - 1; k >= 0; k--)
                  {
                    var helper = helperList[k];
                    if (helper.HelperId == id)
                    {
                      var matrix = bot.WorldMatrix;
                      helper.Orientation = Quaternion.CreateFromRotationMatrix(matrix);
                      helper.Position = matrix.Translation;
                      helper.ToolPhysicalItem = bot.ToolDefinition?.PhysicalItemId ?? (botChar.EquippedTool as IMyHandheldGunObject<MyDeviceBase>)?.PhysicalItemDefinition?.Id;
                      helper.InventoryItems?.Clear();
                      helper.RemainInPlace = bot.UseAPITargets;

                      var gridGraph = bot._currentGraph as CubeGridMap;
                      var grid = gridGraph?.MainGrid ?? null;
                      helper.GridEntityId = grid?.EntityId ?? 0L;

                      if (bot.Character?.Parent is IMyCockpit)
                        helper.SeatEntityId = bot.Character.Parent.EntityId;
                      else
                        helper.SeatEntityId = 0L;

                      var inventory = botChar.GetInventory() as MyInventory;
                      if (inventory?.ItemCount > 0)
                      {
                        if (helper.InventoryItems == null)
                          helper.InventoryItems = new List<InventoryItem>();

                        var items = inventory.GetItems();
                        for (int ll = 0; ll < items.Count; ll++)
                        {
                          var item = items[ll];
                          helper.InventoryItems.Add(new InventoryItem(item.Content.GetId(), item.Amount));
                        }
                      }

                      botFound = true;
                      break;
                    }
                  }

                  if (!botFound)
                  {
                    var gridGraph = bot._currentGraph as CubeGridMap;
                    var grid = gridGraph?.MainGrid ?? null;
                    var botType = bot.BotType;

                    CrewBot.CrewType? crewType = null;
                    if (botType == BotType.Crew)
                    {
                      var crewBot = bot as CrewBot;
                      crewType = crewBot?.CrewFunction;
                    }

                    var damageOnly = bot.TargetPriorities?.DamageToDisable ?? false;
                    var weldFirst = bot.RepairPriorities?.WeldBeforeGrind ?? true;
                    var priList = bot is RepairBot ? bot.RepairPriorities?.PriorityTypes : bot.TargetPriorities?.PriorityTypes;
                    var ignList = bot.RepairPriorities?.IgnoreList;
                    playerData.AddHelper(bot.Character, botType, priList, ignList, damageOnly, weldFirst, grid, bot._patrolList, crewType, false, bot._patrolName);
                  }

                  bot.Close();
                }

                break;
              }
            }

            if (!playerFound)
            {
              var playerData = new HelperData(player, null, null);
              foreach (var bot in botList)
              {
                var gridGraph = bot._currentGraph as CubeGridMap;
                var grid = gridGraph?.MainGrid ?? null;
                var botType = bot.BotType;

                CrewBot.CrewType? crewType = null;
                if (botType == BotType.Crew)
                {
                  var crewBot = bot as CrewBot;
                  crewType = crewBot?.CrewFunction;
                }

                var damageOnly = bot.TargetPriorities?.DamageToDisable ?? false;
                var weldFirst = bot.RepairPriorities?.WeldBeforeGrind ?? true;
                var priList = bot is RepairBot ? bot.RepairPriorities?.PriorityTypes : bot.TargetPriorities?.PriorityTypes;
                var ignList = bot.RepairPriorities?.IgnoreList;
                playerData.AddHelper(bot.Character, botType, priList, ignList, damageOnly, weldFirst, grid, bot._patrolList, crewType, false, bot._patrolName);
                bot.Close();
              }

              ModSaveData.PlayerHelperData.Add(playerData);
            }

            SaveModData(true);
            botList.Clear();
          }
        }

        IMyPlayer _;
        List<long> __;
        Players.TryRemove(playerId, out _);
        PlayerToHelperDict.TryRemove(playerId, out botList);
        PlayerToHelperIdentity.TryRemove(playerId, out __);
      }
      catch(Exception ex)
      {
        Logger.Error($"Exception in AiSession.PlayerDisconnected: {ex}");
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
          Projectiles.Add(bot, tgt, info);
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

      //var soundComp = bot.Components?.Get<MyCharacterSoundComponent>();

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

        //soundComp?.PlayActionSound(sp);
        PlaySoundAtPosition(bot.WorldAABB.Center, sp, false);

        if (includeIcon && !_botSpeakers.ContainsKey(entityId))
        {
          var info = GetIconInfo();
          info.Set(bot, 120);
          if (!_botSpeakers.TryAdd(entityId, info))
            ReturnIconInfo(info);
        }
      }
    }

    public void PlaySoundAtPosition(Vector3D position, MySoundPair soundPair, bool stop)
    {
      if (PlayerData != null && PlayerData.BotVolumeModifier == 0)
        return;

      var emitter = GetEmitter();
      emitter.SetPosition(position);

      var volMulti = emitter.VolumeMultiplier;
      emitter.VolumeMultiplier = PlayerData?.BotVolumeModifier ?? volMulti;
      emitter.PlaySoundWithDistance(soundPair.SoundId);
      //emitter.PlaySound(soundPair);

      emitter.VolumeMultiplier = volMulti;
      ReturnEmitter(emitter);
    }

    public void PlaySoundAtPosition(Vector3D position, string sound, bool stop)
    {
      if (PlayerData != null && PlayerData.BotVolumeModifier == 0)
        return;

      var emitter = GetEmitter();
      emitter.SetPosition(position);

      MySoundPair soundPair;
      if (!SoundPairDict.TryGetValue(sound, out soundPair))
      {
        soundPair = new MySoundPair(sound);
        SoundPairDict[sound] = soundPair;
      }

      var volMulti = emitter.VolumeMultiplier;
      emitter.VolumeMultiplier = PlayerData?.BotVolumeModifier ?? volMulti;
      emitter.PlaySoundWithDistance(soundPair.SoundId);
      //emitter.PlaySound(soundPair);

      emitter.VolumeMultiplier = volMulti;
      ReturnEmitter(emitter);
    }

    void PlaySoundForEntity(MyEntity entity, string sound, bool stop)
    {
      if (PlayerData != null && PlayerData.BotVolumeModifier == 0)
        return;

      var emitter = GetEmitter();
      emitter.SetPosition(entity.PositionComp.WorldAABB.Center);

      MySoundPair soundPair;
      if (!SoundPairDict.TryGetValue(sound, out soundPair))
      {
        soundPair = new MySoundPair(sound);
        SoundPairDict[sound] = soundPair;
      }

      var volMulti = emitter.VolumeMultiplier;
      emitter.VolumeMultiplier = PlayerData?.BotVolumeModifier ?? volMulti;
      emitter.PlaySoundWithDistance(soundPair.SoundId);
      //emitter.PlaySound(soundPair);

      emitter.VolumeMultiplier = volMulti;
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

        //var grid = obj as MyCubeGrid;
        //if (grid != null)
        //  Logger.Log($"Grid being removed: Name = {grid.DisplayName}, {grid.DebugName}, Id = {grid.EntityId}");

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
              Logger.Warning($"AiSession.OnEntityRemove: Control bot removed, but did not find its control info in the dictionary!");
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
        Logger.Error($"Exception in AiSession.OnEntityRemove: {ex}");
      }
    }

    StringBuilder _nameSB = new StringBuilder(128);
    string[] _nameArray = new string[3];

    bool SplitGridName(string name)
    {
      _nameSB.Clear();
      name = name.Trim();

      int idx = -1;

      for (int i = 0; i < name.Length; i++)
      {
        var ch = name[i];

        bool isSpace = char.IsWhiteSpace(ch);

        if (!isSpace)
          _nameSB.Append(ch);

        if (isSpace || i == name.Length - 1)
        {
          idx++;
          if (idx > 2)
            return false;

          _nameArray[idx] = _nameSB.ToString();
          _nameSB.Clear();
        }
      }

      return idx == 2;
    }

    private void MyEntities_OnEntityAdd(MyEntity obj)
    {
      try
      {
        var grid = obj?.GetTopMostParent() as MyCubeGrid;
        if (grid != null)
        {
          if (SplitGridName(grid.DisplayName))
          {
            var tag = _nameArray[0];
            var idStr = _nameArray[2];

            long id;
            if (long.TryParse(idStr, out id))
            {
              var faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(tag);
              if (faction != null && !faction.AcceptHumans)
                _prefabsToCheck.Enqueue(MyTuple.Create(grid.EntityId, grid.DisplayName, MyAPIGateway.Session.GameplayFrameCounter));
            }
          }

          return;
        }

        var character = obj as IMyCharacter;
        if (character?.Definition == null || !string.IsNullOrWhiteSpace(character.DisplayName) || !RobotSubtypes.Contains(character.Definition.Id.SubtypeName))
          return;

        if (!IsServer && MyAPIGateway.Session.Player != null)
        {
          List<long> helperIds;
          var player = MyAPIGateway.Session.Player;
          if (!PlayerToHelperIdentity.TryGetValue(player.IdentityId, out helperIds) || helperIds == null)
          {
            helperIds = new List<long>();
            PlayerToHelperIdentity[player.IdentityId] = helperIds;
          }
          else if (helperIds.Contains(character.EntityId))
          {
            PendingBotRespawns.Remove(character.Name);
          }
        }

        if (Bots.ContainsKey(character.EntityId))
        {
          if (!MyAPIGateway.Utilities.IsDedicated)
          {
            ControlInfo info;
            if (BotToControllerInfoDict.TryGetValue(character.EntityId, out info) && info != null)
            {
              info.Controller.TakeControl(character);
            }
          }

          return;
        }

        if (!_isControlBot && !MyAPIGateway.Utilities.IsDedicated) // IsClient)
        {
          if (character.ControllerInfo?.Controller == null && _controllerInfo.Count > 0)
          {
            bool found = false;

            for (int i = 0; i < _controllerInfo.Count; i++)
            {
              var info = _controllerInfo[i];
              if (info.EntityId == character.EntityId)
              {
                _controllerInfo.RemoveAtImmediately(i);
                _controllerInfo.ApplyChanges();

                info.Controller.TakeControl(character);

                BotToControllerInfoDict[character.EntityId] = info;
                found = true;
                break;
              }
            }

            if (!found && !IsServer)
            {
              ControlInfo info;
              if (BotToControllerInfoDict.TryGetValue(character.EntityId, out info) && info != null)
              {
                found = true;
                info.Controller.TakeControl(character);
              }
            }
          }

          if (character.Definition.Id.SubtypeName == "Drone_Bot")
          {
            var jetpack = character.Components?.Get<MyCharacterJetpackComponent>();
            if (jetpack == null)
            {
              Logger.Warning($"AiSession.OnEntityAdded: Drone_Bot was added with null jetpack component!");
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

        if (_robots.Count >= ModSaveData.MaxBotsInWorld)
        {
          character.Delete();
        }
        else
        {
          character.OnClose += MyEntities_OnEntityRemove;
        }
      }
      catch (Exception e)
      {
        Logger?.Error($"Exception occurred in AiEnabled.AiSession.MyEntities_OnEntityAdd:\n{e}");
      }
    }

    MyCommandLine _cli = new MyCommandLine();
    private void OnMessageEntered(string messageText, ref bool sendToOthers)
    {
      try
      {
        if (!Registered || !messageText.StartsWith("botai", StringComparison.OrdinalIgnoreCase) || !_cli.TryParse(messageText))
        {
          return;
        }

        sendToOthers = false;
        var player = MyAPIGateway.Session.LocalHumanPlayer;

        string cmd = _cli.ArgumentCount < 2 ? null : _cli.Argument(1);
        if (cmd == null || cmd == "?" || cmd.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
          ShowCommandHelp(player.PromoteLevel >= MyPromoteLevel.Admin);
          return;
        }

        var character = player?.Character;
        if (character == null || character.IsDead)
        {
          ShowMessage("Respawn and try again.", timeToLive: 5000);
          return;
        }

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
        else if (cmd.Equals("drawobstacles", StringComparison.OrdinalIgnoreCase))
        {
          if (!IsServer)
          {
            ShowMessage("Debug draw is only available server side.", timeToLive: 5000);
            return;
          }

          bool b = !DrawObstacles;
          if (_cli.ArgumentCount > 2)
          {
            if (!bool.TryParse(_cli.Argument(2), out b))
              b = !DrawObstacles;
          }

          DrawObstacles = b;
          MyAPIGateway.Utilities.ShowNotification($"Draw Obstacles set to {b} (requires Debug Draw enabled)", 5000);
        }
        else if (cmd.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
          if (!IsServer)
          {
            ShowMessage("Debug draw is only available server side.", timeToLive: 5000);
            return;
          }

          if (_cli.ArgumentCount < 3)
          {
            var b = !DrawDebug;
            MyAPIGateway.Utilities.ShowNotification($"DrawDebug set to {b}", 5000);
            DrawDebug = b;
          }
          else
          {
            var b = !DrawDebug2;
            MyAPIGateway.Utilities.ShowNotification($"DrawDebug 2 set to {b}", 5000);
            if (b)
              DrawDebug = true;

            DrawDebug2 = b;
          }
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
          }

          bool roleSwitch = _cli.Switch("r");
          bool nameSwitch = _cli.Switch("n");
          bool modelSwitch = _cli.Switch("m");
          bool factionSwitch = _cli.Switch("f");
          bool distSwitch = _cli.Switch("d");
          bool multSwitch = _cli.Switch("x");
          bool colorSwitch = _cli.Switch("c");

          if (!roleSwitch && !nameSwitch && !modelSwitch && !factionSwitch && !distSwitch && !multSwitch && !colorSwitch && _cli.ArgumentCount > 2)
          {
            ShowMessage("This command now requires the use of switches. Use [botai ?] to view command info", "White", 10000);
            return;
          }

          string role = null;
          string subtype = null;
          string factionTag = null;
          string name = "";
          float distance = 25;
          int numSpawns = 1;
          long? owner = null;
          Color? clr = null;
          IMyFaction faction = null;

          if (roleSwitch)
            role = _cli.Switch("r", 0);
          else
            role = "Combat";

          if (modelSwitch)
            subtype = _cli.Switch("m", 0);

          if (nameSwitch)
            name = _cli.Switch("n", 0);

          if (factionSwitch)
          {
            factionTag = _cli.Switch("f", 0);
            faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(factionTag);
          }

          if (distSwitch)
          {
            if (!float.TryParse(_cli.Switch("d", 0), out distance))
              distance = 25;
          }

          if (multSwitch)
          {
            if (!int.TryParse(_cli.Switch("x", 0), out numSpawns))
              numSpawns = 1;
          }

          if (colorSwitch)
          {
            var colorString = _cli.Switch("c", 0);
            clr = colorString.ToColor();
          }

          float num;
          var natGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(character.WorldAABB.Center, out num);
          var artGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(character.WorldAABB.Center, num);
          var tGrav = natGrav + artGrav;

          Vector3D forward, up;
          if (tGrav.LengthSquared() > 0)
          {
            up = Vector3D.Normalize(-tGrav);
            forward = Vector3D.CalculatePerpendicularVector(up);
          }
          else
          {
            up = character.WorldMatrix.Up;
            forward = character.WorldMatrix.Forward;
          }

          var position = character.WorldAABB.Center + character.WorldMatrix.Forward * distance;
          var planet = MyGamePruningStructure.GetClosestPlanet(position);
          if (planet != null && natGrav.LengthSquared() > 0 && GridBase.PointInsideVoxel(position, planet))
          {
            var surfacePoint = planet.GetClosestSurfacePointGlobal(position);
            var lineTo = surfacePoint - planet.PositionComp.GetPosition();
            position = surfacePoint + Vector3D.Normalize(lineTo) * 5;
          }

          role = role.ToUpperInvariant();
          if (role.EndsWith("BOT"))
          {
            role = role.Substring(0, role.Length - 3);
          }

          if (role != "NOMAD")
          {
            BotFactory.BotRoleEnemy br;
            if (Enum.TryParse(role, out br))
              role = br.ToString();
            else
              owner = player.IdentityId;
          }

          ShowMessage("Sending spawn message to server", "White");
          var packet = new SpawnPacket(position, forward, up, subtype, role, owner, name, faction?.FactionId, numSpawns, clr);
          Network.SendToServer(packet);
        }
        else if (cmd.Equals("killall", StringComparison.OrdinalIgnoreCase))
        {
          bool includeFriendly = false;

          if (_cli.ArgumentCount > 2)
            bool.TryParse(_cli.Argument(2), out includeFriendly);

          var pkt = new AdminPacket(includeFriendly);
          Network.SendToServer(pkt);
        }
        else if (cmd.Equals("builddict", StringComparison.OrdinalIgnoreCase))
        {
          var matrix = character.WorldMatrix;
          matrix.Translation += character.WorldMatrix.Forward * 10;

          var grid = new MyObjectBuilder_CubeGrid()
          {
            CubeBlocks = new List<MyObjectBuilder_CubeBlock>(),
            GridPresenceTier = MyUpdateTiersGridPresence.Normal,
            IsRespawnGrid = false,
            DampenersEnabled = false,
            GridGeneralDamageModifier = 1,
            GridSizeEnum = MyCubeSize.Large,
            IsStatic = true,
            IsPowered = true,
            PersistentFlags = MyPersistentEntityFlags2.Enabled | MyPersistentEntityFlags2.InScene,
            PlayerPresenceTier = MyUpdateTiersPlayerPresence.Normal,
            CreatePhysics = true,
            DestructibleBlocks = true,
          };

          Dictionary<Vector3I, string> directionChecks = new Dictionary<Vector3I, string>(Vector3I.Comparer);
          var result = new StringBuilder(1024);
          foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
          {
            var cubeDef = def as MyCubeBlockDefinition;
            if (cubeDef == null || !cubeDef.Public || cubeDef.CubeSize != MyCubeSize.Large || _ignoreTypes.ContainsItem(cubeDef.Id.TypeId)) // || BlockInfo.IsKnown(cubeDef.Id))
              continue;

            if (cubeDef.DLCs == null)
              continue;

            if (!cubeDef.DLCs.Contains("MyObjectBuilder_DlcDefinition/Contact") && !cubeDef.DLCs.Contains("Contact"))
              continue;

            if (!cubeDef.HasPhysics)
            {
              Logger.Log($"{cubeDef.Id} has no physics");

              result.Append($"{{\n  new MyDefinitionId(typeof({cubeDef.Id.TypeId}), \"{cubeDef.Id.SubtypeName}\"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)\n  {{\n    ");

              foreach (var kvp in cubeDef.IsCubePressurized)
              {
                var cell = kvp.Key;
                result.Append($"{{ new Vector3I({cell.X},{cell.Y},{cell.Z}), MyTuple.Create(new Direction[] {{  }}, Vector3.Zero, 0f, false, false) }},\n");
              }

              result.Append($"}}\n}},\n");
              continue;
            }

            var cube = (MyObjectBuilder_CubeBlock)MyObjectBuilderSerializer.CreateNewObject(cubeDef.Id);
            grid.CubeBlocks.Clear();
            grid.CubeBlocks.Add(cube);
            grid.DisplayName = $"AIE_{cube.Name}";
            grid.PositionAndOrientation = new MyPositionAndOrientation(ref matrix);

            var ent = MyEntities.CreateFromObjectBuilderAndAdd(grid, true) as MyCubeGrid;
            matrix.Translation += ent.WorldMatrix.Right * ent.PositionComp.WorldAABB.HalfExtents.AbsMax() * 2 + 5;

            var block = ent.GetCubeBlock(ent.Min) as IMySlimBlock;
            if (block == null)
            {
              ShowMessage($"Block is null for {ent.DisplayName}");
              Logger.Log($"Block is null for {ent.DisplayName}");
              continue;
            }

            result.Append($"{{\n  new MyDefinitionId(typeof({cubeDef.Id.TypeId}), \"{cubeDef.Id.SubtypeName}\"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)\n  {{\n    ");

            var min = block.Min;
            var max = block.Max;
            var blockMatrix = new MatrixI(block.Orientation);
            MatrixI invMatrix;
            MatrixI.Invert(ref blockMatrix, out invMatrix);

            directionChecks.Clear();
            directionChecks[blockMatrix.UpVector] = " Direction.Up,";
            directionChecks[blockMatrix.DownVector] = " Direction.Down,";
            directionChecks[blockMatrix.LeftVector] = " Direction.Left,";
            directionChecks[blockMatrix.RightVector] = " Direction.Right,";
            directionChecks[blockMatrix.ForwardVector] = " Direction.Forward,";
            directionChecks[blockMatrix.BackwardVector] = " Direction.Backward,";

            Vector3I_RangeIterator iter = new Vector3I_RangeIterator(ref min, ref max);
            while (iter.IsValid())
            {
              var cell = iter.Current;
              iter.MoveNext();

              result.Append($"{{ new Vector3I({cell.X},{cell.Y},{cell.Z}), MyTuple.Create(new Direction[] {{ ");

              var local = Vector3I.TransformNormal(cell, ref invMatrix);
              var world = ent.GridIntegerToWorld(local);

              // Run checks
              foreach (var kvp in directionChecks)
              {
                var dirVec = kvp.Key;

                var testLocal = local + dirVec;
                var testWorld = ent.GridIntegerToWorld(testLocal);
                var dir = Vector3D.Normalize(world - testWorld);

                for (int i = 0; i < 4; i++)
                {
                  var spherePos = testWorld + dir * i * 0.5;
                  var sphere = new BoundingSphereD(spherePos, 0.6);

                  if (ent.GetIntersectionWithSphere(ref sphere))
                  {
                    result.Append(kvp.Value);
                    break;
                  }
                }
              }

              if (result[result.Length - 1] == ',')
                result.Length--;

              result.Append($" }}, Vector3.Zero, 0f, false, false) }},\n  ");

              if (iter.IsValid())
                result.Append("  ");
            }

            result.Append($"}}\n}},\n");
          }

          directionChecks.Clear();
          Logger.Log($"\n\n{result.ToString()}\n\n");
        }
        else if (cmd.Equals("GetName", StringComparison.OrdinalIgnoreCase) && _cli.ArgumentCount > 2)
        {
          var blockSubtype = _cli.Argument(2);

          foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
          {
            var cubeDef = def as MyCubeBlockDefinition;
            if (cubeDef == null)
              continue;

            if (cubeDef.Id.SubtypeName == blockSubtype)
            {
              MyAPIGateway.Utilities.ShowNotification(cubeDef.DisplayNameText, 5000);
              break;
            }
          }
        }
        else if (cmd.Equals("GetSpecials", StringComparison.OrdinalIgnoreCase))
        {
          var grid = MyCubeBuilder.Static.FindClosestGrid();
          if (grid == null)
          {
            MyAPIGateway.Utilities.ShowNotification($"No grid found for comparison...");
          }
          else
          {
            var defs = BlockInfo.GetSpecialsOnly();

            if (defs.Count > 0)
            {
              var list = new List<string>(defs.Count);
              var blocks = grid.GetBlocks().ToHashSet<IMySlimBlock>();
              var blockHash = new HashSet<MyDefinitionId>(blocks.Count);

              foreach (var b in blocks)
                blockHash.Add(b.BlockDefinition.Id);

              foreach (var def in defs)
              {
                if (!blockHash.Contains(def))
                {
                  var cubeDef = MyDefinitionManager.Static.GetCubeBlockDefinition(def);
                  if (cubeDef != null)
                    list.Add(cubeDef.DisplayNameText);
                }
              }

              var text = string.Join(Environment.NewLine, list);
              MyAPIGateway.Utilities.ShowMissionScreen("Specials Info", "", "", text, null, "Close");
            }
            else
            {
              MyAPIGateway.Utilities.ShowNotification($"No specials found!");
            }
          }
        }
      }
      catch (Exception ex)
      {
        ShowMessage($"Error during command execution: {ex.Message}");
        Logger.Error($"Exception during command execution: '{messageText}'\n {ex}");
      }
    }

    StringBuilder _commandInfo = new StringBuilder(1024);
    void ShowCommandHelp(bool isAdmin)
    {
      _commandInfo.Clear()
        .Append("NOTE: All commands are prefixed with 'botai'\n\n")
        .Append("? / Help - show this help screen\n\n")
        .Append("Fix - used to teleport any lost helpers to the player's location\n\n");

      if (isAdmin)
      {
        _commandInfo.Append('~', 15)
          .Append(" Admin-only commands ")
          .Append('~', 15)
          .Append('\n')
          .Append("NOTE: Debug and Debug 2 are only available offline\n\n")
          .Append("DrawObstacles - toggles displaying obstacle nodes when debug mode\n")
          .Append("          is active.\n\n")
          .Append("Debug - toggles debug mode on / off, which shows active bots' pathing\n")
          .Append("          info on screen. This will toggle debug 2 off, but not on.\n\n")
          .Append("Debug 2 - toggles tier 2 debug info, which includes map nodes near any\n")
          .Append("          active bots (sim hit). This will toggle debug mode on, but not off.\n\n")
          .Append("KillAll - removes all non-friendly bots. To include friendly, use 'KillAll true'.\n\n")
          .Append("Spawn - spawns a bot using the following optional switches\n")
          .Append("  -r = role\n")
          .Append("  -m = model (see available models below)\n")
          .Append("  -n = name\n")
          .Append("  -f = faction tag\n")
          .Append("  -d = distance (in front of player)\n")
          .Append("  -c = color, can be name (Red), hex (#FF0000), or rgb (255,0,0)\n")
          .Append("  -x = number of bots to spawn\n\n")
          .Append("  EXAMPLE: botai spawn -r combat -m \"Female Astronaut 4\"\n                    -n Annie -f ADV -d 5 -c Aqua -x 2\n\n");

        _commandInfo.Append($"Below are all valid bot models available for use in the Spawn command:\n");

        foreach (var charDef in MyDefinitionManager.Static.Characters)
        {
          if (charDef == null)
            continue;

          _commandInfo.Append(' ', 2)
            .Append(charDef.Name ?? charDef.Id.SubtypeName)
            .Append('\n');
        }
      }

      var text = _commandInfo.ToString();
      MyAPIGateway.Utilities.ShowMissionScreen("AiEnabled Command Info", "", "", text, null, "Close");
    }

    bool _drawMenu, _capsLock;
    TimeSpan _lastClickTime = new TimeSpan(DateTime.Now.Ticks);
    public override void HandleInput()
    {
      try
      {
        if (MyAPIGateway.Utilities.IsDedicated)
          return;

        if (MyAPIGateway.Input?.IsNewKeyPressed(MyKeys.CapsLock) == true)
          _capsLock = !_capsLock;

        var player = MyAPIGateway.Session?.Player?.Character;
        if (!Registered || MyAPIGateway.Gui.IsCursorVisible || CommandMenu?.Registered != true || player == null)
          return;

        bool uiDrawn = _drawMenu || CommandMenu.ShowInventory || CommandMenu.ShowPatrol || CommandMenu.AwaitingName;
        Input?.CheckKeys();

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

        if (!MyAPIGateway.Gui.ChatEntryVisible && CommandMenu.UseControl.IsNewPressed())
        {
          if (CommandMenu.SendTo || CommandMenu.PatrolTo)
          {
            CommandMenu.PlayHudClick();
            CommandMenu.ResetCommands();
          }
          else if (uiDrawn)
          {
            CommandMenu.PlayHudClick();

            if (CommandMenu.ShowInventory)
            {
              CommandMenu.SetInventoryScreenVisibility(false);
            }
            else if (CommandMenu.NameInputVisible)
            {
              CommandMenu.SetNameInputVisibility(false);
            }
            else if (CommandMenu.ShowPatrol)
            {
              CommandMenu.SetPatrolMenuVisibility(false);
            }
            else if (_drawMenu)
            {
              _drawMenu = false;
            }
          }
          else if (_selectedBot?.IsDead == false)
          {
            CommandMenu.PlayHudClick();
            _drawMenu = true;
          }
        }
        else if (CommandMenu.ShowInventory || CommandMenu.ShowPatrol || CommandMenu.SendTo || CommandMenu.PatrolTo || CommandMenu.NameInputVisible)
        {
          var slot0 = MyAPIGateway.Input.GetGameControl(MyControlsSpace.SLOT0);
          var unequipPri = slot0?.GetKeyboardControl();
          var unequipSec = slot0?.GetSecondKeyboardControl();
          var isEscape = MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Escape);
          var chatVis = MyAPIGateway.Gui.ChatEntryVisible;

          if (isEscape || MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.None
            || (!chatVis && unequipPri.HasValue && MyAPIGateway.Input.IsNewKeyPressed(unequipPri.Value))
            || (!chatVis && unequipSec.HasValue && MyAPIGateway.Input.IsNewKeyPressed(unequipSec.Value))
            || (chatVis && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F2)))
          {
            if (!isEscape)
              CommandMenu.PlayHudClick();
  
            CommandMenu.ResetCommands();
            _drawMenu = false;
          }
          else
          {
            //var newLMBPress = MyAPIGateway.Input.IsNewGameControlPressed(CommandMenu.PrimaryActionControl.GetGameControlEnum());
            var newLMBPress = MyAPIGateway.Input.IsNewLeftMousePressed();

            if (CommandMenu.SendTo || CommandMenu.PatrolTo)
            {
              var secondaryControl = CommandMenu.SecondaryActionControl.GetGameControlEnum();
              var newRMBPress = MyAPIGateway.Input.IsNewGameControlPressed(secondaryControl);
              var ctrlPressed = MyAPIGateway.Input.IsAnyCtrlKeyPressed();

              if (newLMBPress)
                CommandMenu.Activate(null, ctrlPressed);
              else if (CommandMenu.PatrolTo && newRMBPress)
                CommandMenu.Activate(null, ctrlPressed, patrolFinished: true);
            }
            else
            {
              var delta = MyAPIGateway.Input.GetCursorPositionDelta();
              if (!Vector2.IsZero(ref delta))
                CommandMenu.UpdateCursorPosition(delta);

              if (!CommandMenu.NameInputVisible)
              {
                var deltaWheel = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
                if (deltaWheel != 0)
                  CommandMenu.ApplyMouseWheelMovement(Math.Sign(deltaWheel));
              }

              if (CommandMenu.NameInputVisible)
              {
                if (!MyAPIGateway.Gui.ChatEntryVisible)
                {
                  var control = MyAPIGateway.Input.GetGameControl(MyControlsSpace.CHAT_SCREEN);
                  var controlString = CommandMenu.GetControlString(control);

                  MyAPIGateway.Utilities.ShowNotification($"Chat box must be enabled to enter route name - Press [{controlString}]", 32);
                }

                if (newLMBPress)
                  CommandMenu.SubmitRouteName();
              }
              else if (CommandMenu.ShowPatrol)
              {
                if (newLMBPress)
                  CommandMenu.ActivatePatrol();
              }
              else // show inventory stuffs
              {
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
        }
        else if (_drawMenu)
        {
          var slot0 = MyAPIGateway.Input.GetGameControl(MyControlsSpace.SLOT0);
          var unequipPri = slot0?.GetKeyboardControl();
          var unequipSec = slot0?.GetSecondKeyboardControl();
          var isEscape = MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Escape);
          var chatVis = MyAPIGateway.Gui.ChatEntryVisible;

          if (isEscape || MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.None
            || (!chatVis && unequipPri.HasValue && MyAPIGateway.Input.IsNewKeyPressed(unequipPri.Value))
            || (!chatVis && unequipSec.HasValue && MyAPIGateway.Input.IsNewKeyPressed(unequipSec.Value))
            || (chatVis && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F2)))
          {
            if (!isEscape)
              CommandMenu.PlayHudClick();

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
        Logger.Error($"Exception in AiSession.HandleInput: {ex}");
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
        if (MyAPIGateway.Utilities.IsDedicated || MyParticlesManager.Paused)
          return;

        var player = MyAPIGateway.Session?.Player;
        var character = player?.Character;
        if (!Registered || character == null)
          return;

        Projectiles.UpdateWeaponEffects();
        UpdateParticles();
        UpdateGPSLocal(player);

        if (_botAnalyzers.Count > 0 || _botSpeakers.Count > 0 || _healthBars.Count > 0 || _botHealings.Count > 0)
          DrawOverHeadIcons();

        if (CommandMenu == null || !CommandMenu.Registered)
          return;

        if (CommandMenu.ShowInventory || CommandMenu.ShowPatrol || CommandMenu.SendTo || CommandMenu.PatrolTo || CommandMenu.AwaitingName || CommandMenu.AwaitingRename)
        {
          if (CommandMenu.RadialVisible)
            CommandMenu.CloseMenu();
          else if (CommandMenu.InteractVisible)
            CommandMenu.CloseInteractMessage();

          if ((CommandMenu.AwaitingName || CommandMenu.AwaitingRename) && !CommandMenu.NameInputVisible)
            CommandMenu.DrawNameInput();

          if (CommandMenu.ShowPatrol)
          {
            if (!CommandMenu.PatrolVisible)
              CommandMenu.DrawPatrolMenu();
          }
          else if (CommandMenu.ShowInventory)
            CommandMenu.DrawInventoryScreen(character);
          else
            CommandMenu.DrawSendTo(character);
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
        Logger.Error($"Exception in AiSession.Draw: {ex}");
      }
      finally
      {
        base.Draw();
      }
    }

    private void OnPrefabSpawned(long entityId, string prefabName)
    {
      if (EconomyGrids == null || !EconomyGrids.Contains(entityId))
        _prefabsToCheck.Enqueue(MyTuple.Create(entityId, prefabName, MyAPIGateway.Session.GameplayFrameCounter));
    }


    bool IsEconGrid(MyCubeGrid grid)
    {
      var owner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : 0L;

      if (owner > 0)
      {
        var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner);
        if (faction == null || faction.AcceptHumans)
          return false;
      }

      var result = IsInAnySafeZone(grid);
      return result;
    }

    bool IsInAnySafeZone(MyEntity entity)
    {
      if (entity == null)
        return false;

      var impossibleAction = 1 << 31;
      foreach (var zone in MySessionComponentSafeZones.SafeZones)
      {
        if (!zone.IsActionAllowed(entity, AiUtils.CastHax(MySessionComponentSafeZones.AllowedActions, impossibleAction), 0L))
          return true;
      }

      return false;
    }

    public bool IsBotAllowedToUse(BotBase bot, string toolSubtype, out string reason)
    {
      var definition = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtype);
      var handItemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(definition);

      reason = null;
      if (bot is CombatBot)
      {
        bool allowed = handItemDef != null && handItemDef.WeaponType != MyItemWeaponType.None;

        if (!allowed)
          reason = "The CombatBot can only use rifles, pistols, and rocket launchers.";

        return allowed;
      }
      
      if (bot is RepairBot)
      {
        bool allowed = handItemDef != null 
          && (handItemDef.Id.TypeId == typeof(MyObjectBuilder_Welder) || handItemDef.Id.TypeId == typeof(MyObjectBuilder_AngleGrinder));

        if (!allowed)
          reason = "The RepairBot can only use welders and grinders";

        return allowed;
      }

      if (bot is CrewBot)
      {
        bool allowed = handItemDef != null && (handItemDef.WeaponType == MyItemWeaponType.Pistol || handItemDef.WeaponType == MyItemWeaponType.Rifle);

        if (!allowed)
          reason = "The CrewBot can only use rifles and pistols.";

        return allowed;
      }

      if (bot is ScavengerBot)
      {
        reason = "The ScavengerBot cannot use tools";
      }

      return false;
    }

    public bool IsBotAllowedToUse(string botRole, string toolSubtype)
    {
      var definition = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtype);
      var handItemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(definition);

      if (handItemDef != null)
      {
        if (botRole.IndexOf("Repair", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return handItemDef.Id.TypeId == typeof(MyObjectBuilder_Welder) || handItemDef.Id.TypeId == typeof(MyObjectBuilder_AngleGrinder);
        }

        if (botRole.IndexOf("Combat", StringComparison.OrdinalIgnoreCase) >= 0 
          || botRole.IndexOf("Soldier", StringComparison.OrdinalIgnoreCase) >= 0 
          || botRole.IndexOf("Enforcer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return handItemDef.WeaponType != MyItemWeaponType.None;
        }

        if (botRole.IndexOf("Nomad", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return handItemDef.WeaponType == MyItemWeaponType.Pistol;
        }

        if (botRole.IndexOf("Crew", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return handItemDef.WeaponType == MyItemWeaponType.Pistol || handItemDef.WeaponType == MyItemWeaponType.Rifle;
        }

        if (botRole.IndexOf("Grinder", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return handItemDef.Id.TypeId == typeof(MyObjectBuilder_AngleGrinder);
        }
      }

      return false;
    }

    public override MyObjectBuilder_SessionComponent GetObjectBuilder()
    {
      // This fires before the final save, but need a way to know it is the FINAL save
      return base.GetObjectBuilder();
    }

    public override void SaveData()
    {
      try
      {
        if (IsServer)
          SaveModData(true);

        if (!MyAPIGateway.Utilities.IsDedicated)
          UpdatePlayerConfig(true);
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in AiSession.SaveData(): {ex}");
      }
      finally
      {
        base.SaveData();
      }
    }

    public void SaveModData(bool writeConfig = false)
    {
      for (int i = ModSaveData.PlayerHelperData.Count - 1; i >= 0; i--)
      {
        var playerData = ModSaveData.PlayerHelperData[i];

        for (int j = playerData.Helpers.Count - 1; j >= 0; j--)
        {
          var helperData = playerData.Helpers[j];
          if (!helperData.IsActiveHelper)
            continue;

          BotBase bot;
          if (!Bots.TryGetValue(helperData.HelperId, out bot) || bot.IsDead)
            continue;

          var botChar = bot.Character;
          var gridGraph = bot._currentGraph as CubeGridMap;
          var grid = gridGraph?.MainGrid ?? null;

          var crewBot = bot as CrewBot;
          if (crewBot?.CrewFunction > 0)
            helperData.CrewFunction = (int)crewBot.CrewFunction;
          else
            helperData.CrewFunction = null;

          helperData.GridEntityId = grid?.EntityId ?? 0L;
          helperData.Position = botChar.GetPosition();
          helperData.Orientation = Quaternion.CreateFromRotationMatrix(botChar.WorldMatrix);
          helperData.InventoryItems?.Clear();
          helperData.ToolPhysicalItem = bot.ToolDefinition?.PhysicalItemId ?? (botChar.EquippedTool as IMyHandheldGunObject<MyDeviceBase>)?.PhysicalItemDefinition?.Id;
          helperData.RemainInPlace = bot.UseAPITargets;

          if (botChar.Parent is IMyCockpit)
            helperData.SeatEntityId = botChar.Parent.EntityId;
          else
            helperData.SeatEntityId = 0L;

          bool isRepair = bot is RepairBot;
          var priList = isRepair ? bot.RepairPriorities?.PriorityTypes : bot.TargetPriorities?.PriorityTypes;

          if (helperData.Priorities == null)
            helperData.Priorities = new List<string>();
          else if (priList != null)
          {
            helperData.Priorities.Clear();

            foreach (var item in priList)
            {
              var prefix = item.Value ? "[X]" : "[  ]";
              helperData.Priorities.Add($"{prefix} {item.Key}");
            }
          }

          if (isRepair || bot is ScavengerBot)
          {
            var ignList = bot.RepairPriorities?.IgnoreList;
            if (ignList?.Count > 0)
            {
              helperData.IgnoreList.Clear();

              foreach (var item in ignList)
              {
                if (item.Value)
                {
                  var entry = $"[X] {item.Key}";
                  if (!helperData.IgnoreList.Contains(entry))
                    helperData.IgnoreList.Add(entry);
                }
              }
            }
          }

          var inventory = botChar.GetInventory() as MyInventory;
          if (inventory?.ItemCount > 0)
          {
            if (helperData.InventoryItems == null)
              helperData.InventoryItems = new List<InventoryItem>();
            else
              helperData.InventoryItems.Clear();

            var items = inventory.GetItems();
            for (int k = 0; k < items.Count; k++)
            {
              var item = items[k];
              helperData.InventoryItems.Add(new InventoryItem(item.Content.GetId(), item.Amount));
            }
          }

          if (bot.PatrolMode && bot._patrolList?.Count > 0)
          {
            if (helperData.PatrolRoute == null)
              helperData.PatrolRoute = new List<SerializableVector3I>();
            else
              helperData.PatrolRoute.Clear();

            for (int k = 0; k < bot._patrolList.Count; k++)
              helperData.PatrolRoute.Add(bot._patrolList[k]);

            helperData.PatrolName = bot._patrolName;
          }
          else
          {
            helperData.PatrolName = null;
            helperData.PatrolRoute?.Clear();
          }
        }
      }

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
        ++_statusRequestTimer;
        ++_ticks;
        _isTick10 = _ticks % 10 == 0;
        _isTick100 = _isTick10 && _ticks % 100 == 0;

        DoTickNow();

        if (_isTick10)
        {
          if (IsServer)
            DoTick10();

          var player = MyAPIGateway.Session?.Player;
          if (player?.Character?.IsDead == false)
            CheckForSelectedBot(player);
        }

        if (_isTick100)
          DoTick100();

        if (_needsUpdate)
          UpdatePlayerConfig();

        if (_needsAdminUpdate)
          UpdateAdminConfig();

        if (_needsAdminSettingSync)
          UpdateAdminSettingSync();
      }
      catch (Exception ex)
      {
        ShowMessage($"Error in UpdateBeforeSim: {ex.Message}", timeToLive: 5000);
        Logger.Error($"Exception in AiSession.UpdateBeforeSim: {ex}");
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
          if (PlayerData.HighlightHelpersOnMouseOver)
            MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);
  
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
          if (PlayerData.HighlightHelpersOnMouseOver)
            MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);

          _selectedBot = null;
        }

        return;
      }

      if (CommandMenu != null && CommandMenu.Registered && (_drawMenu || CommandMenu.ShowInventory || CommandMenu.ShowPatrol 
        || CommandMenu.SendTo || CommandMenu.PatrolTo || CommandMenu.AwaitingName || CommandMenu.AwaitingRename))
        return;

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
            if (PlayerData.HighlightHelpersOnMouseOver)
              MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);

            _selectedBot = null;
          }

          return;
        }
      }

      if (!currentNull && _selectedBot.EntityId != bot.EntityId)
      {
        if (PlayerData.HighlightHelpersOnMouseOver)
          MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);

        _selectedBot = null;
        currentNull = true;
      }

      if (helperIds.Contains(bot.EntityId))
      {
        if (PlayerData.ShowHealthBars)
        {
          bool show = true;
          if (!PlayerData.ShowHealthWhenFull)
          {
            var statComp = bot.Components.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
            if (statComp == null || statComp.HealthRatio >= 1)
              show = false;
          }

          if (show)
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
        }

        _selectedBot = bot;

        if (PlayerData.HighlightHelpersOnMouseOver)
          MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name);
      }
      else if (!currentNull)
      {
        if (PlayerData.HighlightHelpersOnMouseOver)
          MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);

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
      List<long> helperIds = null;
      if (MyAPIGateway.Session?.Player != null)
        PlayerToActiveHelperIds.TryGetValue(MyAPIGateway.Session.Player.IdentityId, out helperIds);
      
      for (int i = 0; i < analyzers.Count; i++)
      {
        var botId = analyzers[i];
        if (_botAnalyzers.ContainsKey(botId))
          continue;

        if (helperIds != null && helperIds.Contains(botId))
        {
          if (!PlayerData.ShowMapIconFriendly)
            continue;
        }
        else if (!PlayerData.ShowMapIconNonFriendly)
        {
          continue;
        }

        var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
        if (bot == null || bot.MarkedForClose)
          continue;

        var info = GetIconInfo();
        info.Set(bot, 90);
        if (!_botAnalyzers.TryAdd(botId, info))
          ReturnIconInfo(info);
      }
    }

    public void AddHealingIcons(List<long> healings)
    {
      if (healings == null)
        return;

      for (int i = 0; i < healings.Count; i++)
      {
        var botId = healings[i];
        if (_botHealings.ContainsKey(botId))
          continue;

        var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
        if (bot == null || bot.MarkedForClose)
          continue;

        var info = GetIconInfo();
        info.Set(bot, 90);
        if (!_botHealings.TryAdd(botId, info))
          ReturnIconInfo(info);
      }
    }

    MyStringId _material_Analyze = MyStringId.GetOrCompute("AiEnabled_Analyze");
    MyStringId _material_Chat = MyStringId.GetOrCompute("AiEnabled_Chat");
    MyStringId _material_Heal = MyStringId.GetOrCompute("AiEnabled_Heal");
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

      if (_botHealings.Count > 0)
      {
        _iconRemovals.Clear();

        foreach (var kvp in _botHealings)
        {
          var info = kvp.Value;
          if (info.Update(ref matrix, ref _material_Heal, ref _iconColor))
            _iconRemovals.Add(kvp.Key);
        }

        foreach (var botId in _iconRemovals)
        {
          IconInfo info;
          if (_botHealings.TryRemove(botId, out info))
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

        Scheduler.UpdateAndExecuteJobs();
        Projectiles.UpdateProjectiles();
        //CommandMenu?.CheckUpdates();

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
        Logger.Error($"Exception in DoTickNow.IsClient: {ex}");
        return;
      }

      try
      {
        if (Players.Count == 0)
        {
          return;
        }

        BlockRepairDelays.Update();

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
              bot?.Delete();

            _botCharsToClose.Clear();
          }
        }

        if (_controllerSet && _controllerInfo.Count + _pendingControllerInfo.Count < _controllerCacheNum)
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
              bot.Save = false;
              bool useBot = true;

              if (bot.ControllerInfo?.ControllingIdentityId > 0)
              {
                var spawnIdentityId = bot.ControllerInfo.ControllingIdentityId;
                var spawnFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(spawnIdentityId);

                foreach (var kvp in MyAPIGateway.Session.Factions.Factions)
                {
                  if (kvp.Key != spawnFaction?.FactionId && kvp.Value.IsMember(spawnIdentityId))
                  {
                    Logger.Warning($"Found duplicate identity in faction {kvp.Value.Name} (discarding identity)");
                    useBot = false;
                    bot.Delete();
                    break;
                  }
                }

                IMyPlayer p;
                if (Players.TryGetValue(spawnIdentityId, out p) && p != null && p.Character?.EntityId != bot.EntityId)
                {
                  Logger.Warning($"Found duplicate identity for player {p.DisplayName} (discarding identity)");
                  useBot = false;
                  bot.Delete();
                }

                if (spawnFaction != null)
                {
                  try
                  {
                    MyAPIGateway.Session.Factions.KickMember(spawnFaction.FactionId, spawnIdentityId);
                  }
                  catch
                  {
                    Logger.Warning($"Attempt to kick dummy bot {spawnIdentityId} from faction {spawnFaction.Tag} failed!");
                  }
                }
              }
              else
              {
                Logger.Warning($"Spawned a dummy bot, but it did not have an IdentityId > 0!");
              }

              if (useBot)
              {
                _controllerSet = false;
                _controlBotIds.Add(botId);
                Scheduler.Schedule(GetBotController);
              }
            }
            else
            {
              Logger.Warning($"Attempted to spawn a dummy bot, but found it to be null!");
            }
          }
          catch
          {
            if (spawnOkay)
              throw;
            else
            {
              Logger.Warning($"Spawn issue detected. Waiting to try again...");
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
          if (gridGraph?.MainGrid != null && gridGraph.Ready && !gridGraph.MainGrid.IsStatic)
            gridGraph.RecalculateOBB();
        }

        for (int i = _robots.Count - 1; i >= 0; i--)
        {
          var robot = _robots[i];
          if (robot?.Character == null || robot.IsDead || robot.Character.MarkedForClose)
          {
            _robots.RemoveAtFast(i);
            continue;
          }

          var hasOwner = robot.Owner != null;

          if (hasOwner && ModSaveData.ChargePlayersForBotUpkeep)
          {
            var upkeep = BotUpkeepPrices.GetValueOrDefault(robot.BotType, 0);
            if (upkeep > 0)
              UpdateBotUpkeep(robot, upkeep);
          }

          if (!robot.Update() || robot.Character.Parent is IMyCockpit)
          {
            continue;
          }

          if (hasOwner)
          {
            robot.CheckPathCounter();
          }

          robot.MoveToTarget();
        }

        if (DrawDebug && MyAPIGateway.Session.Player != null)
        {
          foreach (var key in GridsToDraw)
          {
            CubeGridMap gridMap;
            VoxelGridMap voxelMap;
            if (GridGraphDict.TryGetValue((long)key, out gridMap))
            {
              gridMap.DrawDebug();
            }
            else if (VoxelGraphDict.TryGetValue(key, out voxelMap))
            {
              voxelMap.DrawDebug();
            }
          }

          GridsToDraw.Clear();
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Exception in DoTickNow.ServerStuff: {ex}");
      }
    }

    void UpdateBotUpkeep(BotBase bot, long credits)
    {
      try
      {
        var name = bot.Character?.Name;
        if (name == null || bot.Owner == null)
          return;

        int ticks;
        _activeHelpersToUpkeep.TryGetValue(name, out ticks);

        ticks++;
        var upkeepTicks = (ModSaveData?.BotUpkeepTimeInMinutes ?? 30) * 60 * 60;
        
        if (ticks >= upkeepTicks)
        {
          long balance;
          bot.Owner.TryGetBalanceInfo(out balance);

          if (balance < credits)
          {
            // store bot
            var pkt = new MessagePacket($"Insufficient funds for helper, {name} will now be stored.", ttl: 10000);
            Network.SendToPlayer(pkt, bot.Owner.SteamUserId);

            var storePkt = new StoreBotPacket(bot.Character.EntityId);
            Network.SendToServer(storePkt);
          }
          else
          {
            ticks = 0;
            bot.Owner.RequestChangeBalance(-credits);

            var pkt = new MessagePacket($"You have been charged {credits} SC for {name}.", ttl: 10000);
            Network.SendToPlayer(pkt, bot.Owner.SteamUserId);
          }
        }

        _activeHelpersToUpkeep[name] = ticks;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.ToString());
      }
    }

    public HashSet<ulong> GridsToDraw = new HashSet<ulong>();

    void UpdateParticles()
    {
      foreach (var item in ParticleDictionary)
      {
        var isShieldDeath = item.Value.ParticleType == ParticleInfoBase.ParticleType.Shield;
        if (!isShieldDeath && ParticleInfoDict.ContainsKey(item.Key))
        {
          continue;
        }

        var val = item.Value;
        var bot = MyEntities.GetEntityById(val.BotEntityId) as IMyCharacter;
        if (bot == null && !isShieldDeath)
        {
          continue;
        }

        switch (val.ParticleType)
        {
          case ParticleInfoBase.ParticleType.Shield:
            if (val.BotEntityId > 0 && val.WorldPosition.HasValue)
            {
              ParticleInfoBase pBase;
              if (ParticleInfoDict.TryGetValue(val.BotEntityId, out pBase))
              {
                if (pBase?.Type != ParticleInfoBase.ParticleType.Shield)
                {
                  if (ParticleInfoDict.TryRemove(val.BotEntityId, out pBase))
                  {
                    pBase?.Close();
                  }

                  var added = ParticleInfoDict.TryAdd(val.BotEntityId, new ShieldDeathParticleInfo(val.WorldPosition.Value, val.BotEntityId));
                }
              }
              else
              {
                var added = ParticleInfoDict.TryAdd(val.BotEntityId, new ShieldDeathParticleInfo(val.WorldPosition.Value, val.BotEntityId));
              }
            }
            break;
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
          case ParticleInfoBase.ParticleType.Weld:
          case ParticleInfoBase.ParticleType.Grind:

            var isWeld = val.IsWelderParticle && val.ParticleType == ParticleInfoBase.ParticleType.Weld;
            if (val.BlockEntityId > 0)
            {
              var block = MyEntities.GetEntityById(val.BlockEntityId) as IMyTerminalBlock;
              if (block != null)
                ParticleInfoDict.TryAdd(val.BotEntityId, new BuilderParticleInfo(bot, block.SlimBlock, isWeld));
            }
            else if (val.GridEntityId > 0 && val.BlockPosition != null)
            {
              var grid = MyEntities.GetEntityById(val.GridEntityId) as MyCubeGrid;
              if (grid != null)
              {
                var slim = grid.GetCubeBlock(val.BlockPosition.Value) as IMySlimBlock;
                if (slim != null)
                  ParticleInfoDict.TryAdd(val.BotEntityId, new BuilderParticleInfo(bot, slim, isWeld));
              }
            }
            break;
        }
      }

      foreach (var item in ParticleInfoDict)
      {
        if (item.Value?.Type != ParticleInfoBase.ParticleType.Shield && (item.Value?.Bot == null || item.Value.Bot.MarkedForClose))
        {
          ParticleInfoBase pBase;
          if (ParticleInfoDict.TryRemove(item.Key, out pBase))
            pBase?.Close();

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
        if (graph?.MainGrid == null || graph.MainGrid.MarkedForClose || !graph.IsGridGraph || !graph.IsValid)
        {
          _graphRemovals.Add(kvp.Key);
          continue;
        }

        graph.AdjustWorldMatrix();
        bool updateInventory = false;

        if (graph.Remake || graph.Dirty)
        {
          if (graph.Remake)
            graph.Refresh();

          graph.InventoryCache._needsUpdate = true;
          updateInventory = true;
        }
        else if (graph.Ready)
        {
          graph.LastActiveTicks++;
          if (graph.LastActiveTicks > 6)
          {
            if (clearObstacles)
              graph.ClearTempObstacles();

            graph.IsActive = false;
            continue;
          }

          if (graph.NeedsGridUpdate)
            graph.UpdateGridCollection();

          if (clearObstacles)
          {
            graph.ClearTempObstacles();
            updateInventory = graph.InventoryCache.AccessibleInventoryCount == 0;
          }

          if (graph.HasMoved)
          {
            if (!graph.PlanetTilesRemoved)
              graph.RemovePlanetTiles();

            var grid = graph.MainGrid;
            if (grid.IsStatic || (grid.Physics.LinearVelocity.LengthSquared() < 0.01 && grid.Physics.AngularVelocity.LengthSquared() < 0.001))
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

        if (graph.Remake)
        {
          graph.Refresh();
        }
        else if (graph.Ready)
        {
          graph.LastActiveTicks++;
          if (graph.LastActiveTicks > 6)
          {
            if (clearObstacles)
              graph.ClearTempObstacles();

            graph.IsActive = false;
            continue;
          }

          if (clearObstacles)
            graph.ClearTempObstacles();
        }
      }

      var localPlayer = MyAPIGateway.Session.Player;
      var mpActive = MyAPIGateway.Multiplayer.MultiplayerActive;

      if (AnalyzeHash.Count > 0)
      {
        _iconAddList.AddRange(AnalyzeHash);
        if (mpActive)
        {
          var pkt = new OverHeadIconPacket(_iconAddList, false);
          Network.RelayToClients(pkt);
        }

        if (localPlayer != null)
          AddOverHeadIcons(_iconAddList);

        _iconAddList.Clear();
        AnalyzeHash.Clear();
      }

      if (HealingHash.Count > 0)
      {
        _iconAddList.AddRange(HealingHash);
        if (mpActive)
        {
          var pkt = new OverHeadIconPacket(_iconAddList, true);
          Network.RelayToClients(pkt);
        }

        if (localPlayer != null)
          AddHealingIcons(_iconAddList);

        _iconAddList.Clear();
        HealingHash.Clear();
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

      var count = _prefabsToCheck.Count;
      if (count > 0)
      {
        var curFrame = MyAPIGateway.Session.GameplayFrameCounter;

        for (int i = 0; i < count; i++)
        {
          var tuple = _prefabsToCheck.Dequeue();

          if (curFrame - tuple.Item3 < 100)
          {
            _prefabsToCheck.Enqueue(tuple);
            continue;
          }

          var ent = MyEntities.GetEntityById(tuple.Item1);

          if (ent == null)
            MyEntities.TryGetEntityByName(tuple.Item2, out ent);

          var grid = ent as MyCubeGrid;
          if (grid != null && IsEconGrid(grid))
          {
            EconomyGrids.Add(grid.EntityId);
          }
        }
      }
    }

    bool _gpsUpdatesAvailable;
    int _planetCheckTimer;
    void DoTick100()
    {
      if (IsServer)
      {
        if (!_firstCacheComplete && CanSpawn)
        {
          _firstCacheComplete = true;
          var elapsed = MyAPIGateway.Session.ElapsedPlayTime - _firstFrameTime;
          var hours = elapsed.Hours;
          var mins = elapsed.Minutes - (60 * hours);
          var secs = Math.Round(elapsed.TotalSeconds - (60 * mins), 3);

          if (secs >= 60)
          {
            mins++;
            secs -= 60;
          }

          Logger.Log($"Mod setup completed in {hours}H {mins}M {secs:0.###}S");
        }

        if (_gpsUpdatesAvailable)
          SendGPSEntriesToPlayers();

        if (_newPlayerIds.Count > 0)
          UpdatePlayers();

        if (_controllerSet && FutureBotQueue.Count > 0 && _controllerInfo.Count >= _controllerCacheNum)
        {
          bool allowSpawns = true;

          if (!MyAPIGateway.Utilities.IsDedicated)
          {
            // Fix for folks using EEM
            // EEM validates faction join requests and for the first minute or so the stored identities will be linked to the local player's SteamId
            //var controlInfo = _controllerInfo[_controllerInfo.Count - 1];
            var controlInfo = _controllerInfo[0];

            var steamId = MyAPIGateway.Players.TryGetSteamId(controlInfo.Identity.IdentityId);
            if (steamId != 0)
            {
              allowSpawns = false;
            }
          }

          if (allowSpawns)
          {
            while (FutureBotQueue.Count > 0)
            {
              if (_controllerInfo.Count < MIN_SPAWN_COUNT)
                break;

              var future = FutureBotQueue.Dequeue();
              if (future.HelperInfo == null)
                continue;

              var info = future.HelperInfo;

              var matrix = MatrixD.CreateFromQuaternion(info.Orientation);
              matrix.Translation = info.Position;
              var posOr = new MyPositionAndOrientation(matrix);

              MyCubeGrid grid = null;
              if (info.GridEntityId > 0)
              {
                grid = MyEntities.GetEntityById(info.GridEntityId) as MyCubeGrid;
              }

              CrewBot.CrewType? crewType = null;
              if (info.CrewFunction.HasValue)
                crewType = (CrewBot.CrewType)info.CrewFunction.Value;

              var botRole = (BotType)info.Role;
              if (!info.AdminSpawned && !CanSpawnBot(botRole))
              {
                IMyPlayer player;
                if (Players.TryGetValue(future.OwnerId, out player) && player != null)
                {
                  MyVisualScriptLogicProvider.SendChatMessageColored($"Server settings prevented {botRole}Bot from being spawned.", Color.Orange, "AiEnabled", future.OwnerId);

                  var saveData = ModSaveData.PlayerHelperData;
                  for (int i = 0; i < saveData.Count; i++)
                  {
                    var playerData = saveData[i];
                    if (playerData.OwnerIdentityId == player.IdentityId)
                    {
                      var helperData = playerData.Helpers;
                      for (int j = helperData.Count - 1; j >= 0; j--)
                      {
                        var helper = helperData[j];
                        if (helper.HelperId == info.HelperId)
                        {
                          helperData.RemoveAtFast(j);
                          break;
                        }
                        //else if (helper.HelperId == bot.EntityId)
                        //{
                        //  helper.ToolSubtype = info.ToolSubtype;
                        //}
                      }

                      var pkt2 = new ClientHelperPacket(helperData);
                      Network.SendToPlayer(pkt2, player.SteamUserId);

                      break;
                    }
                  }
                }

                continue;
              }
                           
              var bot = BotFactory.SpawnHelper(info.CharacterDefinitionName, info.DisplayName ?? "", future.OwnerId, posOr, grid, ((BotType)info.Role).ToString(), info.ToolPhysicalItem?.SubtypeName, info.BotColor, crewType, info.AdminSpawned);
              if (bot == null)
              {
                Logger.Warning($"{GetType().FullName}: FutureBot returned null from spawn event");
              }
              else
              {
                BotBase botBase;
                if (Bots.TryGetValue(bot.EntityId, out botBase) && botBase != null)
                {
                  if (info.PatrolRoute?.Count > 0)
                    botBase.UpdatePatrolPoints(info.PatrolRoute, info.PatrolName);

                  if (info.Priorities != null)
                  {
                    if (botBase is RepairBot)
                    {
                      botBase.RepairPriorities = new RepairPriorities(info.Priorities);
                      botBase.TargetPriorities = new TargetPriorities();
                    }
                    else
                    {
                      botBase.RepairPriorities = new RepairPriorities();
                      botBase.TargetPriorities = new TargetPriorities(info.Priorities);
                    }
                  }
                  else
                  {
                    botBase.RepairPriorities = new RepairPriorities();
                    botBase.TargetPriorities = new TargetPriorities();
                  }

                  if (info.IgnoreList != null && (botBase is RepairBot || botBase is ScavengerBot))
                  {
                    botBase.RepairPriorities.UpdateIgnoreList(info.IgnoreList);
                  }

                  botBase.RepairPriorities.WeldBeforeGrind = info.WeldBeforeGrind;
                  botBase.TargetPriorities.DamageToDisable = info.DamageToDisable;

                  if (botBase.ToolDefinition != null && !(botBase is CrewBot))
                    Scheduler.Schedule(botBase.EquipWeapon);

                  try
                  {
                    var botInventory = bot.GetInventory() as MyInventory;
                    if (info.InventoryItems?.Count > 0 && botInventory != null)
                    {
                      foreach (var item in info.InventoryItems)
                      {
                        var ob = MyObjectBuilderSerializer.CreateNewObject(item.ItemDefinition);
                        botInventory.AddItems(item.Amount, ob);
                      }
                    }
                  }
                  catch (Exception ex)
                  {
                    Logger.Warning($"Error trying to add items to future bot ({future.HelperInfo.DisplayName}) inventory: {ex.ToString()}");
                  }
                }

                if (future.HelperInfo.SeatEntityId > 0)
                {
                  IMyEntity seat;
                  MyAPIGateway.Entities.TryGetEntityById(future.HelperInfo.SeatEntityId, out seat);

                  if (seat != null)
                  {
                    botBase.UseAPITargets = future.HelperInfo.RemainInPlace;
                    Scheduler.Schedule(() => BotFactory.TrySeatBot(botBase, seat as IMyCockpit), 10);
                  }
                }

                IMyPlayer player;
                if (Players.TryGetValue(future.OwnerId, out player) && player != null)
                {
                  var pkt = new SpawnPacketClient(bot.EntityId, false);
                  Network.SendToPlayer(pkt, player.SteamUserId);

                  var saveData = ModSaveData.PlayerHelperData;
                  for (int i = 0; i < saveData.Count; i++)
                  {
                    var playerData = saveData[i];
                    if (playerData.OwnerIdentityId == player.IdentityId)
                    {
                      var helperData = playerData.Helpers;
                      for (int j = helperData.Count - 1; j >= 0; j--)
                      {
                        var helper = helperData[j];
                        if (helper.HelperId == info.HelperId)
                        {
                          helperData.RemoveAtFast(j);
                          break;
                        }
                        //else if (helper.HelperId == bot.EntityId)
                        //{
                        //  helper.ToolSubtype = info.ToolSubtype;
                        //}
                      }

                      var pkt2 = new ClientHelperPacket(helperData);
                      Network.SendToPlayer(pkt2, player.SteamUserId);

                      break;
                    }
                  }
                }

                break;
              }
            }

            if (FutureBotQueue.Count == 0)
            {
              Scheduler.Schedule(() => SaveModData(true), 15);
            }
          }
        }
        else if (FutureBotQueue.Count == 0 && FutureBotAPIQueue.Count > 0 && _controllerInfo.Count >= MIN_SPAWN_COUNT)
        {
          var future = FutureBotAPIQueue.Dequeue();
          future?.Spawn();
        }

        ++_planetCheckTimer;
        var checkPlanets = _planetCheckTimer % 10 == 0;

        foreach (var graph in GridGraphDict.Values)
        {
          if (graph.GraphLocked || !graph.IsActive)
            continue;

          if (checkPlanets)
            graph.CheckPlanet();

          if (graph.NeedsBlockUpdate)
            graph.ProcessBlockChanges();
          else if (graph.NeedsVoxelUpdate)
            graph.UpdateVoxels();
          else if (graph.NeedsInteriorNodesUpdate)
            graph.InteriorNodesCheck();

          if (graph.Ready)
            graph.UpdateTempObstacles();
        }

        bool removedGraphThisTick = false;

        foreach (var kvp in VoxelGraphDict)
        {
          var graph = kvp.Value;
          if (graph.GraphLocked || !graph.IsActive)
          {
            if (!graph.GraphLocked && graph.LastActiveTicks > 200 && !removedGraphThisTick)
            {
              removedGraphThisTick = true;

              graph.Close();
              VoxelGraphDict.TryRemove(kvp.Key, out graph);
            }

            continue;
          }

          if (graph.NeedsVoxelUpdate)
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

    public bool CanSpawnBot(BotType role)
    {
      switch (role)
      {
        case BotType.Combat:
          return ModSaveData.AllowCombatBot;
        case BotType.Crew:
          return ModSaveData.AllowCrewBot;
        case BotType.Repair:
          return ModSaveData.AllowRepairBot;
        case BotType.Scavenger:
          return ModSaveData.AllowScavengerBot;
        default:
          return false;
      }
    }

    public bool CanSpawnBot(string botRole)
    {
      if (botRole.Equals("Combat", StringComparison.OrdinalIgnoreCase))
        return ModSaveData.AllowCombatBot;

      if (botRole.Equals("Crew", StringComparison.OrdinalIgnoreCase))
        return ModSaveData.AllowCrewBot;

      if (botRole.Equals("Repair", StringComparison.OrdinalIgnoreCase))
        return ModSaveData.AllowRepairBot;

      if (botRole.Equals("Scavenger", StringComparison.OrdinalIgnoreCase))
        return ModSaveData.AllowScavengerBot;

      return false;
    }

    public ControlInfo GetBotIdentity()
    {
      if (_controllerInfo.Count == 0)
        return null;

      var info = _controllerInfo[0];

      if (!MyAPIGateway.Utilities.IsDedicated)
      {
        // Fix for folks using EEM
        // EEM validates faction join requests and for the first minute or so the stored identities will be linked to the local player's SteamId
        var steamId = MyAPIGateway.Players.TryGetSteamId(info.Identity.IdentityId);
        if (steamId != 0)
        {
          return null;
        }
      }

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

          var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);
          if (faction != null)
          {
            try
            {
              Logger?.Debug($"GetBotControllerClient: Kicking bot id {player.IdentityId} from faction {faction.Tag}");
              MyAPIGateway.Session.Factions.KickMember(faction.FactionId, player.IdentityId);
            }
            catch { }
          }

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
      if (!IsServer || Players.Count == 0)
        return;

      var idDedicated = MyAPIGateway.Utilities.IsDedicated;
      var gpsCollectionValid = MyAPIGateway.Session?.GPS != null;

      foreach (var player in Players)
      {
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

          if (bb.Owner.IdentityId == player.Value.IdentityId)
          {
            _gpsAddIDs.Add(kvp.Key);
            _gpsOwnerIDs.Add(bb.Owner.IdentityId);
          }
        }

        foreach (var botId in _gpsRemovals)
        {
          IMyGps gps;
          if (BotGPSDictionary.TryRemove(botId, out gps) && !idDedicated && gpsCollectionValid)
            MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
        }

        if (_gpsAddIDs.Count > 0)
        {
          var packet = new GpsUpdatePacket(_gpsAddIDs, _gpsOwnerIDs);
          Network.SendToPlayer(packet, player.Value.SteamUserId);
        }
      }
    }

    void UpdateGPSLocal(IMyPlayer player)
    {
      bool showGPS = PlayerData.ShowHelperGPS;
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
        if (!showGPS || bot == null || bot.IsDead || character == null || character.IsDead)
        {
          if (contains)
            MyAPIGateway.Session.GPS.RemoveLocalGps(gps);

          continue;
        }

        var botPosition = bot.WorldAABB.Center;
        var subtype = bot.Definition.Id.SubtypeId;

        if (subtype == PlushieSubtype)
          botPosition -= bot.WorldMatrix.Up * 0.5;
        else if (subtype != RoboDogSubtype)
          botPosition += bot.WorldMatrix.Up * 0.25;

        var distanceSq = Vector3D.DistanceSquared(character.WorldAABB.Center, botPosition);

        if (!showGPS || distanceSq > 250 * 250)
        {
          if (gps.ShowOnHud)
          {
            gps.ShowOnHud = false;
            MyAPIGateway.Session.GPS.SetShowOnHud(player.IdentityId, gps.Hash, false);
          }

          continue;
        }

        gps.Coords = botPosition;

        if (!contains)
        {
          gps.UpdateHash();
          MyAPIGateway.Session.GPS.AddLocalGps(gps);
        }
        else if (!gps.ShowOnHud)
        {
          gps.ShowOnHud = true;
          MyAPIGateway.Session.GPS.SetShowOnHud(player.IdentityId, gps.Hash, true);
        }
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
              Vector3I vector = PlayerData.HelperGpsColorRGB;
              gpsColor = new Color(vector.X, vector.Y, vector.Z); // new Color(117, 201, 241);
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

    public void SwitchBot(BotBase newBot)
    {
      bool found = false;

      for (int i = 0; i < _robots.Count; i++)
      {
        var bot = _robots[i];
        if (bot?.Character == null || bot.IsDead)
          continue;

        if (bot.Character.EntityId == newBot.Character.EntityId)
        {
          found = true;
          _robots[i] = newBot;
          break;
        }
      }

      if (!found)
      {
        _robots.Add(newBot);
      }
    }

    public void AddBot(BotBase bot, long ownerId = 0L, bool adminSpawn = false)
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
                var grid = gridGraph?.MainGrid ?? null;

                helper.Orientation = Quaternion.CreateFromRotationMatrix(matrix);
                helper.Position = matrix.Translation;
                helper.GridEntityId = grid?.EntityId ?? 0L;
                helper.ToolPhysicalItem = bot.ToolDefinition?.PhysicalItemId ?? (bot.Character.EquippedTool as IMyHandheldGunObject<MyDeviceBase>)?.PhysicalItemDefinition?.Id;
                helper.InventoryItems?.Clear();

                var inventory = bot.Character.GetInventory() as MyInventory;
                if (inventory?.ItemCount > 0)
                {
                  if (helper.InventoryItems == null)
                    helper.InventoryItems = new List<InventoryItem>();

                  var items = inventory.GetItems();
                  for (int k = 0; k < items.Count; k++)
                  {
                    var item = items[k];
                    helper.InventoryItems.Add(new InventoryItem(item.Content.GetId(), item.Amount));
                  }
                }

                add = false;
                break;
              }
            }

            if (add)
            {
              var gridGraph = bot._currentGraph as CubeGridMap;
              var grid = gridGraph?.MainGrid ?? null;
              var crewBot = bot as CrewBot;
              var crewType = crewBot?.CrewFunction;

              var damageOnly = bot.TargetPriorities?.DamageToDisable ?? false;
              var weldFirst = bot.RepairPriorities?.WeldBeforeGrind ?? true;
              var priList = bot is RepairBot ? bot.RepairPriorities?.PriorityTypes : bot.TargetPriorities?.PriorityTypes;
              var ignList = bot.RepairPriorities?.IgnoreList;
              helperData.AddHelper(bot.Character, bot.BotType, priList, ignList, damageOnly, weldFirst, grid, bot._patrolList, crewType, adminSpawn, bot._patrolName);
            }

            var pkt = new ClientHelperPacket(helperData.Helpers);
            Network.SendToPlayer(pkt, player.SteamUserId);

            break;
          }
        }

        if (!found)
        {
          var data = new HelperData(player, null, null);
          var gridGraph = bot._currentGraph as CubeGridMap;
          var grid = gridGraph?.MainGrid ?? null;
          var botType = bot.BotType;
          var crewBot = bot as CrewBot;
          var crewType = crewBot?.CrewFunction;

          var damageOnly = bot.TargetPriorities?.DamageToDisable ?? false;
          var weldFirst = bot.RepairPriorities?.WeldBeforeGrind ?? true;
          var priList = bot is RepairBot ? bot.RepairPriorities?.PriorityTypes : bot.TargetPriorities?.PriorityTypes;
          var ignList = bot.RepairPriorities?.IgnoreList;
          data.AddHelper(bot.Character, botType, priList, ignList, damageOnly, weldFirst, grid, bot._patrolList, crewType, adminSpawn, bot._patrolName);
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
          AdjustFactionRepForPlayer(p.IdentityId);
        }
      }

      if (clearControllers && IsServer)
        ClearBotControllers();
    }

    void AdjustFactionRepForPlayer(long playerId)
    {
      var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
      IMyFaction helperFaction = null;
      if (playerFaction != null)
        BotFactions.TryGetValue(playerFaction.FactionId, out helperFaction);

      foreach (var tag in BotFactionTags)
      {
        var faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(tag);
        if (faction != null)
        {
          if (helperFaction != null && helperFaction.FactionId == faction.FactionId)
          {
            MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(playerId, faction.FactionId, int.MaxValue);
            MyAPIGateway.Session.Factions.SetReputation(playerFaction.FactionId, faction.FactionId, int.MaxValue);
          }
          else
          {
            var rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(playerId, faction.FactionId);
            if (rep != 0)
              MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(playerId, faction.FactionId, 0);
          }
        }
      }
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

            var future = new FutureBot(helper, data.OwnerIdentityId);
            FutureBotQueue.Enqueue(future);
          }

          break;
        }
      }
    }

    public IMyFaction GetBotFactionAssignment(IMyFaction factionToPairWith)
    {
      if (factionToPairWith == null)
        return null;

      IMyFaction helperFaction;
      if (BotFactions.TryGetValue(factionToPairWith.FactionId, out helperFaction))
        return helperFaction;

      if (!factionToPairWith.AcceptHumans)
        return factionToPairWith;

      Logger.Log($"Attempting to pair bot faction with {factionToPairWith.Name}");
      while (helperFaction == null)
      {
        if (BotFactionTags.Count == 0)
        {
          Logger.Warning($"AiSession.BeforeStart: BotFactionTags found empty during faction pairing!");
          break;
        }

        var rand = MyUtils.GetRandomInt(0, BotFactionTags.Count);
        var botFactionTag = BotFactionTags[rand];
        BotFactionTags.RemoveAtFast(rand);

        helperFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag(botFactionTag);
      }

      if (helperFaction == null || !BotFactions.TryAdd(factionToPairWith.FactionId, helperFaction))
      {
        Logger.Warning($"Aisession.BeforeStart: Failed to add faction pair - ID: {factionToPairWith.FactionId}, BotFactionTag: {helperFaction?.Tag ?? "NULL"}");
      }
      else
      {
        Logger.Log($" -> Paired {factionToPairWith.Name} with {helperFaction.Name}");

        try
        {
          foreach (var zone in MySessionComponentSafeZones.SafeZones)
          {
            var blockId = zone.SafeZoneBlockId;
            if (blockId == 0)
              continue;

            var block = MyEntities.GetEntityById(blockId) as IMySafeZoneBlock;
            if (block == null)
              continue;

            var zoneFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(block.OwnerId);
            if (zoneFaction == null || zoneFaction.FactionId != factionToPairWith.FactionId)
              continue;

            if (zone.AccessTypeFactions == MySafeZoneAccess.Whitelist)
            {
              var ob = zone.GetObjectBuilder() as MyObjectBuilder_SafeZone;
              if (ob.Factions == null)
              {
                ob.Factions = new long[0];
              }

              var factionArray = new long[ob.Factions.Length + 1];
              int idx = 0;
              foreach (var id in ob.Factions)
              {
                if (id != factionToPairWith.FactionId && BotFactions.ContainsKey(id))
                  continue;

                var faction = MyAPIGateway.Session.Factions.TryGetFactionById(id);
                if (faction == null || BotFactionTags.Contains(faction.Tag))
                  continue;

                factionArray[idx] = id;
                idx++;
              }

              factionArray[idx] = helperFaction.FactionId;
              ob.Factions = factionArray;

              MySessionComponentSafeZones.UpdateSafeZone(ob, true);
            }
          }
        }
        catch (Exception ex)
        {
          Logger.Error($"Error trying to update faction safezones: {ex}");
        }
      }

      return helperFaction;
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

    byte _statusRequestTimer;

    internal void SendBotStatusRequest(out int helperCount)
    {
      helperCount = -1;
      if (_statusRequestTimer < 100)
        return;

      _statusRequestTimer = 0;

      var player = MyAPIGateway.Session?.Player;
      if (player == null)
        return;

      List<long> helperIds;
      if (PlayerToActiveHelperIds.TryGetValue(player.IdentityId, out helperIds) && helperIds?.Count > 0)
      {
        var pkt = new BotStatusRequestPacket();
        Network.SendToServer(pkt);
      }

      helperCount = helperIds?.Count ?? 0;
    }

    internal void PropagateBotStatusUpdate(List<BotStatus> stats)
    {
      OnBotStatsUpdate?.Invoke(stats);
    }

    public event Action<List<BotStatus>> OnBotStatsUpdate;
  }
}
