using AiEnabled.Bots.Behaviors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;

namespace AiEnabled.Bots.Behaviors
{
  public class ZombieBehavior : BotBehavior
  {
    public ZombieBehavior(IMyCharacter bot) : base(bot)
    {
      Phrases.Add("ZombieGroan001");
      Phrases.Add("ZombieGroan002");
      Phrases.Add("ZombieGroan003");
      Phrases.Add("ZombieGroan004");
      Phrases.Add("ZombieGroan005");
    }
  }
}
