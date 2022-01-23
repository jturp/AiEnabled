using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

using VRage;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class SoundPacket : PacketBase
  {
    [ProtoMember(1)] public string SoundName;
    [ProtoMember(2)] public long EntityId;
    [ProtoMember(3)] public SerializableVector3D? Position;
    [ProtoMember(4)] public bool Stop;
    [ProtoMember(5)] public bool IncludeIcon;

    public SoundPacket() { }

    public SoundPacket(string soundName, long entityId, bool stop = false, bool includeIcon = false)
    {
      SoundName = soundName;
      EntityId = entityId;
      Stop = stop;
      IncludeIcon = includeIcon;
    }

    public SoundPacket(string soundName, Vector3D position, bool stop = false)
    {
      SoundName = soundName;
      Position = position;
      Stop = stop;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      try
      {
        if (EntityId > 0)
          AiSession.Instance.PlaySoundForEntity(EntityId, SoundName, Stop, IncludeIcon);
        else if (Position.HasValue)
          AiSession.Instance.PlayeSoundAtPosition(Position.Value, SoundName, Stop);
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in SoundPacket.Received: {ex.Message}\n{ex.StackTrace}", Utilities.MessageType.ERROR);
      }

      return false;
    }
  }
}
