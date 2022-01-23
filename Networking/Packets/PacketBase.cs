using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

using Sandbox.ModAPI;

namespace AiEnabled.Networking
{
  [ProtoInclude(1001, typeof(SoundPacket))]
  [ProtoInclude(1002, typeof(WeaponFirePacket))]
  [ProtoInclude(1003, typeof(SpawnPacket))]
  [ProtoInclude(1004, typeof(AdminPacket))]
  [ProtoInclude(1005, typeof(FactorySpawnPacket))]
  [ProtoInclude(1006, typeof(ParticlePacket))]
  [ProtoInclude(1007, typeof(MessagePacket))]
  [ProtoInclude(1008, typeof(SpawnPacketClient))]
  [ProtoInclude(1009, typeof(GpsUpdatePacket))]
  [ProtoInclude(1010, typeof(RepChangePacket))]
  [ProtoInclude(1011, typeof(CommandPacket))]
  [ProtoInclude(1012, typeof(InventoryUpdatePacket))]
  [ProtoInclude(1013, typeof(EquipWeaponPacket))]
  [ProtoInclude(1014, typeof(OverHeadIconPacket))]
  [ProtoInclude(1015, typeof(HealthBarPacket))]
  [ProtoInclude(1016, typeof(BotRecallPacket))]
  [ProtoInclude(1017, typeof(ColorUpdatePacket))]
  [ProtoInclude(1018, typeof(FixBotPacket))]
  [ProtoInclude(1019, typeof(FactoryDismissPacket))]
  [ProtoInclude(1020, typeof(FactoryRecallPacket))]
  [ProtoInclude(1021, typeof(StoreBotPacket))]
  [ProtoInclude(1022, typeof(ClientHelperPacket))]
  [ProtoInclude(1023, typeof(SettingRequestPacket))]
  [ProtoInclude(1024, typeof(ShieldHitPacket))]
  [ProtoContract]
  public abstract class PacketBase
  {
    [ProtoMember(1)] public readonly ulong SenderId;

    public PacketBase()
    {
      SenderId = MyAPIGateway.Multiplayer.MyId;
    }

    public abstract bool Received(NetworkHandler netHandler);
  }
}
