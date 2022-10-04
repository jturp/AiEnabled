using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles;
using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using VRage;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class SpawnPacket : PacketBase
  {
    [ProtoMember(1)] readonly SerializableVector3D Position;
    [ProtoMember(2)] readonly SerializableVector3D Forward;
    [ProtoMember(3)] readonly SerializableVector3D Up;
    [ProtoMember(4)] readonly string Subtype;
    [ProtoMember(5)] readonly string Role;
    [ProtoMember(6)] readonly long? OwnerId;
    [ProtoMember(7)] readonly string Name;

    public SpawnPacket() { }

    public SpawnPacket(Vector3D pos, Vector3D forward, Vector3D up, string subtype = "Target_Dummy", string role = "Combat", long? ownerId = null, string name = null)
    {
      Position = pos;
      Forward = forward;
      Up = up;
      Subtype = subtype;
      Role = role;
      OwnerId = ownerId;
      Name = name;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (OwnerId.HasValue)
      {
        if (!AiSession.Instance.CanSpawn)
        {
          var pkt = new MessagePacket($"Unable to spawn bot. Try again in a moment...");
          netHandler.SendToPlayer(pkt, SenderId);
          return false;
        }

        if (Role.StartsWith("Combat", StringComparison.OrdinalIgnoreCase) || Role.StartsWith("Repair", StringComparison.OrdinalIgnoreCase)
          || Role.StartsWith("Crew", StringComparison.OrdinalIgnoreCase) || Role.StartsWith("Scavenger", StringComparison.OrdinalIgnoreCase))
        {
          var ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(OwnerId.Value);
          if (ownerFaction == null)
          {
            var pkt = new MessagePacket($"Unable to spawn bot. Owner is not in a faction!");
            netHandler.SendToPlayer(pkt, SenderId);
            return false;
          }
        }
      }

      Vector3D pos = Position;
      Vector3D fwd = Forward;
      Vector3D up = Up;

      var posOr = new MyPositionAndOrientation((Vector3)pos, (Vector3)fwd, (Vector3)up);
      var bot = BotFactory.SpawnBotFromAPI(Subtype, Name, posOr, null, Role, OwnerId, adminSpawn: true);
      if (bot == null)
      {
        var pkt = new MessagePacket($"Bot was null after creation!");
        netHandler.SendToPlayer(pkt, SenderId);
      }
      else
      {
        BotBase b;
        if (AiSession.Instance.Bots.TryGetValue(bot.EntityId, out b) && b?.ToolDefinition != null)
          b.AddWeapon();
      }

      return false;
    }
  }
}
