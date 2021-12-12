using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiEnabled.ConfigData
{
  public class SaveData
  {
    public int MaxBotsInWorld = 100;
    public int MaxHelpersPerPlayer = 2;
    public bool AllowBotMusic;
    public List<HelperData> PlayerHelperData = new List<HelperData>();
    public List<FactionData> FactionPairings = new List<FactionData>();

    public SaveData() { }
  }
}
