using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Networking;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class SoldierBot : EnemyBotBase
  {
    public SoldierBot(IMyCharacter bot, GridBase gridBase, AiSession.ControlInfo ctrlInfo, string toolType = null) : base(bot, 5, 15, gridBase, ctrlInfo)
    {
      Behavior = new EnemyBehavior(this);
      var toolSubtype = toolType ?? "RapidFireAutomaticRifleItem";
      ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtype));

      _sideNodeWaitTime = 60;
      _ticksSinceFoundTarget = 241;
      _ticksBetweenAttacks = 200;
      _blockDamagePerSecond = 175;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);
      _shotAngleDeviationTan = (float)Math.Tan(MathHelper.ToRadians(1.5f));
      _allowedToSwitchWalk = true;

      _attackSounds.Add(new MySoundPair("Enemy"));
      _attackSoundStrings.Add("Enemy");

      if (ToolDefinition != null)
      {
        AiSession.Instance.Scheduler.Schedule(AddWeapon);

        if (AiSession.Instance.WcAPILoaded)
        {
          AiSession.Instance.WcAPI.ShootRequestHandler(Character.EntityId, false, WCShootCallback);
        }
      }
    }

    internal override bool Update()
    {
      if (!base.Update())
        return false;

      if (_sideNodeTimer < byte.MaxValue)
        ++_sideNodeTimer;

      if (_firePacketSent)
      {
        _ticksSinceFirePacket++;
        if (_ticksSinceFirePacket > TicksBetweenProjectiles * 25)
          _firePacketSent = false;
      }

      if (WaitForLOSTimer)
      {
        ++_lineOfSightTimer;
        if (_lineOfSightTimer > 100)
        {
          _lineOfSightTimer = 0;
          WaitForLOSTimer = false;
        }
      }

      return true;
    }
  }
}
