using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;

using ProtoBuf;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;

using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class EquipWeaponPacket : PacketBase
  {
    [ProtoMember(1)] long _botEntityId;
    [ProtoMember(2)] SerializableDefinitionId _itemDefinition;

    public EquipWeaponPacket() { }

    public EquipWeaponPacket(long botId, MyDefinitionId weaponDef)
    {
      _botEntityId = botId;
      _itemDefinition = weaponDef;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      BotBase bot;
      if (AiSession.Instance.Bots.TryGetValue(_botEntityId, out bot) && bot?.Owner != null)
      {
        var usable = MyDefinitionManager.Static.GetDefinition(_itemDefinition) as MyUsableItemDefinition;
        if (usable != null)
        {
          var consumable = usable as MyConsumableItemDefinition;
          if (consumable != null)
          {
            var inv = bot.Character.GetInventory() as MyInventory;
            if (inv != null)
            {
              var comp = bot.Character.Components.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
              foreach (var statItem in consumable.Stats)
              {
                MyEntityStat stat;
                if (comp.Stats.TryGetValue(MyStringHash.GetOrCompute(statItem.Name), out stat))
                  stat.ClearEffects();
              }

              inv.ConsumeItem(usable.Id, 1, bot.Character.EntityId);
              return false;
            }
          }
        }

        string reason;
        var controlEnt = bot.Character as Sandbox.Game.Entities.IMyControllableEntity;
        if (AiSession.Instance.IsBotAllowedToUse(bot, _itemDefinition.SubtypeId, out reason) && controlEnt.CanSwitchToWeapon(_itemDefinition))
        {
          controlEnt.SwitchToWeapon(_itemDefinition);
          bot.ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(_itemDefinition);
          bot.HasWeaponOrTool = true;
          bot.SetShootInterval();
        }
        else
        {
          var pkt = new MessagePacket(reason ?? "Bot was unable to switch weapons.");
          netHandler.SendToPlayer(pkt, bot.Owner.SteamUserId);
        }
      }

      return false;
    }
  }
}
