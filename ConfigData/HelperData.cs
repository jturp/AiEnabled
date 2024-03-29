﻿using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRage.Game.ModAPI;

using VRageMath;
using ProtoBuf;
using VRage.ObjectBuilders;
using Sandbox.Game;
using VRage.Game.Entity;
using Sandbox.Game.Weapons;
using VRage.Game;
using AiEnabled.Bots.Roles.Helpers;
using Sandbox.Definitions;

namespace AiEnabled.ConfigData
{
  [ProtoContract]
  public class InventoryItem
  {
    [ProtoMember(200)] public SerializableDefinitionId ItemDefinition;
    [ProtoMember(201)] public MyFixedPoint Amount;

    public InventoryItem() { }

    public InventoryItem(MyDefinitionId id, MyFixedPoint amount)
    {
      ItemDefinition = id;
      Amount = amount;
    }
  }

  [ProtoContract]
  public class HelperInfo
  {
    [ProtoMember(100)] public long HelperId;
    [ProtoMember(101)] public long GridEntityId;
    [ProtoMember(117)] public long SeatEntityId;
    [ProtoMember(102)] public string CharacterDefinitionName;
    [ProtoMember(103)] public string DisplayName;
    [ProtoMember(104)] public SerializableDefinitionId? ToolPhysicalItem;
    [ProtoMember(105)] public bool IsActiveHelper;
    [ProtoMember(106)] public SerializableVector3D Position;
    [ProtoMember(107)] public SerializableQuaternion Orientation;
    [ProtoMember(108)] public int Role;
    [ProtoMember(109)] public int? CrewFunction;
    [ProtoMember(110)] public Color BotColor;
    [ProtoMember(111)] public List<InventoryItem> InventoryItems;
    [ProtoMember(112)] public List<SerializableVector3I> PatrolRoute;
    [ProtoMember(113)] public string PatrolName;
    [ProtoMember(114)] public List<string> Priorities;
    [ProtoMember(119)] public List<string> IgnoreList;
    [ProtoMember(115)] public bool DamageToDisable;
    [ProtoMember(120)] public bool WeldBeforeGrind;
    [ProtoMember(116)] public bool AdminSpawned;
    [ProtoMember(118)] public bool RemainInPlace;

    public HelperInfo() { }

    public HelperInfo(IMyCharacter bot, AiSession.BotType botType, List<string> priList, List<string> ignList, bool disableOnly, bool weldFirst, MyCubeGrid grid = null, List<Vector3I> route = null, CrewBot.CrewType? crewRole = null, bool adminSpawn = false, string patrolName = null, bool remainStill = false)
    {
      HelperId = bot.EntityId;
      GridEntityId = grid?.EntityId ?? 0L;
      CharacterDefinitionName = ((MyCharacterDefinition)bot.Definition).Name;
      ToolPhysicalItem = (bot.EquippedTool as IMyHandheldGunObject<MyDeviceBase>)?.DefinitionId;
      DisplayName = bot.Name ?? "";
      Position = bot.GetPosition();
      Orientation = Quaternion.CreateFromRotationMatrix(bot.WorldMatrix);
      IsActiveHelper = true;
      Role = (int)botType;
      Priorities = priList;
      IgnoreList = ignList;
      DamageToDisable = disableOnly;
      AdminSpawned = adminSpawn;
      RemainInPlace = remainStill;
      WeldBeforeGrind = weldFirst;
      
      if (bot.Parent is IMyCockpit)
        SeatEntityId = bot.Parent.EntityId;

      if (crewRole.HasValue)
        CrewFunction = (int)crewRole.Value;
      else
        CrewFunction = null;

      var hsvOffset = ((MyObjectBuilder_Character)bot.GetObjectBuilder()).ColorMaskHSV;
      var hsv = MyColorPickerConstants.HSVOffsetToHSV(hsvOffset);
      BotColor = hsv.HSVtoColor();

      if (route?.Count > 0)
      {
        PatrolRoute = new List<SerializableVector3I>(route.Count);

        for (int i = 0; i < route.Count; i++)
          PatrolRoute.Add(route[i]);

        PatrolName = patrolName;
      }

      var inventory = bot.GetInventory() as MyInventory;
      if (inventory?.ItemCount > 0)
      {
        InventoryItems = new List<InventoryItem>();

        var items = inventory.GetItems();
        for (int i = 0; i < items.Count; i++)
        {
          var item = items[i];
          InventoryItems.Add(new InventoryItem(item.Content.GetId(), item.Amount));
        }
      }
    }
  }

  public class HelperData
  {
    public long OwnerIdentityId;
    public string OwnerDisplayName;
    public Vector3? RepairBotIgnoreColorMask;
    public Vector3? RepairBotGrindColorMask;
    public List<HelperInfo> Helpers;

    public HelperData() { }

    public HelperData(IMyPlayer owner, Vector3? hsvRepair, Vector3? hsvGrind)
    {
      OwnerIdentityId = owner.IdentityId;
      OwnerDisplayName = owner.DisplayName;
      RepairBotIgnoreColorMask = hsvRepair;
      RepairBotGrindColorMask = hsvGrind;
      Helpers = new List<HelperInfo>();
    }

    public void AddHelper(IMyCharacter helper, AiSession.BotType botType, List<KeyValuePair<string, bool>> priList, List<KeyValuePair<string, bool>> ignList, bool damageOnly, bool weldFirst, MyCubeGrid grid, List<Vector3I> patrolRoute, CrewBot.CrewType? crewRole = null, bool adminSpawn = false, string patrolName = null)
    {
      if (Helpers == null)
        Helpers = new List<HelperInfo>();

      List<string> pris = new List<string>();
      if (priList != null)
      {
        foreach (var item in priList)
        {
          var prefix = item.Value ? "[X]" : "[  ]";
          pris.Add($"{prefix} {item.Key}");
        }
      }
      else
      {
        pris = botType == AiSession.BotType.Repair ? API.RemoteBotAPI.GetDefaultRepairPriorities() : API.RemoteBotAPI.GetDefaultTargetPriorities();
      }

      List<string> ignoreList = new List<string>();
      if (ignList != null)
      {
        foreach ( var item in ignList)
        {
          if (item.Value)
            ignoreList.Add($"[X] {item.Key}");
        }
      }

      Helpers.Add(new HelperInfo(helper, botType, pris, ignoreList, damageOnly, weldFirst, grid, patrolRoute, crewRole, adminSpawn, patrolName));
    }

    public bool RemoveHelper(long id)
    {
      if (Helpers != null)
      {
        for (int i = Helpers.Count - 1; i >= 0; i--)
        {
          if (Helpers[i].HelperId == id)
          {
            Helpers.RemoveAtFast(i);
            return true;
          }
        }
      }

      return false;
    }

    public void Close()
    {
      Helpers?.Clear();
      Helpers = null;
    }
  }
}
