﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

using Sandbox.Definitions;
using Sandbox.ModAPI;

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
    [ProtoMember(3)] public SerializableVector3D? TargetPosition;
    [ProtoMember(4)] public string NeededItem;

    public BotStatus() { }

    public bool Update(BotBase bot)
    {
      if (bot?.Character != null && !bot.IsDead)
      {
        BotName = bot.Character.Name;

        if (bot.Target.PositionsValid)
        {
          TargetPosition = bot.Target.CurrentActualPosition;
        }
        else
        {
          TargetPosition = null;
        }

        if (bot.FollowMode && bot.Owner != null)
        {
          Action = $"Following {bot.Owner.DisplayName}";
        }
        else if (bot.Character.Parent is IMyCockpit)
        {
          Action = $"Sitting";
        }
        else if (bot.BotType == AiSession.BotType.Repair)
        {
          if (bot.Target.IsInventory)
          {
            var terminal = bot.Target.Inventory.FatBlock as IMyTerminalBlock;
            Action = $"Gathering mats from {terminal.CustomName}";
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
            if (terminal != null)
            {
              Action = $"Repairing {terminal.CustomName}";
            }
            else
            {
              var def = slim.BlockDefinition as MyCubeBlockDefinition;
              Action = $"Repairing {def.DisplayNameText}";
            }
          }
          else if (bot.PatrolMode && bot._patrolList?.Count > 0)
          {
            var suffix = bot._patrolList.Count > 1 ? "waypoints" : "waypoint";
            Action = $"Patrolling {bot._patrolList.Count} {suffix}";
          }
          else
          {
            Action = "Idle";
          }

          var rBot = bot as RepairBot;
          NeededItem = rBot?.FirstMissingItemForRepairs;
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

            TargetPosition = terminal.GetPosition();
          }
          else if (bot.PatrolMode && bot._patrolList?.Count > 0)
          {
            var suffix = bot._patrolList.Count > 1 ? "waypoints" : "waypoint";
            Action = $"Patrolling {bot._patrolList.Count} {suffix}";
          }
          else
          {
            Action = "Wandering";
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
              Action = "Idle";
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
          var suffix = bot._patrolList.Count > 1 ? "waypoints" : "waypoint";
          Action = $"Patrolling {bot._patrolList.Count} {suffix}";
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
      TargetPosition = null;
    }
  }
}