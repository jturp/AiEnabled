using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Support;
using AiEnabled.Utilities;

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
  public class BruiserBot : EnemyBotBase
  {
    public BruiserBot(IMyCharacter bot, GridBase gridBase, AiSession.ControlInfo ctrlInfo) : base(bot, 15, 25, gridBase, ctrlInfo)
    {
      Behavior = new EnemyBehavior(this);

      _ticksBeforeDamage = 35;
      _ticksBetweenAttacks = 400;
      _blockDamagePerSecond = 360;
      _blockDamagePerAttack = _blockDamagePerSecond * 0.25f * (_ticksBetweenAttacks / 60f); // 4-punch combo, so divide by 4 per attack
      _allowedToSwitchWalk = true;

      _attackSounds.Add(new MySoundPair("DroneLoopMedium"));
      _attackSoundStrings.Add("DroneLoopMedium");
    }

    internal override void Attack()
    {
      if (_ticksSinceLastAttack < _ticksBetweenAttacks)
        return;

      _ticksSinceLastAttack = 0;
      _damageTicks = 0;
      DamagePending = true;

      Character.TriggerCharacterAnimationEvent("emote", true);
      Character.TriggerCharacterAnimationEvent("QuadPunch", true);
      PlaySound();
    }

    internal override void UpdateDamagePending()
    {
      ++_damageTicks;

      if (_damageTicks == 65 || _damageTicks == 79 || _damageTicks == 93 || _damageTicks == 107)
      {
        var damage = MyUtils.GetRandomFloat(_minDamage, _maxDamage) * 0.2f;
        DoDamage(damage);
      }

      if (_damageTicks >= 107)
        DamagePending = false;
    }
  }
}
