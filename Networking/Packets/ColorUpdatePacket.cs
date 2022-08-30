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
    [ProtoMember(2)] SerializableVector3? _repairColor;
    [ProtoMember(3)] SerializableVector3? _grindColor;

    public ColorUpdatePacket() { }

    public ColorUpdatePacket(long playerId, Vector3? clrRepair, Vector3? clrGrind)
    {
      _playerIdentityId = playerId;
      _repairColor = clrRepair;
      _grindColor = clrGrind;
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
          playerData.RepairBotIgnoreColorMask = _repairColor;
          playerData.RepairBotGrindColorMask = _grindColor;
          break;
        }
      }

      if (!found)
      {
        var playerData = new HelperData(_playerIdentityId, _repairColor, _grindColor);
        data.PlayerHelperData.Add(playerData);
      }

      AiSession.Instance.SaveModData(true);
      return false;
    }
  }
}
