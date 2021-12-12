using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Particles;

using ProtoBuf;

using VRage;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class ParticlePacket : PacketBase
  {
    [ProtoMember(1)] public long BotEntityId;
    [ProtoMember(2)] public long BlockEntityId;
    [ProtoMember(3)] public long GridEntityId;
    [ProtoMember(4)] public SerializableVector3I? BlockPosition;
    [ProtoMember(5)] public int ParticleType;
    [ProtoMember(6)] public bool Remove;
    [ProtoMember(7)] public bool Stop;
    [ProtoMember(8)] public bool IsWelder;

    public ParticlePacket() { }

    public ParticlePacket(long botId, ParticleInfoBase.ParticleType particle, long blockId = 0, long gridId = 0, Vector3I? position = null, bool isWelder = false, bool remove = false, bool stop = false)
    {
      BotEntityId = botId;
      BlockEntityId = blockId;
      GridEntityId = gridId;
      BlockPosition = position;
      ParticleType = (int)particle;
      Remove = remove;
      Stop = stop;
      IsWelder = isWelder;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (Stop)
      {
        ParticleInfoBase pBase;
        if (AiSession.Instance.ParticleInfoDict.TryGetValue(BotEntityId, out pBase))
          pBase.Stop();
      }
      else if (Remove)
      {
        ParticleInfoClient _;
        AiSession.Instance.ParticleDictionary.TryRemove(BotEntityId, out _);

        ParticleInfoBase pBase;
        if (AiSession.Instance.ParticleInfoDict.TryRemove(BotEntityId, out pBase))
          pBase?.Close();
      }
      else
      {
        var pType = (ParticleInfoBase.ParticleType)ParticleType;
        var info = new ParticleInfoClient(pType, BotEntityId, BlockEntityId, GridEntityId, BlockPosition, IsWelder);

        if (!AiSession.Instance.ParticleDictionary.TryAdd(BotEntityId, info))
          AiSession.Instance.Logger.Log($"Unable to add particle to dictionary: BotId = {BotEntityId}, BlockId = {BlockEntityId}, Particle = {pType}", Utilities.MessageType.WARNING);
      }

      return false;
    }
  }
}
