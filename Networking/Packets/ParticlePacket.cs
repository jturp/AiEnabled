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
    [ProtoMember(5)] public SerializableVector3D? WorldPosition;
    [ProtoMember(6)] public int ParticleType;
    [ProtoMember(7)] public bool Remove;
    [ProtoMember(8)] public bool Stop;
    [ProtoMember(9)] public bool IsWelder;

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

    public ParticlePacket(long botId, ParticleInfoBase.ParticleType particle, Vector3D position, bool remove = false, bool stop = false)
    {
      BotEntityId = botId;
      ParticleType = (int)particle;
      WorldPosition = position;
      Remove = remove;
      Stop = stop;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (Stop)
      {
        ParticleInfoBase pBase;
        if (AiSession.Instance.ParticleInfoDict.TryGetValue(BotEntityId, out pBase))
          pBase.Stop();
      }
      else
      {
        ParticleInfoClient pClient;
        if (AiSession.Instance.ParticleDictionary.TryGetValue(BotEntityId, out pClient) && pClient?.ParticleType == ParticleInfoBase.ParticleType.Shield)
          return false;

        AiSession.Instance.ParticleDictionary.TryRemove(BotEntityId, out pClient);

        ParticleInfoBase pBase;
        if (AiSession.Instance.ParticleInfoDict.TryRemove(BotEntityId, out pBase))
          pBase?.Close();

        if (!Remove)
        {
          var pType = (ParticleInfoBase.ParticleType)ParticleType;
          var info = new ParticleInfoClient(pType, BotEntityId, BlockEntityId, GridEntityId, BlockPosition, WorldPosition, IsWelder);

          if (!AiSession.Instance.ParticleDictionary.TryAdd(BotEntityId, info))
          {
            AiSession.Instance.Logger.Log($" -> Unable to add particle to dictionary, BlockId = {BlockEntityId}, Particle = {pType}, InDictionary = {AiSession.Instance.ParticleDictionary.ContainsKey(BotEntityId)}", Utilities.MessageType.WARNING);
          }
        }
      }

      return false;
    }
  }
}
