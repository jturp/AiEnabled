using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;

namespace AiEnabled.Bots.Behaviors
{
  public class WorkerBehavior : BotBehavior
  {
    public WorkerBehavior(BotBase bot) : base(bot)
    {
      Songs.Add("CallItTheBlues");

      Phrases.Add("RobotChirps001");
      Phrases.Add("RobotChirps002");
      Phrases.Add("RobotChirps003");
      //Phrases.Add("Taunt_WannaDance");
      //Phrases.Add("HelloHumanoid");

      //Actions.Add("");
    }
  }
}
