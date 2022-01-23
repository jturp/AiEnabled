using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

using Sandbox.Game.Entities;

using VRage.Game.ModAPI;

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

      BotBase helperBase;
      if (AiSession.Instance.Bots.TryGetValue(_botEntityId, out helperBase) && helperBase != null && !helperBase.IsDead)
        helperBase.Close();

      long price = AiSession.Instance.BotPrices[AiSession.BotType.Combat] / 2;

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
              var role = (AiSession.BotType)helper.Role;
              if (role == AiSession.BotType.Repair)
                price = AiSession.Instance.BotPrices[AiSession.BotType.Repair] / 2;

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

      player.RequestChangeBalance(price);
      return false;
    }
  }
}
