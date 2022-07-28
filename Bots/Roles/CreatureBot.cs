using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace AiEnabled.Bots.Roles
{
  public class CreatureBot : EnemyBotBase
  {
    public CreatureBot(IMyCharacter bot, GridBase gridBase) : base(bot, 10, 15, gridBase)
    {
      Behavior = new CreatureBehavior(this);

      _ticksBetweenAttacks = 150;
      _blockDamagePerSecond = 175;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);

      CanUseSpaceNodes = RequiresJetpack;
      CanUseAirNodes = RequiresJetpack;
      CanUseLadders = false;
      CanUseSeats = false;
      CanDamageGrid = false;

      var subtype = bot.Definition.Id.SubtypeName;

      if (subtype.IndexOf("spider", StringComparison.OrdinalIgnoreCase) >= 0)
        subtype = "SpaceSpider";
      else if (subtype.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0)
        subtype = "Wolf";

      var botDef = new MyDefinitionId(_animalBotType, subtype);
      var agentDef = MyDefinitionManager.Static.GetBotDefinition(botDef) as MyAgentDefinition;
        
      if (!string.IsNullOrWhiteSpace(agentDef?.AttackSound))
      {
        _attackSounds.Add(new MySoundPair(agentDef.AttackSound));
        _attackSoundStrings.Add(agentDef.AttackSound);
      }

      var cDef = bot.Definition as MyCharacterDefinition;
      
      if (!string.IsNullOrWhiteSpace(cDef?.DeathSoundName))
      {
        _deathSound = new MySoundPair(cDef.DeathSoundName);
        _deathSoundString = cDef.DeathSoundName;
      }
    }
  }
}
