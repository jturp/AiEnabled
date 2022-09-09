using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;
using AiEnabled.Utilities;
using AiEnabled.Ai.Support;
using AiEnabled.Ai;
using AiEnabled.Networking;
using AiEnabled.Support;
using VRageMath;
using VRage.Game.Entity.UseObject;
using VRage.Game.Entity;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;
using Task = ParallelTasks.Task;
using VRage.Utils;
using VRage.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.Game;
using Sandbox.Game.Components;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using VRage.ObjectBuilders;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Bots.Roles.Helpers;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using AiEnabled.Graphics.Support;
using VRage.Input;
using VRage;
using AiEnabled.Bots;
using VRage.Voxels;

using static AiEnabled.API.HudAPIv2;
using AiEnabled.ConfigData;

namespace AiEnabled.Graphics
{
  public class PlayerMenu
  {
    internal MenuRootCategory Menu, AdminMenu;
    internal MenuItem ShowHealthBars;
    internal MenuSliderInput MouseSensitivity;
    internal MenuKeybindInput RecallBotsKeyBind;
    internal MenuTextInput RepairBotIgnoreColorInput, RepairBotGrindColorInput, MaxBots, MaxHelpers;
    PlayerData _playerData;

    public PlayerMenu(PlayerData playerData)
    {
      _playerData = playerData;
    }

    public void Register()
    {
      bool showHealthBars = _playerData.ShowHealthBars;
      float mouseSensitivity = _playerData.MouseSensitivityModifier;

      Menu = new MenuRootCategory("AiEnabled", MenuRootCategory.MenuFlag.PlayerMenu, "Settings");
      ShowHealthBars = CreateMenuItemToggle(Menu, showHealthBars, "Show health bars", ShowHealthBars_Clicked);
      MouseSensitivity = new MenuSliderInput($"Mouse Sensitivity: {mouseSensitivity}", Menu, mouseSensitivity * 0.5f, OnSubmitAction: MouseSensitivity_Submitted, SliderPercentToValue: PercentToValueFunc);

      var colorMenu = new MenuSubCategory("Color Settings               <color=teal>==>", Menu, "Color Settings");
      Vector3? vec = _playerData.RepairBotIgnoreColorHSV;
      var x = vec?.X.ToString() ?? "N";
      var y = vec?.Y.ToString() ?? "N";
      var z = vec?.Z.ToString() ?? "N";

      var color = (vec == null) ? "<color=yellow>" : "<color=green>";
      RepairBotIgnoreColorInput = new MenuTextInput($"RepairBot ignore color (HSV): {color}{{H:{x}, S:{y}, V:{z}}}", colorMenu, "Assign the ignore color for Repair Bots, in format H,S,V", TextInputIgnore_Submitted);

      vec = _playerData.RepairBotGrindColorHSV;
      x = vec?.X.ToString() ?? "N";
      y = vec?.Y.ToString() ?? "N";
      z = vec?.Z.ToString() ?? "N";

      color = (vec == null) ? "<color=yellow>" : "<color=green>";
      RepairBotGrindColorInput = new MenuTextInput($"RepairBot grind color (HSV): {color}{{H:{x}, S:{y}, V:{z}}}", colorMenu, "Assign the grind color for Repair Bots, in format H,S,V", TextInputGrind_Submitted);

      var keyBindMenu = new MenuSubCategory("Key Bindings                <color=teal>==>", Menu, "Key Bindings");
      RecallBotsKeyBind = new MenuKeybindInput($"Recall Bots: <color=yellow>None", keyBindMenu, "Press any key to bind\nCan be combined with alt/ctrl/shift", RecallBotsKeyBind_Submitted);

      AdminMenu = new MenuRootCategory("AiEnabled", MenuRootCategory.MenuFlag.AdminMenu, "Admin Settings");
      MaxBots = new MenuTextInput($"Max Bots allowed in world: {AiSession.Instance.MaxBots}", AdminMenu, "Set the maximum number of bots allowed in the world", MaxBots_Submitted);
      MaxHelpers = new MenuTextInput($"Max active Helpers allowed per player: {AiSession.Instance.MaxHelpers}", AdminMenu, "Set the maximum number of active helpers allowed per player", MaxHelpers_Submitted);
    }

    public void Close()
    {
      try
      {
        if (Menu != null)
        {
          Menu.Text = null;
          Menu.BackingObject = null;
          Menu = null;
        }

        if (AdminMenu != null)
        {
          AdminMenu.Text = null;
          AdminMenu.BackingObject = null;
          AdminMenu = null;
        }

        if (ShowHealthBars != null)
        {
          ShowHealthBars.OnClick = null;
          ShowHealthBars.Text = null;
          ShowHealthBars.BackingObject = null;
          ShowHealthBars = null;
        }

        if (MouseSensitivity != null)
        {
          MouseSensitivity.Text = null;
          MouseSensitivity.BackingObject = null;
          MouseSensitivity = null;
        }

        if (RecallBotsKeyBind != null)
        {
          RecallBotsKeyBind.OnSubmitAction = null;
          RecallBotsKeyBind.InputDialogTitle = null;
          RecallBotsKeyBind.Text = null;
          RecallBotsKeyBind.BackingObject = null;
          RecallBotsKeyBind = null;
        }

        if (RepairBotIgnoreColorInput != null)
        {
          RepairBotIgnoreColorInput.Text = null;
          RepairBotIgnoreColorInput.OnSubmitAction = null;
          RepairBotIgnoreColorInput.BackingObject = null;
          RepairBotIgnoreColorInput = null;
        }

        if (MaxBots != null)
        {
          MaxBots.Text = null;
          MaxBots.OnSubmitAction = null;
          MaxBots.BackingObject = null;
          MaxBots = null;
        }

        if (MaxHelpers != null)
        {
          MaxHelpers.Text = null;
          MaxHelpers.OnSubmitAction = null;
          MaxHelpers.BackingObject = null;
          MaxHelpers = null;
        }

        _playerData = null;
      }
      catch { }
    }

    MenuItem CreateMenuItemToggle(MenuCategoryBase category, bool toggleItem, string title, Action onClick, bool interactable = true)
    {
      var color = toggleItem ? "<color=green>" : "<color=yellow>";
      var text = $"{title}: {color}{toggleItem.ToString()}";

      return new MenuItem(text, category, onClick, interactable);
    }

    object PercentToValueFunc(float input)
    {
      return (float)MathHelper.Clamp(Math.Round(input * 2, 2), 0.1, 2);
    }

    internal void MaxBots_Submitted(string input)
    {
      double num;
      if (double.TryParse(input, out num) && num > 0)
      {
        int newAmount = (int)Math.Ceiling(num);
        UpdateMaxBots(newAmount);

        var pkt = new AdminPacket(newAmount, AiSession.Instance.MaxHelpers, AiSession.Instance.MaxBotProjectileDistance, AiSession.Instance.AllowMusic, null, null);
        AiSession.Instance.Network.SendToServer(pkt);
      }
    }

    internal void UpdateMaxBots(int num)
    {
      AiSession.Instance.MaxBots = num;
      MaxBots.Text = $"Max Bots allowed in world: {num}";
    }

    internal void MaxHelpers_Submitted(string input)
    {
      double num;
      if (double.TryParse(input, out num) && num > 0)
      {
        int newAmount = (int)Math.Ceiling(num);
        UpdateMaxHelpers(newAmount);

        var pkt = new AdminPacket(AiSession.Instance.MaxBots, newAmount, AiSession.Instance.MaxBotProjectileDistance, AiSession.Instance.AllowMusic, null, null);
        AiSession.Instance.Network.SendToServer(pkt);
      }
    }

    internal void UpdateMaxHelpers(int num)
    {
      AiSession.Instance.MaxHelpers = num;
      MaxHelpers.Text = $"Max active Helpers allowed per player: {num}";
    }

    internal void ShowHealthBars_Clicked()
    {
      var enabled = !AiSession.Instance.PlayerData.ShowHealthBars;
      var color = enabled ? "<color=green>" : "<color=yellow>";
      ShowHealthBars.Text = $"Show health bars: {color}{enabled.ToString()}";
      _playerData.ShowHealthBars = enabled;
      AiSession.Instance.StartUpdateCounter();

      var player = MyAPIGateway.Session?.Player;
      if (player != null)
      {
        var pkt = new AdminPacket(player.IdentityId, enabled);
        AiSession.Instance.Network.SendToServer(pkt);
      }
    }

    internal void MouseSensitivity_Submitted(float num)
    {
      num = (float)MathHelper.Clamp(Math.Round(num * 2, 2), 0.1, 2);
      _playerData.MouseSensitivityModifier = num;
      MouseSensitivity.Text = $"Mouse Sensitivity: {num}";
      MouseSensitivity.InitialPercent = num;
      AiSession.Instance.StartUpdateCounter();
    }

    internal void RecallBotsKeyBind_Submitted(MyKeys key, bool shift, bool ctrl, bool alt)
    {
      try
      {
        var tuple = MyTuple.Create(key, shift, ctrl, alt);
        var text = $"Recall Bots: <color=green>{(ctrl ? "CTRL+" : "")}{(alt ? "ALT+" : "")}{(shift ? "SHIFT+" : "")}{key}";
        AiSession.Instance.Input.AddKeybind(tuple, RecallBots_Used);
        RecallBotsKeyBind.Text = text;
      }
      catch (Exception e)
      {
        AiSession.Instance.Logger.Log($"Exception in MapKeyBind_Submitted:\n{e.Message}\n\n{e.StackTrace}", MessageType.ERROR);
      }
    }

    internal void TextInputIgnore_Submitted(string text)
    {
      var split = text.Split(',');
      if (split.Length != 3)
        return;

      float h, s, v;
      if (float.TryParse(split[0], out h) && float.TryParse(split[1], out s) && float.TryParse(split[2], out v))
      {
        h = (float)Math.Round(MathHelper.Clamp(h, 0, 360), 1);
        s = (float)Math.Round(MathHelper.Clamp(s, 0, 100), 1);
        v = (float)Math.Round(MathHelper.Clamp(v, 0, 100), 1);

        var vec = new Vector3(h, s, v);
        var color = (vec == Vector3.Zero) ? "<color=yellow>" : "<color=green>";

        _playerData.RepairBotIgnoreColorHSV = vec;
        RepairBotIgnoreColorInput.Text = $"RepairBot ignore color (HSV): {color}{{H:{vec.X}, S:{vec.Y}, V:{vec.Z}}}";

        var pkt = new ColorUpdatePacket(MyAPIGateway.Session.Player.IdentityId, vec, _playerData.RepairBotGrindColorHSV);
        AiSession.Instance.Network.SendToServer(pkt);
      }
      else
      {
        AiSession.Instance.ShowMessage("HSV was in improper format. Use format: H,S,V", timeToLive: 5000);
      }
    }

    internal void TextInputGrind_Submitted(string text)
    {
      var split = text.Split(',');
      if (split.Length != 3)
        return;

      float h, s, v;
      if (float.TryParse(split[0], out h) && float.TryParse(split[1], out s) && float.TryParse(split[2], out v))
      {
        h = (float)Math.Round(MathHelper.Clamp(h, 0, 360), 1);
        s = (float)Math.Round(MathHelper.Clamp(s, 0, 100), 1);
        v = (float)Math.Round(MathHelper.Clamp(v, 0, 100), 1);

        var vec = new Vector3(h, s, v);
        var color = (vec == Vector3.Zero) ? "<color=yellow>" : "<color=green>";

        _playerData.RepairBotGrindColorHSV = vec;
        RepairBotGrindColorInput.Text = $"RepairBot grind color (HSV): {color}{{H:{vec.X}, S:{vec.Y}, V:{vec.Z}}}";

        var pkt = new ColorUpdatePacket(MyAPIGateway.Session.Player.IdentityId, _playerData.RepairBotIgnoreColorHSV, vec);
        AiSession.Instance.Network.SendToServer(pkt);
      }
      else
      {
        AiSession.Instance.ShowMessage("HSV was in improper format. Use format: H,S,V", timeToLive: 5000);
      }
    }

    internal void RecallBots_Used()
    {
      var player = MyAPIGateway.Session.Player;
      if (player == null)
        return;

      var pkt = new BotRecallPacket(player.IdentityId);
      AiSession.Instance.Network.SendToServer(pkt);
    }
  }
}
