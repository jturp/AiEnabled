using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Networking;
using AiEnabled.Particles;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class GhostBot : EnemyBotBase
  {
    EnemyParticleInfo _particleInfo;
    MyConsumableItemDefinition _consumable;

    string[] _shiverSounds = new string[3]
    {
      "PlayerShiver001",
      "PlayerShiver002",
      "PlayerShiver003"
    };

    public GhostBot(IMyCharacter bot, GridBase gridBase, AiSession.ControlInfo ctrlInfo) : base(bot, 1, 1, gridBase, ctrlInfo)
    {
      Behavior = new ZombieBehavior(this);

      _ticksBeforeDamage = 35; // 35 for police bot punch, 63 for the zombie slap
      _ticksBetweenAttacks = 200;
      _blockDamagePerSecond = 175;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);

      CanUseSpaceNodes = RequiresJetpack;
      CanUseAirNodes = RequiresJetpack;

      _deathSound = new MySoundPair("ZombieDeath");
      _deathSoundString = "ZombieDeath";

      _attackSounds.Add(new MySoundPair("ZombieAttack001"));
      _attackSounds.Add(new MySoundPair("ZombieAttack002"));
      _attackSounds.Add(new MySoundPair("ZombieAttack003"));
      _attackSounds.Add(new MySoundPair("ZombieAttack004"));
      _attackSoundStrings.Add("ZombieAttack001");
      _attackSoundStrings.Add("ZombieAttack002");
      _attackSoundStrings.Add("ZombieAttack003");
      _attackSoundStrings.Add("ZombieAttack004");

      _consumable = new MyConsumableItemDefinition
      {
        Stats = new List<MyConsumableItemDefinition.StatValue>() { new MyConsumableItemDefinition.StatValue("Health", -0.01f, 5) }
      };
    }

    internal override void CleanUp(bool cleanConfig = false, bool removeBot = true)
    {
      if (!BugZapped && _particleInfo?.Bot != null)
      {
        var packet = new ParticlePacket(_particleInfo.Bot.EntityId, _particleInfo.Type, remove: true);

        if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
          AiSession.Instance.Network.RelayToClients(packet);

        if (MyAPIGateway.Session.Player != null)
          packet.Received(AiSession.Instance.Network);
      }

      _shiverSounds = null;
      _consumable = null;
      _particleInfo?.Close();
      base.CleanUp(cleanConfig, removeBot);
    }

    internal override bool DoDamage(float amount = 0)
    {
      bool result = base.DoDamage(amount);

      if (result)
      {
        var character = Target.Entity as IMyCharacter;
        var resistCheck = MyUtils.GetRandomInt(0, 100);
        bool resist = resistCheck < 10;

        string msg;
        if (resist)
        {
          msg = "You resisted the ghost's icy touch!";
        }
        else
        {
          if (_consumable != null)
          {
            var statComp = character.Components.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
            statComp?.Consume(1, _consumable);
          }

          var ch = Target.Entity as IMyCharacter;
          if (ch?.EquippedTool != null && resistCheck > 92)
          {
            msg = "Your tool falls from your frostbitten fingers!";

            var gun = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
            gun?.OnControlReleased();

            var controlEnt = ch as Sandbox.Game.Entities.IMyControllableEntity;
            controlEnt?.SwitchToWeapon(null);
          }
          else
          {
            msg = "You are freezing!";
          }

          var num = MyUtils.GetRandomInt(0, _shiverSounds.Length);
          var sound = _shiverSounds[num];
          PlaySoundServer(sound, character.EntityId);
        }

        var targetId = Target?.Player?.SteamUserId;
        if (!string.IsNullOrEmpty(msg) && targetId.HasValue)
        {
          if (targetId == MyAPIGateway.Multiplayer.MyId)
          {
            AiSession.Instance.ShowMessage(msg);
          }
          else if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
          {
            var packet = new MessagePacket(msg);
            AiSession.Instance.Network.SendToPlayer(packet, targetId.Value);
          }
        }
      }

      return result;
    }

    internal override bool Update()
    {
      if (BugZapped)
        return false;

      if (_particleInfo == null)
      {
        if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
        {
          var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Ghost);
          AiSession.Instance.Network.RelayToClients(packet);
        }

        _particleInfo = new EnemyParticleInfo(Character, "GhostIce");
      }

      if (MyAPIGateway.Session.Player != null)
        _particleInfo.Update();
  
      return base.Update();
    }
  }
}
