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
    [ProtoMember(6)] public int TicksBetweenProjectiles;
    [ProtoMember(7)] public int CurrentAmmo;
    [ProtoMember(8)] public List<float> Randoms;

    public WeaponFirePacket() { }

    public WeaponFirePacket(long botId, long tgtId, float damage, List<float> rand, int ticksBetween, int ammoLeft, bool isGrinder, bool isWelder)
    {
      BotEntityId = botId;
      TargetEntityId = tgtId;
      Damage = damage;
      TicksBetweenProjectiles = ticksBetween;
      IsGrinder = isGrinder;
      IsWelder = isWelder;
      CurrentAmmo = ammoLeft;
      Randoms = rand;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      AiSession.Instance.StartWeaponFire(BotEntityId, TargetEntityId, Damage, Randoms, TicksBetweenProjectiles, CurrentAmmo, IsGrinder, IsWelder);
      return false;
    }
  }
}
