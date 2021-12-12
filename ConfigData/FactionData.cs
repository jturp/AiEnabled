using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiEnabled.ConfigData
{
  public class FactionData
  {
    public long PlayerFactionId;
    public long BotFactionId;

    public FactionData() { }

    public FactionData(long playerFactionId, long botFactionId)
    {
      PlayerFactionId = playerFactionId;
      BotFactionId = botFactionId;
    }    
  }
}
