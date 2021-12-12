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
    const float WELDER_AMOUNT_PER_SECOND = 1f;
    const float WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED = 0.6f;
    const float TOOL_COOLDOWN_MS = 250;

    float _weldSpeedMultiplier = 1f;
    bool _multiplierChecked;

    public float WeldAmount => MyAPIGateway.Session.WelderSpeedMultiplier * WELDER_AMOUNT_PER_SECOND * _weldSpeedMultiplier * TOOL_COOLDOWN_MS * 0.001f;
    public float BoneFixAmount => WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED * TOOL_COOLDOWN_MS * 0.001f;

    public void CheckMultiplier(MyDefinitionId toolDefinition)
    {
      if (_multiplierChecked)
        return;

      _multiplierChecked = true;
      var handItemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(toolDefinition);
      var toolBaseDef = handItemDef as MyEngineerToolBaseDefinition;
      _weldSpeedMultiplier = toolBaseDef?.SpeedMultiplier ?? 1f;
    }
  }
}
