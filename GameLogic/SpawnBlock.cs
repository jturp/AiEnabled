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
using Sandbox.Game.Entities.Character.Components;

namespace AiEnabled.GameLogic
{
  [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "BotSpawner")]
  public class Spawner : MyGameLogicComponent
  {
    internal class WeaponInfo
    {
      internal string Subtype;
      internal int UseChanceThreshold;

      internal void SetInfo(string subtype, int threshold)
      {
        Subtype = subtype;
        UseChanceThreshold = threshold;
      }
    }

    List<MyIniKey> _iniKeys = new List<MyIniKey>();
    List<string> _iniLines = new List<string>();
    Dictionary<string, KeyValuePair<string, string>> _subtypeToRole = new Dictionary<string, KeyValuePair<string, string>>(10);
    Dictionary<string, string> _botTypeToLootContainerId = new Dictionary<string, string>(10);
    Stack<WeaponInfo> _wepInfoStack = new Stack<WeaponInfo>(10);
    List<string> _allSubtypes = new List<string>(10);
    List<string> _creatureTypes = new List<string>(5);
    List<string> _neutralTypes = new List<string>(5);
    List<WeaponInfo> _weaponSubtypes = new List<WeaponInfo>(5);
    string _soldierColor, _zombieColor, _grinderColor, _nomadColor, _enforcerColor;
    bool _useRandomColor = true, _allowFlight = true, _spidersOnly, _wolvesOnly, _customOnly;

    int _minSecondsBetweenSpawns = 60;
    int _maxSecondsBetweenSpawns = 180;
    int _maxSimultaneousSpawns = 2;

    Sandbox.ModAPI.IMyUpgradeModule _block;
    MyShipController _fakeBlock = new MyShipController();
    MyIni _ini = new MyIni();
    CubeGridMap _gridMap;
    string _lastConfig, _knownLootContainers;
    bool _hasSpawned = true, _isClient, _isServer;
    bool _allowBossBot = true, _nomadBotOnly, _enforcerBotOnly, _neutralBotOnly, _creatureBotOnly;
    int _nextSpawnTime = 1000, _spawnTimer, _currentSpawnCount;

    public override void Close()
    {
      try
      {
        //_block.CustomDataChanged -= Block_CustomDataChanged;

        _ini?.Clear();
        _iniKeys?.Clear();
        _iniLines?.Clear();
        _allSubtypes?.Clear();
        _subtypeToRole?.Clear();
        _creatureTypes?.Clear();
        _neutralTypes?.Clear();
        _weaponSubtypes?.Clear();
        _wepInfoStack?.Clear();
        _botTypeToLootContainerId?.Clear();

        _fakeBlock = null;
        _allSubtypes = null;
        _block = null;
        _ini = null;
        _iniKeys = null;
        _iniLines = null;
        _subtypeToRole = null;
        _creatureTypes = null;
        _neutralTypes = null;
        _weaponSubtypes = null;
        _wepInfoStack = null;
        _botTypeToLootContainerId = null;
      }
      catch(Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in SpawnBlock.Close: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
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

        if (_isClient || _block?.CubeGrid?.Physics == null)
          return;

        if (string.IsNullOrEmpty(_knownLootContainers))
        {
          if (AiSession.Instance.KnownLootContainerIds.Count > 0)
          {
            _knownLootContainers = " " + string.Join("\n ", AiSession.Instance.KnownLootContainerIds);
          }
          else
          {
            _knownLootContainers = "";
            foreach (var botDef in MyDefinitionManager.Static.GetBotDefinitions())
            {
              var agentDef = botDef as MyAgentDefinition;
              if (agentDef != null)
              {
                _knownLootContainers += $" {agentDef.InventoryContainerTypeId.SubtypeName}\n";
              }
            }
          }

          _knownLootContainers.TrimEnd('\n', ' ');
        }

        SetupIni();

        //_block.CustomDataChanged += Block_CustomDataChanged;
        NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
      }
      catch (Exception e)
      {
        MyAPIGateway.Utilities.ShowMissionScreen("Exception Occurred", null, null,$"In UpdateOnceBeforeFrame:\n{e.Message}\n{e.StackTrace}");
      }

      base.UpdateOnceBeforeFrame();
    }

    private void Block_CustomDataChanged(string data)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(data) || !_ini.TryParse(data))
          SetupIni(false);
        else
          ParseConfig(_ini);
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in SpawnBlock.OnCustomDataChanged: {ex}");
      }
    }

    void SetupIni(bool parseNew = true)
    {
      if (parseNew && _ini.TryParse(_block.CustomData) && _ini.ContainsSection("AiEnabled"))
      {
        ParseConfig(_ini);
        return;
      }

      var defaultColor = Color.Red;
      var hexColor = $"#{defaultColor.R:X2}{defaultColor.G:X2}{defaultColor.B:X2}";
      _soldierColor = _zombieColor = _grinderColor = _nomadColor = _enforcerColor = hexColor;

      _allSubtypes.Clear();
      _creatureTypes.Clear();
      _neutralTypes.Clear();
      _weaponSubtypes.Clear();

      _allSubtypes.Add("Police_Bot");
      _allSubtypes.Add("Space_Skeleton");
      _allSubtypes.Add("Space_Zombie");
      _allSubtypes.Add("Ghost_Bot");
      _creatureTypes.Add("Space_Wolf");
      _creatureTypes.Add("Space_spider_black");
      _creatureTypes.Add("Space_spider_brown");
      _creatureTypes.Add("Space_spider_green");
      _creatureTypes.Add("Space_spider");
      _neutralTypes.Add("Default_Astronaut");
      _neutralTypes.Add("Default_Astronaut_Female");

      _ini.Clear();
      _ini.Set("AiEnabled", "Known Loot Container Ids", _knownLootContainers);
      _ini.Set("AiEnabled", "Min Spawn Interval", _minSecondsBetweenSpawns);
      _ini.Set("AiEnabled", "Max Spawn Interval", _maxSecondsBetweenSpawns);
      _ini.Set("AiEnabled", "Max Simultaneous Spawns", _maxSimultaneousSpawns);
      _ini.Set("AiEnabled", "Allow Spawns to Fly", _allowFlight);
      _ini.Set("AiEnabled", "Use Random Spawn Colors", true);
      _ini.Set("AiEnabled", "Allow SoldierBot", true);
      _ini.Set("AiEnabled", "SoldierBot Color", hexColor);
      _ini.Set("AiEnabled", "SoldierBot Loot Container Id", string.Empty);
      _ini.Set("AiEnabled", "Allow GrinderBot", true);
      _ini.Set("AiEnabled", "GrinderBot Color", hexColor);
      _ini.Set("AiEnabled", "GrinderBot Loot Container Id", string.Empty);
      _ini.Set("AiEnabled", "Allow ZombieBot", true);
      _ini.Set("AiEnabled", "ZombieBot Color", hexColor);
      _ini.Set("AiEnabled", "ZombieBot Loot Container Id", string.Empty);
      _ini.Set("AiEnabled", "Allow GhostBot", true);
      _ini.Set("AiEnabled", "GhostBot Loot Container Id", string.Empty);
      _ini.Set("AiEnabled", "Allow BruiserBot", true);
      _ini.Set("AiEnabled", "BruiserBot Loot Container Id", string.Empty);
      _ini.Set("AiEnabled", "Neutral Bots Only", _neutralBotOnly);
      _ini.Set("AiEnabled", "NomadBots Only", _nomadBotOnly);
      _ini.Set("AiEnabled", "EnforcerBots Only", _enforcerBotOnly);
      _ini.Set("AiEnabled", "NomadBot Color", hexColor);
      _ini.Set("AiEnabled", "EnforcerBot Color", hexColor);
      _ini.Set("AiEnabled", "NomadBot Loot Container Id", string.Empty);
      _ini.Set("AiEnabled", "EnforcerBot Loot Container Id", string.Empty);
      _ini.Set("AiEnabled", "CreatureBots Only", _creatureBotOnly);
      _ini.Set("AiEnabled", "Wolves Only", _wolvesOnly);
      _ini.Set("AiEnabled", "Spiders Only", _spidersOnly);
      _ini.Set("AiEnabled", "Wolf Loot Container Id", string.Empty);
      _ini.Set("AiEnabled", "Spider Loot Container Id", string.Empty);
      _ini.Set("AiEnabled", "Custom NPCs Only", _customOnly);

      _ini.SetSectionComment("AiEnabled", " \n Enable or Disable the spawning of certain types by switching\n their values to TRUE or FALSE. Colors must be hex values (ie #FF0000).\n Loot Container Ids are taken from mod / game files, leave blank\n to use the value from the character definition.\n ");
      _ini.SetComment("AiEnabled", "Known Loot Container Ids", " \n These are the loot container ids found by the mod. This may not be\n all of them. NOTE: you only need to assign them if you want to\n override the defaults.\n ");
      _ini.SetComment("AiEnabled", "Min Spawn Interval", " \n The Minimum number of Seconds between spawns. (min = 1)\n ");
      _ini.SetComment("AiEnabled", "Max Spawn Interval", " \n The Maximum number of Seconds between spawns.\n ");
      _ini.SetComment("AiEnabled", "Max Simultaneous Spawns", " \n The Maximum number of active spawns allowed at any given time.\n ");
      _ini.SetComment("AiEnabled", "Allow Spawns to Fly", " \n If True, bots spawned by this block will be allowed to fly.\n Note that Admin settings override this, and bot must have\n a valid jetpack component.\n ");
      _ini.SetComment("AiEnabled", "Use Random Spawn Colors", " \n If True, all spawns will use a random color.\n ");
      _ini.SetComment("AiEnabled", "Allow SoldierBot", " \n The SoldierBot uses an automatic rifle to hunt you down.\n ");
      _ini.SetComment("AiEnabled", "Allow GrinderBot", " \n The GrinderBot uses a grinder to hunt you down.\n ");
      _ini.SetComment("AiEnabled", "Allow ZombieBot", " \n The ZombieBot applies poison damage over time with its attacks.\n ");
      _ini.SetComment("AiEnabled", "Allow GhostBot", " \n The GhostBot applies cold damage over time with its attacks.\n Not colorable.\n ");
      _ini.SetComment("AiEnabled", "Allow BruiserBot", " \n The BruiserBot is a boss encounter; it is harder to kill than\n the others and packs a heavy punch. Not colorable.\n ");
      _ini.SetComment("AiEnabled", "Neutral Bots Only", " \n Neutral Bots are neutral encounters that will only attack if / when\n provoked. Enabling this will disable all others.\n ");
      _ini.SetComment("AiEnabled", "CreatureBots Only", " \n The CreatureBot can be used for hostile creatures (wolf, spider).\n Enabling this will disable all others, including Neutral Bots.\n ");
      _ini.SetComment("AiEnabled", "Custom NPCs Only", " \n If you only want the block to spawn your custom NPCs, set this to true.\n Requires valid entries in the Additional Subtypes section below.\n ");

      _ini.Set("SoldierBot Weapon Subtypes", "AddWeaponSubtypeHere", 100);
      _ini.SetSectionComment("SoldierBot Weapon Subtypes", "\n You can add additional weapon subtypes for the SoldierBot to use\n by placing one subtype per line, along with the minimum threshold\n for each. A roll between 0 and 100 will be used to determine\n which subtype to use based on highest threshold that is > rolled #\n If roll doesn't match anything, bot will use the default rapid fire rifle\n EXAMPLE:\n    BasicHandHeldLauncherItem = 90 (if roll is > 90)\n    SuperCoolModRifle = 75 (if roll > 75)\n    ElitePistolItem = 10 (if roll > 10)\n ");

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
      _neutralTypes.Clear();
      _iniLines.Clear();

      for (int i = 0; i < _weaponSubtypes.Count; i++)
        _wepInfoStack.Push(_weaponSubtypes[i]);

      _weaponSubtypes.Clear();

      var defaultColor = Color.Red;
      var hexColor = $"#{defaultColor.R:X2}{defaultColor.G:X2}{defaultColor.B:X2}";
      _customOnly = ini.Get("AiEnabled", "Custom NPCs Only").ToBoolean(false);
      _allowFlight = ini.Get("AiEnabled", "Allow Spawns to Fly").ToBoolean(_allowFlight);

      var allowSoldier = ini.Get("AiEnabled", "Allow SoldierBot").ToBoolean(true);
      _soldierColor = ini.Get("AiEnabled", "SoldierBot Color").ToString(hexColor);
      if (!allowSoldier || _customOnly)
      {
        _allSubtypes.Remove("Police_Bot");
      }
      else if (!_allSubtypes.Contains("Police_Bot"))
      {
        _allSubtypes.Add("Police_Bot");
      }

      var allowGrinder = ini.Get("AiEnabled", "Allow GrinderBot").ToBoolean(true);
      _grinderColor = ini.Get("AiEnabled", "GrinderBot Color").ToString(hexColor);
      if (!allowGrinder || _customOnly)
      {
        _allSubtypes.Remove("Space_Skeleton");
      }
      else if (!_allSubtypes.Contains("Space_Skeleton"))
      {
        _allSubtypes.Add("Space_Skeleton");
      }

      var allowZombie = ini.Get("AiEnabled", "Allow ZombieBot").ToBoolean(true);
      _zombieColor = ini.Get("AiEnabled", "ZombieBot Color").ToString(hexColor);
      if (!allowZombie || _customOnly)
      {
        _allSubtypes.Remove("Space_Zombie");
      }
      else if (!_allSubtypes.Contains("Space_Zombie"))
      { 
        _allSubtypes.Add("Space_Zombie");
      }

      var allowGhost = ini.Get("AiEnabled", "Allow GhostBot").ToBoolean(true);
      if (!allowGhost || _customOnly)
      {
        _allSubtypes.Remove("Ghost_Bot");
      }
      else if (!_allSubtypes.Contains("Ghost_Bot"))
      {
        _allSubtypes.Add("Ghost_Bot");
      }

      _creatureBotOnly = ini.Get("AiEnabled", "CreatureBots Only").ToBoolean(false);
      _wolvesOnly = ini.Get("AiEnabled", "Wolves Only").ToBoolean(false);
      _spidersOnly = ini.Get("AiEnabled", "Spiders Only").ToBoolean(false);

      if (_creatureBotOnly)
      {
        if (_wolvesOnly)
        {
          _creatureTypes.Add("Space_Wolf");
        }
        else if (_spidersOnly)
        {
          _creatureTypes.Add("Space_spider_black");
          _creatureTypes.Add("Space_spider_brown");
          _creatureTypes.Add("Space_spider_green");
          _creatureTypes.Add("Space_spider");
        }
        else
        {
          _creatureTypes.Add("Space_Wolf");
          _creatureTypes.Add("Space_spider_black");
          _creatureTypes.Add("Space_spider_brown");
          _creatureTypes.Add("Space_spider_green");
          _creatureTypes.Add("Space_spider");
        }
      }

      _neutralBotOnly = ini.Get("AiEnabled", "Neutral Bots Only").ToBoolean(false);
      _nomadBotOnly = ini.Get("AiEnabled", "NomadBots Only").ToBoolean(false);
      _nomadColor = ini.Get("AiEnabled", "NomadBot Color").ToString(hexColor);
      _enforcerBotOnly = ini.Get("AiEnabled", "EnforcerBots Only").ToBoolean(false);
      _enforcerColor = ini.Get("AiEnabled", "Enforcer Color").ToString(hexColor);

      if (_neutralBotOnly)
      {
        _neutralTypes.Add("Default_Astronaut");
        _neutralTypes.Add("Default_Astronaut_Female");
      }

      _useRandomColor = ini.Get("AiEnabled", "Use Random Spawn Colors").ToBoolean(true);
      _allowBossBot = ini.Get("AiEnabled", "Allow BruiserBot").ToBoolean(true);
      _minSecondsBetweenSpawns = Math.Max(1, ini.Get("AiEnabled", "Min Spawn Interval").ToInt32(60));
      _maxSecondsBetweenSpawns = Math.Max(_minSecondsBetweenSpawns, ini.Get("AiEnabled", "Max Spawn Interval").ToInt32(180));
      _maxSimultaneousSpawns = Math.Max(1, ini.Get("AiEnabled", "Max Simultaneous Spawns").ToInt32(2));

      if (_maxSecondsBetweenSpawns * 60 > _nextSpawnTime)
      {
        _spawnTimer = _nextSpawnTime - 300;
      }

      var soldierLoot = ini.Get("AiEnabled", "SoldierBot Loot Container Id").ToString(string.Empty);
      var grinderLoot = ini.Get("AiEnabled", "GrinderBot Loot Container Id").ToString(string.Empty);
      var zombieLoot = ini.Get("AiEnabled", "ZombieBot Loot Container Id").ToString(string.Empty);
      var ghostLoot = ini.Get("AiEnabled", "GhostBot Loot Container Id").ToString(string.Empty);
      var bruiserLoot = ini.Get("AiEnabled", "BruiserBot Loot Container Id").ToString(string.Empty);
      var nomadLoot = ini.Get("AiEnabled", "NomadBot Loot Container Id").ToString(string.Empty);
      var enforcerLoot = ini.Get("AiEnabled", "EnforcerBot Loot Container Id").ToString(string.Empty);
      var wolfLoot = ini.Get("AiEnabled", "Wolf Loot Container Id").ToString(string.Empty);
      var spiderLoot = ini.Get("AiEnabled", "Spider Loot Container Id").ToString(string.Empty);

      _botTypeToLootContainerId.Clear();
      _botTypeToLootContainerId["SoldierLoot"] = soldierLoot;
      _botTypeToLootContainerId["GrinderLoot"] = grinderLoot;
      _botTypeToLootContainerId["ZombieLoot"] = zombieLoot;
      _botTypeToLootContainerId["GhostLoot"] = ghostLoot;
      _botTypeToLootContainerId["BruiserLoot"] = bruiserLoot;
      _botTypeToLootContainerId["NomadLoot"] = nomadLoot;
      _botTypeToLootContainerId["EnforcerLoot"] = enforcerLoot;
      _botTypeToLootContainerId["WolfLoot"] = wolfLoot;
      _botTypeToLootContainerId["SpiderLoot"] = spiderLoot;

      _iniKeys.Clear();
      ini.GetKeys("SoldierBot Weapon Subtypes", _iniKeys);

      foreach (var iniKey in _iniKeys)
      {
        var subtype = iniKey.Name;
        var threshold = ini.Get("SoldierBot Weapon Subtypes", subtype).ToInt32(-1);
        if (threshold < 0 || threshold > 100)
          continue;

        if (AiSession.Instance.IsBotAllowedToUse("Soldier", subtype))
        {
          bool found = false;
          for (int j = 0; j < _weaponSubtypes.Count; j++)
          {
            if (_weaponSubtypes[j].Subtype == subtype)
            {
              found = true;
              break;
            }
          }

          if (!found)
          {
            var info = _wepInfoStack.Count > 0 ? _wepInfoStack.Pop() : new WeaponInfo();
            info.SetInfo(subtype, (int)threshold);
            _weaponSubtypes.Add(info);
          }
        }
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

        if (!string.IsNullOrWhiteSpace(subtype) && !string.IsNullOrWhiteSpace(role) && (_customOnly
          || (!subtype.Equals("Police_Bot", StringComparison.OrdinalIgnoreCase)
          && !subtype.Equals("Space_Skeleton", StringComparison.OrdinalIgnoreCase)
          && !subtype.Equals("Space_Zombie", StringComparison.OrdinalIgnoreCase)
          && !subtype.Equals("Ghost_Bot", StringComparison.OrdinalIgnoreCase)
          && !subtype.Equals("Boss_Bot", StringComparison.OrdinalIgnoreCase))))
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

              if (!_neutralTypes.Contains(subtype))
                _neutralTypes.Add(subtype);

              break;

            case "ENFORCER":

              _subtypeToRole[subtype] = new KeyValuePair<string, string>(role, color);

              if (!_allSubtypes.Contains(subtype))
                _allSubtypes.Add(subtype);

              if (!_neutralTypes.Contains(subtype))
                _neutralTypes.Add(subtype);

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
      ini.Set("AiEnabled", "Known Loot Container Ids", _knownLootContainers);
      ini.Set("AiEnabled", "Min Spawn Interval", _minSecondsBetweenSpawns);
      ini.Set("AiEnabled", "Max Spawn Interval", _maxSecondsBetweenSpawns);
      ini.Set("AiEnabled", "Max Simultaneous Spawns", _maxSimultaneousSpawns);
      ini.Set("AiEnabled", "Allow Spawns to Fly", _allowFlight);
      ini.Set("AiEnabled", "Use Random Spawn Colors", _useRandomColor);
      ini.Set("AiEnabled", "Allow SoldierBot", allowSoldier);
      ini.Set("AiEnabled", "SoldierBot Color", _soldierColor);
      ini.Set("AiEnabled", "SoldierBot Loot Container Id", soldierLoot);
      ini.Set("AiEnabled", "Allow GrinderBot", allowGrinder);
      ini.Set("AiEnabled", "GrinderBot Color", _grinderColor);
      ini.Set("AiEnabled", "GrinderBot Loot Container Id", grinderLoot);
      ini.Set("AiEnabled", "Allow ZombieBot", allowZombie);
      ini.Set("AiEnabled", "ZombieBot Color", _zombieColor);
      ini.Set("AiEnabled", "ZombieBot Loot Container Id", zombieLoot);
      ini.Set("AiEnabled", "Allow GhostBot", allowGhost);
      ini.Set("AiEnabled", "GhostBot Loot Container Id", ghostLoot);
      ini.Set("AiEnabled", "Allow BruiserBot", _allowBossBot);
      ini.Set("AiEnabled", "BruiserBot Loot Container Id", bruiserLoot);
      ini.Set("AiEnabled", "Neutral Bots Only", _neutralBotOnly);
      ini.Set("AiEnabled", "NomadBots Only", _nomadBotOnly);
      ini.Set("AiEnabled", "EnforcerBots Only", _enforcerBotOnly);
      ini.Set("AiEnabled", "NomadBot Color", _nomadColor);
      ini.Set("AiEnabled", "EnforcerBot Color", _enforcerColor);
      ini.Set("AiEnabled", "NomadBot Loot Container Id", nomadLoot);
      ini.Set("AiEnabled", "EnforcerBot Loot Container Id", enforcerLoot);
      ini.Set("AiEnabled", "CreatureBots Only", _creatureBotOnly);
      ini.Set("AiEnabled", "Wolves Only", _wolvesOnly);
      ini.Set("AiEnabled", "Spiders Only", _spidersOnly);
      ini.Set("AiEnabled", "Wolf Loot Container Id", wolfLoot);
      ini.Set("AiEnabled", "Spider Loot Container Id", spiderLoot);
      ini.Set("AiEnabled", "Custom NPCs Only", _customOnly);

      ini.SetSectionComment("AiEnabled", " \n Enable or Disable the spawning of certain types by switching\n their values to TRUE or FALSE. Colors must be hex values (ie #FF0000).\n Loot Container Ids are taken from mod / game files, leave blank\n to use the value from the character definition.\n ");
      ini.SetComment("AiEnabled", "Known Loot Container Ids", " \n These are the loot container ids found by the mod. This may not be\n all of them. NOTE: you only need to assign them if you want to\n override the defaults.\n ");
      ini.SetComment("AiEnabled", "Min Spawn Interval", " \n The Minimum number of Seconds between spawns. (min = 1)\n ");
      ini.SetComment("AiEnabled", "Max Spawn Interval", " \n The Maximum number of Seconds between spawns.\n ");
      ini.SetComment("AiEnabled", "Max Simultaneous Spawns", " \n The Maximum number of active spawns allowed at any given time.\n ");
      ini.SetComment("AiEnabled", "Allow Spawns to Fly", " \n If True, bots spawned by this block will be allowed to fly.\n Note that Admin settings override this, and bot must have\n a valid jetpack component.\n ");
      ini.SetComment("AiEnabled", "Use Random Spawn Colors", " \n If True, all spawns will use a random color.\n ");
      ini.SetComment("AiEnabled", "Allow SoldierBot", " \n The SoldierBot uses an automatic rifle to hunt you down.\n ");
      ini.SetComment("AiEnabled", "Allow GrinderBot", " \n The GrinderBot uses a grinder to hunt you down.\n ");
      ini.SetComment("AiEnabled", "Allow ZombieBot", " \n The ZombieBot applies poison damage over time with its attacks.\n ");
      ini.SetComment("AiEnabled", "Allow GhostBot", " \n The GhostBot applies cold damage over time with its attacks.\n Not colorable.\n ");
      ini.SetComment("AiEnabled", "Allow BruiserBot", " \n The BruiserBot is a boss encounter; it is harder to kill than\n the others and packs a heavy punch. Not colorable.\n ");
      ini.SetComment("AiEnabled", "Neutral Bots Only", " \n Neutral Bots are neutral encounters that will only attack if / when\n provoked. Enabling this will disable all others.\n ");
      ini.SetComment("AiEnabled", "CreatureBots Only", " \n The CreatureBot can be used for hostile creatures (wolf, spider).\n Enabling this will disable all others, including Neutral Bots.\n ");
      ini.SetComment("AiEnabled", "Custom NPCs Only", " \n If you only want the block to spawn your custom NPCs, set this to true.\n Requires valid entries in the Additional Subtypes section below.\n ");

      if (_weaponSubtypes.Count > 0)
      {
        for (int i = 0; i < _weaponSubtypes.Count; i++)
        {
          var weapon = _weaponSubtypes[i];
          ini.Set("SoldierBot Weapon Subtypes", weapon.Subtype, weapon.UseChanceThreshold);
        }
      }
      else
      {
        ini.Set("SoldierBot Weapon Subtypes", "AddWeaponSubtypeHere", 100);
      }

      bool clearIni = false;
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
        ini.Set("AiEnabled", "Custom NPCs Only", false);

        if (_customOnly)
        {
          clearIni = true;
          _customOnly = false;
          ini.Clear();
        }
      }

      if (!clearIni)
      {
        ini.SetSectionComment("SoldierBot Weapon Subtypes", "\n You can add additional weapon subtypes for the SoldierBot to use\n by placing one subtype per line, along with the minimum threshold\n for each. A roll between 0 and 100 will be used to determine\n which subtype to use based on highest threshold that is > rolled #\n If roll doesn't match anything, bot will use the default rapid fire rifle\n EXAMPLE:\n    BasicHandHeldLauncherItem = 90 (if roll is > 90)\n    SuperCoolModRifle = 75 (if roll > 75)\n    ElitePistolItem = 10 (if roll > 10)\n ");

        ini.SetSectionComment("Additional Subtypes", " \n You can have the block spawn additional characters by\n adding their subtypes below, one per line, in the following format:\n   SubtypeId = Role;ColorHexValue\n   Valid Roles: GRINDER, SOLDIER, ZOMBIE, GHOST, BRUISER,\n                        CREATURE, NOMAD\n   EXAMPLE: Default_Astronaut=GRINDER;#112233\n   NOTE: The same subtype cannot be used for multiple roles\n ");
      }

      _lastConfig = ini.ToString();
      _block.CustomData = _lastConfig;
    }

    public override void UpdateAfterSimulation100()
    {
      try
      {
        if (_isClient || AiSession.Instance?.Registered != true)
          return;

        if (_block == null || _block.MarkedForClose)
          return;

        var data = _block.CustomData;
        if (data != _lastConfig)
          Block_CustomDataChanged(data);

        if (!_block.Enabled || !_block.IsFunctional || !_block.IsWorking || _fakeBlock.GridResourceDistributor.ResourceState == MyResourceStateEnum.NoPower)
          return;

        if (AiSession.Instance.Players.Count == 0 || !AiSession.Instance.CanSpawn)
          return;

        if (AiSession.Instance.FutureBotQueue.Count > 0) // make sure we spawn all saved helpers first
          return;

        if (_currentSpawnCount >= _maxSimultaneousSpawns || AiSession.Instance.GlobalSpawnTimer < 60 || (_hasSpawned && CheckSpawnTimer()))
          return;

        if (_gridMap == null)
        {
          _gridMap = AiSession.Instance.GetGridGraph((MyCubeGrid)_block.CubeGrid, _block.WorldMatrix);
        }
        else if (_gridMap.LastActiveTicks > 100)
        {
          _gridMap.LastActiveTicks = 10;
        }

        if (_gridMap == null || !_gridMap.Ready)
          return;

        var grid = _gridMap.MainGrid;
        bool hasRegular = _allSubtypes.Count > 0;

        if (!hasRegular && !_allowBossBot 
          && (!_creatureBotOnly || _creatureTypes == null || _creatureTypes.Count == 0) 
          && (!_neutralBotOnly || _neutralTypes == null || _neutralTypes.Count == 0))
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
            string botType = null, lootType = null, role = null;
            bool assignRole = true;

            if (_customOnly)
            {
              if (_subtypeToRole.Count > 0 && _allSubtypes.Count > 0)
              {
                var rand = MyUtils.GetRandomInt(_allSubtypes.Count);
                botType = _allSubtypes[rand];

                if (!_subtypeToRole.ContainsKey(botType))
                  botType = null;
              }

              if (botType == null)
                break;
            }
            else if (_creatureBotOnly && _creatureTypes?.Count > 0)
            {
              assignRole = false;
              role = "CREATURE";

              int num;
              if (_wolvesOnly)
              {
                num = 0;
              }
              else
              {
                num = MyUtils.GetRandomInt(0, _creatureTypes.Count);
              }

              botType = _creatureTypes[num];
              if (botType.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0)
                lootType = _botTypeToLootContainerId.GetValueOrDefault("WolfLoot");
              else 
                lootType = _botTypeToLootContainerId.GetValueOrDefault("SpiderLoot");
            }
            else if (_neutralBotOnly && _neutralTypes?.Count > 0)
            {
              assignRole = false;
              var num = MyUtils.GetRandomInt(0, _neutralTypes.Count);
              botType = _neutralTypes[num];

              if (_enforcerBotOnly)
                role = "ENFORCER";
              else if (_nomadBotOnly)
                role = "NOMAD";
              else
                role = MyUtils.GetRandomInt(0, 10) > 5 ? "ENFORCER" : "NOMAD";

              if (role == "NOMAD")
                lootType = _botTypeToLootContainerId.GetValueOrDefault("NomadLoot");
              else
                lootType = _botTypeToLootContainerId.GetValueOrDefault("EnforcerLoot");
            }
            else if (_allowBossBot && (!hasRegular || _gridMap.TotalSpawnCount > 10 && MyUtils.GetRandomInt(1, 101) >= _gridMap.BossSpawnChance))
            {
              _gridMap.BossSpawnChance = 100;
              _gridMap.TotalSpawnCount = 0;
              botType = "Boss_Bot";
              lootType = _botTypeToLootContainerId.GetValueOrDefault("BruiserLoot");
            }
            else if (hasRegular)
            {
              var num = MyUtils.GetRandomInt(0, _allSubtypes.Count);
              botType = _allSubtypes[num];

              if (_gridMap.TotalSpawnCount > 3)
                _gridMap.BossSpawnChance--;

              if (string.IsNullOrEmpty(lootType))
              {
                switch (botType)
                {
                  case "Police_Bot":
                    lootType = _botTypeToLootContainerId.GetValueOrDefault("SoldierLoot");
                    break;
                  case "Space_Zombie":
                    lootType = _botTypeToLootContainerId.GetValueOrDefault("ZombieLoot");
                    break;
                  case "Ghost_Bot":
                    lootType = _botTypeToLootContainerId.GetValueOrDefault("GhostLoot");
                    break;
                  case "Space_Skeleton":
                    lootType = _botTypeToLootContainerId.GetValueOrDefault("GrinderLoot");
                    break;
                  case "Boss_Bot":
                    lootType = _botTypeToLootContainerId.GetValueOrDefault("BruiserLoot");
                    break;
                }
              }
            }
            else
              break;

            Color? color = null;
            KeyValuePair<string, string> roleAndColor;

            if (_useRandomColor)
            {
              var r = MyUtils.GetRandomInt(0, 256);
              var g = MyUtils.GetRandomInt(0, 256);
              var b = MyUtils.GetRandomInt(0, 256);

              color = new Color(r, g, b);
            }

            if (_subtypeToRole.TryGetValue(botType, out roleAndColor))
            {
              if (assignRole) 
                role = roleAndColor.Key;
  
              if (!_useRandomColor)
                color = ColorExtensions.FromHtml(roleAndColor.Value);
            }
            else if (!_useRandomColor)
            {
              switch (botType)
              {
                case "Police_Bot":
                  color = ColorExtensions.FromHtml(_soldierColor);
                  break;
                case "Space_Zombie":
                  color = ColorExtensions.FromHtml(_zombieColor);
                  break;
                case "Space_Skeleton":
                  color = ColorExtensions.FromHtml(_grinderColor);
                  break;
                default:

                  if (_nomadBotOnly)
                    color = ColorExtensions.FromHtml(_nomadColor);
                  else if (_enforcerBotOnly)
                    color = ColorExtensions.FromHtml(_enforcerColor);
                  else
                    color = null;
  
                  break;
              }
            }

            string toolType = null;
            if (_weaponSubtypes.Count > 0)
            {
              bool setTool = false;
              if (!string.IsNullOrWhiteSpace(role))
                setTool = role.Equals("soldier", StringComparison.OrdinalIgnoreCase);
              else
                setTool = (botType == "Police_Bot");

              if (setTool)
              {
                var random = MyUtils.GetRandomInt(0, 101);
                var num = -1;

                for (int i = 0; i < _weaponSubtypes.Count; i++)
                {
                  var weapon = _weaponSubtypes[i];

                  if (random > weapon.UseChanceThreshold)
                  {
                    if (weapon.UseChanceThreshold > num)
                    {
                      num = weapon.UseChanceThreshold;
                      toolType = weapon.Subtype;
                    }
                    else if (weapon.UseChanceThreshold == num && MyUtils.GetRandomInt(0, 10) > 5)
                    {
                      toolType = weapon.Subtype;
                    }
                  }
                }
              }
            }

            if (toolType == null && role?.ToUpperInvariant() == "NOMAD")
            {
              var rand = MyUtils.GetRandomInt(0, 100);

              if (rand >= 90)
                toolType = "FullAutoPistolItem";
              else if (rand >= 60)
                toolType = "SemiAutoPistolItem";
            }

            var intVecFwd = -Base6Directions.GetIntVector(_block.Orientation.Forward);
            var localPoint = _block.Position + intVecFwd;

            while (_block.CubeGrid.CubeExists(localPoint))
              localPoint += intVecFwd;

            var spawnPoint = _block.CubeGrid.GridIntegerToWorld(localPoint);
            var posOr = new MyPositionAndOrientation(spawnPoint, (Vector3)_block.WorldMatrix.Backward, (Vector3)_block.WorldMatrix.Up);

            var botChar = BotFactory.SpawnNPC(botType, "", posOr, grid, role, toolType, color: color);
            if (botChar != null)
            {
              AiSession.Instance.GlobalSpawnTimer = 0;
              _currentSpawnCount++;
              _gridMap.TotalSpawnCount++;
              botChar.OnMarkForClose += Bot_OnMarkForClose;
              botChar.CharacterDied += Bot_OnMarkForClose;
              botChar.OnClose += Bot_OnMarkForClose;

              BotBase bot;
              if (AiSession.Instance.Bots.TryGetValue(botChar.EntityId, out bot) && bot != null)
              {
                if (!string.IsNullOrEmpty(lootType))
                {
                  bot._lootContainerSubtype = lootType;
                }

                if (!_allowFlight)
                {
                  var botFlightSetting = bot is NeutralBotBase ? AiSession.Instance.ModSaveData.AllowNeutralsToFly : AiSession.Instance.ModSaveData.AllowEnemiesToFly;

                  var jetpack = botChar.Components.Get<MyCharacterJetpackComponent>();
                  var jetpackWorldSetting = MyAPIGateway.Session.SessionSettings.EnableJetpack;
                  var jetRequired = jetpack != null && botChar.Definition.Id.SubtypeName == "Drone_Bot";
                  var jetAllowed = jetpack != null && jetpackWorldSetting && (jetRequired || (botFlightSetting && _allowFlight));
                  var flightAllowed = jetRequired || jetAllowed;

                  bot.CanUseAirNodes = flightAllowed;
                  bot.CanUseSpaceNodes = flightAllowed;

                  if (!flightAllowed && jetpack.TurnedOn)
                    jetpack.SwitchThrusts();
                }
              }
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
