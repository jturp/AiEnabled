using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class HealthBarPacket : PacketBase
  {
    [ProtoMember(1)] List<long> _healthBars;

    public HealthBarPacket() { }

    public HealthBarPacket(List<long> list)
    {
      _healthBars = list;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (AiSession.Instance.PlayerData.ShowHealthBars && _healthBars?.Count > 0)
      {
        AiSession.Instance.AddHealthBarIcons(_healthBars);
      }

      return false;
    }
  }
}
