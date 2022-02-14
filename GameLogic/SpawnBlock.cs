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
using IMyFunctionalBlock = Sandbox.ModAPI.IMyFunctionalBlock;

namespace AiEnabled.GameLogic
{
  [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "BotSpawner")]
  public class Spawner : MyGameLogicComponent
  {
    List<MyIniKey> _iniKeys = new List<MyIniKey>();
    Dictionary<string, KeyValuePair<string, string>> _subtypeToRole = new Dictionary<string, KeyValuePair<string, string>>(10);
    List<string> _allSubtypes = new List<string>(10);
    List<string> _creatureTypes = new List<string>(5);
    List<string> _nomadTypes = new List<string>(5);

    int _minSecondsBetweenSpawns = 60;
    int _maxSecondsBetweenSpawns = 180;
    int _maxSimultaneousSpawns = 2;

    Sandbox.ModAPI.IMyUpgradeModule _block;
    MyShipController _fakeBlock = new MyShipController();
    MyIni _ini = new MyIni();
    CubeGridMap _gridMap;
    string _lastConfig;
    bool _hasSpawned = true, _isClient, _isServer;
    bool _allowBossBot = true, _nomadBotOnly, _creatureBotOnly;
    int _nextSpawnTime = 1000, _spawnTimer, _currentSpawnCount;

    public override void Close()
    {
      try
      {
        _ini?.Clear();
        _iniKeys?.Clear();
        _allSubtypes?.Clear();
        _subtypeToRole?.Clear();
        _creatureTypes?.Clear();
        _nomadTypes?.Clear();

        _fakeBlock = null;
        _allSubtypes = null;
        _block = null;
        _ini = null;
        _iniKeys = null;
        _subtypeToRole = null;
        _creatureTypes = null;
        _nomadTypes = null;
      }
      catch(Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in SpawnBlock.Close: {ex.Message}\n{ex.StackTrace}");
      }
      finally
      {
        NeedsUpdate = MyEntityUpdateEnum.NONE;
        base.Close();
      }
    }

    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
      _block = (Sandbox.ModAPI.IMyUpgradeModule)Entity;
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

      _allSubtypes.Clear();
      _creatureTypes.Clear();
      _nomadTypes.Clear();

      _allSubtypes.Add("Police_Bot");
      _allSubtypes.Add("Space_Skeleton");
      _allSubtypes.Add("Space_Zombie");
      _allSubtypes.Add("Ghost_Bot");
      _creatureTypes.Add("Space_Wolf");
      _nomadTypes.Add("Default_Astronaut");
      _nomadTypes.Add("Default_Astronaut_Female");

      _ini.Clear();
      _ini.Set("AiEnabled", "Min Spawn Interval", _minSecondsBetweenSpawns);
      _ini.Set("AiEnabled", "Max Spawn Interval", _maxSecondsBetweenSpawns);
      _ini.Set("AiEnabled", "Max Simultaneous Spawns", _maxSimultaneousSpawns);
      _ini.Set("AiEnabled", "Allow SoldierBot", true);
      _ini.Set("AiEnabled", "Allow GrinderBot", true);
      _ini.Set("AiEnabled", "Allow ZombieBot", true);
      _ini.Set("AiEnabled", "Allow GhostBot", true);
      _ini.Set("AiEnabled", "Allow BruiserBot", true);
      _ini.Set("AiEnabled", "NomadBots Only", _nomadBotOnly);
      _ini.Set("AiEnabled", "CreatureBots Only", _creatureBotOnly);

      _ini.SetSectionComment("AiEnabled", " \n Enable or Disable the spawning of certain types by switching\n their values to TRUE or FALSE \n ");
      _ini.SetComment("AiEnabled", "Min Spawn Interval", " \n The Minimum number of Seconds betweeen spawns. (min = 1)\n ");
      _ini.SetComment("AiEnabled", "Max Spawn Interval", " \n The Maximum number of Seconds between spawns.\n ");
      _ini.SetComment("AiEnabled", "Max Simultaneous Spawns", " \n The Maximum number of active spawns allowed at any given time.\n ");
      _ini.SetComment("AiEnabled", "Allow SoldierBot", " \n The SoldierBot uses an automatic rifle to hunt you down.\n ");
      _ini.SetComment("AiEnabled", "Allow GrinderBot", " \n The GrinderBot uses a grinder to hunt you down.\n ");
      _ini.SetComment("AiEnabled", "Allow ZombieBot", " \n The ZombieBot applies poison damage over time with its attacks.\n ");
      _ini.SetComment("AiEnabled", "Allow GhostBot", " \n The GhostBot applies cold damage over time with its attacks.\n ");
      _ini.SetComment("AiEnabled", "Allow BruiserBot", " \n The BruiserBot is a boss encounter; it is harder to kill than\n the others and packs a heavy punch.\n ");
      _ini.SetComment("AiEnabled", "NomadBots Only", " \n The NomadBot is a neutral encounter that will only attack when\n provoked. Enabling this will disable all others.\n ");
      _ini.SetComment("AiEnabled", "CreatureBots Only", " \n The CreatureBot can be used for hostile creatures (wolf, spider).\n Enabling this will disable all others, including NomadBot.\n ");

      _ini.Set("Additional Subtypes", "Subtype", "BotRole;#112233");
      _ini.SetSectionComment("Additional Subtypes", " \n You can have the block spawn additional characters by\n adding their subtypes below, one per line, in the following format:\n   SubtypeId = Role;ColorHexValue\n   Valid Roles: GRINDER, SOLDIER, ZOMBIE, GHOST, BRUISER,\n                        CREATURE, NOMAD\n   EXAMPLE: Default_Astronaut=GRINDER;#112233\n   NOTE: The same subtype cannot be used for multiple roles\n ");

      _lastConfig = _ini.ToString();
      _block.CustomData = _lastConfig;
    }

    void ParseConfig(MyIni ini)
    {
      // TODO: add Frosty_Zombie as a Ghost / Zombie ?

      _allSubtypes.Clear();
      _creatureTypes.Clear();
      _nomadTypes.Clear();

      var allowSoldier = ini.Get("AiEnabled", "Allow SoldierBot").ToBoolean(true);
      if (!allowSoldier)
      {
        _allSubtypes.Remove("Police_Bot");
      }
      else if (!_allSubtypes.Contains("Police_Bot"))
      {
        _allSubtypes.Add("Police_Bot");
      }

      var allowGrinder = ini.Get("AiEnabled", "Allow GrinderBot").ToBoolean(true);
      if (!allowGrinder)
      {
        _allSubtypes.Remove("Space_Skeleton");
      }
      else if (!_allSubtypes.Contains("Space_Skeleton"))
      {
        _allSubtypes.Add("Space_Skeleton");
      }

      var allowZombie = ini.Get("AiEnabled", "Allow ZombieBot").ToBoolean(true);
      if (!allowZombie)
      {
        _allSubtypes.Remove("Space_Zombie");
      }
      else if (!_allSubtypes.Contains("Space_Zombie"))
      { 
        _allSubtypes.Add("Space_Zombie");
      }

      var allowGhost = ini.Get("AiEnabled", "Allow GhostBot").ToBoolean(true);
      if (!allowGhost)
      {
        _allSubtypes.Remove("Ghost_Bot");
      }
      else if (!_allSubtypes.Contains("Ghost_Bot"))
      {
        _allSubtypes.Add("Ghost_Bot");
      }

      _creatureBotOnly = ini.Get("AiEnabled", "CreatureBots Only").ToBoolean(false);

      if (_creatureBotOnly)
      {
        _creatureTypes.Add("Space_Wolf");
      }

      _nomadBotOnly = ini.Get("AiEnabled", "NomadBots Only").ToBoolean(false);

      if (_nomadBotOnly)
      {
        _nomadTypes.Add("Default_Astronaut");
        _nomadTypes.Add("Default_Astronaut_Female");
      }

      _allowBossBot = ini.Get("AiEnabled", "Allow BruiserBot").ToBoolean(true);
      _minSecondsBetweenSpawns = Math.Max(1, ini.Get("AiEnabled", "Min Spawn Interval").ToInt32(60));
      _maxSecondsBetweenSpawns = Math.Max(_minSecondsBetweenSpawns, ini.Get("AiEnabled", "Max Spawn Interval").ToInt32(180));
      _maxSimultaneousSpawns = Math.Max(1, ini.Get("AiEnabled", "Max Simultaneous Spawns").ToInt32(2));

      if (_maxSecondsBetweenSpawns * 60 > _nextSpawnTime)
      {
        _spawnTimer = _nextSpawnTime - 300;
      }

      _iniKeys.Clear();
      _subtypeToRole.Clear();
      ini.GetKeys("Additional Subtypes", _iniKeys);

      foreach (var iniKey in _iniKeys)
      {
        var subtype = iniKey.Name;
        var kvp = ini.Get(iniKey.Section, subtype).ToString("").Split(';');
        var role = kvp[0].ToUpperInvariant();
        var color = (kvp.Length > 1) ? kvp[1] : "";

        if (!string.IsNullOrWhiteSpace(subtype) && !string.IsNullOrWhiteSpace(role)
          && !subtype.Equals("Police_Bot", StringComparison.OrdinalIgnoreCase)
          && !subtype.Equals("Space_Skeleton", StringComparison.OrdinalIgnoreCase)
          && !subtype.Equals("Space_Zombie", StringComparison.OrdinalIgnoreCase)
          && !subtype.Equals("Ghost_Bot", StringComparison.OrdinalIgnoreCase)
          && !subtype.Equals("Boss_Bot", StringComparison.OrdinalIgnoreCase))
        {
          switch (role)
          {
            case "SOLDIER":

              _subtypeToRole[subtype] = new KeyValuePair<string, string>(role, color);

              if (allowSoldier)
              {
                if (!_allSubtypes.Contains(subtype))
                {
                  _allSubtypes.Add(subtype);
                }
              }
              else
              {
                _allSubtypes.Remove(subtype);
              }
              break;
            case "GRINDER":

              _subtypeToRole[subtype] = new KeyValuePair<string, string>(role, color);

              if (allowGrinder)
              {
                if (!_allSubtypes.Contains(subtype))
                {
                  _allSubtypes.Add(subtype);
                }
              }
              else
              {
                _allSubtypes.Remove(subtype);
              }
              break;
            case "ZOMBIE":

              _subtypeToRole[subtype] = new KeyValuePair<string, string>(role, color);

              if (allowZombie)
              {
                if (!_allSubtypes.Contains(subtype))
                {
                  _allSubtypes.Add(subtype);
                }
              }
              else
              {
                _allSubtypes.Remove(subtype);
              }
              break;
            case "GHOST":

              _subtypeToRole[subtype] = new KeyValuePair<string, string>(role, color);

              if (allowGhost)
              {
                if (!_allSubtypes.Contains(subtype))
                {
                  _allSubtypes.Add(subtype);
                }
              }
              else
              {
                _allSubtypes.Remove(subtype);
              }
              break;
            case "BRUISER":

              _subtypeToRole[subtype] = new KeyValuePair<string, string>(role, color);

              if (_allowBossBot)
              {
                if (!_allSubtypes.Contains(subtype))
                {
                  _allSubtypes.Add(subtype);
                }
              }
              else
              {
                _allSubtypes.Remove(subtype);
              }
              break;

            case "NOMAD":

              _subtypeToRole[subtype] = new KeyValuePair<string, string>(role, color);

              if (!_allSubtypes.Contains(subtype))
                _allSubtypes.Add(subtype);

              if (!_nomadTypes.Contains(subtype))
                _nomadTypes.Add(subtype);

              break;

            case "CREATURE":

              _subtypeToRole[subtype] = new KeyValuePair<string, string>(role, color);

              if (!_allSubtypes.Contains(subtype))
                _allSubtypes.Add(subtype);

              if (!_creatureTypes.Contains(subtype))
                _creatureTypes.Add(subtype);

              break;
          }
        }
      }

      ini.Clear();
      ini.Set("AiEnabled", "Min Spawn Interval", _minSecondsBetweenSpawns);
      ini.Set("AiEnabled", "Max Spawn Interval", _maxSecondsBetweenSpawns);
      ini.Set("AiEnabled", "Max Simultaneous Spawns", _maxSimultaneousSpawns);
      ini.Set("AiEnabled", "Allow SoldierBot", allowSoldier);
      ini.Set("AiEnabled", "Allow GrinderBot", allowGrinder);
      ini.Set("AiEnabled", "Allow ZombieBot", allowZombie);
      ini.Set("AiEnabled", "Allow GhostBot", allowGhost);
      ini.Set("AiEnabled", "Allow BruiserBot", _allowBossBot);
      ini.Set("AiEnabled", "NomadBots Only", _nomadBotOnly);
      ini.Set("AiEnabled", "CreatureBots Only", _creatureBotOnly);

      ini.SetSectionComment("AiEnabled", " \n Enable or Disable the spawning of certain types by switching\n their values to TRUE or FALSE \n ");
      ini.SetComment("AiEnabled", "Min Spawn Interval", " \n The Minimum number of Seconds betweeen spawns. (min = 1)\n ");
      ini.SetComment("AiEnabled", "Max Spawn Interval", " \n The Maximum number of Seconds between spawns.\n ");
      ini.SetComment("AiEnabled", "Max Simultaneous Spawns", " \n The Maximum number of active spawns allowed at any given time.\n ");
      ini.SetComment("AiEnabled", "Allow SoldierBot", " \n The SoldierBot uses an automatic rifle to hunt you down.\n ");
      ini.SetComment("AiEnabled", "Allow GrinderBot", " \n The GrinderBot uses a grinder to hunt you down.\n ");
      ini.SetComment("AiEnabled", "Allow ZombieBot", " \n The ZombieBot applies poison damage over time with its attacks.\n ");
      ini.SetComment("AiEnabled", "Allow GhostBot", " \n The GhostBot applies cold damage over time with its attacks.\n ");
      ini.SetComment("AiEnabled", "Allow BruiserBot", " \n The BruiserBot is a boss encounter; it is harder to kill than\n the others and packs a heavy punch.\n ");
      ini.SetComment("AiEnabled", "NomadBots Only", " \n The NomadBot is a neutral encounter that will only attack when\n provoked. Enabling this will disable all others.\n ");
      ini.SetComment("AiEnabled", "CreatureBots Only", " \n The CreatureBot can be used for hostile creatures (wolf, spider).\n Enabling this will disable all others, including NomadBot.\n ");

      if (_subtypeToRole.Count > 0)
      {
        foreach (var kvp in _subtypeToRole)
        {
          ini.Set("Additional Subtypes", kvp.Key, $"{kvp.Value.Key};{kvp.Value.Value}");
        }
      }
      else
      {
        ini.Set("Additional Subtypes", "Subtype", "BotRole;#112233");
      }

      ini.SetSectionComment("Additional Subtypes", " \n You can have the block spawn additional characters by\n adding their subtypes below, one per line, in the following format:\n   SubtypeId = Role;ColorHexValue\n   Valid Roles: GRINDER, SOLDIER, ZOMBIE, GHOST, BRUISER,\n                        CREATURE, NOMAD\n   EXAMPLE: Default_Astronaut=GRINDER;#112233\n   NOTE: The same subtype cannot be used for multiple roles\n ");

      _lastConfig = ini.ToString();
      _block.CustomData = _lastConfig;
    }

    public override void UpdateAfterSimulation100()
    {
      try
      {
        if (_isClient || AiSession.Instance?.Registered != true || !AiSession.Instance.CanSpawn)
          return;

        if (_block == null || _block.MarkedForClose || !_block.Enabled || !_block.IsFunctional || !_block.IsWorking || AiSession.Instance.Players.Count == 0)
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

        if (_currentSpawnCount >= _maxSimultaneousSpawns || AiSession.Instance.GlobalSpawnTimer < 60 || (_hasSpawned && CheckSpawnTimer()))
          return;

        if (_gridMap == null)
        {
          List<IMyCubeGrid> list;
          if (!AiSession.Instance.GridGroupListStack.TryPop(out list))
            list = new List<IMyCubeGrid>();
          else
            list.Clear();

          _block.CubeGrid.GetGridGroup(GridLinkTypeEnum.Logical).GetGrids(list);

          MyCubeGrid biggest = _block.CubeGrid as MyCubeGrid;
          foreach (var g in list)
          {
            if (g.GridSize > 1 && g.WorldAABB.Volume > biggest.PositionComp.WorldAABB.Volume)
              biggest = g as MyCubeGrid;
          }

          list.Clear();
          AiSession.Instance.GridGroupListStack.Push(list);
          _gridMap = AiSession.Instance.GetGridGraph(biggest, _block.WorldMatrix);
        }
        else if (_gridMap.LastActiveTicks > 100)
        {
          _gridMap.LastActiveTicks = 10;
        }

        if (_gridMap == null || !_gridMap.Ready)
          return;

        var grid = _gridMap.MainGrid;
        bool hasRegular = _allSubtypes.Count > 0;

        if (!hasRegular && !_allowBossBot)
          return;

        var maxPlayerDistance = AiSession.Instance.ModSaveData.MaxBotHuntingDistanceEnemy;
        maxPlayerDistance *= maxPlayerDistance;

        foreach (var kvp in AiSession.Instance.Players)
        {
          var player = kvp.Value?.Character;
          if (player == null || player.IsDead)
            continue;

          var playerPosition = player.PositionComp.WorldAABB.Center;
          if (Vector3D.DistanceSquared(playerPosition, _block.WorldAABB.Center) < maxPlayerDistance)
          {
            string botType, role = null;
            bool assignRole = true;

            if (_creatureBotOnly && _creatureTypes?.Count > 0)
            {
              assignRole = false;
              role = "CREATURE";
              var num = MyUtils.GetRandomInt(0, _creatureTypes.Count);
              botType = _creatureTypes[num];
            }
            else if (_nomadBotOnly && _nomadTypes?.Count > 0)
            {
              assignRole = false;
              role = "NOMAD";
              var num = MyUtils.GetRandomInt(0, _nomadTypes.Count);
              botType = _nomadTypes[num];
            }
            else if (_allowBossBot && (!hasRegular || _gridMap.TotalSpawnCount > 10 && MyUtils.GetRandomInt(1, 101) >= _gridMap.BossSpawnChance))
            {
              _gridMap.BossSpawnChance = 100;
              _gridMap.TotalSpawnCount = 0;
              botType = "Boss_Bot";
            }
            else if (hasRegular)
            {
              var num = MyUtils.GetRandomInt(0, _allSubtypes.Count);
              botType = _allSubtypes[num];

              if (_gridMap.TotalSpawnCount > 3)
                _gridMap.BossSpawnChance--;
            }
            else
              break;

            Color? color = null;
            KeyValuePair<string, string> roleAndColor;
            if (_subtypeToRole.TryGetValue(botType, out roleAndColor))
            {
              if (assignRole) 
                role = roleAndColor.Key;
  
              color = ColorExtensions.FromHtml(roleAndColor.Value);
            }

            var intVecFwd = -Base6Directions.GetIntVector(_block.Orientation.Forward);
            var localPoint = _block.Position + intVecFwd;

            while (_block.CubeGrid.CubeExists(localPoint))
              localPoint += intVecFwd;

            var spawnPoint = _block.CubeGrid.GridIntegerToWorld(localPoint);
            var posOr = new MyPositionAndOrientation(spawnPoint, (Vector3)_block.WorldMatrix.Backward, (Vector3)_block.WorldMatrix.Up);

            var bot = BotFactory.SpawnNPC(botType, "", posOr, grid, role, color: color);
            if (bot != null)
            {
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
