using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;

namespace AiEnabled.Bots.Behaviors
{
  public class FriendlyBehavior : BotBehavior
  {
    public FriendlyBehavior(IMyCharacter bot) : base(bot)
    {
      Songs.Add("BadToTheBone");

      Phrases.Add("TakeThatYouFilth");
      Phrases.Add("HowIsTheTasting");
      Phrases.Add("Enemy");
      //Phrases.Add("CriticalBatteries");

      //Actions.Add("");
    }
  }
}
