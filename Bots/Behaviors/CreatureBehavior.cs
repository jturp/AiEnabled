using Sandbox.Definitions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;

namespace AiEnabled.Bots.Behaviors
{
  public class CreatureBehavior : BotBehavior
  {
    public CreatureBehavior(BotBase bot) : base(bot)
    {
      var cDef = bot?.Character?.Definition as MyCharacterDefinition;
      
      if (!string.IsNullOrWhiteSpace(cDef?.PainSoundName))
      {
        PainSounds.Add(cDef.PainSoundName);
      }
    }
  }
}
