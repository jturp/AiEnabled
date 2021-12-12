using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.ConfigData;

using ProtoBuf;

using VRage;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class ColorUpdatePacket : PacketBase
  {
    [ProtoMember(1)] long _playerIdentityId;
    [ProtoMember(2)] SerializableVector3? _color;

    public ColorUpdatePacket() { }

    public ColorUpdatePacket(long playerId, Vector3? clr)
    {
      _playerIdentityId = playerId;
      _color = clr;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      var data = AiSession.Instance.ModSaveData;
      bool found = false;

      for (int i = 0; i < data.PlayerHelperData.Count; i++)
      {
        var playerData = data.PlayerHelperData[i];
        if (playerData.OwnerIdentityId == _playerIdentityId)
        {
          found = true;
          playerData.RepairBotIgnoreColorMask = _color;
          break;
        }
      }

      if (!found)
      {
        var playerData = new HelperData(_playerIdentityId, _color);
        data.PlayerHelperData.Add(playerData);
      }

      AiSession.Instance.SaveModData(true);
      return false;
    }
  }
}
