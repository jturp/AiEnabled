using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace AiEnabled.Bots
{
  [ProtoContract]
  public class BotStatus
  {
    [ProtoMember(1)] public string BotName;
    [ProtoMember(2)] public string Action;
    [ProtoMember(3)] public string NeededItem;

    public BotStatus() { }

    public bool Update(BotBase bot)
    {
      if (bot?.Character != null && !bot.IsDead)
      {
        BotName = bot.Character.Name;

        if (bot.FollowMode && bot.Owner != null)
        {
          Action = $"Following {bot.Owner.DisplayName} [Follow Mode]";
        }
        else if (bot.Character.Parent is IMyCockpit)
        {
          var cpit = bot.Character.Parent as IMyCockpit;
          Action = $"Seated in {cpit.DisplayName}";
        }
        else if (bot.BotType == AiSession.BotType.Repair)
        {
          var grinder = bot.Character.EquippedTool as IMyAngleGrinder;
          RepairBot.BuildMode buildMode = grinder != null ? RepairBot.BuildMode.Grind : RepairBot.BuildMode.Weld;

          if (bot.Target.IsInventory)
          {
            var terminal = bot.Target.Inventory.FatBlock as IMyTerminalBlock;
            if (buildMode == RepairBot.BuildMode.Weld)
              Action = $"Gathering mats from {terminal.CustomName}";
            else
              Action = $"Placing mats in {terminal.CustomName}";
          }
          else if (bot.Target.IsFloater)
          {
            var floater = bot.Target.Entity as IMyFloatingObject;
            Action = $"Picking up {floater.DisplayName}";
          }
          else if (bot.Target.IsSlimBlock)
          {
            var slim = bot.Target.Entity as IMySlimBlock;
            var terminal = slim.FatBlock as IMyTerminalBlock;
            string slimName;
            if (terminal != null)
            {
              slimName = terminal.CustomName;
            }
            else
            {
              var def = slim.BlockDefinition as MyCubeBlockDefinition;
              slimName = def.DisplayNameText;
            }

            if (buildMode == RepairBot.BuildMode.Weld)
              Action = $"Repairing {slimName}";
            else
              Action = $"Grinding {slimName}";
          }
          else if (bot.PatrolMode && bot._patrolList?.Count > 0)
          {
            var count = bot._patrolList.Count;
            var idx = bot._patrolIndex % count + 1;
            string name;
            if (bot._patrolName?.Length > 0)
            {
              var bktIndex = bot._patrolName.LastIndexOf('[');
              if (bktIndex > 0)
                name = bot._patrolName.Substring(0, bktIndex - 1).Trim();
              else
                name = bot._patrolName.Trim();
            }
            else
            {
              name = "Unknown Route";
            }

            Action = $"Patrolling wp {idx }/{count} for '{name}'";
          }
          else if (bot._currentGraph?.Ready != true)
          {
            Action = "Analyzing terrain";
          }
          else
          {
            Action = $"Following {bot.Owner.DisplayName} [Idle]";
          }

          if (buildMode == RepairBot.BuildMode.Weld)
          {
            var rBot = bot as RepairBot;
            NeededItem = rBot?.FirstMissingItemForRepairs;
          }
          else
          {
            NeededItem = null;
          }
        }
        else if (bot.BotType == AiSession.BotType.Crew)
        {
          var cb = bot as CrewBot;

          if (cb.CrewFunction != CrewBot.CrewType.NONE)
            BotName += $" [{cb.CrewFunction}]";

          if (cb.AttachedBlock != null)
          {
            var terminal = cb.AttachedBlock as IMyTerminalBlock;
            Action = $"Inspecting {terminal?.CustomName ?? cb.AttachedBlock.DefinitionDisplayNameText}";
          }
          else if (bot.PatrolMode && bot._patrolList?.Count > 0)
          {
            var count = bot._patrolList.Count;
            var idx = bot._patrolIndex % count + 1;
            string name;
            if (bot._patrolName?.Length > 0)
            {
              var bktIndex = bot._patrolName.LastIndexOf('[');
              if (bktIndex > 0)
                name = bot._patrolName.Substring(0, bktIndex - 1).Trim();
              else
                name = bot._patrolName.Trim();
            }
            else
            {
              name = "Unknown Route";
            }

            Action = $"Patrolling wp {idx }/{count} for '{name}'";
          }
          else if (bot._currentGraph?.Ready != true)
          {
            Action = "Analyzing terrain";
          }
          else
          {
            Action = "Wandering [Idle]";
          }
        }
        else if (bot.Target.IsCubeBlock)
        {
          var cube = bot.Target.Entity as IMyCubeBlock;
          var terminal = cube as IMyTerminalBlock;

          Action = $"Targeting {terminal?.CustomName ?? cube.DefinitionDisplayNameText}";
        }
        else if (bot.Target.Entity != null)
        {
          var ch = bot.Target.Entity as IMyCharacter;
          if (ch != null)
          {
            if (ch.ControllerInfo?.ControllingIdentityId == bot.Owner.IdentityId)
            {
              Action = $"Following {bot.Owner.DisplayName} [Idle]";
            }
            else if (AiSession.Instance.Bots.ContainsKey(ch.EntityId))
            {              
              Action = $"Targeting {ch.Name}";
            }
            else if (!string.IsNullOrWhiteSpace(ch.DisplayName))
            {
              Action = $"Targeting {ch.DisplayName}";
            }
            else
            {
              var chDef = ch.Definition as MyCharacterDefinition;
              Action = $"Targeting {chDef?.Name ?? "wildlife"}";
            }
          }
          else
          {
            var ent = bot.Target.Entity as IMyEntity;
            if (ent != null)
            {
              Action = $"Targeting {ent.DisplayName ?? ent.GetType().Name}";
            }
            else
            {
              Action = $"Targeting {bot.Target.Entity.GetType().Name}";
            }
          }
        }
        else if (bot.PatrolMode && bot._patrolList?.Count > 0)
        {
          var count = bot._patrolList.Count;
          var idx = bot._patrolIndex % count + 1;
          string name;
          if (bot._patrolName?.Length > 0)
          {
            var bktIndex = bot._patrolName.LastIndexOf('[');
            if (bktIndex > 0)
              name = bot._patrolName.Substring(0, bktIndex - 1).Trim();
            else
              name = bot._patrolName.Trim();
          }
          else
          {
            name = "Unknown Route";
          }

          Action = $"Patrolling wp {idx }/{count} for '{name}'";
        }
        else if (bot._currentGraph?.Ready != true)
        {
          Action = "Analyzing terrain";
        }
        else
        {
          Action = "Idle";
        }

        return true;
      }

      return false;
    }

    public void Reset()
    {
      BotName = null;
      Action = null;
      NeededItem = null;
    }
  }
}
