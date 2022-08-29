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

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using SpaceEngineers.Game.ModAPI;

using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace AiEnabled.Bots.Roles.Helpers
{
  public class CombatBot : FriendlyBotBase
  {
    public CombatBot(IMyCharacter bot, GridBase gridBase, long ownerId, AiSession.ControlInfo ctrlInfo, string toolType = null) : base(bot, 3, 10, gridBase, ownerId, ctrlInfo)
    {
      BotType = AiSession.BotType.Combat;
      Behavior = new FriendlyBehavior(this);
      var toolSubtype = toolType ?? "RapidFireAutomaticRifleItem";
      ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtype));

      _sideNodeWaitTime = 30;
      _ticksSinceFoundTarget = 241;
      _ticksBetweenAttacks = 150;
      _blockDamagePerSecond = 200;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);
      _shotAngleDeviationTan = (float)Math.Tan(MathHelper.ToRadians(1.5f));

      // testing only
      //CanUseAirNodes = CanUseSpaceNodes = false;
      //JetpackEnabled = false;

      _attackSounds.Add(new MySoundPair("DroneLoopSmall"));
      _attackSoundStrings.Add("DroneLoopSmall");
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
        if (_ticksSinceFirePacket > TicksBetweenProjectiles * 20)
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

      if (HasWeaponOrTool)
      {
        var gun = Character?.EquippedTool as IMyHandheldGunObject<MyGunBase>;
        if (gun != null)
        {
          var ammoCount = gun.CurrentMagazineAmount;
          if (ammoCount <= 0 && !MyAPIGateway.Session.CreativeMode && !MyAPIGateway.Session.SessionSettings.InfiniteAmmo)
          {
            var ammoType = gun.GunBase.CurrentAmmoMagazineDefinition;
            var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
            controlEnt?.SwitchToWeapon(null);
            HasWeaponOrTool = HasLineOfSight = false;

            if (Owner != null)
            {
              var pkt = new MessagePacket($"{Character.Name} is out of ammunition ({ammoType.DisplayNameText})!");
              AiSession.Instance.Network.SendToPlayer(pkt, Owner.SteamUserId);
            }

            if (_pathCollection != null)
              _pathCollection.Dirty = true;
          }
          else if (Target.HasTarget && !(Character.Parent is IMyCockpit))
            MyAPIGateway.Utilities.InvokeOnGameThread(CheckLineOfSight, "AiEnabled");
          else
            HasLineOfSight = false;
        }
        else
          HasLineOfSight = false;
      }

      return true;
    }
  }
}
