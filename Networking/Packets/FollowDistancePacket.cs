using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

using Sandbox.ModAPI;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class FollowDistancePacket : PacketBase
  {
    public float FollowDistance;

    public FollowDistancePacket() { }

    public FollowDistancePacket(float distance)
    {
      FollowDistance = distance;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      var identityId = MyAPIGateway.Players.TryGetIdentityId(SenderId);
      if (identityId > 0 && AiSession.Instance.Players.ContainsKey(identityId))
      {
        AiSession.Instance.PlayerFollowDistanceDict[identityId] = FollowDistance;
      }

      return false;
    }
  }
}
