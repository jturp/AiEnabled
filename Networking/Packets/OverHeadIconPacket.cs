using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class OverHeadIconPacket : PacketBase
  {
    [ProtoMember(1)] List<long> _analyzeBotList;

    public OverHeadIconPacket() { }

    public OverHeadIconPacket(List<long> analyzers)
    {
      _analyzeBotList = analyzers;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (_analyzeBotList?.Count > 0)
      {
        AiSession.Instance.AddOverHeadIcons(_analyzeBotList);
      }

      return false;
    }
  }
}
