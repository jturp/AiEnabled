using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

using VRage;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class WeaponFirePacket : PacketBase
  {
    [ProtoMember(1)] public long BotEntityId;
    [ProtoMember(2)] public long TargetEntityId;
    [ProtoMember(3)] public bool IsGrinder;
    [ProtoMember(4)] public bool IsWelder;
    [ProtoMember(5)] public float Damage;
    [ProtoMember(6)] public float AngleDeviationDegrees;
    [ProtoMember(7)] public int TicksBetweenProjectiles;
    [ProtoMember(8)] public int CurrentAmmo;
    [ProtoMember(9)] public List<float> Randoms;
    [ProtoMember(10)] public bool LeadTargets;
    [ProtoMember(11)] public SerializableVector3I? Position;

    public WeaponFirePacket() { }

    public WeaponFirePacket(long botId, long tgtId, float damage, float angleDeviationDegrees, List<float> rand, int ticksBetween, int ammoLeft, bool isGrinder, bool isWelder, bool leadTargets, Vector3I? position = null)
    {
      BotEntityId = botId;
      TargetEntityId = tgtId;
      Damage = damage;
      AngleDeviationDegrees = angleDeviationDegrees;
      TicksBetweenProjectiles = ticksBetween;
      IsGrinder = isGrinder;
      IsWelder = isWelder;
      CurrentAmmo = ammoLeft;
      Randoms = rand;
      LeadTargets = leadTargets;
      Position = position;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      AiSession.Instance.StartWeaponFire(BotEntityId, TargetEntityId, Damage, AngleDeviationDegrees, Randoms, TicksBetweenProjectiles, CurrentAmmo, IsGrinder, IsWelder, LeadTargets, Position);
      return false;
    }
  }
}
