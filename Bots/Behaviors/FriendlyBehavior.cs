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
    public FriendlyBehavior(BotBase bot) : base(bot)
    {
      Songs.Add("BadToTheBone");

      Taunts.Add("TakeThatYouFilth");
      Taunts.Add("HowIsTheTasting");
      Taunts.Add("Enemy");
      //Taunts.Add("CriticalBatteries");

      //Actions.Add("");
    }
  }
}
