using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class CharacterSwapPacket : PacketBase
  {
    [ProtoMember(1)] long oldCharacterEntityId;
    [ProtoMember(2)] long newCharacterEntityId;

    public CharacterSwapPacket() { }

    public CharacterSwapPacket(long oldId, long newId)
    {
      oldCharacterEntityId = oldId;
      newCharacterEntityId = newId;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      AiSession.ControlInfo info;
      AiSession.Instance.BotToControllerInfoDict.TryRemove(oldCharacterEntityId, out info);
      AiSession.Instance.UpdateControllerAfterResync(oldCharacterEntityId, newCharacterEntityId);

      if (info != null)
      {
        AiSession.Instance.BotToControllerInfoDict[newCharacterEntityId] = info;
      }
      else
      {
        AiSession.Instance.Logger.Log($"CharacterSwapPacket.Received: ControllerInfo was null, unable to assign to new bot character.", Utilities.MessageType.WARNING);
      }

      return false;
    }
  }
}
