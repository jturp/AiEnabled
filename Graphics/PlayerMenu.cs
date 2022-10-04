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
    internal MenuSliderInput MouseSensitivity;
    internal MenuKeybindInput RecallBotsKeyBind;
    internal MenuItem ShowHealthBars, ShowHelperGPS, ObeyProjectionIntegrity, DisableCollisionOnDeath;
    internal MenuItem AllowRepairBot, AllowCombatBot, AllowScavengerBot, AllowCrewBot, AllowBotMusic;
    internal MenuItem AllowFriendlyFlight, AllowEnemyFlight, AllowNeutralFlight, AllowNeutralTargets;
    internal MenuItem AllowIdleMovement, AllowIdleTransitions, EnforceWalkingOnPatrol, EnforceGroundPathingFirst;
    internal MenuItem AllowHelmetVisorChanges;
    internal MenuTextInput RepairBotIgnoreColorInput, RepairBotGrindColorInput, MaxBots, MaxHelpers;
    internal MenuTextInput PlayerDamageModifier, BotDamageModifier, MaxPathfindingTimeInSeconds;
    internal MenuTextInput MaxEnemyHuntRadius, MaxFriendlyHuntRadius, MaxBotProjectileDistance;
    PlayerData _playerData;

    public PlayerMenu(PlayerData playerData)
    {
      _playerData = playerData;
    }

    public void Register()
    {
      bool showHealthBars = _playerData.ShowHealthBars;
      bool showGPS = _playerData.ShowHelperGPS;
      float mouseSensitivity = _playerData.MouseSensitivityModifier;

      Menu = new MenuRootCategory("AiEnabled", MenuRootCategory.MenuFlag.PlayerMenu, "Settings");
      ShowHealthBars = CreateMenuItemToggle(Menu, showHealthBars, "Show health bars", ShowHealthBars_Clicked);
      ShowHelperGPS = CreateMenuItemToggle(Menu, showGPS, "Show helper GPS", ShowHelperGPS_Clicked);
      MouseSensitivity = new MenuSliderInput($"Mouse sensitivity: {mouseSensitivity}", Menu, mouseSensitivity * 0.5f, OnSubmitAction: MouseSensitivity_Submitted, SliderPercentToValue: PercentToValueFunc);

      var colorMenu = new MenuSubCategory("Color Settings               <color=cyan>==>", Menu, "Color Settings");
      Vector3? vec = _playerData.RepairBotIgnoreColorHSV;
      var x = vec?.X.ToString() ?? "N";
      var y = vec?.Y.ToString() ?? "N";
      var z = vec?.Z.ToString() ?? "N";

      var color = (vec == null) ? "<color=yellow>" : "<color=orange>";
      RepairBotIgnoreColorInput = new MenuTextInput($"RepairBot ignore color (HSV): {color}{{H:{x}, S:{y}, V:{z}}}", colorMenu, "Assign the ignore color for Repair Bots, in format H,S,V", TextInputIgnore_Submitted);

      vec = _playerData.RepairBotGrindColorHSV;
      x = vec?.X.ToString() ?? "N";
      y = vec?.Y.ToString() ?? "N";
      z = vec?.Z.ToString() ?? "N";

      color = (vec == null) ? "<color=yellow>" : "<color=orange>";
      RepairBotGrindColorInput = new MenuTextInput($"RepairBot grind color (HSV): {color}{{H:{x}, S:{y}, V:{z}}}", colorMenu, "Assign the grind color for Repair Bots, in format H,S,V", TextInputGrind_Submitted);

      var keyBindMenu = new MenuSubCategory("Key Bindings                <color=cyan>==>", Menu, "Key Bindings");
      RecallBotsKeyBind = new MenuKeybindInput($"Recall bots: <color=yellow>None", keyBindMenu, "Press any key to bind\nCan be combined with alt/ctrl/shift", RecallBotsKeyBind_Submitted);

      var data = AiSession.Instance.ModSaveData;
      AdminMenu = new MenuRootCategory("AiEnabled", MenuRootCategory.MenuFlag.AdminMenu, "Admin Settings");
      MaxBots = new MenuTextInput($"Max bots allowed in world: <color=orange>{data.MaxBotsInWorld}", AdminMenu, "Set the maximum number of bots allowed in the world", MaxBots_Submitted);
      
      MaxHelpers = new MenuTextInput($"Max active helpers allowed per player: <color=orange>{data.MaxHelpersPerPlayer}", AdminMenu, "Set the maximum number of active helpers allowed per player", MaxHelpers_Submitted);

      PlayerDamageModifier = new MenuTextInput($"Player weapon damage modifier: <color=orange>{data.PlayerWeaponDamageModifier}", AdminMenu, "Set Player weapon damage modifier (1 = normal damage)", PlayerDamageMod_Submitted);
      
      BotDamageModifier = new MenuTextInput($"Bot weapon damage modifier: <color=orange>{data.BotWeaponDamageModifier}", AdminMenu, "Set Bot weapon damage modifier (1 = normal damage)", BotDamageMod_Submitted);
      
      MaxPathfindingTimeInSeconds = new MenuTextInput($"Max pathfinding time in seconds: <color=orange>{data.MaxPathfindingTimeInSeconds}", AdminMenu, "Set max number of seconds pathfinding system should look for a valid path (default = 30)", MaxPathTime_Submitted);
      
      MaxEnemyHuntRadius = new MenuTextInput($"Max enemy hunting radius: <color=orange>{data.MaxBotHuntingDistanceEnemy}", AdminMenu, "Set max hunting radius for enemy bots, between 50-1000 meters (default = 300)", MaxEnemyHuntRange_Submitted);
      
      MaxFriendlyHuntRadius = new MenuTextInput($"Max friendly hunting radius: <color=orange>{data.MaxBotHuntingDistanceFriendly}", AdminMenu, "Set max hunting radius for helper bots, between 50-1000 meters (default = 150)", MaxFriendlyHuntRange_Submitted);
      
      MaxBotProjectileDistance = new MenuTextInput($"Max projectile distance: <color=orange>{data.MaxBotProjectileDistance}", AdminMenu, "Set max bot projectile distance in meters (default = 150)", MaxProjectileRange_Submitted);

      color = data.AllowRepairBot ? "<color=orange>" : "<color=yellow>";
      AllowRepairBot = new MenuItem($"Allow RepairBot helpers: {color}{data.AllowRepairBot}", AdminMenu, AllowRepairBot_Clicked);

      color = data.AllowCombatBot ? "<color=orange>" : "<color=yellow>";
      AllowCombatBot = new MenuItem($"Allow CombatBot helpers: {color}{data.AllowCombatBot}", AdminMenu, AlloCombatBot_Clicked);

      color = data.AllowScavengerBot ? "<color=orange>" : "<color=yellow>";
      AllowScavengerBot = new MenuItem($"Allow ScavengerBot helpers: {color}{data.AllowScavengerBot}", AdminMenu, AllowScavengerBot_Clicked);

      color = data.AllowCrewBot ? "<color=orange>" : "<color=yellow>";
      AllowCrewBot = new MenuItem($"Allow CrewBot helpers: {color}{data.AllowCrewBot}", AdminMenu, AllowCrewBot_Clicked);

      color = data.AllowBotMusic ? "<color=orange>" : "<color=yellow>";
      AllowBotMusic = new MenuItem($"Allow bot music: {color}{data.AllowBotMusic}", AdminMenu, AllowBotMusic_Clicked);

      color = data.AllowEnemiesToFly ? "<color=orange>" : "<color=yellow>";
      AllowEnemyFlight = new MenuItem($"Allow enemy bots to fly: {color}{data.AllowEnemiesToFly}", AdminMenu, AllowEnemyFlight_Clicked);

      color = data.AllowNeutralsToFly ? "<color=orange>" : "<color=yellow>";
      AllowNeutralFlight = new MenuItem($"Allow neutral bots to fly: {color}{data.AllowNeutralsToFly}", AdminMenu, AllowNeutralFlight_Clicked);

      color = data.AllowHelpersToFly ? "<color=orange>" : "<color=yellow>";
      AllowFriendlyFlight = new MenuItem($"Allow helper bots to fly: {color}{data.AllowHelpersToFly}", AdminMenu, AllowFriendlyFlight_Clicked);

      color = data.AllowNeutralTargets ? "<color=orange>" : "<color=yellow>";
      AllowNeutralTargets = new MenuItem($"Allow enemy bots to target neutrals: {color}{data.AllowNeutralTargets}", AdminMenu, AllowNeutralTargets_Clicked);

      color = data.AllowIdleMovement ? "<color=orange>" : "<color=yellow>";
      AllowIdleMovement = new MenuItem($"Allow idle bot movement: {color}{data.AllowIdleMovement}", AdminMenu, AllowIdleMovement_Clicked);

      color = data.AllowIdleMapTransitions ? "<color=orange>" : "<color=yellow>";
      AllowIdleTransitions = new MenuItem($"Allow idle map transitions: {color}{data.AllowIdleMapTransitions}", AdminMenu, AllowIdleTransitions_Clicked);

      color = data.AllowHelmetVisorChanges ? "<color=orange>" : "<color=yellow>";
      AllowHelmetVisorChanges = new MenuItem($"Allow helmet visor changes: {color}{data.AllowHelmetVisorChanges}", AdminMenu, AllowVisorChanges_Clicked);

      color = data.EnforceGroundPathingFirst ? "<color=orange>" : "<color=yellow>";
      EnforceGroundPathingFirst = new MenuItem($"Enforce ground pathing first: {color}{data.EnforceGroundPathingFirst}", AdminMenu, EnforceGroundPathing_Clicked);

      color = data.EnforceWalkingOnPatrol ? "<color=orange>" : "<color=yellow>";
      EnforceWalkingOnPatrol = new MenuItem($"Enforce walking on patrol: {color}{data.EnforceWalkingOnPatrol}", AdminMenu, EnforcePatrolWalking_Clicked);

      color = data.ObeyProjectionIntegrityForRepairs ? "<color=orange>" : "<color=yellow>";
      ObeyProjectionIntegrity = new MenuItem($"Obey projection integrity for repairs: {color}{data.ObeyProjectionIntegrityForRepairs}", AdminMenu, ObeyProjectionIntegrity_Clicked);

      color = data.DisableCharacterCollisionOnBotDeath ? "<color=orange>" : "<color=yellow>";
      DisableCollisionOnDeath = new MenuItem($"Disable character collision on bot death: {color}{data.DisableCharacterCollisionOnBotDeath}", AdminMenu, DisableCollisions_Clicked);
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

        if (ObeyProjectionIntegrity != null)
        {
          ObeyProjectionIntegrity.Text = null;
          ObeyProjectionIntegrity.OnClick = null;
          ObeyProjectionIntegrity.BackingObject = null;
          ObeyProjectionIntegrity = null;
        }

        if (DisableCollisionOnDeath != null)
        {
          DisableCollisionOnDeath.Text = null;
          DisableCollisionOnDeath.OnClick = null;
          DisableCollisionOnDeath.BackingObject = null;
          DisableCollisionOnDeath = null;
        }

        if (AllowRepairBot != null)
        {
          AllowRepairBot.Text = null;
          AllowRepairBot.OnClick = null;
          AllowRepairBot.BackingObject = null;
          AllowRepairBot = null;
        }

        if (AllowCombatBot != null)
        {
          AllowCombatBot.Text = null;
          AllowCombatBot.OnClick = null;
          AllowCombatBot.BackingObject = null;
          AllowCombatBot = null;
        }

        if (AllowScavengerBot != null)
        {
          AllowScavengerBot.Text = null;
          AllowScavengerBot.OnClick = null;
          AllowScavengerBot.BackingObject = null;
          AllowScavengerBot = null;
        }

        if (AllowCrewBot != null)
        {
          AllowCrewBot.Text = null;
          AllowCrewBot.OnClick = null;
          AllowCrewBot.BackingObject = null;
          AllowCrewBot = null;
        }

        if (AllowBotMusic != null)
        {
          AllowBotMusic.Text = null;
          AllowBotMusic.OnClick = null;
          AllowBotMusic.BackingObject = null;
          AllowBotMusic = null;
        }

        if (AllowEnemyFlight != null)
        {
          AllowEnemyFlight.Text = null;
          AllowEnemyFlight.OnClick = null;
          AllowEnemyFlight.BackingObject = null;
          AllowEnemyFlight = null;
        }

        if (AllowNeutralFlight != null)
        {
          AllowNeutralFlight.Text = null;
          AllowNeutralFlight.OnClick = null;
          AllowNeutralFlight.BackingObject = null;
          AllowNeutralFlight = null;
        }

        if (AllowNeutralTargets != null)
        {
          AllowNeutralTargets.Text = null;
          AllowNeutralTargets.OnClick = null;
          AllowNeutralTargets.BackingObject = null;
          AllowNeutralTargets = null;
        }

        if (AllowIdleMovement != null)
        {
          AllowIdleMovement.Text = null;
          AllowIdleMovement.OnClick = null;
          AllowIdleMovement.BackingObject = null;
          AllowIdleMovement = null;
        }

        if (AllowIdleTransitions != null)
        {
          AllowIdleTransitions.Text = null;
          AllowIdleTransitions.OnClick = null;
          AllowIdleTransitions.BackingObject = null;
          AllowIdleTransitions = null;
        }

        if (EnforceGroundPathingFirst != null)
        {
          EnforceGroundPathingFirst.Text = null;
          EnforceGroundPathingFirst.OnClick = null;
          EnforceGroundPathingFirst.BackingObject = null;
          EnforceGroundPathingFirst = null;
        }

        if (EnforceWalkingOnPatrol != null)
        {
          EnforceWalkingOnPatrol.Text = null;
          EnforceWalkingOnPatrol.OnClick = null;
          EnforceWalkingOnPatrol.BackingObject = null;
          EnforceWalkingOnPatrol = null;
        }

        if (PlayerDamageModifier != null)
        {
          PlayerDamageModifier.Text = null;
          PlayerDamageModifier.OnSubmitAction = null;
          PlayerDamageModifier.BackingObject = null;
          PlayerDamageModifier = null;
        }

        if (BotDamageModifier != null)
        {
          BotDamageModifier.Text = null;
          BotDamageModifier.OnSubmitAction = null;
          BotDamageModifier.BackingObject = null;
          BotDamageModifier = null;
        }

        if (MaxPathfindingTimeInSeconds != null)
        {
          MaxPathfindingTimeInSeconds.Text = null;
          MaxPathfindingTimeInSeconds.OnSubmitAction = null;
          MaxPathfindingTimeInSeconds.BackingObject = null;
          MaxPathfindingTimeInSeconds = null;
        }

        if (MaxEnemyHuntRadius != null)
        {
          MaxEnemyHuntRadius.Text = null;
          MaxEnemyHuntRadius.OnSubmitAction = null;
          MaxEnemyHuntRadius.BackingObject = null;
          MaxEnemyHuntRadius = null;
        }

        if (MaxFriendlyHuntRadius != null)
        {
          MaxFriendlyHuntRadius.Text = null;
          MaxFriendlyHuntRadius.OnSubmitAction = null;
          MaxFriendlyHuntRadius.BackingObject = null;
          MaxFriendlyHuntRadius = null;
        }

        if (MaxBotProjectileDistance != null)
        {
          MaxBotProjectileDistance.Text = null;
          MaxBotProjectileDistance.OnSubmitAction = null;
          MaxBotProjectileDistance.BackingObject = null;
          MaxBotProjectileDistance = null;
        }

        _playerData = null;
      }
      catch (Exception ex)
      {
        if (AiSession.Instance?.Logger != null)
          AiSession.Instance.Logger.Log($"Exception in PlayerMenu.Close: {ex.ToString()}");
        else
          MyLog.Default.Error($"Exception in PlayerMenu.Close: {ex.ToString()}");
      }
    }

    MenuItem CreateMenuItemToggle(MenuCategoryBase category, bool toggleItem, string title, Action onClick, bool interactable = true)
    {
      var color = toggleItem ? "<color=orange>" : "<color=yellow>";
      var text = $"{title}: {color}{toggleItem.ToString()}";

      return new MenuItem(text, category, onClick, interactable);
    }

    object PercentToValueFunc(float input)
    {
      return (float)MathHelper.Clamp(Math.Round(input * 2, 2), 0.1, 2);
    }

    #region PlayerOnly
    internal void ShowHealthBars_Clicked()
    {
      var enabled = !_playerData.ShowHealthBars;
      var color = enabled ? "<color=orange>" : "<color=yellow>";
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

    private void ShowHelperGPS_Clicked()
    {
      var enabled = !_playerData.ShowHelperGPS;
      var color = enabled ? "<color=orange>" : "<color=yellow>";
      ShowHelperGPS.Text = $"Show helper GPS: {color}{enabled.ToString()}";
      _playerData.ShowHelperGPS = enabled;
      AiSession.Instance.StartUpdateCounter();
    }

    internal void MouseSensitivity_Submitted(float num)
    {
      num = (float)MathHelper.Clamp(Math.Round(num * 2, 2), 0.1, 2);
      _playerData.MouseSensitivityModifier = num;
      MouseSensitivity.Text = $"Mouse sensitivity: {num}";
      MouseSensitivity.InitialPercent = num;
      AiSession.Instance.StartUpdateCounter();
    }

    internal void RecallBotsKeyBind_Submitted(MyKeys key, bool shift, bool ctrl, bool alt)
    {
      try
      {
        var tuple = MyTuple.Create(key, shift, ctrl, alt);
        var text = $"Recall bots: <color=orange>{(ctrl ? "CTRL+" : "")}{(alt ? "ALT+" : "")}{(shift ? "SHIFT+" : "")}{key}";
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
        var color = (vec == Vector3.Zero) ? "<color=yellow>" : "<color=orange>";

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
        var color = (vec == Vector3.Zero) ? "<color=yellow>" : "<color=orange>";

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
    #endregion

    #region AdminOnly
    internal void AllowIdleTransitions_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.AllowIdleMapTransitions;
      data.AllowIdleMapTransitions = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowIdleTransitions.Text = $"Allow idle map transitions: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AllowIdleMovement_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.AllowIdleMovement;
      data.AllowIdleMovement = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowIdleMovement.Text = $"Allow idle bot movement: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AllowNeutralTargets_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.AllowNeutralTargets;
      data.AllowNeutralTargets = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowNeutralTargets.Text = $"Allow enemy bots to target neutrals: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AllowNeutralFlight_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.AllowNeutralsToFly;
      data.AllowNeutralsToFly = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowNeutralFlight.Text = $"Allow neutral bots to fly: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AllowEnemyFlight_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.AllowEnemiesToFly;
      data.AllowEnemiesToFly = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowEnemyFlight.Text = $"Allow enemy bots to fly: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    private void AllowFriendlyFlight_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.AllowHelpersToFly;
      data.AllowHelpersToFly = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowFriendlyFlight.Text = $"Allow helper bots to fly: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AllowVisorChanges_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.AllowHelmetVisorChanges;
      data.AllowHelmetVisorChanges = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowHelmetVisorChanges.Text = $"Allow helmet visor changes: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void DisableCollisions_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.DisableCharacterCollisionOnBotDeath;
      data.DisableCharacterCollisionOnBotDeath = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      DisableCollisionOnDeath.Text = $"Disable character collision on bot death: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void ObeyProjectionIntegrity_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.ObeyProjectionIntegrityForRepairs;
      data.ObeyProjectionIntegrityForRepairs = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      ObeyProjectionIntegrity.Text = $"Obey projection integrity for repairs: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void EnforcePatrolWalking_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.EnforceWalkingOnPatrol;
      data.EnforceWalkingOnPatrol = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      EnforceWalkingOnPatrol.Text = $"Enforce walking on patrol: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void EnforceGroundPathing_Clicked()
    {
      var data = AiSession.Instance.ModSaveData;
      var newValue = !data.EnforceGroundPathingFirst;
      data.EnforceGroundPathingFirst = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      EnforceGroundPathingFirst.Text = $"Enforce ground pathing first: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AllowBotMusic_Clicked()
    {
      var newValue = !AiSession.Instance.ModSaveData.AllowBotMusic;
      AiSession.Instance.ModSaveData.AllowBotMusic = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowBotMusic.Text = $"Allow bot music: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AllowCrewBot_Clicked()
    {
      var newValue = !AiSession.Instance.ModSaveData.AllowCrewBot;
      AiSession.Instance.ModSaveData.AllowCrewBot = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowCrewBot.Text = $"Allow CrewBot helpers: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AllowScavengerBot_Clicked()
    {
      var newValue = !AiSession.Instance.ModSaveData.AllowScavengerBot;
      AiSession.Instance.ModSaveData.AllowScavengerBot = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowScavengerBot.Text = $"Allow ScavengerBot helpers: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AlloCombatBot_Clicked()
    {
      var newValue = !AiSession.Instance.ModSaveData.AllowCombatBot;
      AiSession.Instance.ModSaveData.AllowCombatBot = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowCombatBot.Text = $"Allow CombatBot helpers: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void AllowRepairBot_Clicked()
    {
      var newValue = !AiSession.Instance.ModSaveData.AllowRepairBot;
      AiSession.Instance.ModSaveData.AllowRepairBot = newValue;

      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowRepairBot.Text = $"Allow RepairBot helpers: {color}{newValue}";

      AiSession.Instance.StartAdminUpdateCounter();
      AiSession.Instance.StartSettingSyncCounter();
    }

    internal void MaxProjectileRange_Submitted(string input)
    {
      float num;
      if (float.TryParse(input, out num))
      {
        var newValue = (int)Math.Max(0, Math.Ceiling(num));
        var data = AiSession.Instance.ModSaveData;
        if (data.MaxBotProjectileDistance != newValue)
        {
          MaxBotProjectileDistance.Text = $"Max projectile distance: <color=orange>{newValue}";
          data.MaxBotProjectileDistance = newValue;
          AiSession.Instance.StartAdminUpdateCounter();
          AiSession.Instance.StartSettingSyncCounter();
        }
      }
    }

    internal void MaxFriendlyHuntRange_Submitted(string input)
    {
      float num;
      if (float.TryParse(input, out num))
      {
        var newValue = (int)MathHelper.Clamp(num, 50, 1000);
        var data = AiSession.Instance.ModSaveData;
        if (data.MaxBotHuntingDistanceFriendly != newValue)
        {
          MaxFriendlyHuntRadius.Text = $"Max friendly hunting radius: <color=orange>{newValue}";
          data.MaxBotHuntingDistanceFriendly = newValue;
          AiSession.Instance.StartAdminUpdateCounter();
          AiSession.Instance.StartSettingSyncCounter();
        }
      }
    }

    internal void MaxEnemyHuntRange_Submitted(string input)
    {
      float num;
      if (float.TryParse(input, out num))
      {
        var newValue = (int)MathHelper.Clamp(num, 50, 1000);
        var data = AiSession.Instance.ModSaveData;
        if (data.MaxBotHuntingDistanceEnemy != newValue)
        {
          MaxEnemyHuntRadius.Text = $"Max enemy hunting radius: <color=orange>{newValue}";
          data.MaxBotHuntingDistanceEnemy = newValue;
          AiSession.Instance.StartAdminUpdateCounter();
          AiSession.Instance.StartSettingSyncCounter();
        }
      }
    }

    internal void MaxPathTime_Submitted(string input)
    {
      float num;
      if (float.TryParse(input, out num))
      {
        var newValue = (long)Math.Max(0, Math.Ceiling(num));
        var data = AiSession.Instance.ModSaveData;
        if (data.MaxPathfindingTimeInSeconds != newValue)
        {
          MaxPathfindingTimeInSeconds.Text = $"Max pathfinding time in seconds: <color=orange>{newValue}";
          data.MaxPathfindingTimeInSeconds = newValue;
          AiSession.Instance.StartAdminUpdateCounter();
          AiSession.Instance.StartSettingSyncCounter();
        }
      }
    }

    internal void BotDamageMod_Submitted(string input)
    {
      float num;
      if (float.TryParse(input, out num))
      {
        var newValue = Math.Max(0, num);
        var data = AiSession.Instance.ModSaveData;
        if (data.BotWeaponDamageModifier != newValue)
        {
          BotDamageModifier.Text = $"Bot weapon damage modifier: <color=orange>{newValue}";
          data.BotWeaponDamageModifier = newValue;
          AiSession.Instance.StartAdminUpdateCounter();
          AiSession.Instance.StartSettingSyncCounter();
        }
      }
    }

    internal void PlayerDamageMod_Submitted(string input)
    {
      float num;
      if (float.TryParse(input, out num))
      {
        var newValue = Math.Max(0, num);
        var data = AiSession.Instance.ModSaveData;
        if (data.PlayerWeaponDamageModifier != newValue)
        {
          PlayerDamageModifier.Text = $"Player weapon damage modifier: <color=orange>{newValue}";
          data.PlayerWeaponDamageModifier = newValue;
          AiSession.Instance.StartAdminUpdateCounter();
          AiSession.Instance.StartSettingSyncCounter();
        }
      }
    }

    internal void MaxBots_Submitted(string input)
    {
      float num;
      if (float.TryParse(input, out num) && num > 0)
      {
        int newAmount = (int)Math.Ceiling(num);
        if (newAmount != AiSession.Instance.ModSaveData.MaxBotsInWorld)
        {
          MaxBots.Text = $"Max bots allowed in world: <color=orange>{newAmount}";
          AiSession.Instance.ModSaveData.MaxBotsInWorld = newAmount;
          AiSession.Instance.StartAdminUpdateCounter();
          AiSession.Instance.UpdateAdminSettingSync(true);
        }
      }
    }

    internal void MaxHelpers_Submitted(string input)
    {
      float num;
      if (float.TryParse(input, out num) && num >= 0)
      {
        int newAmount = (int)Math.Ceiling(num);
        if (newAmount != AiSession.Instance.ModSaveData.MaxHelpersPerPlayer)
        {
          MaxHelpers.Text = $"Max active helpers allowed per player: <color=orange>{newAmount}";
          AiSession.Instance.ModSaveData.MaxHelpersPerPlayer = newAmount;
          AiSession.Instance.StartAdminUpdateCounter();
          AiSession.Instance.UpdateAdminSettingSync(true);
        }
      }
    }

    internal void UpdateAdminSettings(SaveData data)
    {
      var newValue = data.AllowIdleMapTransitions;
      var color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowIdleTransitions.Text = $"Allow idle map transitions: {color}{newValue}";

      newValue = data.AllowIdleMovement;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowIdleMovement.Text = $"Allow idle bot movement: {color}{newValue}";

      newValue = data.AllowNeutralTargets;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowNeutralTargets.Text = $"Allow enemy bots to target neutrals: {color}{newValue}";

      newValue = data.AllowNeutralsToFly;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowNeutralFlight.Text = $"Allow neutral bots to fly: {color}{newValue}";

      newValue = data.AllowEnemiesToFly;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowEnemyFlight.Text = $"Allow enemy bots to fly: {color}{newValue}";

      newValue = data.DisableCharacterCollisionOnBotDeath;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      DisableCollisionOnDeath.Text = $"Disable character collision on bot death: {color}{newValue}";

      newValue = data.ObeyProjectionIntegrityForRepairs;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      ObeyProjectionIntegrity.Text = $"Obey projection integrity for repairs: {color}{newValue}";

      newValue = data.EnforceWalkingOnPatrol;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      EnforceWalkingOnPatrol.Text = $"Enforce walking on patrol: {color}{newValue}";

      newValue = data.EnforceGroundPathingFirst;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      EnforceGroundPathingFirst.Text = $"Enforce ground pathing first: {color}{newValue}";

      newValue = data.AllowBotMusic;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowBotMusic.Text = $"Allow bot music: {color}{newValue}";

      newValue = data.AllowCrewBot;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowCrewBot.Text = $"Allow CrewBot helpers: {color}{newValue}";

      newValue = data.AllowScavengerBot;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowScavengerBot.Text = $"Allow ScavengerBot helpers: {color}{newValue}";

      newValue = data.AllowCombatBot;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowCombatBot.Text = $"Allow CombatBot helpers: {color}{newValue}";

      newValue = data.AllowRepairBot;
      color = newValue ? "<color=orange>" : "<color=yellow>";
      AllowRepairBot.Text = $"Allow RepairBot helpers: {color}{newValue}";

      MaxBotProjectileDistance.Text = $"Max projectile distance: <color=orange>{data.MaxBotProjectileDistance}";
      MaxFriendlyHuntRadius.Text = $"Max friendly hunting radius: <color=orange>{data.MaxBotHuntingDistanceFriendly}";
      MaxEnemyHuntRadius.Text = $"Max enemy hunting radius: <color=orange>{data.MaxBotHuntingDistanceEnemy}";
      MaxPathfindingTimeInSeconds.Text = $"Max pathfinding time in seconds: <color=orange>{data.MaxPathfindingTimeInSeconds}";
      BotDamageModifier.Text = $"Bot weapon damage modifier: <color=orange>{data.BotWeaponDamageModifier}";
      PlayerDamageModifier.Text = $"Player weapon damage modifier: <color=orange>{data.PlayerWeaponDamageModifier}";
      MaxBots.Text = $"Max bots allowed in world: <color=orange>{data.MaxBotsInWorld}";
      MaxHelpers.Text = $"Max active helpers allowed per player: <color=orange>{data.MaxHelpersPerPlayer}";
    }
    #endregion
  }
}
