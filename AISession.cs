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
using VRage.Network;
using VRage.Game.ModAPI.Network;
using VRage.Sync;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;

namespace AiEnabled
{
  [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
  public partial class AiSession : MySessionComponentBase, IMyEventProxy
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

    public static int MainThreadId = 1;
    public static AiSession Instance;
    public const string VERSION = "v0.17b";
    const int MIN_SPAWN_COUNT = 5;

    public int MaxBots = 100;
    public int MaxHelpers = 2;
    public int MaxBotProjectileDistance = 150;
    public uint GlobalSpawnTimer, GlobalSpeakTimer, GlobalMapInitTimer;
    public bool AllowMusic;

    public int BotNumber => _robots?.Count ?? 0;
    public Logger Logger { get; protected set; }
    public bool Registered { get; protected set; }
    public bool CanSpawn
    {
      get
      {
        if (!Registered || _controllerInfo == null || _controllerInfo.Count < MIN_SPAWN_COUNT || BotNumber >= MaxBots)
          return false;

        if (!MyAPIGateway.Utilities.IsDedicated)
        {
          //var info = _controllerInfo[_controllerInfo.Count - 1];
          var info = _controllerInfo[0];
          if (info?.Identity == null)
            return false;

          return MyAPIGateway.Players.TryGetSteamId(info.Identity.IdentityId) == 0;
        }

        return true;
      }
    }

    public bool FactoryControlsHooked;
    public bool FactoryControlsCreated;
    public bool FactoryActionsCreated;
    public bool IsServer, IsClient;
    public bool DrawDebug;
    public bool ShieldAPILoaded, WcAPILoaded, IndOverhaulLoaded, EemLoaded;
    public bool InfiniteAmmoEnabled;
    public readonly MyStringHash FactorySorterHash = MyStringHash.GetOrCompute("RoboFactory");

    public List<HelperInfo> MyHelperInfo;
    public CommandMenu CommandMenu;
    public PlayerMenu PlayerMenu;
    public NetworkHandler Network;
    public SaveData ModSaveData;
    public BotPricing ModPriceData;
    public PlayerData PlayerData;
    public Inputs Input;
    public ProjectileInfo Projectiles = new ProjectileInfo();
    public RepairDelay BlockRepairDelays = new RepairDelay();

    // APIs
    public HudAPIv2 HudAPI;
    public DefenseShieldsAPI ShieldAPI = new DefenseShieldsAPI();
    public WcApi WcAPI = new WcApi();
    LocalBotAPI _localBotAPI;

    IMyHudNotification _hudMsg;
    Vector3D _starterPosition;
    bool _isControlBot;
    int _controllerCacheNum = 20;
    bool? _allowEnemyFlight, _allowNeutralTargets;
    float? _botDamageMult, _playerDamageMult;
    int _maxHuntEnemy, _maxHuntFriendly, _maxProjectile;

    public GridBase GetNewGraph(MyCubeGrid grid, Vector3D newGraphPosition, MatrixD worldMatrix)
    {
      if (grid != null)
        return GetGridGraph(grid, worldMatrix);

      return GetVoxelGraph(newGraphPosition, worldMatrix);
    }

    public CubeGridMap GetGridGraph(MyCubeGrid grid, MatrixD worldMatrix)
    {
      if (grid == null || grid.MarkedForClose)
        return null;

      List<IMyCubeGrid> gridGroup;
      if (!GridGroupListStack.TryPop(out gridGroup))
      {
        gridGroup = new List<IMyCubeGrid>();
      }
      else
      {
        gridGroup.Clear();
      }

      grid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(gridGroup);
      foreach (var g in gridGroup)
      {
        if (g == null || g.MarkedForClose || g.EntityId == grid.EntityId || g.GridSizeEnum == MyCubeSize.Small)
          continue;

        var mygrid = g as MyCubeGrid;
        if (mygrid.BlocksCount > grid.BlocksCount || grid.GridSizeEnum == MyCubeSize.Small)
        {
          grid = mygrid;
        }
      }

      gridGroup.Clear();
      GridGroupListStack.Push(gridGroup);

      if (grid.GridSizeEnum == MyCubeSize.Small)
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
    public VoxelGridMap GetVoxelGraph(Vector3D worldPosition, MatrixD worldMatrix, bool forceRefresh = false, bool returnFirstFound = true)
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
          if (returnFirstFound)
          {
            if (forceRefresh)
              voxelGraph.Refresh();

            return voxelGraph;
          }

          var graphDistance = Vector3D.DistanceSquared(worldPosition, voxelGraph.OBB.Center);
          if (graphDistance < 10)
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

    public void StartWeaponFire(long botId, long targetId, float damage, float angleDeviationTan, List<float> rand, int ticksBetweenAttacks, int ammoRemaining, bool isGrinder, bool isWelder, bool leadTargets)
    {
      var bot = MyEntities.GetEntityById(botId) as IMyCharacter;
      if (bot == null || bot.IsDead)
        return;

      var tgt = MyEntities.GetEntityById(targetId);
      if (!isWelder && (tgt == null || tgt.MarkedForClose))
        return;

      var info = GetWeaponInfo();
      info.Set(bot, tgt, damage, angleDeviationTan, rand, ticksBetweenAttacks, ammoRemaining, isGrinder, isWelder, leadTargets);

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
      finally
      {
        Instance = null;
        base.UnloadData();
      }
    }

    void UnloadModData()
    {
      Logger?.Log($"Unloading mod data. Registered = {Registered}");
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
        MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);

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

      BlockRepairDelays?.Close();
      CommandMenu?.Close();
      PlayerMenu?.Close();
      Input?.Close();
      HudAPI?.Close();
      Projectiles?.Close();
      Network?.Unregister();

      AllCoreWeaponDefinitions?.Clear();
      RepairWorkStack?.Clear();
      GraphWorkStack?.Clear();
      PathWorkStack?.Clear();
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
      FutureBotAPIStack?.Clear();
      FutureBotAPIQueue?.Clear();
      SpawnDataStack?.Clear();
      RobotSubtypes?.Clear();
      UseObjectsAPI?.Clear();
      BotToSeatRelativePosition?.Clear();
      BotToSeatShareMode?.Clear();
      ComponentInfoDict?.Clear();
      StorageStack?.Clear();
      AcceptedItemDict?.Clear();
      ItemOBDict?.Clear();
      PatrolListStack?.Clear();
      VoxelMapListStack?.Clear();
      AllGameDefinitions?.Clear();
      ScavengerItemList?.Clear();
      MissingCompsDictStack?.Clear();
      EmptySorterCache?.Clear();
      FactorySorterCache?.Clear();
      ApiWorkDataStack?.Clear();
      LocalVectorHashStack?.Clear();
      ConsumableItemList?.Clear();
      CrewAnimations?.Clear();

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
      _analyzeList?.Clear();
      _botCharsToClose?.Clear();
      _botSpeakers?.Clear();
      _botAnalyzers?.Clear();
      _healthBars?.Clear();

      AllCoreWeaponDefinitions = null;
      RepairWorkStack = null;
      GraphWorkStack = null;
      PathWorkStack = null;
      BlockRepairDelays = null;
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
      ComponentInfoDict = null;
      StorageStack = null;
      VoxelGraphDict = null;
      GridGraphDict = null;
      AcceptedItemDict = null;
      ItemOBDict = null;
      ShieldAPI = null;
      PatrolListStack = null;
      VoxelMapListStack = null;
      BotComponents = null;
      AllGameDefinitions = null;
      ScavengerItemList = null;
      MissingCompsDictStack = null;
      EmptySorterCache = null;
      FactorySorterCache = null;
      ApiWorkDataStack = null;
      LocalVectorHashStack = null;
      DirArray = null;
      ConsumableItemList = null;
      CrewAnimations = null;

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
      _iconInfoStack = null;
      _healthInfoStack = null;
      _controlBotIds = null;
      _botsToClose = null;
      _botEntityIds = null;
      _useObjList = null;
      _localBotAPI = null;
      _keyPresses = null;
      _iconRemovals = null;
      _hBarRemovals = null;
      _analyzeList = null;
      _botCharsToClose = null;
      _botSpeakers = null;
      _botAnalyzers = null;
      _healthBars = null;

      ProjectileConstants.Close();

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

        Network = new NetworkHandler(55387, this);
        Network.Register();

        IsServer = MyAPIGateway.Multiplayer.IsServer;
        IsClient = !IsServer;
        MainThreadId = Environment.CurrentManagedThreadId;
        _controllerCacheNum = Math.Max(20, MyAPIGateway.Session.MaxPlayers * 2);

        bool sessionOK = MyAPIGateway.Session != null;
        if (sessionOK && MyAPIGateway.Session.Mods?.Count > 0)
        {
          foreach (var mod in MyAPIGateway.Session.Mods)
          {
            if (mod.PublishedFileId == 1365616918 || mod.PublishedFileId == 2372872458
              || mod.GetPath().Contains("AppData\\Roaming\\SpaceEngineers\\Mods\\DefenseShields"))
            {
              ShieldAPILoaded = ShieldAPI.Load();
              Logger.Log($"Defense Shields Mod found. API loaded successfully = {ShieldAPILoaded}");
            }
            else if (mod.PublishedFileId == 2200451495)
            {
              Logger.Log($"Water Mod v{WaterAPI.ModAPIVersion} found");
            }
            else if (mod.PublishedFileId == 1918681825)
            {
              WcAPI.Load(WeaponCoreRegistered);
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
                //MyAPIGateway.Utilities.ShowMessage("AiEnabled", "Spawns will be delayed until EEM faction validation passes.");
              }
            }
          }
        }
        else
        {
          Logger.Log($"Unable to check for mods in BeforeStart. Session OK = {sessionOK}", MessageType.WARNING);
        }

        foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
        {
          if (def == null || !def.Public || def.Id.SubtypeName == "ZoneChip")
            continue;

          if (def is MyGasTankDefinition || def is MyOxygenGeneratorDefinition)
            continue;

          var consumable = def as MyConsumableItemDefinition;
          if (consumable != null)
            ConsumableItemList.Add(consumable);

          // Thanks to Digi for showing me how to figure out what is craftable :)
          var prodDef = def as MyProductionBlockDefinition;
          if (prodDef != null)
          {
            foreach (MyBlueprintClassDefinition bpClass in prodDef.BlueprintClasses)
            {
              foreach (MyBlueprintDefinitionBase bp in bpClass)
              {
                foreach (MyBlueprintDefinitionBase.Item result in bp.Results)
                {
                  var compDef = MyDefinitionManager.Static.GetDefinition(result.Id) as MyComponentDefinition;
                  if (compDef != null && !AllGameDefinitions.ContainsKey(compDef.Id))
                    AllGameDefinitions[compDef.Id] = compDef;
                }
              }
            }
          }
        }

        if (IsServer)
        {
          _localBotAPI = new LocalBotAPI();

          foreach (var def in MyDefinitionManager.Static.Characters)
          {
            var subtype = def.Id.SubtypeName;
            AnimationControllerDictionary[subtype] = def.AnimationController;
            RobotSubtypes.Add(subtype);
          }

          foreach (var def in AllGameDefinitions)
          {
            ScavengerItemList.Add(def.Key);
          }

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
              Logger.Log($"AiSession.BeforeStart: Unable to disable wolves and spiders, or adjust max factions - SessionSettings was null!", MessageType.WARNING);
          }
          else
            Logger.Log($"APIGateway.Session was null in BeforeStart", MessageType.WARNING);

          VoxelGraphDict = new ConcurrentDictionary<ulong, VoxelGridMap>();

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

              var bp = new BotPrice
              {
                BotType = bType,
                SpaceCredits = credits,
                Components = BotComponents.GetValueOrDefault(bType, new List<SerialId>() { new SerialId() })
              };

              ModPriceData.BotPrices.Add(bp);
            }
          }
          else
          {
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
                      comps.Add(c);
                  }

                  BotComponents[botPrice.BotType.Value] = comps;
                }
                else
                {
                  BotComponents[botPrice.BotType.Value]?.Clear();
                }

                if (botPrice.SpaceCredits.HasValue)
                  BotPrices[botPrice.BotType.Value] = botPrice.SpaceCredits.Value;
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
              MaxBotsInWorld = MaxBots,
              MaxHelpersPerPlayer = MaxHelpers,
              MaxBotProjectileDistance = 150,
              MaxBotHuntingDistanceEnemy = 300,
              MaxBotHuntingDistanceFriendly = 150,
              
            };
          }

          if (ModSaveData.EnforceGroundPathingFirst)
            Logger.Log($"EnforceGroundNodesFirst is enabled. This is a use-at-your-own-risk option that may result in lag.", MessageType.WARNING);

          ModSaveData.MaxBotHuntingDistanceEnemy = Math.Max(50, Math.Min(1000, ModSaveData.MaxBotHuntingDistanceEnemy));
          ModSaveData.MaxBotHuntingDistanceFriendly = Math.Max(50, Math.Min(1000, ModSaveData.MaxBotHuntingDistanceFriendly));
          ModSaveData.MaxBotProjectileDistance = Math.Max(50, Math.Min(500, ModSaveData.MaxBotProjectileDistance));
          MaxBotProjectileDistance = Math.Max(0, ModSaveData.MaxBotProjectileDistance);
          AllowMusic = ModSaveData.AllowBotMusic;

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

                var future = new FutureBot(helper, data.OwnerIdentityId);
                FutureBotQueue.Enqueue(future);
                data.Helpers.RemoveAtFast(i);
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

                  if (!leaderOK && kvpMember.Value.IsLeader)
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

          factionMembers.Clear();
          factionMembers = null;

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
              var pkt = new AdminPacket(playerId, PlayerData.ShowHealthBars);
              Network.SendToServer(pkt);
            }
            else
              Logger.Log($"Player was null in BeforeStart!", MessageType.WARNING);
          }

          CommandMenu = new CommandMenu();
          PlayerMenu = new PlayerMenu(PlayerData);
          HudAPI = new HudAPIv2(HudAPICallback);
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

    void WeaponCoreRegistered()
    {
      WcAPILoaded = WcAPI.IsReady;
      WcAPI.GetAllCoreWeapons(AllCoreWeaponDefinitions);
      Logger.Log($"WeaponCore Mod found. API loaded successfully = {WcAPILoaded}");
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

    public void UpdateConfig(bool force = false)
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
      if (++_updateCounterAdmin < UpdateTime)
        return;

      _needsAdminUpdate = false;
      ModSaveData.MaxBotsInWorld = MaxBots;
      ModSaveData.MaxHelpersPerPlayer = MaxHelpers;

      Config.WriteFileToWorldStorage("AiEnabled.cfg", typeof(SaveData), ModSaveData, Logger);
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
        Logger.Log($"Exception in HudAPICallback: {ex.Message}\n{ex.StackTrace}");
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
        Logger.Log($"Exception in Factions_FactionAutoAcceptChanged: {ex.Message}\n{ex.StackTrace}");
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
        Logger.Log($"Exception in Factions_FactionStateChanged: {ex.Message}\n{ex.StackTrace}");
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
      catch (Exception ex)
      {
        Logger.Log($"Exception in Factions_FactionEdited: {ex.Message}\n{ex.StackTrace}");
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
      catch (Exception ex)
      {
        Logger.Log($"Exception in Factions_FactionCreated: {ex.Message}\n{ex.StackTrace}");
      }
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

    void PlayerLeftCockpitDelayed(string entityname, long playerId, string gridName)
    {
      try
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

          if (recallBots)
          {
            bot.PatrolMode = false;
            bot.FollowMode = false;
            bot.UseAPITargets = false;
            bot.Target?.RemoveOverride(false);
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
              if (seatCube != null)
                seatCube.IDModule.ShareMode = shareMode;
            }

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
            {
              bot.SetTarget();
              bot.Target.Update();
            }

            Vector3D actualPosition = bot.Target.CurrentActualPosition;
            bot.StartCheckGraph(ref actualPosition, true);

            var botMatrix = bot.WorldMatrix;

            Vector3D relPosition;
            BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out relPosition);
            var position = seat.GetPosition() + Vector3D.Rotate(relPosition, seat.WorldMatrix) + botMatrix.Down;

            var voxelGraph = bot._currentGraph as VoxelGridMap;
            var gridGraph = bot._currentGraph as CubeGridMap;
            MatrixD? newMatrix = null;

            if (voxelGraph != null)
            {
              Vector3D up = botMatrix.Up;

              float interference;
              var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out interference);
              if (gravity.LengthSquared() == 0)
                gravity = MyAPIGateway.Physics.CalculateArtificialGravityAt(position, interference);

              if (gravity.LengthSquared() > 0)
                up = Vector3D.Normalize(-gravity);

              if (relPosition.LengthSquared() < 2)
                position += Vector3D.CalculatePerpendicularVector(up) * (seat.CubeGrid.WorldAABB.HalfExtents.AbsMax() + 5);

              var surfacePoint = voxelGraph.GetClosestSurfacePointFast(bot, position, up);
              if (surfacePoint.HasValue)
                position = surfacePoint.Value + up;

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
                position = gridGraph.LocalToWorld(openNode) + botMatrix.Down;
              }
              else
              {
                Logger.Log($"{GetType().FullName}: Unable to find valid position upon exit from seat! Grid = {gridGraph.MainGrid?.DisplayName ?? "NULL"}", MessageType.WARNING);
              }
            }
            else if (relPosition.LengthSquared() < 2)
            {
              Vector3D up = botMatrix.Up;
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
      catch (Exception ex)
      {
        Logger.Log($"Exception in PlayerLeftCockpitDelayed: {ex.Message}\n{ex.StackTrace}");
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

        MyAPIGateway.Utilities.InvokeOnGameThread(() => PlayerLeftCockpitDelayed(entityName, playerId, gridName));
      }
      catch (Exception ex)
      {
        Logger.Log($"Exception in PlayerLeftCockpit: {ex.Message}\n{ex.StackTrace}");
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

        List<IMyCubeGrid> gridGroup;
        if (!GridGroupListStack.TryPop(out gridGroup))
          gridGroup = new List<IMyCubeGrid>();
        else
          gridGroup.Clear();

        List<IMySlimBlock> gridSeats;
        if (!SlimListStack.TryPop(out gridSeats))
          gridSeats = new List<IMySlimBlock>();
        else
          gridSeats.Clear();

        playerGrid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(gridGroup);

        foreach (var grid in gridGroup)
        {
          if (grid == null || grid.MarkedForClose)
            continue;

          grid.GetBlocks(gridSeats, b =>
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

        gridGroup.Clear();
        GridGroupListStack.Push(gridGroup);

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

            var botPosition = bot.GetPosition();

            if (!bot.CanUseSeats || Vector3D.DistanceSquared(ownerPos, botPosition) > 10000)
              continue;

            if (gridSeats.Count == 0)
              break;

            if (bot.UseAPITargets || bot.PatrolMode || bot.Character.Parent is IMyCockpit)
              continue;

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
                  gridSeats.RemoveAtFast(i);
                  var seatPos = seatPosition;
                  bot.Target.SetOverride(seatPos);
                  break;
                }
              }

              _useObjList.Clear();
              var useComp = seat.Components.Get<MyUseObjectsComponentBase>();
              useComp?.GetInteractiveObjects(_useObjList);
              if (_useObjList.Count > 0)
              {
                var relativePosition = Vector3D.Rotate(botPosition - seatPosition, MatrixD.Transpose(seat.WorldMatrix));
                BotToSeatRelativePosition[bot.Character.EntityId] = relativePosition;

                var useObj = _useObjList[0];
                var seatCube = seat as MyCubeBlock;

                if (useObj != null)
                {
                  var shareMode = seatCube.IDModule?.ShareMode ?? MyOwnershipShareModeEnum.All;
                  bool changeBack = false;

                  if (shareMode != MyOwnershipShareModeEnum.All)
                  {
                    var owner = bot.Owner?.IdentityId ?? bot.BotIdentityId;
                    var gridOwner = seat.CubeGrid.BigOwners?.Count > 0 ? seat.CubeGrid.BigOwners[0] : seat.CubeGrid.SmallOwners?.Count > 0 ? seat.CubeGrid.SmallOwners[0] : seat.SlimBlock.BuiltBy;

                    var relation = MyIDModule.GetRelationPlayerPlayer(owner, gridOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
                    if (relation != MyRelationsBetweenPlayers.Enemies)
                    {
                      changeBack = true;
                      seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
                    }
                  }

                  if (seatCube.IDModule == null || seatCube.IDModule.ShareMode == MyOwnershipShareModeEnum.All)
                  {
                    useObj.Use(UseActionEnum.Manipulate, bot.Character);
                  }

                  if (changeBack)
                  {
                    BotToSeatShareMode[bot.Character.EntityId] = shareMode;
                    //seatCube.IDModule.ShareMode = shareMode;
                  }

                  useObj.Use(UseActionEnum.Manipulate, bot.Character);
                  bot._pathCollection?.CleanUp(true);
                  bot.Target?.RemoveTarget();

                  gridSeats.RemoveAtFast(j);
                  break;
                }
              }
            }
          }
        }

        gridSeats.Clear();
        SlimListStack.Push(gridSeats);
      }
      catch (Exception ex)
      {
        Logger.Log($"Exception in PlayerEnteredCockpitDelayed: {ex.Message}\n{ex.StackTrace}");
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

        MyAPIGateway.Utilities.InvokeOnGameThread(() => PlayerEnteredCockpitDelayed(entityName, playerId, gridName));
      }
      catch (Exception ex)
      {
        Logger.Log($"Exception in PlayerEnteredCockpit: {ex.Message}\n{ex.StackTrace}");
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

        BotBase bot;
        bool targetIsBot = false;
        bool targetIsPlayer = false;
        bool targetisWildLife = false;
        if (Bots.TryGetValue(character.EntityId, out bot))
        {
          if (bot != null && !bot.IsDead)
          {
            targetIsBot = true;
            if (bot.Behavior?.PainSounds?.Count > 0)
              bot.Behavior.ApplyPain();
          }
        }
        else if (Players.ContainsKey(character.ControllerInfo.ControllingIdentityId))
        {
          targetIsPlayer = true;
        }
        else if (string.IsNullOrWhiteSpace(character.DisplayName) 
          || character.Definition.Id.SubtypeName.StartsWith("space_spider", StringComparison.OrdinalIgnoreCase)
          || character.Definition.Id.SubtypeName.StartsWith("space_wolf", StringComparison.OrdinalIgnoreCase))
        {
          targetisWildLife = true;
        }

        long ownerId, ownerIdentityId = 0;
        float damageAmount;
        switch (info.Type.String)
        {
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
              damageAmount = info.Amount * 0.4f;
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
                  damageAmount = 10f;
                else
                  damageAmount = info.Amount;
              }
              else
              {
                damageAmount = 10f;
              }
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
        Logger.Log($"Exception in BeforeDamageHandler: Target = {target?.GetType().FullName ?? "NULL"}, Info = {info.Type}x{info.Amount} by {info.AttackerId}\nExeption: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
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
        var nomad = bot as NeutralBotBase;
        if (nomad != null && !nomad.Target.HasTarget)
        {
          nomad.SetHostile(attackerId);
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

                    var gridGraph = bot._currentGraph as CubeGridMap;
                    var grid = gridGraph?.MainGrid ?? null;
                    helper.GridEntityId = grid?.EntityId ?? 0L;

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

                  playerData.AddHelper(bot.Character, botType, grid, bot._patrolList, crewType);
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
              var grid = gridGraph?.MainGrid ?? null;
              var botType = bot.BotType;

              CrewBot.CrewType? crewType = null;
              if (botType == BotType.Crew)
              {
                var crewBot = bot as CrewBot;
                crewType = crewBot?.CrewFunction;
              }

              playerData.AddHelper(bot.Character, botType, grid, bot._patrolList, crewType);
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

    public void PlayeSoundAtPosition(Vector3D position, string sound, bool stop)
    {
      var emitter = GetEmitter();
      emitter.SetPosition(position);

      MySoundPair soundPair;
      if (!SoundPairDict.TryGetValue(sound, out soundPair))
      {
        soundPair = new MySoundPair(sound);
        SoundPairDict[sound] = soundPair;
      }

      emitter.PlaySound(soundPair);
      ReturnEmitter(emitter);
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

    MyCommandLine _cli = new MyCommandLine();
    private void OnMessageEntered(string messageText, ref bool sendToOthers)
    {
      try
      {
        if (!Registered || !messageText.StartsWith("botai", StringComparison.OrdinalIgnoreCase) || !_cli.TryParse(messageText) || _cli.ArgumentCount < 2)
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
            var packet = new AdminPacket(num, MaxHelpers, MaxBotProjectileDistance, AllowMusic, null, null, null);
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
            var pkt = new AdminPacket(AllowMusic, null, null);
            Network.SendToServer(pkt);
          }

          ShowMessage($"AllowMusic set to {on}");
        }
        else if (cmd.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
          if (!IsServer)
          {
            ShowMessage("Debug draw is only available server side.", timeToLive: 5000);
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

          string role = null;
          string subtype = null;
          long? owner = null;

          if (_cli.ArgumentCount > 2)
            role = _cli.Argument(2);
          else
            role = "Combat";

          if (_cli.ArgumentCount > 3)
            subtype = _cli.Argument(3);

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
          var packet = new SpawnPacket(position, forward, up, subtype, role, owner);
          Network.SendToServer(packet);
        }
        else if (cmd.Equals("maxhuntenemy", StringComparison.OrdinalIgnoreCase))
        {
          int distance;
          if (_cli.ArgumentCount > 2 && int.TryParse(_cli.Argument(2), out distance))
          {
            distance = Math.Max(50, Math.Min(1000, distance));

            if (distance != _maxHuntEnemy)
            {
              _maxHuntEnemy = distance;

              var pkt = new AdminPacket(distance, null, null);
              Network.SendToServer(pkt);

              ShowMessage($"Max Enemy Hunting Distance set to {distance}m");
            }
          }
        }
        else if (cmd.Equals("maxhuntfriendly", StringComparison.OrdinalIgnoreCase))
        {
          int distance;
          if (_cli.ArgumentCount > 2 && int.TryParse(_cli.Argument(2), out distance))
          {
            distance = Math.Max(50, Math.Min(1000, distance));

            if (distance != _maxHuntFriendly)
            {
              _maxHuntFriendly = distance;

              var pkt = new AdminPacket(null, distance, null);
              Network.SendToServer(pkt);

              ShowMessage($"Max Friendly Hunting Distance set to {distance}m");
            }
          }
        }
        else if (cmd.Equals("maxbulletdistance", StringComparison.OrdinalIgnoreCase))
        {
          int distance;
          if (_cli.ArgumentCount > 2 && int.TryParse(_cli.Argument(2), out distance))
          {
            distance = Math.Max(50, Math.Min(500, distance));

            if (distance != _maxProjectile)
            {
              _maxProjectile = distance;

              var pkt = new AdminPacket(null, null, distance);
              Network.SendToServer(pkt);

              ShowMessage($"Max Projectile Distance set to {distance}m");
            }
          }
        }
        else if (cmd.Equals("enemyflight", StringComparison.OrdinalIgnoreCase))
        {
          bool b;
          if (_cli.ArgumentCount > 2 && bool.TryParse(_cli.Argument(2), out b))
          {
            if (_allowEnemyFlight == null || b != _allowEnemyFlight.Value)
            {
              _allowEnemyFlight = b;

              var pkt = new AdminPacket(AllowMusic, b, null);
              Network.SendToServer(pkt);

              ShowMessage($"Allow Enemy Flight set to {b}");
            }
          }
        }
        else if (cmd.Equals("targetneutral", StringComparison.OrdinalIgnoreCase))
        {
          bool b;
          if (_cli.ArgumentCount < 3 || !bool.TryParse(_cli.Argument(2), out b))
            b = _allowNeutralTargets.HasValue && !_allowNeutralTargets.Value;

          if (_allowNeutralTargets == null || b != _allowNeutralTargets.Value)
          {
            _allowNeutralTargets = b;

            var pkt = new AdminPacket(AllowMusic, null, b);
            Network.SendToServer(pkt);

            ShowMessage($"Allow Neutral Targets set to {b}");
          }
        }
        else if (cmd.Equals("botdamage", StringComparison.OrdinalIgnoreCase))
        {
          if (_cli.ArgumentCount < 3)
          {
            ShowMessage("Command 'BotDamage' requires a value as the third argument. Example [botai BotDamage 1.25]");
            return;
          }

          var value = _cli.Argument(2);
          float num;
          if (!float.TryParse(value, out num))
          {
            ShowMessage($"Invalid value found for command: '{value}'. Value must be a number! Example [botai BotDamage 1.25]");
            return;
          }

          if (_botDamageMult == null || num != _botDamageMult)
          {
            _botDamageMult = num;

            var pkt = new AdminPacket(num, true);
            Network.SendToServer(pkt);

            ShowMessage($"Setting Bot Damage Modifier to {num}");
          }
        }
        else if (cmd.Equals("playerdamage", StringComparison.OrdinalIgnoreCase))
        {
          if (_cli.ArgumentCount < 3)
          {
            ShowMessage("Command 'PlayerDamage' requires a value as the third argument. Example [botai BotDamage 1.25]");
            return;
          }

          var value = _cli.Argument(2);
          float num;
          if (!float.TryParse(value, out num))
          {
            ShowMessage($"Invalid value found for command: '{value}'. Value must be a number! Example [botai BotDamage 1.25]");
            return;
          }

          if (_playerDamageMult == null || num != _playerDamageMult)
          {
            _playerDamageMult = num;

            var pkt = new AdminPacket(num, false);
            Network.SendToServer(pkt);

            ShowMessage($"Setting Player Damage Modifier to {num}");
          }
        }
      }
      catch (Exception ex)
      {
        ShowMessage($"Error during command execution: {ex.Message}");
        Logger.Log($"Exception during command execution: '{messageText}'\n {ex.Message}\n{ex.StackTrace}");
      }
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

              if (newLMBPress)
                CommandMenu.Activate(null);
              else if (CommandMenu.PatrolTo && newRMBPress)
                CommandMenu.Activate(null, true);
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
        if (MyAPIGateway.Utilities.IsDedicated || MyParticlesManager.Paused)
        {
          return;
        }

        var player = MyAPIGateway.Session?.Player;
        var character = player?.Character;
        if (!Registered || character == null)
          return;

        Projectiles.UpdateWeaponEffects();
        UpdateParticles();
        UpdateGPSLocal(player);

        if (_botAnalyzers.Count > 0 || _botSpeakers.Count > 0 || _healthBars.Count > 0)
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
        Logger.Log($"Exception in AiSession.Draw: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
      finally
      {
        base.Draw();
      }
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
        bool allowed = handItemDef != null && handItemDef.Id.TypeId == typeof(MyObjectBuilder_Welder);

        if (!allowed)
          reason = "The RepairBot can only use welders";

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
          return handItemDef.Id.TypeId == typeof(MyObjectBuilder_Welder);
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

        if (botRole.IndexOf("Grinder", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return handItemDef.Id.TypeId == typeof(MyObjectBuilder_AngleGrinder);
        }
      }

      return false;
    }

    public override void SaveData()
    {
      try
      {
        if (IsServer)
          SaveModData(true);

        if (!MyAPIGateway.Utilities.IsDedicated)
          UpdateConfig(true);
      }
      catch (Exception ex)
      {
        Logger.Log($"Exception in AiSession.SaveData(): {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
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

          if (bot._patrolList?.Count > 0)
          {
            if (helperData.PatrolRoute == null)
              helperData.PatrolRoute = new List<SerializableVector3D>();
            else
              helperData.PatrolRoute.Clear();

            for (int k = 0; k < bot._patrolList.Count; k++)
              helperData.PatrolRoute.Add(bot._patrolList[k]);
          }
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
          MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);
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
            MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);
            _selectedBot = null;
          }

          return;
        }
      }

      if (!currentNull && _selectedBot.EntityId != bot.EntityId)
      {
        MyVisualScriptLogicProvider.SetHighlightLocal(_selectedBot.Name, -1);
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

        Projectiles.UpdateProjectiles();

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
        Logger.Log($"Exception in DoTickNow.IsClient: {ex.Message}\n{ex.StackTrace}");
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
              bot?.Close();

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

                if (spawnFaction != null)
                {
                  foreach (var kvp in MyAPIGateway.Session.Factions.Factions)
                  {
                    if (kvp.Key != spawnFaction.FactionId && kvp.Value.IsMember(spawnIdentityId))
                    {
                      Logger.Log($"Found duplicate identity in faction {kvp.Value.Name} (discarding identity)", MessageType.WARNING);
                      useBot = false;
                      bot.Close();
                      break;
                    }
                  }
                }
              }

              if (useBot)
              {
                _controllerSet = false;
                _controlBotIds.Add(botId);
                MyAPIGateway.Utilities.InvokeOnGameThread(GetBotController, "AiEnabled");
              }
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
          if (gridGraph?.Ready == true && gridGraph.MainGrid?.IsStatic == false)
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

          if (!robot.Update() || robot.Character.Parent is IMyCockpit)
          {
            continue;
          }

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
        if (graph?.MainGrid == null || graph.MainGrid.MarkedForClose || !graph.IsGridGraph)
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

        graph.AdjustWorldMatrix();

        if (graph.NeedsGridUpdate)
          graph.UpdateGridCollection();

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

            var grid = graph.MainGrid;
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

    bool _gpsUpdatesAvailable, _controlIdentOK;
    int _planetCheckTimer;
    void DoTick100()
    {
      if (IsServer)
      {
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
            if (steamId != 0) // || !_controlIdentOK)
            {
              _controlIdentOK = (steamId == 0);
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

              var bot = BotFactory.SpawnHelper(info.CharacterSubtype, info.DisplayName ?? "", future.OwnerId, posOr, grid, ((BotType)info.Role).ToString(), info.ToolPhysicalItem?.SubtypeName, info.BotColor, crewType);
              if (bot == null)
              {
                Logger.Log($"{GetType().FullName}: FutureBot returned null from spawn event", MessageType.WARNING);
              }
              else
              {
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

                  BotBase botBase;
                  if (Bots.TryGetValue(bot.EntityId, out botBase) && botBase != null)
                  {
                    if (info.PatrolRoute?.Count > 0)
                      botBase.UpdatePatrolPoints(info.PatrolRoute);

                    if (botBase.ToolDefinition != null && !(botBase is CrewBot))
                      MyAPIGateway.Utilities.InvokeOnGameThread(botBase.EquipWeapon, "AiEnabled");
                  }
                }
                catch (Exception ex)
                {
                  Logger.Log($"Error trying to add items to future bot ({future.HelperInfo.DisplayName}) inventory: {ex.Message}\n{ex.StackTrace}", MessageType.WARNING);
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
              }
            }

            if (FutureBotQueue.Count == 0)
            {
              SaveModData(true);
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

          if (graph.NeedsVoxelUpate)
            graph.UpdateVoxels();

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

      //var info = _controllerInfo[_controllerInfo.Count - 1];
      var info = _controllerInfo[0];

      if (!MyAPIGateway.Utilities.IsDedicated)
      {
        // Fix for folks using EEM
        // EEM validates faction join requests and for the first minute or so the stored identities will be linked to the local player's SteamId
        var steamId = MyAPIGateway.Players.TryGetSteamId(info.Identity.IdentityId);
        if (steamId != 0)
        {
          _controlIdentOK = false;
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

        if (distanceSq > 250 * 250)
        {
          gps.ShowOnHud = false;
          MyAPIGateway.Session.GPS.SetShowOnHud(player.IdentityId, gps.Hash, false);
          continue;
        }

        gps.Coords = botPosition;
        gps.ShowOnHud = true;

        if (!contains)
        {
          gps.UpdateHash();
          MyAPIGateway.Session.GPS.AddLocalGps(gps);
        }
        else
          MyAPIGateway.Session.GPS.SetShowOnHud(player.IdentityId, gps.Hash, true);
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

              helperData.AddHelper(bot.Character, bot.BotType, grid, bot._patrolList, crewType);
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
          var grid = gridGraph?.MainGrid ?? null;
          var botType = bot.BotType;
          var crewBot = bot as CrewBot;
          var crewType = crewBot?.CrewFunction;

          data.AddHelper(bot.Character, botType, grid, bot._patrolList, crewType);
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

            var future = new FutureBot(helper, data.OwnerIdentityId);
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
