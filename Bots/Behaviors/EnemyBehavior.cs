using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;

namespace AiEnabled.Bots.Behaviors
{
  public class EnemyBehavior : BotBehavior
  {
    public EnemyBehavior(IMyCharacter bot) : base(bot)
    {
      Phrases.Add("YourTimeIsCome");
      Phrases.Add("EatLead");
      Phrases.Add("Taunt_MoreChallenge"); 
      Phrases.Add("AutoAimEngaged");
      Phrases.Add("Earthlings");

      //Actions.Add("");
    }
  }
}
