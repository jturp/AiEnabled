 using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.ConfigData;
using AiEnabled.Networking.Packets;
using AiEnabled.Utilities;

using ProtoBuf;

using Sandbox.ModAPI;

using VRage.Utils;

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
  [ProtoInclude(1016, typeof(BotResumePacket))]
  [ProtoInclude(1017, typeof(ColorUpdatePacket))]
  [ProtoInclude(1018, typeof(FixBotPacket))]
  [ProtoInclude(1019, typeof(FactoryDismissPacket))]
  [ProtoInclude(1020, typeof(FactoryRecallPacket))]
  [ProtoInclude(1021, typeof(StoreBotPacket))]
  [ProtoInclude(1022, typeof(ClientHelperPacket))]
  [ProtoInclude(1023, typeof(SettingRequestPacket))]
  [ProtoInclude(1024, typeof(ShieldHitPacket))]
  [ProtoInclude(1025, typeof(BotStatusPacket))]
  [ProtoInclude(1026, typeof(BotStatusRequestPacket))]
  [ProtoInclude(1027, typeof(CharacterSwapPacket))]
  [ProtoInclude(1029, typeof(SettingSyncPacket))]
  [ProtoInclude(1030, typeof(SettingProvidePacket))]
  [ProtoInclude(1031, typeof(HelmetChangePacket))]
  [ProtoInclude(1032, typeof(PriorityUpdatePacket))]
  [ProtoInclude(1033, typeof(FactorySyncPacket))]
  [ProtoInclude(1034, typeof(RadioRecallPacket))]
  [ProtoInclude(1035, typeof(FollowDistancePacket))]
  [ProtoInclude(1036, typeof(ResetMapPacket))]
  [ProtoInclude(1037, typeof(GoToAllPacket))]
  [ProtoContract]
  public abstract class PacketBase
  {
    [ProtoMember(1)] public readonly ulong SenderId;

    public PacketBase()
    {
      SenderId = MyAPIGateway.Multiplayer.MyId;
    }

    public abstract bool Received(NetworkHandler netHandler);

    public static void TestPackets()
    {
      AiSession.Instance.Logger.Log($"TestPackets: Begin");
      TestPacket<SoundPacket>();
      TestPacket<WeaponFirePacket>();
      TestPacket<SpawnPacket>();
      TestPacket<AdminPacket>();
      TestPacket<FactorySpawnPacket>();
      TestPacket<ParticlePacket>();
      TestPacket<MessagePacket>();
      TestPacket<SpawnPacketClient>();
      TestPacket<GpsUpdatePacket>();
      TestPacket<RepChangePacket>();
      TestPacket<CommandPacket>();
      TestPacket<InventoryUpdatePacket>();
      TestPacket<EquipWeaponPacket>();
      TestPacket<OverHeadIconPacket>();
      TestPacket<HealthBarPacket>();
      TestPacket<BotResumePacket>();
      TestPacket<ColorUpdatePacket>();
      TestPacket<FixBotPacket>();
      TestPacket<FactoryDismissPacket>();
      TestPacket<FactoryRecallPacket>();
      TestPacket<StoreBotPacket>();
      TestPacket<ClientHelperPacket>();
      TestPacket<SettingRequestPacket>();
      TestPacket<ShieldHitPacket>();
      TestPacket<BotStatusPacket>();
      TestPacket<BotStatusRequestPacket>();
      TestPacket<CharacterSwapPacket>();
      TestPacket<SettingSyncPacket>();
      TestPacket<SettingProvidePacket>();
      TestPacket<HelmetChangePacket>();
      TestPacket<PriorityUpdatePacket>();
      TestPacket<FactorySyncPacket>();
      TestPacket<RadioRecallPacket>();
      TestPacket<FollowDistancePacket>();
      TestPacket<ResetMapPacket>();
      TestPacket<GoToAllPacket>();
      AiSession.Instance.Logger.Log($"TestPackets: End");
    }

    static void TestPacket<T>() where T : new()
    {
      try
      {
        MyLog.Default.WriteLine($"### AiEnabled :: Testing packet {typeof(T).Name}...");
        AiSession.Instance.Logger.Log($"Testing packet {typeof(T).Name}...");

        T packet = new T();

        byte[] bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);

        MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(bytes);
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLine($"### AiEnabled :: Error serializing or deserializing {typeof(T).Name}\n{e}");
        AiSession.Instance.Logger.Log($"Error serializing or deserializing {typeof(T).Name}\n{e}");

        try
        {
          throw new NullReferenceException(e.Message);
        }
        catch { }
      }
    }
  }
}
