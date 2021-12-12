using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRageMath;

namespace AiEnabled.Particles
{
  public class ParticleInfoClient
  {
    public long BotEntityId;
    public long BlockEntityId;
    public long GridEntityId;
    public Vector3I? BlockPosition;
    public ParticleInfoBase.ParticleType ParticleType;
    public bool IsWelderParticle;

    public ParticleInfoClient(ParticleInfoBase.ParticleType particleType, long botId, long blockId = 0, long gridId = 0, Vector3I? position = null, bool isWelder = false)
    {
      BotEntityId = botId;
      BlockEntityId = blockId;
      GridEntityId = gridId;
      BlockPosition = position;
      ParticleType = particleType;
      IsWelderParticle = isWelder;
    }
  }
}
