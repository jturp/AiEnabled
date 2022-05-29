using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class SettingRequestPacket : PacketBase
  {
    public SettingRequestPacket() { }
    public override bool Received(NetworkHandler netHandler)
    {
      var repairReqs = AiSession.Instance.BotComponents[AiSession.BotType.Repair];
      var combatReqs = AiSession.Instance.BotComponents[AiSession.BotType.Combat];
      var scavReqs = AiSession.Instance.BotComponents[AiSession.BotType.Scavenger];

      var pkt = new AdminPacket(AiSession.Instance.MaxBots, AiSession.Instance.MaxHelpers, AiSession.Instance.ModSaveData.MaxBotProjectileDistance, AiSession.Instance.AllowMusic, repairReqs, combatReqs, scavReqs);
      netHandler.SendToPlayer(pkt, SenderId);

      return false;
    }
  }
}
