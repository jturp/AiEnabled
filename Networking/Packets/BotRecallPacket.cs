using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class BotRecallPacket : PacketBase
  {
    [ProtoMember(1)] long _playerId;

    public BotRecallPacket() { }

    public BotRecallPacket(long playerId)
    {
      _playerId = playerId;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      AiSession.Instance.PlayerLeftCockpit("", _playerId, "AiEnabled_RecallBots");
      return false;
    }
  }
}
