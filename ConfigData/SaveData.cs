using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using ProtoBuf;

namespace AiEnabled.ConfigData
{
  [ProtoContract]
  public class SaveData
  {
    [ProtoMember(1)] public int MaxBotsInWorld = 100;
    [ProtoMember(2)] public int MaxHelpersPerPlayer = 2;
    [ProtoMember(3)] public bool AllowRepairBot = true;
    [ProtoMember(4)] public bool AllowCombatBot = true;
    [ProtoMember(5)] public bool AllowScavengerBot = true;
    [ProtoMember(6)] public bool AllowCrewBot = true;
    [ProtoMember(34)] public bool DisableEnvironmentDamageForHelpers = true;
    [ProtoMember(35)] public bool DisableAsphyxiaDamageForBots = true;
    [ProtoMember(36)] public bool DisableTemperatureDamageForBots = true;
    [ProtoMember(37)] public bool DisableLowPressureDamageForBots = true;
    [ProtoMember(7)] public bool ObeyProjectionIntegrityForRepairs = true;
    [ProtoMember(8)] public bool DisableCharacterCollisionOnBotDeath = true;
    [ProtoMember(9)] public int MaxBotProjectileDistance = 150;
    [ProtoMember(10)] public int MaxBotHuntingDistanceEnemy = 300;
    [ProtoMember(11)] public int MaxBotHuntingDistanceFriendly = 150;
    [ProtoMember(12)] public long MaxPathfindingTimeInSeconds = 30;
    [ProtoMember(13)] public float PlayerWeaponDamageModifier = 1;
    [ProtoMember(14)] public float BotWeaponDamageModifier = 1;
    [ProtoMember(15)] public bool AllowBotMusic = false;
    [ProtoMember(16)] public bool AllowNeutralsToFly = true;
    [ProtoMember(17)] public bool AllowEnemiesToFly = true;
    [ProtoMember(18)] public bool AllowHelpersToFly = true;
    [ProtoMember(19)] public bool AllowNeutralTargets = false;
    [ProtoMember(20)] public bool AllowIdleMovement = true;
    [ProtoMember(21)] public bool AllowIdleMapTransitions = true;
    [ProtoMember(22)] public bool AllowHelmetVisorChanges = true;
    [ProtoMember(23)] public bool EnforceWalkingOnPatrol = false;
    [ProtoMember(24)] public bool EnforceGroundPathingFirst = false;
    [ProtoMember(25)] public bool IgnoreArmorDeformation = false;
    [ProtoMember(26)] public bool AllowHelperTokenBuilding = true;
    [ProtoMember(27)] public bool IncreaseNodeWeightsNearWeapons = false;
    [ProtoMember(28)] public bool AllowScavengerDigging = true;
    [ProtoMember(29)] public bool AllowScavengerLooting = true;
    [ProtoMember(30)] public bool AllowRepairBotGathering = true;
    [ProtoMember(31)] public bool AllowNeutralsToOpenDoors = true;
    [ProtoMember(32)] public bool ChargePlayersForBotUpkeep = false;
    [ProtoMember(33)] public int BotUpkeepTimeInMinutes = 30;

    [XmlArrayItem("Subtype", typeof(string))]
    [ProtoIgnore] public List<string> InventoryItemsToKeep = null;

    [XmlArrayItem("Subtype", typeof(string))]
    [ProtoIgnore] public List<string> AllowedHelperSubtypes = null;

    [XmlArrayItem("Subtype", typeof(string))]
    [ProtoIgnore] public List<string> AllowedBotSubtypes = null;

    [XmlArrayItem("Role", typeof(string))]
    [ProtoIgnore] public List<string> AllowedBotRoles = null;

    [XmlArrayItem("Subtype", typeof(string))]
    [ProtoIgnore] public List<string> AllHumanSubtypes = null;

    [XmlArrayItem("Subtype", typeof(string))]
    [ProtoIgnore] public List<string> AllNonHumanSubtypes = null;

    [ProtoIgnore] public List<HelperData> PlayerHelperData = new List<HelperData>();

    public SaveData() { }
  }
}
 