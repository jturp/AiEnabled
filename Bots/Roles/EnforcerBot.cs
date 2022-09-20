using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class EnforcerBot : NeutralBotBase
  {
    public EnforcerBot(IMyCharacter bot, GridBase gridBase, AiSession.ControlInfo ctrlInfo, string toolType = null) : base(bot, 12, 20, gridBase, ctrlInfo)
    {
      Behavior = new NeutralBehavior(this);

      if (toolType == null)
      {
        var rand = MyUtils.GetRandomInt(100);

        if (rand >= 95)
          toolType = "BasicHandHeldLauncherItem";
        else if (rand >= 50)
          toolType = "RapidFireAutomaticRifleItem";
        else
          toolType = "SemiAutoPistolItem";
      }

      _shotAngleDeviationTan = (float)Math.Tan(MathHelper.ToRadians(2.5f));
      ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolType));

      if (ToolDefinition != null)
        AiSession.Instance.Scheduler.Schedule(AddWeapon);
        //MyAPIGateway.Utilities.InvokeOnGameThread(AddWeapon, "AiEnabled");
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
        }

        if (_shouldMove && Character.EquippedTool == null && ToolDefinition != null && !(Character.Parent is IMyCockpit))
        {
          var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
          controlEnt.SwitchToWeapon(ToolDefinition.PhysicalItemId);
        }
      }

      return true;
    }
  }
}
