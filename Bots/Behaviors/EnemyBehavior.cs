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
    public EnemyBehavior(BotBase bot) : base(bot)
    {
      Taunts.Add("YourTimeIsCome");
      Taunts.Add("EatLead");
      Taunts.Add("Taunt_MoreChallenge");
      Taunts.Add("AutoAimEngaged");
      Taunts.Add("Earthlings");

      //Actions.Add("");
    }
  }
}
