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
    public ZombieBehavior(BotBase bot) : base(bot)
    {
      Taunts.Add("ZombieGroan001");
      Taunts.Add("ZombieGroan002");
      Taunts.Add("ZombieGroan003");
      Taunts.Add("ZombieGroan004");
      Taunts.Add("ZombieGroan005");
    }
  }
}
