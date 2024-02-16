using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;

using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using AiEnabled.Support;
using VRage.Game.ObjectBuilders.Definitions;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game;
using VRageMath;
using VRage.Game.ModAPI;
using AiEnabled.Bots.Roles;
using Sandbox.Game.Entities.Character.Components;
using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Ai.Support;
using AiEnabled.Networking;
using AiEnabled.Utilities;
using AiEnabled.Particles;
using AiEnabled.ConfigData;
using Sandbox.Definitions;
using Sandbox.Game;
using VRage;
using VRage.Utils;
using AiEnabled.API;
using Sandbox.Game.EntityComponents;
using AiEnabled.Networking.Packets;
using System.Linq.Expressions;
using Sandbox.Game.Components;

namespace AiEnabled.GameLogic
{
  [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ConveyorSorter), false, "RoboFactory")]
  public class Factory : MyGameLogicComponent
  {    
    public AiSession.BotType SelectedRole = AiSession.BotType.Repair;
    public MyStringId SelectedModel = MyStringId.GetOrCompute("Default");
    public HelperInfo SelectedHelper;
    public StringBuilder BotName;
    public Color? BotColor;
    public RepairPriorities RepairPriorities;
    public TargetPriorities TargetPriorities;
    public KeyValuePair<string, bool> SelectedRepairPriority;
    public KeyValuePair<string, bool> SelectedTargetPriority;
    public KeyValuePair<string, bool> SelectedIgnoreItem;
    public bool ShowRepairPriorities = true;

    public bool ButtonPressed
    {
      get { return _btnPressed; }
      set
      {
        _btnPressed = value;
        _controlTicks = 0;
      }
    }

    public bool SorterEnabled
    {
      get { return _sorterEnabled; }
      set
      {
        if (value != _sorterEnabled && _block != null)
        {
          _sorterEnabled = value;

          var sorter = (IMyConveyorSorter)_block;
          sorter.DrainAll = value;

          var ent = _block as MyEntity;
          ent.EntityStorage.Write("AiEnabled_SorterEnabled", _sorterEnabled);
        }
      }
    }

    public bool HasHelper => _helperInfo.Item1 != null;

    public struct BotInfo
    {
      public IMyCharacter Bot;
      public long OwnerId;
      public BotInfo(IMyCharacter bot, long ownerId)
      {
        Bot = bot;
        OwnerId = ownerId;
      }
    }

    FactoryParticleInfo _particleInfo;
    IMyTerminalBlock _block;
    MyTuple<IMyCharacter, AiSession.ControlInfo, AiSession.BotType> _helperInfo;
    int _controlTicks, _soundTicks;
    bool _soundPlaying, _playBuildSound, _btnPressed, _sorterEnabled;
    List<IMyCubeGrid> _gridList = new List<IMyCubeGrid>();
    List<string> _priorities = new List<string>();
    List<string> _priListTemp = new List<string>();
    CubeGridMap _gridGraph;

    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
      _block = Entity as Sandbox.ModAPI.IMyTerminalBlock;
      NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;

      // Digi's LCD Fix - thanks for letting me steal it! <3
      try
      {
        if (MyAPIGateway.Session.IsServer && MyAPIGateway.Utilities.IsDedicated)
          return; // DS doesn't need to render'em anyway

        var block = Entity as MyCubeBlock;
        var def = block?.BlockDefinition as MyFunctionalBlockDefinition;
        if (def == null)
          return;

        if (def.ScreenAreas == null || def.ScreenAreas.Count <= 0)
          return; // doesn't need LCDs

        if (block.Render is MyRenderComponentScreenAreas)
          return; // already has LCD support

        var oldRender = block.Render;

        var newRender = new MyRenderComponentScreenAreas(block);
        block.Render = newRender;

        // preserve color, skin, etc
        block.Render.ColorMaskHsv = oldRender.ColorMaskHsv;
        block.Render.EnableColorMaskHsv = oldRender.EnableColorMaskHsv;
        block.Render.TextureChanges = oldRender.TextureChanges;
        block.Render.MetalnessColorable = oldRender.MetalnessColorable;
        block.Render.PersistentFlags = oldRender.PersistentFlags;

        // fix for LCDs not working when block spawns instead of placed
        block.Components.Get<MyMultiTextPanelComponent>()?.SetRender(newRender);
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Error trying to set block render for screen usage: {ex}");
      }
      finally
      {
        base.Init(objectBuilder);
      }
    }

    public override void UpdateOnceBeforeFrame()
    {
      try
      {
        base.UpdateOnceBeforeFrame();
        if (AiSession.Instance?.Registered != true)
        {
          NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
          return;
        }

        if (_block == null)
          return;

        if (!AiSession.Instance.CanSpawnBot(SelectedRole))
        {
          if (AiSession.Instance.CanSpawnBot(AiSession.BotType.Scavenger))
            SelectedRole = AiSession.BotType.Scavenger;
          else if (AiSession.Instance.CanSpawnBot(AiSession.BotType.Combat))
            SelectedRole = AiSession.BotType.Combat;
          else if (AiSession.Instance.CanSpawnBot(AiSession.BotType.Crew))
            SelectedRole = AiSession.BotType.Crew;
          else if (AiSession.Instance.CanSpawnBot(AiSession.BotType.Repair)) // this shouldn't be needed, but just in case
            SelectedRole = AiSession.BotType.Repair;
          else
            AiSession.Instance.Logger.Log($"FactoryBlock.UpdateBeforeFrame: Unable to set role to an allowed type!", MessageType.WARNING);
        }

        var ent = _block as MyEntity;
        if (ent.EntityStorage == null)
          ent.EntityStorage = new MyEntityStorageComponent();

        var repList = ent.EntityStorage.ReadStringList("AiEnabled_RepairPriorityList");
        var tgtList = ent.EntityStorage.ReadStringList("AiEnabled_TargetPriorityList");
        var ignList = ent.EntityStorage.ReadStringList("AiEnabled_IgnoreList");
        var tgtDmgCheck = ent.EntityStorage.ReadBool("AiEnabled_DamageToDisable");
        var repWeldCheck = ent.EntityStorage.ReadBool("AiEnabled_WeldBeforeGrind");
        var priListKVP = new List<KeyValuePair<string, bool>>();

        if (repList?.Count > 0)
        {
          priListKVP.Clear();
          foreach (var item in repList)
          {
            var idx = item.IndexOf("]");
            if (idx >= 0)
            {
              var name = item.Substring(idx + 1).Trim();
              var enabled = item.StartsWith("[X]");
              var entry = new KeyValuePair<string, bool>(name, enabled);

              if (!priListKVP.Contains(entry))
                priListKVP.Add(entry);
            }
          }

          RepairPriorities = new RepairPriorities(priListKVP)
          {
            WeldBeforeGrind = repWeldCheck
          };
        }
        else
        {
          RepairPriorities = new RepairPriorities()
          {
            WeldBeforeGrind = true
          };

          _priListTemp.Clear();
          foreach (var item in RepairPriorities.PriorityTypes)
          {
            var prefix = item.Value ? "[X]" : "[  ]";
            var entry = $"{prefix} {item.Key}";

            if (!_priListTemp.Contains(entry))
              _priListTemp.Add(entry);
          }

          ent.EntityStorage.Write("AiEnabled_RepairPriorityList", _priListTemp);
          ent.EntityStorage.Write("AiEnabled_WeldBeforeGrind", true);
        }

        if (tgtList?.Count > 0)
        {
          priListKVP.Clear();
          foreach (var item in tgtList)
          {
            var idx = item.IndexOf("]");
            if (idx >= 0)
            {
              var enabled = item.StartsWith("[X]");
              var name = item.Substring(idx + 1).Trim();
              var entry = new KeyValuePair<string, bool>(name, enabled);

              if (!priListKVP.Contains(entry))
                priListKVP.Add(entry);
            }
          }

          TargetPriorities = new TargetPriorities(priListKVP)
          {
            DamageToDisable = tgtDmgCheck
          };
        }
        else
        {
          TargetPriorities = new TargetPriorities()
          {
            DamageToDisable = tgtDmgCheck
          };

          _priListTemp.Clear();
          foreach (var item in TargetPriorities.PriorityTypes)
          {
            var prefix = item.Value ? "[X]" : "[  ]";
            var entry = $"{prefix} {item.Key}";

            if (!_priListTemp.Contains(entry))
              _priListTemp.Add(entry);
          }

          ent.EntityStorage.Write("AiEnabled_TargetPriorityList", _priListTemp);
          ent.EntityStorage.Write("AiEnabled_DamageToDisable", tgtDmgCheck);
        }

        if (ignList?.Count > 0)
        {
          bool rewrite = false;
          priListKVP.Clear();
          foreach (var item in ignList)
          {
            var idx = item.IndexOf("]");
            if (idx >= 0)
            {
              var name = item.Substring(idx + 1).Trim();
              if (AiSession.Instance.IgnoreTypeDictionary.ContainsKey(MyStringId.GetOrCompute(name)))
              {
                var enabled = item.StartsWith("[X]");
                var entry = new KeyValuePair<string, bool>(name, enabled);

                if (!priListKVP.Contains(entry))
                  priListKVP.Add(entry);
              }
              else
                rewrite = true;
            }
            else
              rewrite = true;
          }

          RepairPriorities.UpdateIgnoreList(priListKVP);

          if (rewrite)
          {
            _priListTemp.Clear();
            foreach (var item in priListKVP)
            {
              var prefix = item.Value ? "[X]" : "[  ]";
              var entry = $"{prefix} {item.Key}";

              if (!_priListTemp.Contains(entry))
                _priListTemp.Add(entry);
            }

            ent.EntityStorage.Write("AiEnabled_IgnoreList", _priListTemp);
          }
        }
        else
        {
          _priListTemp.Clear();
          foreach (var item in RepairPriorities.IgnoreList)
          {
            if (AiSession.Instance.IgnoreTypeDictionary.ContainsKey(MyStringId.GetOrCompute(item.Key)))
            {
              var prefix = item.Value ? "[X]" : "[  ]";
              var entry = $"{prefix} {item.Key}";
              if (!_priListTemp.Contains(entry))
                _priListTemp.Add(entry);
            }
          }

          ent.EntityStorage.Write("AiEnabled_IgnoreList", _priListTemp);
        }

        var inv = _block.GetInventory() as MyInventory;

        if (inv.Constraint == null)
        {
          inv.Constraint = new MyInventoryConstraint("AiEnabled_Constraint");
        }
        else
        {
          inv.Constraint.Clear();
          inv.Constraint.IsWhitelist = true;
        }

        _sorterEnabled = ent.EntityStorage.ReadBool("AiEnabled_SorterEnabled");
        var sorter = _block as IMyConveyorSorter;
        sorter.DrainAll = _sorterEnabled;

        // Workaround - using SetFilter with a list of items causes CTD on DS due to SE trying to serialize the MyDefinitionIds, which aren't serializable
        sorter.SetFilter(Sandbox.ModAPI.Ingame.MyConveyorSorterMode.Whitelist, AiSession.Instance.EmptySorterCache);

        foreach (var itemFilter in AiSession.Instance.FactorySorterCache)
        {
          inv.Constraint.Add(itemFilter.ItemType);
          sorter.AddItem(itemFilter);
        }

        _block.AppendingCustomInfo += AppendingCustomInfo;
        _block.OnMarkForClose += OnMarkForClose;
        _block.OnClosing += OnMarkForClose;
        _block.OnClose += OnMarkForClose;

        if (AiSession.Instance.FactoryControlsHooked)
          return;

        Controls.CreateControls(_block);
        Controls.CreateActions(_block);
        AiSession.Instance.FactoryControlsHooked = true;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log(ex.ToString());
        NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
      }
    }

    public void UpdatePriorityLists(bool updateRepairList, bool updateTargetList, bool updateIgnoreList)
    {
      var ent = _block as MyEntity;
      if (ent != null)
      {
        if (updateRepairList)
        {
          _priListTemp.Clear();
          foreach (var item in RepairPriorities.PriorityTypes)
          {
            var prefix = item.Value ? "[X]" : "[  ]";
            var entry = $"{prefix} {item.Key}";

            if (!_priListTemp.Contains(entry))
              _priListTemp.Add(entry);
          }

          ent.EntityStorage.Write("AiEnabled_RepairPriorityList", _priListTemp);
          ent.EntityStorage.Write("AiEnabled_WeldBeforeGrind", RepairPriorities.WeldBeforeGrind);
        }
        
        if (updateTargetList)
        {
          _priListTemp.Clear();
          foreach (var item in TargetPriorities.PriorityTypes)
          {
            var prefix = item.Value ? "[X]" : "[  ]";
            var entry = $"{prefix} {item.Key}";

            if (!_priListTemp.Contains(entry))
              _priListTemp.Add(entry);
          }

          ent.EntityStorage.Write("AiEnabled_TargetPriorityList", _priListTemp);
          ent.EntityStorage.Write("AiEnabled_DamageToDisable", TargetPriorities.DamageToDisable);
        }
                
        if (updateIgnoreList)
        {
          _priListTemp.Clear();
          foreach (var item in RepairPriorities.IgnoreList)
          {
            var prefix = item.Value ? "[X]" : "[  ]";
            var entry = $"{prefix} {item.Key}";

            if (!_priListTemp.Contains(entry))
              _priListTemp.Add(entry);
          }

          ent.EntityStorage.Write("AiEnabled_IgnoreList", _priListTemp);
        }

        if (AiSession.Instance != null && !AiSession.Instance.IsServer)
        {
          var ignList = updateIgnoreList ? RepairPriorities.IgnoreList : null;
          var repList = updateRepairList ? RepairPriorities.PriorityTypes : null;
          var tgtList = updateTargetList ? TargetPriorities.PriorityTypes : null;
          var pkt = new FactorySyncPacket(_block.EntityId, TargetPriorities.DamageToDisable, RepairPriorities.WeldBeforeGrind, repList, tgtList, ignList);
          AiSession.Instance.Network.SendToServer(pkt);
        }
      }
    }

    public void SetHelper(MyTuple<IMyCharacter, AiSession.ControlInfo> botInfo, IMyPlayer owner)
    {
      var bot = botInfo.Item1;
      if (_block == null || _block.MarkedForClose || bot == null || owner == null)
        return;

      if (_soundPlaying && _helperInfo.Item1 != null)
        return;

      var jetpack = bot.Components.Get<MyCharacterJetpackComponent>();
      if (jetpack != null && jetpack.TurnedOn)
        jetpack.SwitchThrusts();

      _helperInfo = MyTuple.Create(botInfo.Item1, botInfo.Item2, SelectedRole);
      bot.CharacterDied += OnCharacterDied;
      bot.OnMarkForClose += Char_OnMarkForClose;

      if (_particleInfo == null)
        _particleInfo = new FactoryParticleInfo(bot, _block);
      else
        _particleInfo.Set(bot);

      if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
      {
        var packet = new ParticlePacket(bot.EntityId, ParticleInfoBase.ParticleType.Factory, _block.EntityId);
        AiSession.Instance.Network.RelayToClients(packet);
      }

      AiSession.Instance.FactoryBotInfoDict[bot.EntityId] = new BotInfo(bot, owner.IdentityId);
      _playBuildSound = true;
    }

    private void Char_OnMarkForClose(IMyEntity obj)
    {
      var character = obj as IMyCharacter;
      if (character != null)
        OnCharacterDied(character);
    }

    private void OnCharacterDied(IMyCharacter bot)
    {
      if (bot == null)
        return;

      bot.CharacterDied -= OnCharacterDied;
      bot.OnMarkForClose -= Char_OnMarkForClose;

      BotInfo info;
      List<BotBase> playerHelpers;
      if (AiSession.Instance.FactoryBotInfoDict.TryGetValue(bot.EntityId, out info) && info.OwnerId > 0
        && AiSession.Instance.PlayerToHelperDict.TryGetValue(info.OwnerId, out playerHelpers))
      {
        for (int i = playerHelpers.Count - 1; i >= 0; i--)
        {
          var helper = playerHelpers[i];
          if (helper?.Character?.EntityId == bot.EntityId)
          {
            playerHelpers.RemoveAtFast(i);
            break;
          }
        }
      }

      if (_helperInfo.Item1?.EntityId == bot.EntityId)
      {
        if (_soundPlaying)
        {
          _particleInfo?.Stop();
          _soundPlaying = false;
        }

        _helperInfo = new MyTuple<IMyCharacter, AiSession.ControlInfo, AiSession.BotType>(null, null, AiSession.BotType.Combat);
      }
    }

    public override void UpdateAfterSimulation()
    {
      try
      {
        base.UpdateAfterSimulation();
        if (_helperInfo.Item1 == null)
          return;

        if (_soundPlaying)
          _particleInfo?.Update();

        if (!_playBuildSound)
          return;

        _particleInfo?.Set(_helperInfo.Item1);
        _playBuildSound = false;
        _soundPlaying = true;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in AiSession.FactoryBlock.UpdateAfterSim: {ex}");

        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"Exception in FactoryBlock.UpdateAfterSim: {ex.Message}");
      }
    }

    BotBase CreateBot(MyTuple<IMyCharacter, AiSession.ControlInfo, AiSession.BotType> botInfo, GridBase gBase, long ownerId)
    {
      BotBase botBase = null;
      switch (botInfo.Item3)
      {
        case AiSession.BotType.Repair:
          botBase = new RepairBot(botInfo.Item1, gBase, ownerId, botInfo.Item2);
          AiSession.Instance.Scheduler.Schedule(botBase.AddWeapon);
          break;
        case AiSession.BotType.Combat:
          botBase = new CombatBot(botInfo.Item1, gBase, ownerId, botInfo.Item2);
          AiSession.Instance.Scheduler.Schedule(botBase.AddWeapon);
          break;
        case AiSession.BotType.Crew:
          botBase = new CrewBot(botInfo.Item1, gBase, ownerId, botInfo.Item2);
          AiSession.Instance.Scheduler.Schedule(botBase.AddWeapon);
          break;
        case AiSession.BotType.Scavenger:
          botBase = new ScavengerBot(botInfo.Item1, gBase, ownerId, botInfo.Item2);
          break;
        default:
          SelectedHelper = null;
          throw new ArgumentException($"Invalid value for SelectedRole, '{botInfo.Item3}'");
      }

      return botBase;
    }

    public override void UpdateAfterSimulation10()
    {
      try
      {
        base.UpdateAfterSimulation10();
        if (_block == null || AiSession.Instance?.Registered != true)
          return;

        if (_gridGraph == null)
        {
          _gridGraph = AiSession.Instance.GetGridGraph((MyCubeGrid)_block.CubeGrid, _block.WorldMatrix);
        }
        else if (_gridGraph.LastActiveTicks > 100)
        {
          _gridGraph.LastActiveTicks = 10;
        }

        if (_soundPlaying)
        {
          ++_soundTicks;
          if (_soundTicks > 90)
          {
            _soundTicks = 0;
            _particleInfo?.Stop();
            _soundPlaying = false;

            if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
            {
              var packet = new ParticlePacket(_helperInfo.Item1.EntityId, ParticleInfoBase.ParticleType.Factory, remove: true);
              AiSession.Instance.Network.RelayToClients(packet);
            }

            try
            {
              var botInfo = AiSession.Instance.FactoryBotInfoDict[_helperInfo.Item1.EntityId];
              var ownerId = botInfo.OwnerId;

              var botChar = CreateBot(_helperInfo, _gridGraph, ownerId);
              AssignPriorities(botChar);

              AiSession.Instance.AddBot(botChar, ownerId);

              IMyPlayer player;
              if (AiSession.Instance.Players.TryGetValue(ownerId, out player) && player != null)
              {
                var packet = new SpawnPacketClient(botChar.Character.EntityId, false);
                AiSession.Instance.Network.SendToPlayer(packet, player.SteamUserId);
              }
            }
            catch (Exception ex)
            {
              AiSession.Instance.Logger.Error($"Error trying to spawn bot: {ex}");

              if (MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"Error trying to spawn bot: {ex.Message}");
            }

            _helperInfo = new MyTuple<IMyCharacter, AiSession.ControlInfo, AiSession.BotType>(null, null, AiSession.BotType.Combat);
          }
        }

        if (!ButtonPressed)
          return;

        ++_controlTicks;
        if (_controlTicks > 29)
        {
          ButtonPressed = false;
          Controls.SetContextMessage(_block, "");
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in AiSession.FactoryBlock.UpdateAfterSim10: {ex}");

        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"Exception in FactoryBlock.UpdateAfterSim10: {ex.Message}");
      }
    }

    public override void UpdateAfterSimulation100()
    {
      try
      {
        base.UpdateAfterSimulation100();
        if (_block == null || AiSession.Instance?.Registered != true)
          return;

        var modData = AiSession.Instance.ModSaveData;
        if (modData.MaxHelpersPerPlayer == 0)
        {
          AiSession.Instance.Logger.Log($"{_block.CustomName} removed from {_block.CubeGrid.DisplayName} per server settings [MaxHelpersPerPlayer = 0]");
          _block.Close();
          return;
        }
        else if (!modData.AllowCombatBot && !modData.AllowRepairBot && !modData.AllowCrewBot && !modData.AllowScavengerBot)
        {
          AiSession.Instance.Logger.Log($"{_block.CustomName} removed from {_block.CubeGrid.DisplayName} per server settings [No helpers allowed]");
          _block.Close();
          return;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in AiSession.FactoryBlock.UpdateAfterSim100: {ex}");
      }
    }

    void AssignPriorities(BotBase bot)
    {
      bot.RepairPriorities = new RepairPriorities(RepairPriorities?.PriorityTypes);
      bot.TargetPriorities = new TargetPriorities(TargetPriorities?.PriorityTypes);
    }

    private void OnMarkForClose(IMyEntity obj)
    {
      try
      {
        if (_block != null)
        {
          _block.AppendingCustomInfo -= AppendingCustomInfo;
          _block.OnMarkForClose -= OnMarkForClose;
          _block.OnClosing -= OnMarkForClose;
          _block.OnClose -= OnMarkForClose;
        }

        if (_soundPlaying)
        {
          _particleInfo?.Stop();
          _soundPlaying = false;
        }

        if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer && _particleInfo?.Bot != null)
        {
          var packet = new ParticlePacket(_particleInfo.Bot.EntityId, _particleInfo.Type, remove: true);
          AiSession.Instance.Network.RelayToClients(packet);
        }

        _gridList?.Clear();
        _gridList = null;

        _priorities?.Clear();
        _priorities = null;

        _priListTemp?.Clear();
        _priListTemp = null;

        _particleInfo?.Close();
        _particleInfo = null;

        _gridGraph = null;
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Error($"Exception in FactoryBlock.Close: {ex}");
      }
    }

    int _errorCount;

    private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder sb)
    {
      try
      {
        sb.Append("Bot Factory v1.0\n")
          .Append('-', 30)
          .Append('\n');

        var functional = _block as IMyFunctionalBlock;
        sb.Append("Status: ")
          .Append(functional.Enabled ? "Online" : "Offline")
          .Append('\n');

        sb.Append("Selected Bot: ")
          .Append(SelectedRole)
          .Append('\n', 2);

        var compSubtype = $"AiEnabled_Comp_{SelectedRole}BotMaterial";
        var comp = new MyDefinitionId(typeof(MyObjectBuilder_Component), compSubtype);
        var def = AiSession.Instance.AllGameDefinitions[comp];

        sb.Append("Build Requirements:\n")
          .Append(" - ")
          .Append(def?.DisplayNameText ?? comp.SubtypeName)
          .Append('\n');

        long price;
        if (AiSession.Instance.BotPrices.TryGetValue(SelectedRole, out price) && price > 0)
        {
          sb.Append(" - ")
            .Append(price.ToString("#,###,##0"))
            .Append(" SC")
            .Append('\n');
        }

        if (AiSession.Instance.ModSaveData?.ChargePlayersForBotUpkeep == true 
          && AiSession.Instance.ModSaveData.BotUpkeepTimeInMinutes > 0
          && AiSession.Instance.BotUpkeepPrices.TryGetValue(SelectedRole, out price) && price > 0)
        {
          var mins = AiSession.Instance.ModSaveData.BotUpkeepTimeInMinutes;
          string upkeepStr;
          if (mins >= 60)
          {
            var hrs = mins / 60;
            mins -= hrs * 60;

            upkeepStr = $"{hrs}Hr {mins}Min";
          }
          else
          {
            upkeepStr = $"0Hr {mins}Min";
          }

          sb.Append(" - ")
            .Append(price.ToString("#,###,##0"))
            .Append(" SC per ")
            .Append(upkeepStr)
            .Append('\n', 2);
        }
        else
        {
          sb.Append('\n');
        }

        sb.Append("Description:\n")
          .Append(AiSession.Instance.BotDescriptions[SelectedRole]);
      }
      catch (Exception ex)
      {
        ++_errorCount;
        if (_errorCount == 1 || _errorCount % 1000 == 0) 
          AiSession.Instance.Logger.Error($"Exception in FactoryBlock.AppendingCustomInfo: {ex}");
      }
    }
  }
}
