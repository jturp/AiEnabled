using ParallelTasks;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;
using Sandbox.Game;
using VRage;
using VRage.Input;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using Sandbox;
using Sandbox.Definitions;
using Sandbox.Definitions.GUI;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Game.World;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using System.Collections.Concurrent;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using VRage.Game.Definitions.Animation;
using AiEnabled.Bots;
using AiEnabled.Bots.Roles;
using AiEnabled.Support;
using AiEnabled.Utilities;
using AiEnabled.Ai.Support;
using Sandbox.Game.EntityComponents;

namespace AiEnabled.GameLogic
{
  [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "BotSpawner")]
  public class Spawner : MyGameLogicComponent
  {
    List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();
    List<string> _subtypes = new List<string>()
    {
      "Police_Bot",
      "Space_Skeleton",
      "Space_Zombie",
      "Ghost_Bot",
    };

    int _minSecondsBetweenSpawns = 60;
    int _maxSecondsBetweenSpawns = 180;
    int _maxSimultaneousSpawns = 2;

    Sandbox.ModAPI.IMyTerminalBlock _block;
    MyShipController _fakeBlock = new MyShipController();
    MyIni _ini = new MyIni();
    CubeGridMap _gridMap;
    string _lastConfig;
    bool _hasSpawned = true, _allowBossBot = true, _isClient, _isServer;
    int _nextSpawnTime = 1000, _spawnTimer, _currentSpawnCount;

    public override void Close()
    {
      try
      {
        _ini?.Clear();
        _grids?.Clear();
        _subtypes?.Clear();
        _fakeBlock = null;
        _subtypes = null;
        _grids = null;
        _block = null;
        _ini = null;
      }
      finally
      {
        NeedsUpdate = MyEntityUpdateEnum.NONE;
        base.Close();
      }
    }

    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
      _block = (Sandbox.ModAPI.IMyTerminalBlock)Entity;
      _fakeBlock.SlimBlock = ((MyCubeBlock)_block).SlimBlock;
      NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
      base.Init(objectBuilder);
    }

    public override void UpdateOnceBeforeFrame()
    {
      try
      {
        _isServer = MyAPIGateway.Multiplayer.IsServer;
        _isClient = !_isServer;

        if (_isClient || _block.CubeGrid?.Physics == null)
          return;

        SetupIni();
        NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
      }
      catch (Exception e)
      {
        MyAPIGateway.Utilities.ShowMissionScreen("Exception Occurred", null, null,$"In UpdateOnceBeforeFrame:\n{e.Message}\n{e.StackTrace}");
      }

      base.UpdateOnceBeforeFrame();
    }

    void SetupIni(bool parseNew = true)
    {
      if (parseNew && _ini.TryParse(_block.CustomData) && _ini.ContainsSection("AiEnabled"))
      {
        ParseConfig(_ini);
        return;
      }

      _ini.Clear();
      _ini.Set("AiEnabled", "Min Spawn Interval", _minSecondsBetweenSpawns);
      _ini.Set("AiEnabled", "Max Spawn Interval", _maxSecondsBetweenSpawns);
      _ini.Set("AiEnabled", "Max Simultaneous Spawns", _maxSimultaneousSpawns);
      _ini.Set("AiEnabled", "Allow SoldierBot", true);
      _ini.Set("AiEnabled", "Allow GrinderBot", true);
      _ini.Set("AiEnabled", "Allow ZombieBot", true);
      _ini.Set("AiEnabled", "Allow GhostBot", true);
      _ini.Set("AiEnabled", "Allow BruiserBot", true);

      _ini.SetSectionComment("AiEnabled", " \n Enable or Disable the spawning of certain types by switching\n their values to TRUE or FALSE \n ");
      _ini.SetComment("AiEnabled", "Min Spawn Interval", " \n The Minimum number of Seconds betweeen spawns. (min = 1)\n ");
      _ini.SetComment("AiEnabled", "Max Spawn Interval", " \n The Maximum number of Seconds between spawns.\n ");
      _ini.SetComment("AiEnabled", "Max Simultaneous Spawns", " \n The Maximum number of active spawns allowed at any given time.\n ");
      _ini.SetComment("AiEnabled", "Allow SoldierBot", " \n The SoldierBot uses an automatic rifle to hunt you down.\n ");
      _ini.SetComment("AiEnabled", "Allow GrinderBot", " \n The GrinderBot uses a grinder to hunt you down.\n ");
      _ini.SetComment("AiEnabled", "Allow ZombieBot", " \n The ZombieBot applies poison damage over time with its attacks.\n ");
      _ini.SetComment("AiEnabled", "Allow GhostBot", " \n The GhostBot applies cold damage over time with its attacks.\n ");
      _ini.SetComment("AiEnabled", "Allow BruiserBot", " \n The BruiserBot is a boss encounter; it is harder to kill than\n the others and packs a heavy punch.\n ");
  
      _lastConfig = _ini.ToString();
      _block.CustomData = _lastConfig;
    }

    void ParseConfig(MyIni ini)
    {
      var allowSoldier = ini.Get("AiEnabled", "Allow SoldierBot").ToBoolean(true);
      if (!allowSoldier)
      {
        _subtypes.Remove("Police_Bot");
      }
      else if (!_subtypes.Contains("Police_Bot"))
      {
        _subtypes.Add("Police_Bot");
      }

      var allowGrinder = ini.Get("AiEnabled", "Allow GrinderBot").ToBoolean(true);
      if (!allowGrinder)
      {
        _subtypes.Remove("Space_Skeleton");
      }
      else if (!_subtypes.Contains("Space_Skeleton"))
      {
        _subtypes.Add("Space_Skeleton");
      }

      var allowZombie = ini.Get("AiEnabled", "Allow ZombieBot").ToBoolean(true);
      if (!allowZombie)
      {
        _subtypes.Remove("Space_Zombie");
      }
      else if (!_subtypes.Contains("Space_Zombie"))
      { 
        _subtypes.Add("Space_Zombie");
      }

      var allowGhost = ini.Get("AiEnabled", "Allow GhostBot").ToBoolean(true);
      if (!allowGhost)
      {
        _subtypes.Remove("Ghost_Bot");
      }
      else if (!_subtypes.Contains("Ghost_Bot"))
      {
        _subtypes.Add("Ghost_Bot");
      }

      _allowBossBot = ini.Get("AiEnabled", "Allow BruiserBot").ToBoolean(true);
      _minSecondsBetweenSpawns = Math.Max(1, ini.Get("AiEnabled", "Min Spawn Interval").ToInt32(60));
      _maxSecondsBetweenSpawns = Math.Max(1, ini.Get("AiEnabled", "Max Spawn Interval").ToInt32(180));
      _maxSimultaneousSpawns = Math.Max(1, ini.Get("AiEnabled", "Max Simultaneous Spawns").ToInt32(2));

      ini.Clear();
      ini.Set("AiEnabled", "Min Spawn Interval", _minSecondsBetweenSpawns);
      ini.Set("AiEnabled", "Max Spawn Interval", _maxSecondsBetweenSpawns);
      ini.Set("AiEnabled", "Max Simultaneous Spawns", _maxSimultaneousSpawns);
      ini.Set("AiEnabled", "Allow SoldierBot", allowSoldier);
      ini.Set("AiEnabled", "Allow GrinderBot", allowGrinder);
      ini.Set("AiEnabled", "Allow ZombieBot", allowZombie);
      ini.Set("AiEnabled", "Allow GhostBot", allowGhost);
      ini.Set("AiEnabled", "Allow BruiserBot", _allowBossBot);

      ini.SetSectionComment("AiEnabled", " \n Enable or Disable the spawning of certain types by switching\n their values to TRUE or FALSE \n ");
      ini.SetComment("AiEnabled", "Min Spawn Interval", " \n The Minimum number of Seconds betweeen spawns. (min = 1)\n ");
      ini.SetComment("AiEnabled", "Max Spawn Interval", " \n The Maximum number of Seconds between spawns.\n ");
      ini.SetComment("AiEnabled", "Max Simultaneous Spawns", " \n The Maximum number of active spawns allowed at any given time.\n ");
      ini.SetComment("AiEnabled", "Allow SoldierBot", " \n The SoldierBot uses an automatic rifle to hunt you down.\n ");
      ini.SetComment("AiEnabled", "Allow GrinderBot", " \n The GrinderBot uses a grinder to hunt you down.\n ");
      ini.SetComment("AiEnabled", "Allow ZombieBot", " \n The ZombieBot applies poison damage over time with its attacks.\n ");
      ini.SetComment("AiEnabled", "Allow GhostBot", " \n The GhostBot applies cold damage over time with its attacks.\n ");
      ini.SetComment("AiEnabled", "Allow BruiserBot", " \n The BruiserBot is a boss encounter; it is harder to kill than\n the others and packs a heavy punch.\n ");

      _lastConfig = ini.ToString();
      _block.CustomData = _lastConfig;
    }

    public override void UpdateAfterSimulation100()
    {
      try
      {
        if (_isClient || AiSession.Instance?.Registered != true || !AiSession.Instance.CanSpawn)
          return;

        if (_block == null || _block.MarkedForClose || !_block.IsWorking || AiSession.Instance.Players.Count == 0 || (_hasSpawned && CheckSpawnTimer()))
          return;

        if (_fakeBlock.GridResourceDistributor.ResourceState == MyResourceStateEnum.NoPower)
          return;

        if (AiSession.Instance.BotNumber >= AiSession.Instance.MaxBots)
          return;

        var data = _block.CustomData;
        if (string.IsNullOrWhiteSpace(data) || !_ini.TryParse(data))
          SetupIni(false);
        else if (_ini.ToString() != _lastConfig)
          ParseConfig(_ini);

        if (_currentSpawnCount >= _maxSimultaneousSpawns || AiSession.Instance.GlobalSpawnTimer < 60)
          return;

        if (_gridMap == null)
        {
          var list = new List<IMyCubeGrid>();
          MyAPIGateway.GridGroups.GetGroup(_block.CubeGrid, GridLinkTypeEnum.Logical, list);

          MyCubeGrid biggest = _block.CubeGrid as MyCubeGrid;
          foreach (var g in list)
          {
            if (g.GridSize > 1 && g.WorldAABB.Volume > biggest.PositionComp.WorldAABB.Volume)
              biggest = g as MyCubeGrid;
          }

          list.Clear();

          float num;
          var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(biggest.WorldMatrix.Translation, out num);
          var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(biggest.WorldMatrix.Translation, num);
          var tGrav = nGrav + aGrav;

          MatrixD matrix;
          if (tGrav.LengthSquared() > 0)
          {
            tGrav.Normalize();
            Vector3D up = -tGrav;
            Vector3D fwd = Vector3D.CalculatePerpendicularVector(up);
            matrix = MatrixD.CreateWorld(biggest.WorldMatrix.Translation, fwd, up);
          }
          else
          {
            matrix = _block.WorldMatrix;
            matrix.Translation = biggest.WorldMatrix.Translation;
          }

          _gridMap = AiSession.Instance.GetGridGraph(biggest, matrix);
        }

        if (_gridMap?.Ready != true)
          return;

        var grid = _gridMap.Grid;
        bool hasRegular = _subtypes.Count > 0;

        if (!hasRegular && !_allowBossBot)
          return;

        foreach (var kvp in AiSession.Instance.Players)
        {
          var player = kvp.Value?.Character;
          if (player == null || player.IsDead)
            continue;

          var playerPosition = player.PositionComp.WorldAABB.Center;
          if (Vector3D.DistanceSquared(playerPosition, _block.WorldAABB.Center) < 90000)
          {
            string botType;
            if (_allowBossBot && (!hasRegular || _gridMap.TotalSpawnCount > 2 && MyUtils.GetRandomInt(1, 101) >= _gridMap.BossSpawnChance))
            {
              _gridMap.BossSpawnChance = 100;
              _gridMap.TotalSpawnCount = 0;
              botType = "Boss_Bot";
            }
            else if (hasRegular)
            {
              var num = MyUtils.GetRandomInt(0, _subtypes.Count);
              botType = _subtypes[num];

              if (_gridMap.TotalSpawnCount > 3)
                _gridMap.BossSpawnChance--;
            }
            else
              return;

            var posOr = new MyPositionAndOrientation(_block.PositionComp.WorldAABB.Center + _block.WorldMatrix.Backward * 2.5, (Vector3)_block.WorldMatrix.Backward, (Vector3)(Vector3)_block.WorldMatrix.Up);

            var bot = BotFactory.SpawnNPC(botType, "", posOr, grid);
            if (bot != null)
            {
              //long ownerId;
              //if (_block.CubeGrid.BigOwners?.Count > 0)
              //  ownerId = _block.CubeGrid.BigOwners[0];
              //else if (_block.CubeGrid.SmallOwners?.Count > 0)
              //  ownerId = _block.CubeGrid.SmallOwners[0];
              //else
              //  ownerId = _block.OwnerId;

              //if (ownerId > 0)
              //{
              //  var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
              //  if (faction != null && !faction.AcceptHumans)
              //  {
              //    MyVisualScriptLogicProvider.SetPlayersFaction(bot.ControllerInfo.ControllingIdentityId, faction.Tag);
              //  }
              //}

              AiSession.Instance.GlobalSpawnTimer = 0;
              _currentSpawnCount++;
              _gridMap.TotalSpawnCount++;
              bot.OnMarkForClose += Bot_OnMarkForClose;
              bot.CharacterDied += Bot_OnMarkForClose;
              bot.OnClose += Bot_OnMarkForClose;
            }

            _hasSpawned = true;
            _spawnTimer = 0;
            _nextSpawnTime = MyUtils.GetRandomInt(_minSecondsBetweenSpawns, _maxSecondsBetweenSpawns + 1) * 60;
            break;
          }
        }
      }
      catch (Exception e)
      {
        AiSession.Instance.Logger.ClearCached();
        AiSession.Instance.Logger.Log($"Exception in UpdateAfterSimulation100:\n{e.Message}\n{e.StackTrace}", MessageType.ERROR);

        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"Exception in SpawnBlock.UpdateAfterSimulation100:\n{e.Message}");
  
        _hasSpawned = true;
        _spawnTimer = 0;
      }

      base.UpdateAfterSimulation100();
    }

    private void Bot_OnMarkForClose(IMyEntity obj)
    {
      var bot = obj as IMyCharacter;
      if (bot != null)
      {
        bot.CharacterDied -= Bot_OnMarkForClose;
        bot.OnMarkForClose -= Bot_OnMarkForClose;
        bot.OnClose -= Bot_OnMarkForClose;
      }

      _currentSpawnCount--;
    }

    bool CheckSpawnTimer()
    {
      _spawnTimer += 100;

      if (_spawnTimer >= _nextSpawnTime)
        _hasSpawned = false;

      return _hasSpawned;
    }
  }
}
