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
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Game.ObjectBuilders.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class GrinderBot : EnemyBotBase
  {
    public GrinderBot(IMyCharacter bot, GridBase gridBase, AiSession.ControlInfo ctrlInfo, string toolType = null) : base(bot, 5, 15, gridBase, ctrlInfo)
    {
      Behavior = new ZombieBehavior(this);
      var toolSubtype = toolType ?? "AngleGrinder2Item";
      ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtype));

      _ticksBeforeDamage = 63;
      _ticksBetweenAttacks = 200;
      _deathSound = new MySoundPair("ZombieDeath");
      _deathSoundString = "ZombieDeath";
      _blockDamagePerSecond = 125;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);
      _allowedToSwitchWalk = true;

      _attackSounds.Add(new MySoundPair("ZombieAttack001"));
      _attackSounds.Add(new MySoundPair("ZombieAttack002"));
      _attackSounds.Add(new MySoundPair("ZombieAttack003"));
      _attackSounds.Add(new MySoundPair("ZombieAttack004"));
      _attackSoundStrings.Add("ZombieAttack001");
      _attackSoundStrings.Add("ZombieAttack002");
      _attackSoundStrings.Add("ZombieAttack003");
      _attackSoundStrings.Add("ZombieAttack004");

      MyAPIGateway.Utilities.InvokeOnGameThread(AddWeapon, "AiEnabled");
    }

    internal override void Attack()
    {
      if (((byte)MySessionComponentSafeZones.AllowedActions & 16) == 0)
        return;

      var tool = Character.EquippedTool as IMyAngleGrinder;
      if (tool != null)
      {
        if (_ticksSinceLastAttack >= 60)
        {
          _ticksSinceLastAttack = 0;

          if (!FireWeapon())
            return;
        }

        var tgtEnt = Target.Entity as IMyCharacter;
        var seat = Target.Entity as IMyCockpit;
        if (tgtEnt != null)
        {
          BotBase botTarget;
          bool isPlayer;

          if (tgtEnt.Parent is IMyShipController)
          {
            var p = MyAPIGateway.Players.GetPlayerControllingEntity(tgtEnt.Parent);
            isPlayer = p != null && AiSession.Instance.Players.ContainsKey(p.IdentityId);
          }
          else
          {
            isPlayer = AiSession.Instance.Players.ContainsKey(tgtEnt.ControllerInfo.ControllingIdentityId);
          }

          var damage = 0.2f;
          if (isPlayer)
          {
            damage *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
          }
          else if (AiSession.Instance.Bots.TryGetValue(tgtEnt.EntityId, out botTarget) && botTarget != null)
          {
            var nomad = botTarget as NomadBot;
            if (nomad != null && nomad.Target.Entity == null)
            {
              nomad.SetHostile(Character);
            }
            else if (botTarget.Owner != null)
            {
              damage *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
            }
          }

          tgtEnt.DoDamage(damage, MyDamageType.Grind, true);
        }
        else if (seat != null)
        {
          var damage = 5f * AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
          var casterComp = tool.Components?.Get<MyCasterComponent>();
          if (casterComp != null && casterComp.HitBlock == null)
          {
            casterComp.SetPointOfReference(seat.CenterOfMass);
            seat.SlimBlock.DoDamage(damage, MyDamageType.Grind, true);
          }
        }
      }
      else if (_ticksSinceLastAttack >= _ticksBetweenAttacks)
      {
        _ticksSinceLastAttack = 0;
        _damageTicks = 0;
        DamagePending = true;

        Character.TriggerCharacterAnimationEvent("Attack", true);
        PlaySound();
      }
    }

    internal override bool FireWeapon()
    {
      var gun = Character.EquippedTool as IMyHandheldGunObject<MyDeviceBase>;
      if (gun == null)
        return false;

      if (!MySessionComponentSafeZones.IsActionAllowed(Character.WorldAABB.Center, Utilities.Extensions.CastHax(MySessionComponentSafeZones.AllowedActions, 16)))
        return false;

      var targetEnt = Target.Entity as IMyEntity;
      if (targetEnt == null)
        return false;

      if (MyAPIGateway.Multiplayer.MultiplayerActive)
      {
        var packet = new WeaponFirePacket(Character.EntityId, targetEnt.EntityId, 0.2f, 0f, null, TicksBetweenProjectiles, 100, true, false, false);
        AiSession.Instance.Network.RelayToClients(packet);
      }

      AiSession.Instance.StartWeaponFire(Character.EntityId, targetEnt.EntityId, 0.2f, 0f, null, TicksBetweenProjectiles, 100, true, false, false);
      return true;
    }
  }
}
