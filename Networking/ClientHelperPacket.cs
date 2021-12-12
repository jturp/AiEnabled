using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.ConfigData;

using ProtoBuf;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class ClientHelperPacket : PacketBase
  {
    [ProtoMember(1)] List<HelperInfo> helperInfo;

    public ClientHelperPacket() { }

    public ClientHelperPacket(List<HelperInfo> data)
    {
      helperInfo = data;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      AiSession.Instance.MyHelperInfo = helperInfo;
      return false;
    }
  }
}
