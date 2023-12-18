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
    [ProtoMember(1)] List<long> _iconBotList;
    [ProtoMember(2)] bool _isHealing;

    public OverHeadIconPacket() { }

    public OverHeadIconPacket(List<long> analyzers, bool isHealing)
    {
      _iconBotList = analyzers;
      _isHealing = isHealing;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (_iconBotList?.Count > 0)
      {
        if (_isHealing)
          AiSession.Instance.AddHealingIcons(_iconBotList);
        else
          AiSession.Instance.AddOverHeadIcons(_iconBotList);
      }

      return false;
    }
  }
}
