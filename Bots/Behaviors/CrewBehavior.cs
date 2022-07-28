using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiEnabled.Bots.Behaviors
{
  public class CrewBehavior : BotBehavior
  {
    public CrewBehavior(BotBase bot) : base(bot)
    {
      Actions.AddList(AiSession.Instance.CrewAnimations);

      // TODO: Add crew sounds ??
    }
  }
}
