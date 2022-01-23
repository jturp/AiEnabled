using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage;
using VRage.Game;
using VRage.Game.Entity;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class ShieldHitPacket : PacketBase
  {
    [ProtoMember(1)] public float ShieldDamage;
    [ProtoMember(2)] public long AttackerId;
    [ProtoMember(3)] public long ShieldId;
    [ProtoMember(4)] public SerializableVector3D WorldPosition;

    public ShieldHitPacket() { }

    public ShieldHitPacket(long shieldId, long attackerId, Vector3D position, float damage)
    {
      ShieldDamage = damage;
      WorldPosition = position;
      AttackerId = attackerId;
      ShieldId = shieldId;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (AiSession.Instance.ShieldAPILoaded)
      {
        var shieldEnt = MyEntities.GetEntityById(ShieldId) as IMyTerminalBlock;
        if (shieldEnt != null)
          AiSession.Instance.ShieldAPI.PointAttackShieldCon(shieldEnt, WorldPosition, AttackerId, ShieldDamage, 0f, true, true);
      }

      return false;
    }
  }
}
