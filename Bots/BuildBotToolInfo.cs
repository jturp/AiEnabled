using AiEnabled.Bots.Roles.Helpers;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;

namespace AiEnabled.Bots
{
  public class BuildBotToolInfo
  {
    // from the source files
    const float WELDER_AMOUNT_PER_SECOND = 1f / 30f;
    const float GRINDER_AMOUNT_PER_SECOND = 2f / 30f;
    const float WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED = 0.6f;
    const float TOOL_COOLDOWN_MS = 250;

    float _toolSpeedMultiplier = 1f;

    public float WeldAmount => MyAPIGateway.Session.WelderSpeedMultiplier * WELDER_AMOUNT_PER_SECOND * _toolSpeedMultiplier * TOOL_COOLDOWN_MS * 0.001f;
    public float GrindAmount => MyAPIGateway.Session.GrinderSpeedMultiplier * GRINDER_AMOUNT_PER_SECOND * _toolSpeedMultiplier * TOOL_COOLDOWN_MS * 0.001f;
    public float BoneFixAmount => WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED * TOOL_COOLDOWN_MS * 0.001f;

    public void CheckMultiplier(MyHandItemDefinition toolDef, out RepairBot.BuildMode buildMode)
    {
      var def = toolDef?.PhysicalItemId ?? new MyDefinitionId();
      var handItemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(def);
      var toolBaseDef = handItemDef as MyEngineerToolBaseDefinition;

      if (handItemDef.Id.TypeId == typeof(MyObjectBuilder_AngleGrinder))
        buildMode = RepairBot.BuildMode.Grind;
      else if (handItemDef.Id.TypeId == typeof(MyObjectBuilder_Welder))
        buildMode = RepairBot.BuildMode.Weld;
      else
        buildMode = BotBase.BuildMode.None;

      _toolSpeedMultiplier = toolBaseDef?.SpeedMultiplier ?? 1f;
    }
  }
}
