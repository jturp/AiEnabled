using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

using Sandbox.Game;
using Sandbox.Game.Entities;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  class FactoryDismissPacket : PacketBase
  {
    [ProtoMember(1)] long _botEntityId;
    [ProtoMember(2)] long _ownerIdentityId;

    public FactoryDismissPacket() { }

    public FactoryDismissPacket(long botId, long playerIdentityId)
    {
      _botEntityId = botId;
      _ownerIdentityId = playerIdentityId;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      IMyPlayer player;
      if (!AiSession.Instance.Players.TryGetValue(_ownerIdentityId, out player) || player == null)
        return false;

      List<long> helperIds;
      if (AiSession.Instance.PlayerToActiveHelperIds.TryGetValue(_ownerIdentityId, out helperIds) && helperIds != null)
        helperIds.Remove(_botEntityId);

      long price = -1;
      AiSession.BotType botType = AiSession.BotType.Scavenger;
      BotBase helperBase;
      if (AiSession.Instance.Bots.TryGetValue(_botEntityId, out helperBase) && helperBase != null)
      {
        botType = helperBase.BotType;
        price = AiSession.Instance.BotPrices[botType] / 2;

        if (!helperBase.IsDead) 
          helperBase.Close();
      }

      var saveData = AiSession.Instance.ModSaveData.PlayerHelperData;
      for (int i = 0; i < saveData.Count; i++)
      {
        var playerData = saveData[i];
        if (playerData.OwnerIdentityId == _ownerIdentityId)
        {
          var helperData = playerData.Helpers;
          for (int j = 0; j < helperData.Count; j++)
          {
            var helper = helperData[j];
            if (helper.HelperId == _botEntityId)
            {
              if (price < 0 && helper.Role >= 0 && helper.Role <= 2)
              {
                botType = (AiSession.BotType)helper.Role;
                price = AiSession.Instance.BotPrices[botType] / 2;
              }

              helperData.RemoveAt(j);
              var pkt = new ClientHelperPacket(helperData);
              AiSession.Instance.Network.SendToPlayer(pkt, player.SteamUserId);

              break;
            }
          }

          break;
        }
      }

      var packet = new SpawnPacketClient(_botEntityId, remove: true);
      AiSession.Instance.Network.SendToPlayer(packet, player.SteamUserId);

      if (price < 0)
        price = 5000;

      player.RequestChangeBalance(price);

      var returnComps = AiSession.Instance.BotComponents[botType];
      if (returnComps?.Count > 0 && player.Character != null)
      {
        var inv = player.Character.GetInventory() as MyInventory;
        if (inv != null)
        {
          foreach (var item in returnComps)
          {
            if (item.Amount > 0)
            {
              var def = AiSession.Instance.AllGameDefinitions[item.DefinitionId];
              var amount = Math.Max(1, item.Amount / 2);

              var objectBuilder = MyObjectBuilderSerializer.CreateNewObject(item.DefinitionId) as MyObjectBuilder_PhysicalObject;
              inv.AddItems(amount, objectBuilder);
            }
          }
        }
      }

      return false;
    }
  }
}
