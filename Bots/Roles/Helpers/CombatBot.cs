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
      _ticksBetweenAttacks = 180;
      _blockDamagePerSecond = 200;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);
      _shotAngleDeviationTan = (float)Math.Tan(MathHelper.ToRadians(1.5f));

      // testing only
      //CanUseAirNodes = CanUseSpaceNodes = false;
      //JetpackEnabled = false;

      _attackSounds.Add(new MySoundPair("DroneLoopSmall"));
      _attackSoundStrings.Add("DroneLoopSmall");

      if (AiSession.Instance.WcAPILoaded)
      {
        AiSession.Instance.WcAPI.ShootRequestHandler(Character.EntityId, false, WCShootCallback);
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
          var ammoCount = _wcWeaponMagsLeft ?? gun.CurrentMagazineAmount;
          var infiniteAmmo = MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.SessionSettings.InfiniteAmmo;
          var inventory = Character.GetInventory();

          if (ammoCount <= 0 && !infiniteAmmo)
          {
            var weaponDefinition = ToolDefinition.PhysicalItemId;
            string ammoSubtype = null;

            List<MyTuple<int, MyTuple<MyDefinitionId, string, string, bool>>> magList;
            if (AiSession.Instance.WcAPILoaded && AiSession.Instance.NpcSafeCoreWeaponMagazines.TryGetValue(weaponDefinition, out magList))
            {
              for (int i = 0; i < magList.Count; i++)
              {
                var mag = magList[i];
                var ammo = mag.Item2.Item1;
                var amount = inventory.GetItemAmount(ammo);

                if (amount > 0)
                {
                  ammoSubtype = ammo.SubtypeName;
                  ammoCount = 1;

                  gun.CurrentAmmunition = 1;

                  if (gun.GunBase.CurrentAmmoDefinition.Id != ammo)
                    gun.GunBase.SwitchAmmoMagazine(ammo);

                  gun.Reload();

                  AiSession.Instance.WcAPI.SetMagazine((MyEntity)gun, mag.Item1, ammo, true);
                  break;
                }
              }
            }
            else if (gun.GunBase.SwitchAmmoMagazineToFirstAvailable())
            {
              ammoCount = gun.CurrentAmmunition;
              ammoSubtype = gun.GunBase.CurrentAmmoDefinition.Id.SubtypeName;
              gun.Reload();
            }

            _wcShotFired = false;
          }

          if (ammoCount <= 0 && !infiniteAmmo)
          {
            MyAmmoMagazineDefinition ammoType;

            List<MyTuple<int, MyTuple<MyDefinitionId, string, string, bool>>> magList;
            if (AiSession.Instance.WcAPILoaded && AiSession.Instance.NpcSafeCoreWeaponMagazines.TryGetValue(ToolDefinition.PhysicalItemId, out magList))
            {
              var ammoDef = magList[0].Item2.Item1;
              ammoType = MyDefinitionManager.Static.GetAmmoMagazineDefinition(ammoDef);
            }
            else
            {
              ammoType = gun.GunBase.CurrentAmmoMagazineDefinition;
            }

            var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;

            gun.OnControlReleased();
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
          {
            AiSession.Instance.Scheduler.Schedule(CheckLineOfSight);
          }
          else
          {
            HasLineOfSight = false;
          }
        }
        else
        {
          HasLineOfSight = false;
        }
      }

      return true;
    }
  }
}
