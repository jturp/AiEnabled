using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;

namespace AiEnabled.Bots.Behaviors
{
  public class NeutralBehavior : BotBehavior
  {
    public NeutralBehavior(BotBase bot) : base(bot)
    {
      Actions.AddList(AiSession.Instance.Animations);
    }
  }
}
