using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.GameLogic;
using AiEnabled.Networking;

using Sandbox.Game;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Support
{
  public static class Controls
  {
    private static IMyTerminalControlOnOffSwitch _refreshToggle;

    internal static void CreateControls(IMyTerminalBlock block)
    {
      if (AiSession.Instance.FactoryControlsCreated || !(block is IMyConveyorSorter))
        return;

      AiSession.Instance.FactoryControlsCreated = true;

      HashSet<string> controlsToDisable = new HashSet<string>
      {
        //"OnOff",
        "ShowInTerminal",
        //"ShowInInventory",
        "ShowInToolbarConfig",
        //"Name",
        //"ShowOnHUD",
        "CustomData",
        "DrainAll",
        "blacklistWhitelist",
        "CurrentList",
        "removeFromSelectionButton",
        "candidatesList",
        "addToSelectionButton"
      };

      List<IMyTerminalControl> controls;
      MyAPIGateway.TerminalControls.GetControls<IMyConveyorSorter>(out controls);
      block.ShowInToolbarConfig = false;

      for (int i = 0; i < controls.Count; i++)
      {
        var ctrl = controls[i];
        if (_refreshToggle == null && ctrl.Id == "ShowInToolbarConfig")
          _refreshToggle = ctrl as IMyTerminalControlOnOffSwitch;

        if (!(ctrl is IMyTerminalControlSeparator) && controlsToDisable.Contains(ctrl.Id))
        {
          ctrl.Enabled = CombineFunc.Create(ctrl.Visible, Block => Block.BlockDefinition.SubtypeId != "RoboFactory");
          ctrl.Visible = CombineFunc.Create(ctrl.Visible, Block => Block.BlockDefinition.SubtypeId != "RoboFactory");
        }
      }

      var labelFactory = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyConveyorSorter>("LblFactory");
      labelFactory.Enabled = CombineFunc.Create(labelFactory.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelFactory.Visible = CombineFunc.Create(labelFactory.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelFactory.SupportsMultipleBlocks = false;
      labelFactory.Label = MyStringId.GetOrCompute("Helper Factory");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(labelFactory);
      controls.Add(labelFactory);

      var comboRole = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyConveyorSorter>("CBoxRole");
      comboRole.Enabled = CombineFunc.Create(comboRole.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      comboRole.Visible = CombineFunc.Create(comboRole.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      comboRole.SupportsMultipleBlocks = false;
      comboRole.Title = MyStringId.GetOrCompute("Select Type");
      comboRole.ComboBoxContent = GetTypeContent;
      comboRole.Getter = Block => GetSelectedType(Block);
      comboRole.Setter = SetSelectedType;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(comboRole);
      controls.Add(comboRole);

      var comboModel = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyConveyorSorter>("CBoxModel");
      comboModel.Enabled = CombineFunc.Create(comboModel.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      comboModel.Visible = CombineFunc.Create(comboModel.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      comboModel.SupportsMultipleBlocks = false;
      comboModel.Title = MyStringId.GetOrCompute("Select Model");
      comboModel.ComboBoxContent = GetModelContent;
      comboModel.Getter = Block => GetSelectedModel(Block);
      comboModel.Setter = SetSelectedModel;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(comboModel);
      controls.Add(comboModel);

      var txtBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyConveyorSorter>("TBoxName");
      txtBox.Enabled = CombineFunc.Create(txtBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      txtBox.Visible = CombineFunc.Create(txtBox.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      txtBox.SupportsMultipleBlocks = false;
      txtBox.Title = MyStringId.GetOrCompute("Custom Name");
      txtBox.Tooltip = MyStringId.GetOrCompute("Give your helper a name. If left blank, a random name will be generated.");
      txtBox.Setter = (Block, sb) => SetBotName(Block, sb);
      txtBox.Getter = Block => GetBotName(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(txtBox);
      controls.Add(txtBox);

      var labelPrice = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyConveyorSorter>("LblPrice");
      labelPrice.Enabled = CombineFunc.Create(labelPrice.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelPrice.Visible = CombineFunc.Create(labelPrice.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelPrice.SupportsMultipleBlocks = false;
      labelPrice.Label = MyStringId.GetOrCompute("");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(labelPrice);
      controls.Add(labelPrice);

      var buttonBuild = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnBuild");
      buttonBuild.Enabled = CombineFunc.Create(buttonBuild.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonBuild.Visible = CombineFunc.Create(buttonBuild.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonBuild.SupportsMultipleBlocks = false;
      buttonBuild.Title = MyStringId.GetOrCompute("Build");
      buttonBuild.Tooltip = MyStringId.GetOrCompute("Will deduct the required funds from your account and construct a helper to aid you.");
      buttonBuild.Action = SpawnHelper;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(buttonBuild);
      controls.Add(buttonBuild);

      var separator = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyConveyorSorter>("Separator");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(separator);
      controls.Add(separator);

      var labelMgmt = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyConveyorSorter>("LblManagement");
      labelMgmt.Enabled = CombineFunc.Create(labelMgmt.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelMgmt.Visible = CombineFunc.Create(labelMgmt.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelMgmt.SupportsMultipleBlocks = false;
      labelMgmt.Label = MyStringId.GetOrCompute("Helper Management");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(labelMgmt);
      controls.Add(labelMgmt);

      var comboHelpers = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyConveyorSorter>("CBoxHelper");
      comboHelpers.Enabled = CombineFunc.Create(comboHelpers.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      comboHelpers.Visible = CombineFunc.Create(comboHelpers.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      comboHelpers.SupportsMultipleBlocks = false;
      comboHelpers.Title = MyStringId.GetOrCompute("Select Helper");
      comboHelpers.ComboBoxContent = GetHelperContent;
      comboHelpers.Getter = Block => GetSelectedHelper(Block);
      comboHelpers.Setter = SetSelectedHelper;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(comboHelpers);
      controls.Add(comboHelpers);

      var buttonStore = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnStore");
      buttonStore.Enabled = CombineFunc.Create(buttonStore.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonStore.Visible = CombineFunc.Create(buttonStore.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonStore.SupportsMultipleBlocks = false;
      buttonStore.Title = MyStringId.GetOrCompute("Store");
      buttonStore.Tooltip = MyStringId.GetOrCompute("Will store the selected helper and allow you to summon a new one. Use Respawn to bring a stored helper back!");
      buttonStore.Action = StoreHelper;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(buttonStore);
      controls.Add(buttonStore);

      var buttonRecall = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnRecall");
      buttonRecall.Enabled = CombineFunc.Create(buttonRecall.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonRecall.Visible = CombineFunc.Create(buttonRecall.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonRecall.SupportsMultipleBlocks = false;
      buttonRecall.Title = MyStringId.GetOrCompute("Respawn");
      buttonRecall.Tooltip = MyStringId.GetOrCompute("Will teleport the selected helper to this Factory block.");
      buttonRecall.Action = RecallHelper;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(buttonRecall);
      controls.Add(buttonRecall);

      var buttonDismiss = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnDismiss");
      buttonDismiss.Enabled = CombineFunc.Create(buttonDismiss.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonDismiss.Visible = CombineFunc.Create(buttonDismiss.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonDismiss.SupportsMultipleBlocks = false;
      buttonDismiss.Title = MyStringId.GetOrCompute("Recycle");
      buttonDismiss.Tooltip = MyStringId.GetOrCompute("Will destroy the selected helper and refund half of the original purchase price.");
      buttonDismiss.Action = DismissHelper;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(buttonDismiss);
      controls.Add(buttonDismiss);

      block.RefreshCustomInfo();
    }

    private static void SpawnHelper(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      var player = MyAPIGateway.Session?.LocalHumanPlayer;
      if (player?.Character == null)
        return;

      gameLogic.ButtonPressed = true;

      if (AiSession.Instance?.Registered != true || !AiSession.Instance.CanSpawn)
      {
        SetContextMessage(block, "Capacitors are charging. Please wait...");
        return;
      }

      var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);
      if (playerFaction == null)
      {
        SetContextMessage(block, "Enter a faction and try again!");
        return;
      }

      if (gameLogic.HasHelper)
      {
        gameLogic.ButtonPressed = true;
        SetContextMessage(block, "A helper is currently being built!");
        return;
      }

      List<long> helperIds;
      if (AiSession.Instance.PlayerToActiveHelperIds.TryGetValue(player.IdentityId, out helperIds) && helperIds.Count >= AiSession.Instance.MaxHelpers)
      {
        SetContextMessage(block, $"You already have {helperIds.Count} helper(s).");
        return;
      }

      long balance;
      player.TryGetBalanceInfo(out balance);

      var botRole = gameLogic.SelectedRole;
      var botModel = gameLogic.SelectedModel;
      var price = AiSession.Instance.BotPrices[botRole];
      var credit = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalObject), "SpaceCredit");
      var inv = player.Character?.GetInventory() as MyInventory;
      var inventoryCredits = 0L;

      if (price > balance)
      {
        var needed = price - balance;
        if (inv != null)
        {
          inventoryCredits = Math.Min(needed, (long)inv.GetItemAmount(credit));
          price = Math.Max(0, price - inventoryCredits);
        }

        if (price > balance)
        {
          needed = price - balance;
          SetContextMessage(block, $"Missing {needed:#,###,###} SC");
          return;
        }
      }

      var displayName = gameLogic.BotName?.ToString() ?? "";
      var needsName = string.IsNullOrWhiteSpace(displayName);

      if (AiSession.Instance.IsServer)
      {
        string subtype;

        if (botModel == AiSession.BotModel.Default)
        {
          switch (botRole)
          {
            case AiSession.BotType.Repair:
              if (needsName)
                displayName = "RepairBot";

              subtype = "Drone_Bot";
              break;
            case AiSession.BotType.Combat:
              if (needsName)
                displayName = "CombatBot";

              subtype = "Target_Dummy";
              break;
            case AiSession.BotType.Scavenger:
              if (needsName)
                displayName = "ScavengerBot";

              subtype = "RoboDog";
              break;
            //case AiSession.BotType.Medic:
            // if (needsName)
            //   displayName = "MedicBot";
            //
            // subtype = "Police_Bot";
            // break;
            default:
              return;
          }
        }
        else if (botModel == AiSession.BotModel.DroneBot)
        {
          if (needsName)
            displayName = botRole == AiSession.BotType.Combat ? "CombatBot" : botRole == AiSession.BotType.Repair ? "RepairBot" : "ScavengerBot";

          subtype = "Drone_Bot";
        }
        else if (botModel == AiSession.BotModel.AstronautMale)
        {
          if (needsName)
            displayName = botRole == AiSession.BotType.Combat ? "CombatBot" : botRole == AiSession.BotType.Repair ? "RepairBot" : "ScavengerBot";

          subtype = "Default_Astronaut";
        }
        else if (botModel == AiSession.BotModel.AstronautFemale)
        {
          if (needsName)
            displayName = botRole == AiSession.BotType.Combat ? "CombatBot" : botRole == AiSession.BotType.Repair ? "RepairBot" : "ScavengerBot";

          subtype = "Default_Astronaut_Female";
        }
        else // if (botModel == AiSession.BotModel.TargetBot)
        {
          if (needsName)
            displayName = botRole == AiSession.BotType.Combat ? "CombatBot" : botRole == AiSession.BotType.Repair ? "RepairBot" : "ScavengerBot";

          subtype = "Target_Dummy";
        }

        if (needsName)
        {
          displayName = BotFactory.GetUniqueName(displayName);
        }
        else
        {
          int num = 1;
          var name = displayName;
          while (MyEntities.EntityExists(displayName))
            displayName = $"{name}{num++}";
        }

        var position = block.WorldMatrix.Translation + block.WorldMatrix.Backward + block.WorldMatrix.Down;
        var posOr = new MyPositionAndOrientation(position, (Vector3)block.WorldMatrix.Backward, (Vector3)block.WorldMatrix.Up);

        var helper = BotFactory.CreateBotObject(subtype, displayName, posOr, player.IdentityId);
        if (helper != null)
        {
          helper.SetPosition(position);
          if (helper.Physics != null && block.CubeGrid.Physics != null)
          {
            var gridPhysics = block.CubeGrid.Physics;
            helper.Physics.LinearVelocity = gridPhysics.LinearVelocity;
            helper.Physics.AngularVelocity = gridPhysics.AngularVelocity;
          }

          gameLogic.SetHelper(helper, player);
          player.RequestChangeBalance(-price);
          if (inventoryCredits > 0)
            inv?.RemoveItemsOfType((MyFixedPoint)(float)inventoryCredits, credit);

          SetContextMessage(block, "Enjoy your helper!");
        }
        else
          SetContextMessage(block, "Unable to spawn helper..");
      }
      else
      {
        var packet = new FactorySpawnPacket(botRole, botModel, displayName, block.EntityId, player.IdentityId, price, inventoryCredits);
        AiSession.Instance.Network.SendToServer(packet);
        SetContextMessage(block, "Build request sent!");
      }
    }

    private static void DismissHelper(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      var player = MyAPIGateway.Session?.Player;
      if (player == null)
        return;

      gameLogic.ButtonPressed = true;

      if (AiSession.Instance?.Registered != true)
      {
        SetContextMessage(block, "Capacitors are charging. Please wait...");
        return;
      }

      var helperInfo = gameLogic.SelectedHelper;
      if (helperInfo == null)
      {
        SetContextMessage(block, "No valid helper found...");
        return;
      }

      var pkt = new FactoryDismissPacket(helperInfo.HelperId, player.IdentityId);
      AiSession.Instance.Network.SendToServer(pkt);

      SetContextMessage(block, $"Dismissing {helperInfo.DisplayName}...");
    }

    private static void StoreHelper(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      var player = MyAPIGateway.Session?.Player;
      if (player == null)
        return;

      gameLogic.ButtonPressed = true;

      if (AiSession.Instance?.Registered != true)
      {
        SetContextMessage(block, "Capacitors are charging. Please wait...");
        return;
      }

      List<long> helperIds;
      if (!AiSession.Instance.PlayerToActiveHelperIds.TryGetValue(player.IdentityId, out helperIds) || helperIds == null || helperIds.Count == 0)
      {
        SetContextMessage(block, "No active helpers found...");
        return;
      }

      var helperInfo = gameLogic.SelectedHelper;
      if (helperInfo == null || !helperInfo.IsActiveHelper || !helperIds.Contains(helperInfo.HelperId))
      {
        SetContextMessage(block, "No valid helper found...");
        return;
      }

      var helper = MyEntities.GetEntityById(helperInfo.HelperId) as IMyCharacter;
      if (helper == null || helper.IsDead)
      {
        SetContextMessage(block, "No valid helper found...");
        return;
      }

      var pkt = new StoreBotPacket(helper.EntityId);
      AiSession.Instance.Network.SendToServer(pkt);
      SetContextMessage(block, "Storing helper...");
    }

    private static void RecallHelper(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      var player = MyAPIGateway.Session?.Player;
      if (player == null)
        return;

      gameLogic.ButtonPressed = true;

      if (AiSession.Instance?.Registered != true)
      {
        SetContextMessage(block, "Capacitors are charging. Please wait...");
        return;
      }

      var helpers = AiSession.Instance.MyHelperInfo;
      if (helpers == null || helpers.Count == 0)
      {
        SetContextMessage(block, "No valid helper found...");
        return;
      }

      var helperInfo = gameLogic.SelectedHelper;
      if (helperInfo == null)
      {
        SetContextMessage(block, "No valid helper found...");
        return;
      }

      bool found = false;
      for (int i = 0; i < helpers.Count; i++)
      {
        if (helpers[i].HelperId == helperInfo.HelperId)
        {
          found = true;
          break;
        }
      }

      if (!found)
      {
        SetContextMessage(block, "No valid helper found...");
        return;
      }

      List<long> helperIds;
      if (AiSession.Instance.PlayerToActiveHelperIds.TryGetValue(player.IdentityId, out helperIds) && helperIds.Count >= AiSession.Instance.MaxHelpers)
      {
        found = false;
        for (int i = 0; i < helperIds.Count; i++)
        {
          if (helperIds[i] == helperInfo.HelperId)
          {
            found = helperInfo.IsActiveHelper;
            break;
          }
        }

        if (!found)
        {
          SetContextMessage(block, $"You already have {helperIds.Count} helper(s).");
          return;
        }
      }

      var pkt = new FactoryRecallPacket(block.EntityId, helperInfo.HelperId, player.IdentityId);
      AiSession.Instance.Network.SendToServer(pkt);
      SetContextMessage(block, "Recalling helper...");
    }


    public static void SetContextMessage(IMyTerminalBlock block, string message)
    {
      List<IMyTerminalControl> controls;
      MyAPIGateway.TerminalControls.GetControls<IMyConveyorSorter>(out controls);

      for (int i = 0; i < controls.Count; i++)
      {
        var ctrl = controls[i];
        if (ctrl.Id != "Label")
          continue;

        var label = ctrl as IMyTerminalControlLabel;
        if (label.Label.String == "Helper Factory")
          continue;

        label.Label = MyStringId.GetOrCompute(message);
        label.RedrawControl();
        break;
      }

      RefreshTerminalControls(block);
    }

    private static void SetBotName(IMyTerminalBlock block, StringBuilder sb)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      gameLogic.BotName = sb;
    }

    private static StringBuilder GetBotName(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      return gameLogic?.BotName ?? new StringBuilder();
    }

    public static void SetSelectedType(IMyTerminalBlock block, long value)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      var bot = (AiSession.BotType)value;
      gameLogic.SelectedRole = bot;
      block.RefreshCustomInfo();
      RefreshTerminalControls(block);
    }

    public static void SetSelectedModel(IMyTerminalBlock block, long value)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      var model = (AiSession.BotModel)value;
      gameLogic.SelectedModel = model;
      block.RefreshCustomInfo();
      RefreshTerminalControls(block);
    }

    public static void SetSelectedHelper(IMyTerminalBlock block, long value)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
      {
        return;
      }

      if (value == 0)
      {
        gameLogic.SelectedHelper = null;
        return;
      }

      if (MyAPIGateway.Session?.Player == null)
      {
        return;
      }

      gameLogic.ButtonPressed = true;

      var helpers = AiSession.Instance.MyHelperInfo;
      if (helpers == null || helpers.Count == 0)
      {
        SetContextMessage(block, "You have no helpers...");
        return;
      }

      // I added "None" as a selectable item so we need to decrement to keep it within range here
      value--;

      if (value < 0 || value >= helpers.Count)
      {
        SetContextMessage(block, "Index out of range...");
        return;
      }

      var helperInfo = helpers[(int)value];
      gameLogic.SelectedHelper = helperInfo;
      block.RefreshCustomInfo();
      RefreshTerminalControls(block);
    }

    public static long GetSelectedType(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic?.SelectedRole == null)
        return 0L;

      return (long)gameLogic.SelectedRole;
    }

    public static long GetSelectedModel(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic?.SelectedModel == null)
        return 0L;

      return (long)gameLogic.SelectedModel;
    }

    public static long GetSelectedHelper(IMyTerminalBlock block)
    {
      if (MyAPIGateway.Session?.Player == null)
        return 0L;

      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return 0L;

      var helpers = AiSession.Instance.MyHelperInfo;
      if (helpers == null || helpers.Count == 0)
      {
        gameLogic.ButtonPressed = true;
        SetContextMessage(block, "You have no helpers...");
        return 0L;
      }

      var helperInfo = gameLogic.SelectedHelper;
      if (helperInfo == null)
      {
        gameLogic.SelectedHelper = null;
        return 0L;
      }

      for (int i = 0; i < helpers.Count; i++)
      {
        var info = helpers[i];
        if (info.HelperId == helperInfo.HelperId)
          return i + 1;
      }

      return 0L;
    }

    private static void RefreshTerminalControls(IMyTerminalBlock b)
    {
      if (_refreshToggle != null)
      {
        var originalSetting = _refreshToggle.Getter(b);
        _refreshToggle.Setter(b, !originalSetting);
        _refreshToggle.Setter(b, originalSetting);
      }
    }

    private static void GetTypeContent(List<MyTerminalControlComboBoxItem> list)
    {
      var key = 0;
      foreach (var name in Enum.GetNames(typeof(AiSession.BotType)))
      {
        var item = new MyTerminalControlComboBoxItem()
        {
          Key = key,
          Value = MyStringId.GetOrCompute(name)
        };

        list.Add(item);
        key++;
      }
    }

    private static void GetModelContent(List<MyTerminalControlComboBoxItem> list)
    {
      var key = 0;
      foreach (var name in Enum.GetNames(typeof(AiSession.BotModel)))
      {
        var item = new MyTerminalControlComboBoxItem()
        {
          Key = key,
          Value = MyStringId.GetOrCompute(name)
        };

        list.Add(item);
        key++;
      }
    }

    private static void GetHelperContent(List<MyTerminalControlComboBoxItem> list)
    {
      if (MyAPIGateway.Session?.Player == null)
        return;

      var defaultItem = new MyTerminalControlComboBoxItem()
      {
        Key = 0,
        Value = MyStringId.GetOrCompute("None")
      };

      list.Add(defaultItem);

      var helpers = AiSession.Instance.MyHelperInfo;
      if (helpers == null || helpers.Count == 0)
        return;

      var key = 1;
      for (int i = 0; i < helpers.Count; i++)
      {
        var bot = helpers[i];
        var item = new MyTerminalControlComboBoxItem()
        {
          Key = key,
          Value = MyStringId.GetOrCompute(bot.DisplayName)
        };

        list.Add(item);
        key++;
      }
    }

    internal static void CreateActions(IMyTerminalBlock block)
    {
      if (AiSession.Instance.FactoryActionsCreated || !(block is IMyConveyorSorter))
        return;

      AiSession.Instance.FactoryActionsCreated = true;
      List<IMyTerminalAction> actions;
      MyAPIGateway.TerminalControls.GetActions<IMyConveyorSorter>(out actions);

      for (int i = 0; i < actions.Count; i++)
      {
        var ctrl = actions[i];
        ctrl.Enabled = CombineFunc.Create(ctrl.Enabled, Block => Block.BlockDefinition.SubtypeId != "RoboFactory");
      }
    }
  }
}
