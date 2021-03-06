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

    public SpawnPacket() { }

    public SpawnPacket(Vector3D pos, Vector3D forward, Vector3D up, string subtype = "Target_Dummy", string role = "CombatBot", long? ownerId = null)
    {
      Position = pos;
      Forward = forward;
      Up = up;
      Subtype = subtype;
      Role = role;
      OwnerId = ownerId;
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

        var ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(OwnerId.Value);
        if (ownerFaction == null)
        {
          var pkt = new MessagePacket($"Unable to spawn bot. Owner is not in a faction!");
          netHandler.SendToPlayer(pkt, SenderId);
          return false;
        }

        IMyFaction botFaction;
        if (!AiSession.Instance.BotFactions.TryGetValue(ownerFaction.FactionId, out botFaction))
        {
          var pkt = new MessagePacket($"Unable to spawn bot. There was no bot faction paired with owner's faction!");
          netHandler.SendToPlayer(pkt, SenderId);
          return false;
        }
      }

      Vector3D pos = Position;
      Vector3D fwd = Forward;
      Vector3D up = Up;

      var posOr = new MyPositionAndOrientation((Vector3)pos, (Vector3)fwd, (Vector3)up);
      var bot = BotFactory.SpawnBotFromAPI(Subtype, "", posOr, null, Role, OwnerId);
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
