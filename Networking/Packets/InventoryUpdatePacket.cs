using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class InventoryUpdatePacket : PacketBase
  {
    [ProtoMember(1)] long _fromEntityId;
    [ProtoMember(2)] long _toEntityId;
    [ProtoMember(3)] double _amount;
    [ProtoMember(4)] uint _itemId;
    [ProtoMember(5)] bool _equipAfterMove;

    public InventoryUpdatePacket() { }

    public InventoryUpdatePacket(long fromId, long toId, double amount, uint itemId, bool equip = false)
    {
      _fromEntityId = fromId;
      _toEntityId = toId;
      _amount = amount;
      _itemId = itemId;
      _equipAfterMove = equip;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      var fromEnt = MyEntities.GetEntityById(_fromEntityId) as IMyCharacter;
      var fromInv = fromEnt?.GetInventory() as MyInventory;

      if (fromInv == null || fromEnt.MarkedForClose || fromEnt.IsDead)
        return false;

      var toEnt = MyEntities.GetEntityById(_toEntityId) as IMyCharacter;
      var toInv = toEnt?.GetInventory() as MyInventory;

      if (toInv == null || toEnt.MarkedForClose || toEnt.IsDead)
        return false;

      var amount = (MyFixedPoint)_amount;
      var item = fromInv.GetItemByID(_itemId);

      if (item != null)
      {
        amount = MyFixedPoint.Min(amount, toInv.ComputeAmountThatFits(item.Value.Content.GetId()));
        amount = MyFixedPoint.Min(amount, item.Value.Amount);

        //if (fromInv.RemoveItemsInternal(_itemId, amount)) // Changed in 1.204 Signal, no longer allowed
        fromInv.RemoveItems(_itemId, amount);
        var itemCheck = fromInv.GetItemByID(_itemId);
        if (itemCheck == null || itemCheck.Value.Amount < item.Value.Amount)
        {
          if (!toInv.Add(item.Value, amount))
          {
            fromInv.Add(item.Value, amount);
            toInv.RaiseInventoryContentChanged(item.Value, 0);
            AiSession.Instance.CommandMenu.ResetChanges();
          }
          else
          {
            BotBase bot;
            if (AiSession.Instance.Bots.TryGetValue(_fromEntityId, out bot))
            {
              var itemDef = item.Value.Content.GetId();
              var toolDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(itemDef);
              if (toolDef != null)
              {
                bot.EnsureWeaponValidity();
              }
            }

            if (_equipAfterMove)
            {
              if (AiSession.Instance.Bots.TryGetValue(_toEntityId, out bot) && bot?.Owner != null)
              {
                string reason;
                var content = item.Value.Content;
                var itemDef = content.GetId();

                var consumable = MyDefinitionManager.Static.GetDefinition(itemDef) as MyConsumableItemDefinition;
                if (consumable != null)
                {
                  var comp = bot.Character.Components.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
                  foreach (var statItem in consumable.Stats)
                  {
                    MyEntityStat stat;
                    if (comp.Stats.TryGetValue(MyStringHash.GetOrCompute(statItem.Name), out stat))
                      stat.ClearEffects();
                  }

                  toInv.ConsumeItem(itemDef, 1, bot.Character.EntityId);
                  return false;
                }
                else
                {
                  fromInv.RaiseInventoryContentChanged(item.Value, 0);
                  toInv.RaiseInventoryContentChanged(item.Value, 0);
                  AiSession.Instance.CommandMenu.ResetChanges();
                }

                var controlEnt = bot.Character as Sandbox.Game.Entities.IMyControllableEntity;
                if (AiSession.Instance.IsBotAllowedToUse(bot, itemDef.SubtypeName, out reason) && controlEnt.CanSwitchToWeapon(itemDef))
                {
                  bot.ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(itemDef);
                  var hasWeapon = bot.ToolDefinition != null;

                  bot.HasWeaponOrTool = hasWeapon;
                  if (hasWeapon && !(bot.Character?.Parent is IMyCockpit))
                  {
                    bot.EquipWeapon();
                  }
                }
                else
                {
                  var pkt = new MessagePacket(reason ?? $"{bot.Character.Name} was unable to use the item.");
                  netHandler.SendToPlayer(pkt, bot.Owner.SteamUserId);
                }
              }
              else
              {
                fromInv.RaiseInventoryContentChanged(item.Value, 0);
                toInv.RaiseInventoryContentChanged(item.Value, 0);
                AiSession.Instance.CommandMenu.ResetChanges();
              }
            }
          }
        }
        else
        {
          fromInv.RaiseInventoryContentChanged(item.Value, 0);
          toInv.RaiseInventoryContentChanged(item.Value, 0);
          AiSession.Instance.CommandMenu.ResetChanges();
        }
      }

      return false;
    }
  }
}
