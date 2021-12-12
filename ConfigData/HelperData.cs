using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRage.Game.ModAPI;

using VRageMath;
using ProtoBuf;

namespace AiEnabled.ConfigData
{
  [ProtoContract]
  public class HelperInfo
  {
    [ProtoMember(100)] public long HelperId;
    [ProtoMember(101)] public long GridEntityId;
    [ProtoMember(102)] public string Subtype;
    [ProtoMember(103)] public string DisplayName;
    [ProtoMember(104)] public bool IsActiveHelper;
    [ProtoMember(105)] public SerializableVector3D Position;
    [ProtoMember(106)] public SerializableQuaternion Orientation;
    [ProtoMember(107)] public int Role;

    public HelperInfo() { }

    public HelperInfo(IMyCharacter bot, AiSession.BotType botType, MyCubeGrid grid = null)
    {
      HelperId = bot.EntityId;
      GridEntityId = grid?.EntityId ?? 0L;
      Subtype = bot.Definition.Id.SubtypeName;
      DisplayName = bot.Name ?? "";
      Position = bot.GetPosition();
      Orientation = Quaternion.CreateFromRotationMatrix(bot.WorldMatrix);
      IsActiveHelper = true;
      Role = (int)botType;
    }
  }

  public class HelperData
  {
    public long OwnerIdentityId;
    public Vector3? RepairBotIgnoreColorMask;
    public List<HelperInfo> Helpers;

    public HelperData() { }

    public HelperData(long ident, Vector3? hsv)
    {
      OwnerIdentityId = ident;
      RepairBotIgnoreColorMask = hsv;
      Helpers = new List<HelperInfo>();
    }

    public void AddHelper(IMyCharacter helper, AiSession.BotType botType, MyCubeGrid grid)
    {
      if (Helpers == null)
        Helpers = new List<HelperInfo>();

      Helpers.Add(new HelperInfo(helper, botType, grid));
    }

    public bool RemoveHelper(long id)
    {
      if (Helpers == null)
        return false;

      for (int i = Helpers.Count - 1; i >= 0; i--)
      {
        if (Helpers[i].HelperId == id)
        {
          Helpers.RemoveAtFast(i);
          return true;
        }
      }

      return false;
    }

    public void Close()
    {
      Helpers?.Clear();
      Helpers = null;
    }
  }
}
