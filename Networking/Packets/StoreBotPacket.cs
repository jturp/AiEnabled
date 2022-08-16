using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.ConfigData;
using AiEnabled.Particles;

using ProtoBuf;

using Sandbox.Game;
using Sandbox.ModAPI;

using VRage;
using VRage.Game;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class StoreBotPacket : PacketBase
  {
    [ProtoMember(1)] public long BotId;

    public StoreBotPacket() { }

    public StoreBotPacket(long id)
    {
      BotId = id;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(BotId, out bot) || bot?.Owner == null)
        return false;

      var playerHelperData = AiSession.Instance.ModSaveData.PlayerHelperData;
      for (int i = 0; i < playerHelperData.Count; i++)
      {
        var helperData = playerHelperData[i];
        if (helperData.OwnerIdentityId == bot.Owner.IdentityId)
        {
          var helperList = helperData.Helpers;
          for (int j = 0; j < helperList.Count; j++)
          {
            var helper = helperList[j];
            if (helper.HelperId == bot.Character.EntityId)
            {
              helper.IsActiveHelper = false;

              List<long> helperIds;
              if (!AiSession.Instance.PlayerToActiveHelperIds.TryGetValue(helperData.OwnerIdentityId, out helperIds) || helperIds == null)
              {
                helperIds = new List<long>();
                AiSession.Instance.PlayerToActiveHelperIds[helperData.OwnerIdentityId] = helperIds;
              }
              else if (helperIds.Count > 0)
              {
                helperIds.Remove(helper.HelperId);
              }

              var inventory = bot.Character.GetInventory() as MyInventory;
              if (inventory?.ItemCount > 0)
              {
                if (helper.InventoryItems == null)
                  helper.InventoryItems = new List<InventoryItem>();
                else
                  helper.InventoryItems.Clear();

                var items = inventory.GetItems();
                for (int k = 0; k < items.Count; k++)
                {
                  var item = items[k];
                  helper.InventoryItems.Add(new InventoryItem(item.Content.GetId(), item.Amount));
                }
              }

              if (helper.PatrolRoute?.Count > 0)
                helper.PatrolRoute.Clear();

              AiSession.Instance.SaveModData(true);
              break;
            }
          }

          break;
        }
      }

      bot.Close();
      return false;
    }
  }
}
