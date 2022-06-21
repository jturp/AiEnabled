using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;

namespace AiEnabled.Bots.Behaviors
{
  public class ScavengerBehavior : BotBehavior
  {
    public ScavengerBehavior(BotBase bot) : base(bot)
    {
      Phrases.Add("RoboDogBark001");
      Phrases.Add("RoboDogBark002");
      Taunts.Add("RoboDogBark003");
      Taunts.Add("RoboDogSnarl001");
      //Phrases.Add("RoboDogPant001");
      //Phrases.Add("RoboDogSniff001");

      Actions.Add("RoboDog_Sitting");
      Actions.Add("RoboDog_Digging");
      Actions.Add("RoboDog_Spinning");
    }
  }
}
