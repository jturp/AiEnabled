using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class PriorityUpdatePacket : PacketBase
  {
    [ProtoMember(1)] readonly List<KeyValuePair<string, bool>> _repairPriorities;
    [ProtoMember(2)] readonly List<KeyValuePair<string, bool>> _targetPriorities;
    [ProtoMember(3)] readonly List<KeyValuePair<string, bool>> _ignoreList;
    [ProtoMember(4)] readonly bool _damageToDisable;
    [ProtoMember(5)] readonly long _ownerId;
    [ProtoMember(6)] readonly bool _weldBeforeGrind;

    public PriorityUpdatePacket() { }

    public PriorityUpdatePacket(long ownerIdentityId, List<KeyValuePair<string, bool>> ignList, List<KeyValuePair<string, bool>> repList, List<KeyValuePair<string, bool>> tgtList, bool disableOnly, bool weldBeforeGrind)
    {
      _ignoreList = ignList;
      _repairPriorities = repList;
      _targetPriorities = tgtList;
      _damageToDisable = disableOnly;
      _ownerId = ownerIdentityId;
      _weldBeforeGrind = weldBeforeGrind;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (AiSession.Instance?.PlayerToHelperDict != null && AiSession.Instance.Registered)
      {
        List<BotBase> bots;
        if (AiSession.Instance.PlayerToHelperDict.TryGetValue(_ownerId, out bots) && bots?.Count > 0)
        {
          for (int i = 0; i < bots.Count; i++)
          {
            var helper = bots[i];
            if (helper != null && !helper.IsDead)
            {
              helper.Target.RemoveTarget();

              if (helper.RepairPriorities == null)
              {
                helper.RepairPriorities = new RepairPriorities(_repairPriorities);
              }
              else
              {
                helper.RepairPriorities.PriorityTypes.Clear();
                helper.RepairPriorities.PriorityTypes.AddList(_repairPriorities);
              }

              if (helper is RepairBot || helper is ScavengerBot)
                helper.RepairPriorities.UpdateIgnoreList(_ignoreList);

              if (helper.TargetPriorities == null)
              {
                helper.TargetPriorities = new TargetPriorities(_targetPriorities);
              }
              else
              {
                helper.TargetPriorities.PriorityTypes.Clear();
                helper.TargetPriorities.PriorityTypes.AddList(_targetPriorities);
              }

              helper.TargetPriorities.DamageToDisable = _damageToDisable;
              helper.RepairPriorities.WeldBeforeGrind = _weldBeforeGrind;
            }
          }
        }
      }

      return false;
    }
  }
}
