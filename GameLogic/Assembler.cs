using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;

using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using AiEnabled.Support;
using VRage.Game.ObjectBuilders.Definitions;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game;
using VRageMath;
using VRage.Game.ModAPI;
using AiEnabled.Bots.Roles;
using Sandbox.Game.Entities.Character.Components;
using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Ai.Support;
using AiEnabled.Networking;
using AiEnabled.Utilities;
using AiEnabled.Particles;
using AiEnabled.ConfigData;
using Sandbox.Definitions;
using Sandbox.Game;

namespace AiEnabled.GameLogic
{
  [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Assembler), false)]
  public class Assembler : MyGameLogicComponent
  {
    IMyAssembler _block;

    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
      _block = Entity as Sandbox.ModAPI.IMyAssembler;
      NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
      base.Init(objectBuilder);
    }

    public override void UpdateOnceBeforeFrame()
    {
      base.UpdateOnceBeforeFrame();
      if (AiSession.Instance?.Registered != true)
      {
        NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        return;
      }

      var inv = _block?.InputInventory as MyInventory;
      if (inv == null)
        return;

      if (inv.Constraint == null)
      {
        var constraint = new MyInventoryConstraint("AiEnabledConstraint", whitelist: true);
        inv.Constraint = constraint;
      }

      inv.Constraint.AddObjectBuilderType(typeof(MyObjectBuilder_Component));
    }
  }
}
