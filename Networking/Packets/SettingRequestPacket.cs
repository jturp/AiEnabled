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
      var pkt = new AdminPacket(AiSession.Instance.MaxBots, AiSession.Instance.MaxHelpers, AiSession.Instance.ModSaveData.MaxBotProjectileDistance, AiSession.Instance.AllowMusic);
      netHandler.SendToPlayer(pkt, SenderId);

      return false;
    }
  }
}
