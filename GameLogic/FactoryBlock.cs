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

namespace AiEnabled.GameLogic
{
  [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ConveyorSorter), false, "RoboFactory")]
  public class Factory : MyGameLogicComponent
  {    
    public AiSession.BotType SelectedRole;
    public AiSession.BotModel SelectedModel;
    public HelperInfo SelectedHelper;
    public StringBuilder BotName;
    public bool ButtonPressed;
    public bool HasHelper => _helperBot != null;

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
    IMyCharacter _helperBot;
    int _controlTicks, _soundTicks;
    bool _soundPlaying, _playBuildSound;
    List<IMyCubeGrid> _gridList = new List<IMyCubeGrid>();
    CubeGridMap _gridGraph;

    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
      _block = Entity as Sandbox.ModAPI.IMyTerminalBlock;
      NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
      base.Init(objectBuilder);
    }

    public override void UpdateOnceBeforeFrame()
    {
      base.UpdateOnceBeforeFrame();
      if (AiSession.Instance?.Registered != true)
      {
        NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        return;
      }

      if (_block == null)
        return;

      var sorter = _block as IMyConveyorSorter;
      sorter.DrainAll = true;

      // Workaround - using SetFilter with a list of items causes CTD on DS due to SE trying to serialize the MyDefinitionIds, which aren't serializable
      sorter.SetFilter(Sandbox.ModAPI.Ingame.MyConveyorSorterMode.Whitelist, AiSession.Instance.EmptySorterCache);

      foreach (var itemFilter in AiSession.Instance.FactorySorterCache)
        sorter.AddItem(itemFilter);

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

    public void SetHelper(IMyCharacter bot, IMyPlayer owner)
    {
      if (_block == null || _block.MarkedForClose || bot == null || owner == null)
        return;

      if (_soundPlaying && _helperBot != null)
        return;

      _helperBot = bot;
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

      if (_helperBot?.EntityId == bot.EntityId)
      {
        if (_soundPlaying)
        {
          _particleInfo?.Stop();
          _soundPlaying = false;
        }

        _helperBot = null;
      }
    }

    public override void UpdateAfterSimulation()
    {
      try
      {
        base.UpdateAfterSimulation();
        if (_helperBot == null)
          return;

        if (_soundPlaying)
          _particleInfo?.Update();

        if (!_playBuildSound)
          return;

        _particleInfo?.Set(_helperBot);
        _playBuildSound = false;
        _soundPlaying = true;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in AiSession.FactoryBlock.UpdateAfterSim: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        MyAPIGateway.Utilities.ShowNotification($"Exception in FactoryBlock.UpdateAfterSim: {ex.Message}");
      }
    }

    BotBase CreateBot(IMyCharacter bot, GridBase gBase, long ownerId)
    {
      BotBase botBase = null;
      switch (SelectedRole)
      {
        case AiSession.BotType.Repair:
          botBase = new RepairBot(bot, gBase, ownerId);
          MyAPIGateway.Utilities.InvokeOnGameThread(botBase.AddWeapon, "AiEnabled");
          break;
        case AiSession.BotType.Combat:
          botBase = new CombatBot(bot, gBase, ownerId);
          MyAPIGateway.Utilities.InvokeOnGameThread(botBase.AddWeapon, "AiEnabled");
          break;
        case AiSession.BotType.Crew:
          botBase = new CrewBot(bot, gBase, ownerId);
          MyAPIGateway.Utilities.InvokeOnGameThread(botBase.AddWeapon, "AiEnabled");
          break;
        case AiSession.BotType.Scavenger:
          botBase = new ScavengerBot(bot, gBase, ownerId);
          break;
        default:
          SelectedHelper = null;
          throw new ArgumentException($"Invalid value for SelectedRole, '{SelectedRole}'");
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
              var packet = new ParticlePacket(_helperBot.EntityId, ParticleInfoBase.ParticleType.Factory, remove: true);
              AiSession.Instance.Network.RelayToClients(packet);
            }

            try
            {
              var botInfo = AiSession.Instance.FactoryBotInfoDict[_helperBot.EntityId];
              var ownerId = botInfo.OwnerId;

              var gridMap = _gridGraph;
              if (gridMap == null)
              {
                _gridList.Clear();
                _block.CubeGrid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(_gridList);
                MyCubeGrid grid = _block.CubeGrid as MyCubeGrid;

                foreach (var g in _gridList)
                {
                  if (g.GridSize < 1 || g.WorldAABB.Volume < grid.PositionComp.WorldAABB.Volume)
                    continue;

                  grid = g as MyCubeGrid;
                }

                gridMap = AiSession.Instance.GetGridGraph(grid, _helperBot.WorldMatrix);
              }
              else if (gridMap.LastActiveTicks > 100)
              { 
                gridMap.LastActiveTicks = 10;
              }

              var botChar = CreateBot(_helperBot, gridMap, ownerId);
              AiSession.Instance.AddBot(botChar, ownerId);
            }
            catch (Exception ex)
            {
              AiSession.Instance.Logger.Log($"Error trying to spawn bot: {ex.Message}\n{ex.StackTrace}");

              if (MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"Error trying to spawn bot: {ex.Message}");
            }

            _helperBot = null;
          }
        }

        if (!ButtonPressed)
          return;

        ++_controlTicks;
        if (_controlTicks > 29)
        {
          _controlTicks = 0;
          ButtonPressed = false;
          Controls.SetContextMessage(_block, "");
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in AiSession.FactoryBlock.UpdateAfterSim10: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);

        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"Exception in FactoryBlock.UpdateAfterSim10: {ex.Message}");
      }
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

        _particleInfo?.Close();
        _particleInfo = null;

        _gridGraph = null;
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in FactoryBlock.Close: {ex.Message}\n{ex.StackTrace}");
      }
    }

    private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder sb)
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

      List<SerialId> comps;
      if (AiSession.Instance.BotComponents.TryGetValue(SelectedRole, out comps) && comps?.Count > 0)
      {
        sb.Append(" - ")
          .Append(AiSession.Instance.BotPrices[SelectedRole].ToString("#,###,##0"))
          .Append(" Space Credits")
          .Append('\n', 2);
      }
      else
      {
        sb.Append('\n');
      }

      sb.Append("Description:\n")
        .Append(AiSession.Instance.BotDescriptions[SelectedRole]);
    }
  }
}
