using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.API;
using AiEnabled.Bots;
using AiEnabled.Bots.Roles;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.GameLogic;
using AiEnabled.Networking;
using AiEnabled.Networking.Packets;

using Sandbox.Definitions;
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

using static VRage.Game.MyObjectBuilder_BehaviorTreeDecoratorNode;

namespace AiEnabled.Support
{
  public static class Controls
  {
    private static IMyTerminalControlOnOffSwitch _refreshToggle;

    internal static void CreateControls(IMyTerminalBlock b)
    {
      if (AiSession.Instance.FactoryControlsCreated || !(b is IMyConveyorSorter))
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
        //"CustomData",
        "DrainAll",
        "blacklistWhitelist",
        "CurrentList",
        "removeFromSelectionButton",
        "candidatesList",
        "addToSelectionButton"
      };

      List<IMyTerminalControl> controls;
      MyAPIGateway.TerminalControls.GetControls<IMyConveyorSorter>(out controls);

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

      var sorterToggle = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyConveyorSorter>("SorterToggle");
      sorterToggle.Enabled = CombineFunc.Create(sorterToggle.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      sorterToggle.Visible = CombineFunc.Create(sorterToggle.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      sorterToggle.SupportsMultipleBlocks = false;
      sorterToggle.Title = MyStringId.GetOrCompute("Automatically Pull Bot Materials");
      sorterToggle.Tooltip = MyStringId.GetOrCompute("Toggle to enable / disable the auto-pulling of bot materials.");
      sorterToggle.Getter = Block => GetSorterToggleValue(Block);
      sorterToggle.Setter = SetSorterToggleValue;
      sorterToggle.OnText = MyStringId.GetOrCompute("Yes");
      sorterToggle.OffText = MyStringId.GetOrCompute("No");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(sorterToggle);
      controls.Add(sorterToggle);

      var separator = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyConveyorSorter>("Separator0");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(separator);
      controls.Add(separator);

      var labelPriorities = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyConveyorSorter>("LblPriorities");
      labelPriorities.Enabled = CombineFunc.Create(labelPriorities.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelPriorities.Visible = CombineFunc.Create(labelPriorities.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelPriorities.SupportsMultipleBlocks = false;
      labelPriorities.Label = MyStringId.GetOrCompute("Helper Priorities");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(labelPriorities);
      controls.Add(labelPriorities);

      var PriBtnVisSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyConveyorSorter>("BtnVisSwitch");
      PriBtnVisSwitch.Enabled = CombineFunc.Create(PriBtnVisSwitch.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      PriBtnVisSwitch.Visible = CombineFunc.Create(PriBtnVisSwitch.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      PriBtnVisSwitch.SupportsMultipleBlocks = false;
      PriBtnVisSwitch.Title = MyStringId.GetOrCompute(""); // Switch Priority List
      PriBtnVisSwitch.Tooltip = MyStringId.GetOrCompute("Switches which priority list is shown.");
      PriBtnVisSwitch.OnText = MyStringId.GetOrCompute("Repair");
      PriBtnVisSwitch.OffText = MyStringId.GetOrCompute("Target");
      PriBtnVisSwitch.Setter = (Block, enabled) => PriBoxSwitch_Setter(Block, enabled);
      PriBtnVisSwitch.Getter = (Block) => PriBoxSwitch_Getter(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(PriBtnVisSwitch);
      controls.Add(PriBtnVisSwitch);

      var weldPriCheckBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyConveyorSorter>("CheckBoxWeldPriority");
      weldPriCheckBox.Enabled = CombineFunc.Create(weldPriCheckBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      weldPriCheckBox.Visible = CombineFunc.Create(weldPriCheckBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      weldPriCheckBox.Title = MyStringId.GetOrCompute("Weld before grind");
      weldPriCheckBox.Tooltip = MyStringId.GetOrCompute("If checked, bots will prioritize welding. If not, they will prioritize grinding.");
      weldPriCheckBox.OnText = MyStringId.GetOrCompute("Checked");
      weldPriCheckBox.OffText = MyStringId.GetOrCompute("Unchecked");
      weldPriCheckBox.Getter = (Block) => WeldBeforeGrindBtn_Getter(Block);
      weldPriCheckBox.Setter = (Block, enabled) => WeldBeforeGrindBtn_Setter(Block, enabled);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(weldPriCheckBox);
      controls.Add(weldPriCheckBox);

      var repairPriorityBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyConveyorSorter>("ListBoxRepairPri");
      repairPriorityBox.Enabled = CombineFunc.Create(repairPriorityBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      repairPriorityBox.Visible = CombineFunc.Create(repairPriorityBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      repairPriorityBox.SupportsMultipleBlocks = false;
      repairPriorityBox.VisibleRowsCount = 15;
      repairPriorityBox.Multiselect = false;
      repairPriorityBox.Title = MyStringId.GetOrCompute("Repair Priorities");
      repairPriorityBox.Tooltip = MyStringId.GetOrCompute("Adjust your helpers' repair priorities.");
      repairPriorityBox.ListContent = (Block, lst1, lst2) => RepairBoxListContent(Block, lst1, lst2);
      repairPriorityBox.ItemSelected = (Block, selectedItems) => RepairBox_ItemSelected(Block, selectedItems);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(repairPriorityBox);
      controls.Add(repairPriorityBox);

      var rprPriBtnOnOff = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyConveyorSorter>("BtnOnOffRprPri");
      rprPriBtnOnOff.Enabled = CombineFunc.Create(rprPriBtnOnOff.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      rprPriBtnOnOff.Visible = CombineFunc.Create(rprPriBtnOnOff.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      rprPriBtnOnOff.SupportsMultipleBlocks = false;
      rprPriBtnOnOff.Title = MyStringId.GetOrCompute("Toggle Priority Type");
      rprPriBtnOnOff.Tooltip = MyStringId.GetOrCompute("Toggles the selected priority type On/Off.");
      rprPriBtnOnOff.OnText = MyStringId.GetOrCompute("Enable");
      rprPriBtnOnOff.OffText = MyStringId.GetOrCompute("Disable");
      rprPriBtnOnOff.Setter = (Block, enabled) => RepairPriBoxOnOff_Setter(Block, enabled);
      rprPriBtnOnOff.Getter = (Block) => RepairPriBoxOnOff_Getter(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(rprPriBtnOnOff);
      controls.Add(rprPriBtnOnOff);

      var rprPriBtnUp = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnUpRprPri");
      rprPriBtnUp.Enabled = CombineFunc.Create(rprPriBtnUp.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      rprPriBtnUp.Visible = CombineFunc.Create(rprPriBtnUp.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      rprPriBtnUp.SupportsMultipleBlocks = false;
      rprPriBtnUp.Title = MyStringId.GetOrCompute("Move Priority Up");
      rprPriBtnUp.Tooltip = MyStringId.GetOrCompute("Moves the selected priority type Up.");
      rprPriBtnUp.Action = Block => RepairPriBoxUp_Submitted(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(rprPriBtnUp);
      controls.Add(rprPriBtnUp);

      var rprPriBtnDown = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnDwnRprPri");
      rprPriBtnDown.Enabled = CombineFunc.Create(rprPriBtnDown.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      rprPriBtnDown.Visible = CombineFunc.Create(rprPriBtnDown.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == true);
      rprPriBtnDown.SupportsMultipleBlocks = false;
      rprPriBtnDown.Title = MyStringId.GetOrCompute("Move Priority Down");
      rprPriBtnDown.Tooltip = MyStringId.GetOrCompute("Moves the selected priority type Down.");
      rprPriBtnDown.Action = Block => RepairPriBoxDown_Submitted(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(rprPriBtnDown);
      controls.Add(rprPriBtnDown);

      var tgtDmgCheckBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyConveyorSorter>("CheckBoxDmgToDisable");
      tgtDmgCheckBox.Enabled = CombineFunc.Create(tgtDmgCheckBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      tgtDmgCheckBox.Visible = CombineFunc.Create(tgtDmgCheckBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      tgtDmgCheckBox.Title = MyStringId.GetOrCompute("Damage to Disable");
      tgtDmgCheckBox.Tooltip = MyStringId.GetOrCompute("If checked, bots will only damage functional blocks until they are disabled, otherwise until destroyed.");
      tgtDmgCheckBox.OnText = MyStringId.GetOrCompute("Checked");
      tgtDmgCheckBox.OffText = MyStringId.GetOrCompute("Unchecked");
      tgtDmgCheckBox.Getter = (Block) => TargetDamageToDisableBtn_Getter(Block);
      tgtDmgCheckBox.Setter = (Block, enabled) => TargetDamageToDisableBtn_Setter(Block, enabled);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(tgtDmgCheckBox);
      controls.Add(tgtDmgCheckBox);

      var targetPriorityBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyConveyorSorter>("ListBoxTargetPri");
      targetPriorityBox.Enabled = CombineFunc.Create(targetPriorityBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      targetPriorityBox.Visible = CombineFunc.Create(targetPriorityBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                      && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      targetPriorityBox.SupportsMultipleBlocks = false;
      targetPriorityBox.VisibleRowsCount = 16;
      targetPriorityBox.Multiselect = false;
      targetPriorityBox.Title = MyStringId.GetOrCompute("Target Priorities");
      targetPriorityBox.Tooltip = MyStringId.GetOrCompute("Adjust your helpers' target priorities.");
      targetPriorityBox.ListContent = (Block, lst1, lst2) => TargetBoxListContent(Block, lst1, lst2);
      targetPriorityBox.ItemSelected = (Block, selectedItems) => TargetBox_ItemSelected(Block, selectedItems);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(targetPriorityBox);
      controls.Add(targetPriorityBox);

      var tgtPriBtnOnOff = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyConveyorSorter>("BtnOnOffTgtPri");
      tgtPriBtnOnOff.Enabled = CombineFunc.Create(tgtPriBtnOnOff.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                    && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      tgtPriBtnOnOff.Visible = CombineFunc.Create(tgtPriBtnOnOff.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                    && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      tgtPriBtnOnOff.SupportsMultipleBlocks = false;
      tgtPriBtnOnOff.Title = MyStringId.GetOrCompute("Toggle Priority Type");
      tgtPriBtnOnOff.Tooltip = MyStringId.GetOrCompute("Toggles the selected priority type On/Off.");
      tgtPriBtnOnOff.OnText = MyStringId.GetOrCompute("Enable");
      tgtPriBtnOnOff.OffText = MyStringId.GetOrCompute("Disable");
      tgtPriBtnOnOff.Setter = (Block, enabled) => TargetPriBoxOnOff_Setter(Block, enabled);
      tgtPriBtnOnOff.Getter = (Block) => TargetPriBoxOnOff_Getter(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(tgtPriBtnOnOff);
      controls.Add(tgtPriBtnOnOff);

      var tgtPriBtnUp = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnUpTgtPri");
      tgtPriBtnUp.Enabled = CombineFunc.Create(tgtPriBtnUp.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      tgtPriBtnUp.Visible = CombineFunc.Create(tgtPriBtnUp.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      tgtPriBtnUp.SupportsMultipleBlocks = false;
      tgtPriBtnUp.Title = MyStringId.GetOrCompute("Move Priority Up");
      tgtPriBtnUp.Tooltip = MyStringId.GetOrCompute("Moves the selected priority type Up.");
      tgtPriBtnUp.Action = Block => TargetPriBoxUp_Submitted(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(tgtPriBtnUp);
      controls.Add(tgtPriBtnUp);

      var tgtPriBtnDown = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnDwnTgtPri");
      tgtPriBtnDown.Enabled = CombineFunc.Create(tgtPriBtnDown.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                  && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      tgtPriBtnDown.Visible = CombineFunc.Create(tgtPriBtnDown.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory"
                                                  && Block.GameLogic.GetAs<Factory>()?.ShowRepairPriorities == false);
      tgtPriBtnDown.SupportsMultipleBlocks = false;
      tgtPriBtnDown.Title = MyStringId.GetOrCompute("Move Priority Down");
      tgtPriBtnDown.Tooltip = MyStringId.GetOrCompute("Moves the selected priority type Down.");
      tgtPriBtnDown.Action = Block => TargetPriBoxDown_Submitted(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(tgtPriBtnDown);
      controls.Add(tgtPriBtnDown);

      var updatePriBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnUpdatePri");
      updatePriBtn.Enabled = CombineFunc.Create(updatePriBtn.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      updatePriBtn.Visible = CombineFunc.Create(updatePriBtn.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      updatePriBtn.SupportsMultipleBlocks = false;
      updatePriBtn.Title = MyStringId.GetOrCompute("Update Helper Priorities");
      updatePriBtn.Tooltip = MyStringId.GetOrCompute("Propagates changes to any active helpers for the local player. Does nothing for stored helpers.");
      updatePriBtn.Action = Block => UpdatePriorities_Submitted(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(updatePriBtn);
      controls.Add(updatePriBtn);

      var ignoreListBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyConveyorSorter>("IgnoreListBox");
      ignoreListBox.Enabled = CombineFunc.Create(ignoreListBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      ignoreListBox.Visible = CombineFunc.Create(ignoreListBox.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      ignoreListBox.SupportsMultipleBlocks = false;
      ignoreListBox.VisibleRowsCount = 10;
      ignoreListBox.Multiselect = false;
      ignoreListBox.Title = MyStringId.GetOrCompute("Pickup Ignore List");
      ignoreListBox.Tooltip = MyStringId.GetOrCompute("Adjust your helpers' ignore settings for item pickup. Use Update Priorities button to propagate changes.");
      ignoreListBox.ListContent = (Block, listItems, selectedItems) => IgnoreBox_ListContent(Block, listItems, selectedItems);
      ignoreListBox.ItemSelected = (Block, selectedItems) => IgnoreBox_ItemSelected(Block, selectedItems);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(ignoreListBox);
      controls.Add(ignoreListBox);

      var ignoreBtnOnOff = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyConveyorSorter>("BtnOnOffTgtPri");
      ignoreBtnOnOff.Enabled = CombineFunc.Create(ignoreBtnOnOff.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      ignoreBtnOnOff.Visible = CombineFunc.Create(ignoreBtnOnOff.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      ignoreBtnOnOff.SupportsMultipleBlocks = false;
      ignoreBtnOnOff.Title = MyStringId.GetOrCompute("Toggle Ignore Type");
      ignoreBtnOnOff.Tooltip = MyStringId.GetOrCompute("Toggles the selected type On/Off (On = Ignore).");
      ignoreBtnOnOff.OnText = MyStringId.GetOrCompute("Enable");
      ignoreBtnOnOff.OffText = MyStringId.GetOrCompute("Disable");
      ignoreBtnOnOff.Setter = (Block, enabled) => IgnoreBoxOnOff_Setter(Block, enabled);
      ignoreBtnOnOff.Getter = (Block) => IgnoreBoxOnOff_Getter(Block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(ignoreBtnOnOff);
      controls.Add(ignoreBtnOnOff);

      var separatorOne = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyConveyorSorter>("Separator1");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(separatorOne);
      controls.Add(separatorOne);

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
      comboRole.Title = MyStringId.GetOrCompute("Select Role");
      comboRole.ComboBoxContent = GetTypeContent;
      comboRole.Getter = Block => GetSelectedRole(Block);
      comboRole.Setter = SetSelectedRole;
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

      var colorInput = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyConveyorSorter>("CtrlColor");
      colorInput.Enabled = CombineFunc.Create(colorInput.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      colorInput.Visible = CombineFunc.Create(colorInput.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      colorInput.SupportsMultipleBlocks = false;
      colorInput.Title = MyStringId.GetOrCompute("Select Color");
      colorInput.Tooltip = MyStringId.GetOrCompute("Adjust your helper's color to your liking.");
      colorInput.Getter = Block => GetBotColor(Block);
      colorInput.Setter = (Block, clr) => SetBotColor(Block, clr);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(colorInput);
      controls.Add(colorInput);

      var buttonColorMatch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnColorMatch");
      buttonColorMatch.Enabled = CombineFunc.Create(buttonColorMatch.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonColorMatch.Visible = CombineFunc.Create(buttonColorMatch.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonColorMatch.SupportsMultipleBlocks = false;
      buttonColorMatch.Title = MyStringId.GetOrCompute("Match Player's Color");
      buttonColorMatch.Tooltip = MyStringId.GetOrCompute("Sets the color input to the player's suit color.");
      buttonColorMatch.Action = MatchPlayerColor;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(buttonColorMatch);
      controls.Add(buttonColorMatch);

      var buttonColorRandom = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnColorRandom");
      buttonColorRandom.Enabled = CombineFunc.Create(buttonColorRandom.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonColorRandom.Visible = CombineFunc.Create(buttonColorRandom.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonColorRandom.SupportsMultipleBlocks = false;
      buttonColorRandom.Title = MyStringId.GetOrCompute("Randomize Color");
      buttonColorRandom.Tooltip = MyStringId.GetOrCompute("Randomizes the color input.");
      buttonColorRandom.Action = RandomizeColor;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(buttonColorRandom);
      controls.Add(buttonColorRandom);

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

      var separatorTwo = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyConveyorSorter>("Separator2");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(separatorTwo);
      controls.Add(separatorTwo);

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

      var separatorThree = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyConveyorSorter>("Separator3");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(separatorThree);
      controls.Add(separatorThree);

      var labelReset = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyConveyorSorter>("LblResetMap");
      labelReset.Enabled = CombineFunc.Create(labelReset.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelReset.Visible = CombineFunc.Create(labelReset.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      labelReset.SupportsMultipleBlocks = false;
      labelReset.Label = MyStringId.GetOrCompute("Map Management");
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(labelReset);
      controls.Add(labelReset);

      var buttonMapRedo = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnMapRedo");
      buttonMapRedo.Enabled = CombineFunc.Create(buttonMapRedo.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonMapRedo.Visible = CombineFunc.Create(buttonMapRedo.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonMapRedo.SupportsMultipleBlocks = false;
      buttonMapRedo.Title = MyStringId.GetOrCompute("Reset Map");
      buttonMapRedo.Tooltip = MyStringId.GetOrCompute("Will clean and restart the mapping process for the grid.");
      buttonMapRedo.Action = ReworkGridMap;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(buttonMapRedo);
      controls.Add(buttonMapRedo);

      var buttonObstacles = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("BtnObstacles");
      buttonObstacles.Enabled = CombineFunc.Create(buttonObstacles.Enabled, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonObstacles.Visible = CombineFunc.Create(buttonObstacles.Visible, Block => Block.BlockDefinition.SubtypeId == "RoboFactory");
      buttonObstacles.SupportsMultipleBlocks = false;
      buttonObstacles.Title = MyStringId.GetOrCompute("Clear Obstacles");
      buttonObstacles.Tooltip = MyStringId.GetOrCompute("Will clear currently blocked paths for the grid map.");
      buttonObstacles.Action = ClearObstacles;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(buttonObstacles);
      controls.Add(buttonObstacles);
    }

    private static void ReworkGridMap(IMyTerminalBlock block)
    {
      var grid = block?.CubeGrid;
      if (grid != null)
      {
        var pkt = new ResetMapPacket(grid.EntityId);
        AiSession.Instance.Network.SendToServer(pkt);

        var gameLogic = block.GameLogic.GetAs<Factory>();
        if (gameLogic != null)
        {
          gameLogic.ButtonPressed = true;
          SetContextMessage(block, "Regenerating grid map...");
        }
      }
    }

    private static void ClearObstacles(IMyTerminalBlock block)
    {
      var grid = block?.CubeGrid;
      if (grid != null)
      {
        var pkt = new ResetMapPacket(grid.EntityId, true);
        AiSession.Instance.Network.SendToServer(pkt);

        var gameLogic = block.GameLogic.GetAs<Factory>();
        if (gameLogic != null)
        {
          gameLogic.ButtonPressed = true;
          SetContextMessage(block, "Clearing blocked paths...");
        }
      }
    }


    private static void RandomizeColor(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      int x = MyUtils.GetRandomInt(256);
      int y = MyUtils.GetRandomInt(256);
      int z = MyUtils.GetRandomInt(256);

      gameLogic.BotColor = new Color(x, y, z);
      RefreshTerminalControls(block);
    }

    private static void MatchPlayerColor(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      var playerChar = MyAPIGateway.Session?.Player?.Character;
      if (playerChar != null)
      {
        var ob = playerChar.GetObjectBuilder() as MyObjectBuilder_Character;
        var hsv = MyColorPickerConstants.HSVOffsetToHSV(ob.ColorMaskHSV);
        var color = hsv.HSVtoColor();

        gameLogic.BotColor = color;
        RefreshTerminalControls(block);
      }
    }

    private static bool WeldBeforeGrindBtn_Getter(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      return logic?.RepairPriorities?.WeldBeforeGrind ?? false;
    }

    private static void WeldBeforeGrindBtn_Setter(IMyTerminalBlock block, bool enabled)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      if (logic.RepairPriorities == null)
        logic.RepairPriorities = new RepairPriorities();

      logic.RepairPriorities.WeldBeforeGrind = enabled;
      logic.UpdatePriorityLists(true, false, false);
      RefreshTerminalControls(block);
    }

    private static bool TargetDamageToDisableBtn_Getter(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      return logic?.TargetPriorities?.DamageToDisable ?? false;
    }

    private static void TargetDamageToDisableBtn_Setter(IMyTerminalBlock block, bool enabled)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      if (logic.TargetPriorities == null)
        logic.TargetPriorities = new TargetPriorities();

      logic.TargetPriorities.DamageToDisable = enabled;
      RefreshTerminalControls(block);
    }

    private static bool PriBoxSwitch_Getter(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      return logic?.ShowRepairPriorities ?? true;
    }

    private static void PriBoxSwitch_Setter(IMyTerminalBlock block, bool enabled)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      logic.ShowRepairPriorities = enabled;
      RefreshTerminalControls(block);
    }

    private static void UpdatePriorities_Submitted(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var player = MyAPIGateway.Session.Player;
      if (player == null)
        return;

      var ignList = logic.RepairPriorities.IgnoreList;
      var repList = logic.RepairPriorities.PriorityTypes;
      var tgtList = logic.TargetPriorities.PriorityTypes;

      var pkt = new PriorityUpdatePacket(player.IdentityId, ignList, repList, tgtList, logic.TargetPriorities.DamageToDisable, logic.RepairPriorities.WeldBeforeGrind);
      AiSession.Instance.Network.SendToServer(pkt);

      logic.ButtonPressed = true;
      SetContextMessage(block, "Updates sent!");
    }

    private static void IgnoreBox_ItemSelected(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selectedItems)
    {
      if (selectedItems == null || selectedItems.Count == 0)
        return;

      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var item = selectedItems[0].Text.String;
      var enabled = item.StartsWith("[X]");

      var idx = item.IndexOf("]");
      if (idx >= 0)
        item = item.Substring(idx + 1).Trim();

      logic.SelectedIgnoreItem = new KeyValuePair<string, bool>(item, enabled);
      RefreshTerminalControls(block);
    }

    private static void RepairBox_ItemSelected(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selectedItems)
    {
      if (selectedItems == null || selectedItems.Count == 0)
        return;

      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var item = selectedItems[0].Text.String;
      var enabled = item.StartsWith("[X]");

      var idx = item.IndexOf("]");
      if (idx >= 0)
        item = item.Substring(idx + 1).Trim();

      logic.SelectedRepairPriority = new KeyValuePair<string, bool>(item, enabled);
      RefreshTerminalControls(block);
    }

    private static void TargetBox_ItemSelected(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selectedItems)
    {
      if (selectedItems == null || selectedItems.Count == 0)
        return;

      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var item = selectedItems[0].Text.String;
      var enabled = item.StartsWith("[X]");

      var idx = item.IndexOf("]");
      if (idx >= 0)
        item = item.Substring(idx + 1).Trim();

      logic.SelectedTargetPriority = new KeyValuePair<string, bool>(item, enabled);
      RefreshTerminalControls(block);
    }

    private static void TargetPriBoxUp_Submitted(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var selected = logic.SelectedTargetPriority;
      if (string.IsNullOrEmpty(selected.Key))
        return;

      var index = logic.TargetPriorities.IndexOf(selected.Key);

      if (index > 0)
      {
        logic.TargetPriorities.PriorityTypes.Move(index, index - 1);
        logic.UpdatePriorityLists(false, true, false);
        RefreshTerminalControls(block);
      }
    }

    private static void TargetPriBoxDown_Submitted(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var selected = logic.SelectedTargetPriority;
      if (string.IsNullOrEmpty(selected.Key))
        return;

      var index = logic.TargetPriorities.IndexOf(selected.Key);

      if (index >= 0 && index < logic.TargetPriorities.PriorityTypes.Count - 1)
      {
        logic.TargetPriorities.PriorityTypes.Move(index, index + 1);
        logic.UpdatePriorityLists(false, true, false);
        RefreshTerminalControls(block);
      }
    }

    private static bool TargetPriBoxOnOff_Getter(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return false;

      var selected = logic.SelectedTargetPriority;
      if (string.IsNullOrEmpty(selected.Key))
        return false;

      return selected.Value;
    }

    private static void TargetPriBoxOnOff_Setter(IMyTerminalBlock block, bool enabled)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var selected = logic.SelectedTargetPriority;
      if (string.IsNullOrEmpty(selected.Key))
        return;

      var index = logic.TargetPriorities.IndexOf(selected.Key);
      if (index >= 0)
      {
        var kvp = new KeyValuePair<string, bool>(selected.Key, enabled);
        logic.TargetPriorities.PriorityTypes[index] = kvp;
        logic.SelectedTargetPriority = kvp;
        logic.UpdatePriorityLists(false, true, false);
        RefreshTerminalControls(block);
      }
    }

    private static bool IgnoreBoxOnOff_Getter(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return false;

      var selected = logic.SelectedIgnoreItem;
      if (string.IsNullOrEmpty(selected.Key))
        return false;

      return selected.Value;
    }

    private static void IgnoreBoxOnOff_Setter(IMyTerminalBlock block, bool enabled)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var selected = logic.SelectedIgnoreItem;
      if (string.IsNullOrEmpty(selected.Key))
        return;

      var index = logic.RepairPriorities.IndexOf(selected.Key);
      if (index >= 0)
      {
        var kvp = new KeyValuePair<string, bool>(selected.Key, enabled);
        logic.RepairPriorities.IgnoreList[index] = kvp;
        logic.SelectedIgnoreItem = kvp;
        logic.UpdatePriorityLists(false, false, true);
        RefreshTerminalControls(block);
      }
    }

    private static void RepairPriBoxDown_Submitted(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var selected = logic.SelectedRepairPriority;
      if (string.IsNullOrEmpty(selected.Key))
        return;

      var index = logic.RepairPriorities.PriorityTypes.IndexOf(selected);

      if (index >= 0 && index < logic.RepairPriorities.PriorityTypes.Count - 1)
      {
        logic.RepairPriorities.PriorityTypes.Move(index, index + 1);
        logic.UpdatePriorityLists(true, false, false);
        RefreshTerminalControls(block);
      }
    }

    private static void RepairPriBoxUp_Submitted(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var selected = logic.SelectedRepairPriority;
      if (string.IsNullOrEmpty(selected.Key))
        return;

      var index = logic.RepairPriorities.PriorityTypes.IndexOf(selected);

      if (index > 0)
      {
        logic.RepairPriorities.PriorityTypes.Move(index, index - 1);
        logic.UpdatePriorityLists(true, false, false);
        RefreshTerminalControls(block);
      }
    }

    private static bool RepairPriBoxOnOff_Getter(IMyTerminalBlock block)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return false;

      var selected = logic.SelectedRepairPriority;
      if (string.IsNullOrEmpty(selected.Key))
        return false;

      return selected.Value;
    }

    private static void RepairPriBoxOnOff_Setter(IMyTerminalBlock block, bool enabled)
    {
      var logic = block.GameLogic.GetAs<Factory>();
      if (logic == null)
        return;

      var selected = logic.SelectedRepairPriority;
      if (string.IsNullOrEmpty(selected.Key))
        return;

      var index = logic.RepairPriorities.IndexOf(selected.Key);
      if (index >= 0)
      {
        var kvp = new KeyValuePair<string, bool>(selected.Key, enabled);
        logic.RepairPriorities.PriorityTypes[index] = kvp;
        logic.SelectedRepairPriority = kvp;
        logic.UpdatePriorityLists(true, false, false);
        RefreshTerminalControls(block);
      }
    }

    private static void IgnoreBox_ListContent(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> listItems, List<MyTerminalControlListBoxItem> selectedItems)
    {
      var gameLogic = block?.GameLogic.GetAs<Factory>();
      if (gameLogic?.RepairPriorities == null)
        return;

      listItems.Clear();
      selectedItems.Clear();

      foreach (var pt in gameLogic.RepairPriorities.IgnoreList)
      {
        var prefix = pt.Value ? "[X]" : "[  ]";
        var name = $"{prefix} {pt.Key}";
        var id = MyStringId.GetOrCompute(name);
        var item = new MyTerminalControlListBoxItem(id, MyStringId.NullOrEmpty, null);
        listItems.Add(item);

        if (pt.Key == gameLogic.SelectedIgnoreItem.Key)
        {
          selectedItems.Add(item);
        }
      }
    }

    private static void TargetBoxListContent(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> listItems, List<MyTerminalControlListBoxItem> selectedItems)
    {
      var gameLogic = block?.GameLogic.GetAs<Factory>();
      if (gameLogic?.TargetPriorities == null)
        return;

      listItems.Clear();
      selectedItems.Clear();

      foreach (var pt in gameLogic.TargetPriorities.PriorityTypes)
      {
        var prefix = pt.Value ? "[X]" : "[  ]";
        var name = $"{prefix} {pt.Key}";
        var id = MyStringId.GetOrCompute(name);
        var item = new MyTerminalControlListBoxItem(id, MyStringId.NullOrEmpty, null);
        listItems.Add(item);

        if (pt.Key == gameLogic.SelectedTargetPriority.Key)
        {
          selectedItems.Add(item);
        }
      }
    }

    private static void RepairBoxListContent(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> listItems, List<MyTerminalControlListBoxItem> selectedItems)
    {
      var gameLogic = block?.GameLogic.GetAs<Factory>();
      if (gameLogic?.RepairPriorities == null)
        return;

      listItems.Clear();
      selectedItems.Clear();

      foreach (var pt in gameLogic.RepairPriorities.PriorityTypes)
      {
        var prefix = pt.Value ? "[X]" : "[  ]";
        var name = $"{prefix} {pt.Key}";
        var id = MyStringId.GetOrCompute(name);
        var item = new MyTerminalControlListBoxItem(id, MyStringId.NullOrEmpty, null);
        listItems.Add(item);

        if (pt.Key == gameLogic.SelectedRepairPriority.Key)
        {
          selectedItems.Add(item);
        }
      }
    }

    private static void SetSorterToggleValue(IMyTerminalBlock block, bool enable)
    {
      var gameLogic = block?.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      gameLogic.SorterEnabled = enable;
    }

    private static bool GetSorterToggleValue(IMyTerminalBlock block)
    {
      var gameLogic = block?.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return false;

      return gameLogic.SorterEnabled;
    }

    private static void SetBotColor(IMyTerminalBlock block, Color clr)
    {
      var funcBlock = block as IMyFunctionalBlock;
      if (funcBlock == null || !funcBlock.Enabled)
        return;

      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      gameLogic.BotColor = clr;
    }

    private static Color GetBotColor(IMyTerminalBlock block)
    {
      Color returnColor = Color.White;
      var playerChar = MyAPIGateway.Session?.Player?.Character;
      if (playerChar != null)
      {
        var ob = playerChar.GetObjectBuilder() as MyObjectBuilder_Character;
        var hsv = MyColorPickerConstants.HSVOffsetToHSV(ob.ColorMaskHSV);
        returnColor = hsv.HSVtoColor();
      }

      var funcBlock = block as IMyFunctionalBlock;
      if (funcBlock == null || !funcBlock.Enabled)
        return returnColor;

      var gameLogic = block.GameLogic.GetAs<Factory>();
      return gameLogic?.BotColor ?? returnColor;
    }

    private static void SpawnHelper(IMyTerminalBlock block)
    {
      var funcBlock = block as IMyFunctionalBlock;
      if (funcBlock == null || !funcBlock.Enabled)
        return;

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
      if (AiSession.Instance.PlayerToActiveHelperIds.TryGetValue(player.IdentityId, out helperIds) && helperIds.Count >= AiSession.Instance.ModSaveData.MaxHelpersPerPlayer)
      {
        int num = 0;
        for (int i = 0; i < AiSession.Instance.MyHelperInfo.Count; i++)
        {
          var helperInfo = AiSession.Instance.MyHelperInfo[i];
          if (helperInfo != null && helperInfo.AdminSpawned && helperInfo.IsActiveHelper && helperIds.Contains(helperInfo.HelperId))
            num++;
        }

        if (helperIds.Count - num >= AiSession.Instance.ModSaveData.MaxHelpersPerPlayer)
        {
          SetContextMessage(block, $"You already have {helperIds.Count - num} helper(s).");
          return;
        }
      }

      var botRole = gameLogic.SelectedRole;

      if (!AiSession.Instance.CanSpawnBot(botRole))
      {
        SetContextMessage(block, $"Selected Role not allowed.");
        return;
      }

      var botModel = gameLogic.SelectedModel;
      var botSubtype = AiSession.Instance.BotModelDict[botModel];

      if (botRole == AiSession.BotType.Scavenger)
      {
        var def = new MyDefinitionId(typeof(MyObjectBuilder_Character), botSubtype);
        var charDef = MyDefinitionManager.Static.GetDefinition(def) as MyCharacterDefinition;
        if (charDef != null && charDef.Skeleton == "Default_Astronaut")
        {
          botModel = AiSession.Instance.MODEL_DEFAULT;
        }
      }

      var credit = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalObject), "SpaceCredit");
      var inv = player.Character?.GetInventory() as MyInventory;
      var inventoryCredits = 0L;
      var price = 0L;

      var creativeMode = MyAPIGateway.Session.CreativeMode;
      var copyPasteEnabled = MyAPIGateway.Session.EnableCopyPaste;
      var adminSpawn = creativeMode || copyPasteEnabled;

      if (!adminSpawn)
      {
        long balance;
        player.TryGetBalanceInfo(out balance);
        price = AiSession.Instance.BotPrices[botRole];

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

        var compSubtype = $"AiEnabled_Comp_{botRole}BotMaterial";
        var comp = new MyDefinitionId(typeof(MyObjectBuilder_Component), compSubtype);

        var blockInv = block.GetInventory() as MyInventory;
        var amountInBlock = (int)(blockInv?.GetItemAmount(comp) ?? 0);

        if (amountInBlock > 0)
        {
          if (AiSession.Instance.IsServer)
          {
            blockInv.RemoveItemsOfType(1, comp);
          }
        }
        else
        {
          var amountInPlayer = (int)(inv?.GetItemAmount(comp) ?? 0);

          if (amountInPlayer > 0)
          {
            if (AiSession.Instance.IsServer)
            {
              inv.RemoveItemsOfType(1, comp);
            }
          }
          else
          {
            var def = AiSession.Instance.AllGameDefinitions[comp];
            SetContextMessage(block, $"Missing {def?.DisplayNameText ?? comp.SubtypeName}");
            return;
          }
        }
      }

      var displayName = gameLogic.BotName?.ToString() ?? "";
      var needsName = string.IsNullOrWhiteSpace(displayName);

      if (AiSession.Instance.IsServer)
      {
        string subtype;

        if (botModel == AiSession.Instance.MODEL_DEFAULT)
        {
          var modelDict = AiSession.Instance.BotModelDict;
          MyStringId hash;

          switch (botRole)
          {
            case AiSession.BotType.Repair:
              if (needsName)
                displayName = "RepairBot";

              hash = MyStringId.GetOrCompute("Drone Bot");
              if (!modelDict.TryGetValue(hash, out subtype))
              {
                if (modelDict.Count > 1)
                  subtype = modelDict.FirstOrDefault(x => x.Value != "Default").Value;
              }

              break;
            case AiSession.BotType.Combat:
              if (needsName)
                displayName = "CombatBot";

              hash = MyStringId.GetOrCompute("Target Dummy");
              if (!modelDict.TryGetValue(hash, out subtype))
              {
                if (modelDict.Count > 1)
                  subtype = modelDict.FirstOrDefault(x => x.Value != "Default").Value;
              }

              break;
            case AiSession.BotType.Scavenger:
              if (needsName)
                displayName = "ScavengerBot";

              hash = MyStringId.GetOrCompute("Robo Dog");
              if (!modelDict.TryGetValue(hash, out subtype))
              {
                if (modelDict.Count > 1)
                  subtype = modelDict.FirstOrDefault(x => x.Value != "Default").Value;
              }

              break;
            case AiSession.BotType.Crew:
              if (needsName)
                displayName = "CrewBot";

              subtype = MyUtils.GetRandomInt(0, 10) >= 5 ? "Default_Astronaut" : "Default_Astronaut_Female";
              break;
            default:
              return;
          }
        }
        else
        {
          if (needsName)
            displayName = $"{botRole}Bot";

          subtype = AiSession.Instance.BotModelDict[botModel];
        }

        if (string.IsNullOrEmpty(subtype) || subtype == "Default")
          subtype = "Default_Astronaut";

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
        var botColor = gameLogic.BotColor;

        var tuple = BotFactory.CreateBotObject(subtype, displayName, posOr, player.IdentityId, botColor);
        var helper = tuple.Item1;
        if (helper != null)
        {
          helper.SetPosition(position);
          if (helper.Physics != null && block.CubeGrid.Physics != null)
          {
            var gridPhysics = block.CubeGrid.Physics;
            helper.Physics.LinearVelocity = gridPhysics.LinearVelocity;
            helper.Physics.AngularVelocity = gridPhysics.AngularVelocity;
          }

          gameLogic.SetHelper(tuple, player);
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
        var repairPris = gameLogic.RepairPriorities?.PriorityTypes;
        var targetPris = gameLogic.TargetPriorities?.PriorityTypes;

        var packet = new FactorySpawnPacket(botRole, botModel.String, displayName, block.EntityId, player.IdentityId, price, inventoryCredits, gameLogic.BotColor, repairPris, targetPris, adminSpawn);
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

      var helpers = AiSession.Instance.MyHelperInfo;

      if (helpers == null)
      { 
        helpers = new List<ConfigData.HelperInfo>();
        AiSession.Instance.MyHelperInfo = helpers;
      }

      for (int i = 0; i < helpers.Count; i++)
      {
        var helper = helpers[i];
        if (helper == null)
          continue;

        if (helper.DisplayName == helperInfo.DisplayName)
        {
          helpers.RemoveAt(i);
          break;
        }
      }

      gameLogic.SelectedHelper = null;
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

      if (AiSession.Instance?.Registered != true || !AiSession.Instance.CanSpawn)
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
      if (AiSession.Instance.PlayerToActiveHelperIds.TryGetValue(player.IdentityId, out helperIds) && helperIds.Count >= AiSession.Instance.ModSaveData.MaxHelpersPerPlayer)
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

      if (!AiSession.Instance.PendingBotRespawns.Add(helperInfo.DisplayName))
      {
        SetContextMessage(block, "Helper already pending recall...");
        return;
      }

      List<KeyValuePair<string, bool>> pris;
      if (helperInfo.Role == 2)
        pris = gameLogic.TargetPriorities?.PriorityTypes;
      else
        pris = gameLogic.RepairPriorities?.PriorityTypes;

      var ignList = gameLogic.RepairPriorities?.IgnoreList;
      var pkt = new FactoryRecallPacket(block.EntityId, helperInfo.HelperId, player.IdentityId, pris, ignList);
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
        var labelText = label.Label.String;

        if (labelText == "Helper Factory" || labelText == "Helper Priorities" || labelText == "Helper Management")
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

    public static void SetSelectedRole(IMyTerminalBlock block, long value)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
        return;

      var botModel = gameLogic.SelectedModel;
      var roleString = AiSession.AllowedBotRoles[(int)value];
      AiSession.BotType botRole;
      Enum.TryParse(roleString, out botRole);
      gameLogic.SelectedRole = botRole;

      if (botModel != AiSession.Instance.MODEL_DEFAULT)
      {
        bool invalidModel = false;

        if (gameLogic.SelectedRole == AiSession.BotType.Scavenger)
        {
          var botSubtype = MyStringHash.GetOrCompute(AiSession.Instance.BotModelDict[botModel]);
          var skeleton = AiSession.Instance.SubtypeToSkeletonDictionary[botSubtype];
          if (skeleton == "Humanoid")
          {
            gameLogic.SelectedModel = AiSession.Instance.MODEL_DEFAULT;
            invalidModel = true;
          }
        }
        else // if (gameLogic.SelectedRole == AiSession.BotType.Repair) --> should apply to Repair, Combat, and Crew
        {
          var botSubtype = MyStringHash.GetOrCompute(AiSession.Instance.BotModelDict[botModel]);
          var skeleton = AiSession.Instance.SubtypeToSkeletonDictionary[botSubtype];
          var animController = AiSession.Instance.AnimationControllerDictionary[botSubtype];
          if (skeleton != "Humanoid" || (!animController.StartsWith("Default_Astronaut") && animController != "Space_Skeleton" 
            && animController != "Plushie_Astronaut" && animController != "Robo_Plushie"))
          {
            gameLogic.SelectedModel = AiSession.Instance.MODEL_DEFAULT;
            invalidModel = true;
          }
        }

        block.RefreshCustomInfo();

        if (invalidModel)
          SetContextMessage(block, "Invalid model for role");
        else
          RefreshTerminalControls(block);
      }
      else
      {
        block.RefreshCustomInfo();
        RefreshTerminalControls(block);
      }
    }

    public static void SetSelectedModel(IMyTerminalBlock block, long value)
    {
      try
      {
        var gameLogic = block.GameLogic.GetAs<Factory>();
        if (gameLogic == null)
          return;

        var index = (int)value;
        var botModel = (index < 0 || index > AiSession.Instance.BotModelList.Count - 1) ? AiSession.Instance.MODEL_DEFAULT : AiSession.Instance.BotModelList[index];
        bool invalidModel = false;

        if (botModel != AiSession.Instance.MODEL_DEFAULT)
        {
          if (gameLogic.SelectedRole == AiSession.BotType.Scavenger)
          {
            var botSubtype = MyStringHash.GetOrCompute(AiSession.Instance.BotModelDict[botModel]);
            var skeleton = AiSession.Instance.SubtypeToSkeletonDictionary[botSubtype];
            if (skeleton == "Humanoid")
            {
              botModel = AiSession.Instance.MODEL_DEFAULT;
              invalidModel = true;
            }
          }
          else // if (gameLogic.SelectedRole == AiSession.BotType.Repair) --> should apply to Repair, Combat, and Crew
          {
            var botSubtype = MyStringHash.GetOrCompute(AiSession.Instance.BotModelDict[botModel]);
            var skeleton = AiSession.Instance.SubtypeToSkeletonDictionary[botSubtype];
            var animController = AiSession.Instance.AnimationControllerDictionary[botSubtype];
            if (skeleton != "Humanoid" || (!animController.StartsWith("Default_Astronaut") && animController != "Space_Skeleton"
              && animController != "Plushie_Astronaut" && animController != "Robo_Plushie"))
            {
              botModel = AiSession.Instance.MODEL_DEFAULT;
              invalidModel = true;
            }
          }
        }

        gameLogic.SelectedModel = botModel;
        block.RefreshCustomInfo();

        if (invalidModel)
          SetContextMessage(block, "Invalid model for role");
        else
          RefreshTerminalControls(block);
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log(ex.ToString());
      }
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

    public static long GetSelectedRole(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
      {
        var modData = AiSession.Instance.ModSaveData;
        if (modData.AllowRepairBot)
          return 0L;

        if (modData.AllowScavengerBot)
          return 1L;

        if (modData.AllowCombatBot)
          return 2L;

        if (modData.AllowCrewBot)
          return 3L;

        return 0L;
      }

      var role = gameLogic.SelectedRole.ToString();
      var idx = Math.Max(0, AiSession.AllowedBotRoles.IndexOf(role));
      return (long)idx;
    }

    public static long GetSelectedModel(IMyTerminalBlock block)
    {
      var gameLogic = block.GameLogic.GetAs<Factory>();
      if (gameLogic?.SelectedModel == null)
        return 0L;

      var model = gameLogic.SelectedModel;
      var index = (long)AiSession.Instance.BotModelList.IndexOf(model);
      return Math.Max(0L, index);
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
        return 0L;

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
      AiSession.AllowedBotRoles.Clear();
      var saveData = AiSession.Instance.ModSaveData;
      var key = 0;
      foreach (var name in Enum.GetNames(typeof(AiSession.BotType)))
      {
        if (!AiSession.Instance.CanSpawnBot(name))
          continue;

        var item = new MyTerminalControlComboBoxItem()
        {
          Key = key,
          Value = MyStringId.GetOrCompute(name)
        };

        AiSession.AllowedBotRoles.Add(name);
        list.Add(item);
        key++;
      }
    }

    private static void GetModelContent(List<MyTerminalControlComboBoxItem> list)
    {
      var key = 0;

      foreach (var name in AiSession.Instance.BotModelList)
      {
        var item = new MyTerminalControlComboBoxItem()
        {
          Key = key,
          Value = name
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
        if (bot == null)
          continue;

        var prefix = bot.IsActiveHelper ? "[A]" : "[S]";
        var item = new MyTerminalControlComboBoxItem()
        {
          Key = key,
          Value = MyStringId.GetOrCompute($"{prefix} {bot.DisplayName}")
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