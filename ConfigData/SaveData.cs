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
    public int MaxBotProjectileDistance = 150;
    public int MaxBotHuntingDistanceEnemy = 300;
    public int MaxBotHuntingDistanceFriendly = 150;
    public float PlayerDamageModifier = 1;
    public float BotDamageModifier = 1;
    public bool AllowBotMusic;
    public bool AllowEnemiesToFly;
    public bool AllowNeutralTargets;
    public bool EnforceWalkingOnPatrol;
    public bool EnforceGroundNodesFirst;
    public List<HelperData> PlayerHelperData = new List<HelperData>();
    public List<FactionData> FactionPairings = new List<FactionData>();

    public SaveData() { }
  }
}
