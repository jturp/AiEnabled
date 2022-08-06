using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;

using ProtoBuf;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class BotStatusPacket : PacketBase
  {
    [ProtoMember(1)] public List<BotStatus> StatusItems;

    public BotStatusPacket() { }

    public BotStatusPacket(List<BotStatus> stats)
    {
      StatusItems = stats;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (AiSession.Instance != null && StatusItems?.Count > 0)
      {
        AiSession.Instance.PropagateBotStatusUpdate(StatusItems);
      }

      return false;
    }
  }
}
