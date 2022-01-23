using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class GpsUpdatePacket : PacketBase
  {
    [ProtoMember(1)] public List<long> BotIDs;
    [ProtoMember(2)] public List<long> OwnerIDs;

    public GpsUpdatePacket() { }

    public GpsUpdatePacket(List<long> bots, List<long> owners)
    {
      BotIDs = bots;
      OwnerIDs = owners;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      AiSession.Instance.UpdateGPSCollection(BotIDs, OwnerIDs);
      return false;
    }
  }
}
