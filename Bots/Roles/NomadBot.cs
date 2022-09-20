using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class NomadBot : NeutralBotBase
  {
    public NomadBot(IMyCharacter bot, GridBase gridBase, AiSession.ControlInfo ctrlInfo, string toolType = null) : base(bot, 7, 15, gridBase, ctrlInfo)
    {
      Behavior = new NeutralBehavior(this);

      if (!string.IsNullOrWhiteSpace(toolType))
      {
        ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolType));

        if (ToolDefinition != null)
          AiSession.Instance.Scheduler.Schedule(AddWeapon);
          //MyAPIGateway.Utilities.InvokeOnGameThread(AddWeapon, "AiEnabled");
      }
    }

    internal override bool Update()
    {
      if (!base.Update())
        return false;

      if (_tickCount % 100 == 0)
      {
        if (Target.Entity != null && Target.PositionsValid)
        {
          if (Vector3D.DistanceSquared(Character.WorldAABB.Center, Target.CurrentActualPosition) > 150 * 150)
            Target.RemoveTarget();
        }

        if (Target.Entity == null || Target.IsDestroyed())
        {
          if (BotInfo.IsRunning)
            Character.SwitchWalk();

          if (HasWeaponOrTool)
          {
            var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
            controlEnt?.SwitchToWeapon(null);
            HasWeaponOrTool = false;
            HasLineOfSight = false;
          }
        }
      }

      return true;
    }
  }
}
