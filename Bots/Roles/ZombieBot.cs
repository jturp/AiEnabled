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
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class ZombieBot : EnemyBotBase
  {
    EnemyParticleInfo _particleInfo;
    MyConsumableItemDefinition _consumable;

    public ZombieBot(IMyCharacter bot, GridBase gridBase, AiSession.ControlInfo ctrlInfo) : base(bot, 1, 1, gridBase, ctrlInfo)
    {
      Behavior = new ZombieBehavior(this);

      _ticksBeforeDamage = 63;
      _ticksBetweenAttacks = 200;
      _blockDamagePerSecond = 150;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);
      _deathSound = new MySoundPair("ZombieDeath");
      _deathSoundString = "ZombieDeath";

      CanUseSpaceNodes = RequiresJetpack;
      CanUseAirNodes = RequiresJetpack;
      CanUseLadders = false;
      CanUseSeats = false;

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
      try
      {
        if (!BugZapped && _particleInfo?.Bot != null)
        {
          var packet = new ParticlePacket(_particleInfo.Bot.EntityId, _particleInfo.Type, remove: true);

          if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
            AiSession.Instance.Network.RelayToClients(packet);

          if (MyAPIGateway.Session.Player != null)
            packet.Received(AiSession.Instance.Network);
        }

        _consumable = null;
        _particleInfo?.Close();
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in ZombieBot.CleanUp: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
      finally
      {
        base.CleanUp(cleanConfig, removeBot);
      }
    }

    internal override bool DoDamage(float amount = 0)
    {
      bool result = base.DoDamage(amount);

      if (result)
      {
        var character = Target.Entity as IMyCharacter;
        var resistCheck = MyUtils.GetRandomInt(0, 100);
        bool resist = resistCheck < 10;

        string msg = null;
        if (resist)
        {
          msg = "You resisted the zombie's venomous touch!";
        }
        else if (_consumable != null)
        {
          msg = "You have been poisoned!";
          var statComp = character.Components.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
          statComp?.Consume(1, _consumable);
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
          var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Zombie);
          AiSession.Instance.Network.RelayToClients(packet);
        }

        _particleInfo = new EnemyParticleInfo(Character, "ZombieGas");
      }

      if (MyAPIGateway.Session.Player != null)
        _particleInfo.Update();
  
      return base.Update();
    }
  }
}
