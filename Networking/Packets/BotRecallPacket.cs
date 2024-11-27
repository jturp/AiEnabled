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
  public class BotResumePacket : PacketBase
  {
    [ProtoMember(1)] readonly long _playerId;
    [ProtoMember(2)] readonly int _commandDistance;

    public BotResumePacket() { }

    public BotResumePacket(long playerId, int cmdDistance)
    {
      _playerId = playerId;
      _commandDistance = cmdDistance;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      AiSession.Instance.PlayerLeftCockpit("", _playerId, $"AiEnabled_RecallBots.{_commandDistance}");
      return false;
    }
  }
}
