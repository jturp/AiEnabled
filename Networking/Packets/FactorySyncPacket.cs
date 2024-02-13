using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.API;
using AiEnabled.Bots;
using AiEnabled.GameLogic;
using AiEnabled.Support;

using ProtoBuf;

using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class FactorySyncPacket : PacketBase
  {
    [ProtoMember(1)] readonly bool DamageToDisable;
    [ProtoMember(2)] readonly bool WeldBeforeGrind;
    [ProtoMember(3)] readonly List<KeyValuePair<string, bool>> RepairPriorities;
    [ProtoMember(4)] readonly List<KeyValuePair<string, bool>> TargetPriorities;
    [ProtoMember(5)] readonly List<KeyValuePair<string, bool>> IgnoreList;
    [ProtoMember(6)] readonly long BlockEntityId;

    public FactorySyncPacket() { }

    public FactorySyncPacket(long blockId, bool dmg2Disable, bool weldFirst, List<KeyValuePair<string, bool>> repList, List<KeyValuePair<string, bool>> tgtList, List<KeyValuePair<string, bool>> ignList)
    {
      BlockEntityId = blockId;
      DamageToDisable = dmg2Disable;
      WeldBeforeGrind = weldFirst;
      RepairPriorities = repList;
      TargetPriorities = tgtList;
      IgnoreList = ignList;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      var block = MyEntities.GetEntityById(BlockEntityId) as IMyTerminalBlock;
      var logic = block?.GameLogic.GetAs<Factory>();
      
      if (logic != null)
      {
        if (RepairPriorities != null)
        {
          if (logic.RepairPriorities == null)
          {
            logic.RepairPriorities = new RepairPriorities(RepairPriorities);
          }
          else
          {
            logic.RepairPriorities.PriorityTypes.Clear();
            logic.RepairPriorities.PriorityTypes.AddList(RepairPriorities);
          }

          logic.RepairPriorities.WeldBeforeGrind = WeldBeforeGrind;
        }

        if (TargetPriorities != null)
        {
          if (logic.TargetPriorities == null)
          {
            logic.TargetPriorities = new TargetPriorities(TargetPriorities);
          }
          else
          {
            logic.TargetPriorities.PriorityTypes.Clear();
            logic.TargetPriorities.PriorityTypes.AddList(TargetPriorities);
          }

          logic.TargetPriorities.DamageToDisable = DamageToDisable;
        }

        if (IgnoreList != null)
        {
          if (logic.RepairPriorities == null)
          {
            logic.RepairPriorities = new RepairPriorities(RepairPriorities);
          }

          logic.RepairPriorities.UpdateIgnoreList(IgnoreList);
        }

        logic.UpdatePriorityLists(true, true, true);
        return true;
      }

      return false;
    }
  }
}
