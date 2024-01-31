using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Collections;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

using VRageMath;
using AiEnabled.Ai.Support;
using AiEnabled.Bots;
using Sandbox.Game.Entities;
using AiEnabled.Support;
using VRage.Game.Entity.UseObject;
using VRage.Game.Entity;
using AiEnabled.Particles;
using AiEnabled.GameLogic;
using Sandbox.ModAPI;
using Sandbox.Definitions;
using Sandbox.Common.ObjectBuilders;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using MyItemInfo = VRage.Game.ModAPI.Ingame.MyItemInfo;
using VRage.Voxels;
using AiEnabled.API;
using AiEnabled.Parallel;
using VRage.Input;
using AiEnabled.ConfigData;
using VRage.Utils;
using VRage;
using System.Runtime.ConstrainedExecution;

namespace AiEnabled
{
  public partial class AiSession
  {
    public HashSet<string> RobotSubtypes = new HashSet<string>()
    {
      "Police_Bot",
      "Drone_Bot",
      "Space_Skeleton",
      "Space_Zombie",
      "Boss_Bot",
      "Target_Dummy",
      "Ghost_Bot",
      "RoboDog"
    };

    public List<string> Animations = new List<string>()
    {
      "Wave", "Victory", "Thumb-Up", "FacePalm", "Angry", "AssistEnd",
      "Dance", "Charge", "ComeHereBaby", "DanceDisco1", "DanceDisco2",
      "LookingAround", "Stretching", "Whatever", "FingerGuns", "Yelling",
      "GotHit", "PointAggressive", "PointBack", "PointDown", "PointForward",
      "PointLeft", "PointRight", "Cold", "CheckWrist", "FYou", "Drunk",
      "AssistCome"
    };

    public List<string> CrewAnimations = new List<string>()
    {
      "FacePalm", "LookingAround", "Stretching", "Yelling",
      "PointAggressive", "PointBack", "PointDown", "PointForward",
      "PointLeft", "PointRight", "CheckWrist", "AssistCome"
    };

    public List<string> HelperAnimations = new List<string>()
    {
      "Wave", "Victory", "Thumb-Up", "Stretching", "CheckWrist", "FacePalm", "FingerGuns"
    };

    public List<string> BotFactionTags = new List<string>()
    {
      "BINC", "SCDS", "OTFI", "UNIT", "BLBT", "LEGN", "OBSD", "FLWR",
      "AZRE", "RSBC", "ENGR", "MDFR", "ALPB", "DMGD", "BTTL", "MCHB",
      "PRME", "PREN", "SENT", "JKHP", "RCDR", "OPER", "MTLM", "CRTG",
      "FIBR", "PCKP", "FGHT", "FUTR", "CLBT", "BLDR", "CLTR", "OBSR",
    };

    public Vector3I[] DiagonalDirections = new Vector3I[]
    {
      new Vector3I(-1, 0, -1),
      new Vector3I(1, 0, -1),
      new Vector3I(-1, 0, 1),
      new Vector3I(1, 0, 1),
    };

    public Vector3I[] VoxelMovementDirections = new Vector3I[]
    {
      new Vector3I(0, 1, 1),
      new Vector3I(0, 1, -1),
      new Vector3I(0, -1, 1),
      new Vector3I(0, -1, -1),
      new Vector3I(1, 1, 0),
      new Vector3I(-1, 1, 0),
      new Vector3I(1, -1, 0),
      new Vector3I(-1, -1, 0),
    };

    public Vector3I[] CardinalDirections = new Vector3I[]
    {
      Vector3I.Right,
      Vector3I.Left,
      Vector3I.Backward,
      Vector3I.Forward,
      Vector3I.Up,
      Vector3I.Down,
    };

    public Vector3I[] BlockedVoxelEdges = new Vector3I[]
    {
      new Vector3I(1, 1, 1),
      new Vector3I(1, 1, -1),
      new Vector3I(-1, 1, 1),
      new Vector3I(-1, 1, -1),
      new Vector3I(1, -1, 1),
      new Vector3I(-1, -1, 1),
      new Vector3I(-1, -1, -1)
    };

    public MyDefinitionId[] VanillaTurretDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_LargeGatlingTurret)),
      new MyDefinitionId(typeof(MyObjectBuilder_LargeMissileTurret)),
    };

    public MyDefinitionId[] ButtonPanelDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "ButtonPanelLarge"),
      new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "LargeSciFiButtonPanel"),
      new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "LargeButtonPanelPedestal"),
      new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeControlPanelPedestal"),
    };

    public MyDefinitionId[] FlatWindowDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWindowSquare"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2Flat"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2FlatInv"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1Flat"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1FlatInv"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window2x3Flat"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window2x3FlatInv"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window3x3Flat"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window3x3FlatInv"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfWindow"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfWindowInv"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfWindowCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfWindowCornerInv"),
    };

    public MyDefinitionId[] PipeBlockDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockCylindricalColumn"),
      new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "LargeBlockConveyorSorterIndustrial"),
    };

    public MyDefinitionId[] LockerDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "LargeBlockLockerRoomCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "LargeBlockLockerRoom"),
      new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "LargeBlockLockers"),
      new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "LargeBlockWeaponRack"),
    };

    public MyDefinitionId[] BeamBlockDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlock"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockEnd"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockSlope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockHalfSlope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockSlope2x1Tip"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockSlope2x1Base"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockHalf"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockTJunction"),
    };

    public MyDefinitionId[] AngledWindowDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWindowEdge"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2Slope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1Slope"),
    };

    public MyDefinitionId[] FreightBlockDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Freight1"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Freight2"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Freight3"),
    };

    public MyDefinitionId[] RailingBlockDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingStraight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingDouble"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingHalfRight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingHalfLeft"),
      new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "PassengerSeatLarge"), // easier to add it here instead of creating a whole new array
    };

    public MyDefinitionId[] HalfWallDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeCoverWallHalf"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingHalfRight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingHalfLeft"),
    };

    public MyDefinitionId[] HalfBlockDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHalfArmorBlock"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyHalfArmorBlock"),
      //new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHalfSlopeArmorBlock"), // these are already in the half slope definitions
      //new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyHalfSlopeArmorBlock"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorHalfSlopeCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorHalfSlopeCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorHalfCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorHalfCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorHalfSlopedCorner"), // with a d
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorHalfSlopedCorner"), // with a d
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowWall"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowWallLeft"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowWallRight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_Concrete_Half_Block"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_ReinforcedConcrete_Half_Block"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_Concrete_Half_Block_Slope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_ReinforcedConcrete_Half_Block_Slope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "StorageShelf1"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "StorageShelf2"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "StorageShelf3"),
    };

    public MyDefinitionId[] ArmorPanelFullDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorPanelLight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorPanelHeavy"),
    };

    public MyDefinitionId[] ArmorPanelSlopeDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorSlopedPanelLight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedPanelLight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedPanelTipLight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorSlopedPanelHeavy"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedPanelHeavy"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedPanelTipHeavy"),
    };

    public MyDefinitionId[] ArmorPanelHalfSlopeDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfSlopedPanelLight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedPanelLightRight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedTipPanelLightRight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedPanelLightLeft"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedTipPanelLightLeft"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfSlopedPanelHeavy"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedPanelHeavyRight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedTipPanelHeavyRight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedPanelHeavyLeft"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedTipPanelHeavyLeft"),
    };

    public MyDefinitionId[] ArmorPanelHalfDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfPanelLight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfPanelHeavy"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorQuarterPanelLight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorQuarterPanelHeavy"),
    };

    public MyDefinitionId[] DecorativeBlockDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_Planter), "LargeBlockPlanters"),
      new MyDefinitionId(typeof(MyObjectBuilder_Kitchen), "LargeBlockKitchen"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockBarCounter"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockBarCounterCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockDeskChairless"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockDeskChairlessCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Shower"),
      new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockToilet"),
      new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockCouch"),
      new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockCouchCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockDesk"),
      new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockDeskCorner"),
      new MyDefinitionId(typeof(MyObjectBuilder_Jukebox), "Jukebox"),
      new MyDefinitionId(typeof(MyObjectBuilder_StoreBlock), "AtmBlock"),
      new MyDefinitionId(typeof(MyObjectBuilder_VendingMachine), "VendingMachine"),
      new MyDefinitionId(typeof(MyObjectBuilder_VendingMachine), "FoodDispenser"), // DNSK

      // Decorative Pack 3 and Warfare Evolution 
      new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeCrate"),
      new MyDefinitionId(typeof(MyObjectBuilder_Warhead), "LargeExplosiveBarrel"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBarrel"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussHalf"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussSloped"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussAngled"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloor"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloorHalf"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloorAngled"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloorAngledInverted"),
      //new MyDefinitionId(typeof(MyObjectBuilder_CryoChamber), "LargeBlockCryoRoom"),
      //new MyDefinitionId(typeof(MyObjectBuilder_CryoChamber), "LargeBlockInsetBed"),
      //new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockInsetPlantCouch"),
      new MyDefinitionId(typeof(MyObjectBuilder_CryoChamber), "LargeBlockHalfBed"),
      new MyDefinitionId(typeof(MyObjectBuilder_CryoChamber), "LargeBlockHalfBedOffset"),
    };

    public MyDefinitionId[] ConveyorFullBlockDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_Conveyor), "ConveyorTubeDuctT"),
      new MyDefinitionId(typeof(MyObjectBuilder_ConveyorConnector), "ConveyorTubeDuctCurved"),
      new MyDefinitionId(typeof(MyObjectBuilder_ConveyorConnector), "ConveyorTubeDuct"),
    };

    public MyDefinitionId[] ConveyorEndCapDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockConveyorPipeCap"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockConveyorCap"),
    };

    // Automatons DLC
    public MyDefinitionId[] AutomatonsFullBlockDefinitions = new MyDefinitionId[]
    {
      //new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AngledInteriorWallA"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AngledInteriorWallB"),
      new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LargeBlockInsetLight"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PipeWorkBlockA"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PipeWorkBlockB"),
      new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "AirVentFanFull"),
      new MyDefinitionId(typeof(MyObjectBuilder_EmotionControllerBlock), "EmotionControllerLarge"),
      new MyDefinitionId(typeof(MyObjectBuilder_FlightMovementBlock), "LargeFlightMovement"),
      new MyDefinitionId(typeof(MyObjectBuilder_DefensiveCombatBlock), "LargeDefensiveCombat"),
      new MyDefinitionId(typeof(MyObjectBuilder_OffensiveCombatBlock), "LargeOffensiveCombat"),
      new MyDefinitionId(typeof(MyObjectBuilder_PathRecorderBlock), "LargePathRecorderBlock"),
      new MyDefinitionId(typeof(MyObjectBuilder_BasicMissionBlock), "LargeBasicMission"),
      //new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AirDuctRamp"),
      //new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AirDuctX"),
      //new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AirDuctT"),
      //new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AirDuctCorner"),
      //new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AirDuct1"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AirDuct2"),
      //new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "AirDuctLight"),
    };

    public MyDefinitionId[] AirVentHalfBlockDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "AirVentFan"),
      new MyDefinitionId(typeof(MyObjectBuilder_AirVent)),
    };

    public MyDefinitionId[] AutomatonsFlatBlockDefinitions = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeBlockAccessPanel1"),
      new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeBlockAccessPanel2"),
      new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "LargeBlockAccessPanel3"),
      new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeBlockAccessPanel4"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign1"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign2"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign3"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign4"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign5"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign6"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign7"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign8"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign9"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign10"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign11"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign12"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign13"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster2"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster3"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster9"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster10"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster11"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster13"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AirDuctGrate"),
    };

    public Dictionary<MyDefinitionId, Base6Directions.Direction[]> CatwalkRailDirections { get; protected set; } = new Dictionary<MyDefinitionId, Base6Directions.Direction[]>(MyDefinitionId.Comparer)
    {
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkHalfRailing"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkHalfCenterRailing"), new Base6Directions.Direction[] { Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkHalfOuterRailing"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkWall"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkStraight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkCorner"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkRailingHalfLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkRailingHalfRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkRailingEnd"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSteelCatwalk2Sides"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSteelCatwalkCorner"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSteelCatwalk"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingStraight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingDouble"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingCorner"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "PassengerSeatLarge"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "DeadBody01"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "DeadBody03"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "DeadBody06"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },

      // Grated Catwalks Exapnsion
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkWall"), new Base6Directions.Direction[] { Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkEnd"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkStraight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkCorner"), new Base6Directions.Direction[] { Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWidthCrossoverLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWidthCrossoverRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfTJunction"), new Base6Directions.Direction[] { Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWidthTJunctionBalcony"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWidthBalcony"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkMixedTJunctionRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkMixedTJunctionLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkDiagonalBaseRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkDiagonalBaseLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeCatwalkDiagonalWall"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeCatwalkEndLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeCatwalkEndRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkEndLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkEndRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkHalfWidthCornerA"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkHalfWidthCornerB"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkMixedCornerLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkMixedCornerRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkCurvedWall"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkWall"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkWallOffset"), new Base6Directions.Direction[] { Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkStraight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkCornerBaseLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkCornerBaseRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkLCornerLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkLCornerRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkMixedCornerLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkMixedCornerRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkEndLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkEndRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkSquareEnd"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkTJunctionBaseLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkTJunctionBaseRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalkCorner"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalkCrossoverLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalkCrossoverRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalkTJunction"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkWallLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkWallRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkDiagonalWallLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkDiagonalWallRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkStraightLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkStraightRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkWideEndLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkWideEndRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkNarrowEndLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkNarrowEndRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkAngledEndLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Right, Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkAngledEndRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkObtuseCornerLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkObtuseCornerRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkAcuteCornerLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Right, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkAcuteCornerRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkCorner"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkHalfCornerA"), new Base6Directions.Direction[] { Base6Directions.Direction.Right, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkHalfCornerB"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkMixedCornerLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkMixedCornerRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkDiagonalTJunction"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkEndLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkEndRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkMixedTJunctionLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkMixedTJunctionRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkCorner"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkHalfCornerA"), new Base6Directions.Direction[] { Base6Directions.Direction.Right, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkHalfCornerB"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkMixedCornerLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkMixedCornerRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkDiagonalTJunction"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkEndLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkEndRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkMixedTJunctionLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkMixedTJunctionRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkHalfWidthBalcony"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkFullCurvedWall"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkStraightWall"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkStraight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkDiagonalCornerLeft"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkDiagonalCornerRight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkEnd"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedCatwalkPassageSupported"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedCatwalkPassageSupportedStraight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedCatwalkPassageBaseless"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedCatwalkPassageBaselessStraight"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMLadderCapCaged"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkLadderMiddle"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkLadderBottom"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkEndLadderTop"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkEndLadderMiddle"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkEndLadderBottom"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkStraightLadderTop"), new Base6Directions.Direction[] { Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkStraightLadderMiddle"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkStraightLadderBottom"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkWallLadderTop"), new Base6Directions.Direction[] { Base6Directions.Direction.Forward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkWallLadderMiddle"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
      { new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkWallLadderBottom"), new Base6Directions.Direction[] { Base6Directions.Direction.Backward } },
    };

    Dictionary<string, int> _activeHelpersToUpkeep = new Dictionary<string, int>();

    public Dictionary<BotType, long> BotPrices = new Dictionary<BotType, long>()
    {
      { BotType.Repair, 10000 },
      { BotType.Scavenger, 5000 },
      { BotType.Crew, 15000 },
      //{ BotType.Medic, 30000 },
      { BotType.Combat, 50000 },
    };

    public Dictionary<BotType, long> BotUpkeepPrices = new Dictionary<BotType, long>()
    {
      { BotType.Repair, 100 },
      { BotType.Scavenger, 50 },
      { BotType.Crew, 100 },
      //{ BotType.Medic, 150 },
      { BotType.Combat, 200 },
    };

    public Dictionary<BotType, List<SerialId>> BotComponents = new Dictionary<BotType, List<SerialId>>()
    {
      {
        BotType.Repair, new List<SerialId>()
        {
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"), 10),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SmallTube"), 2),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "LargeTube"), 2),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "Motor"), 2),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "Construction"), 15),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "MetalGrid"), 1),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "PowerCell"), 1),
        }
      },
      {
        BotType.Combat, new List<SerialId>()
        {
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"), 20),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SmallTube"), 4),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "LargeTube"), 4),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "Motor"), 5),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "BulletproofGlass"), 5),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "Reactor"), 1),
        }
      },
      {
        BotType.Scavenger, new List<SerialId>()
        {
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"), 5),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SmallTube"), 4),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "Motor"), 2),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "Detector"), 1),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "PowerCell"), 1),
        }
      },
      {
        BotType.Crew, new List<SerialId>()
        {
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"), 20),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SmallTube"), 4),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "LargeTube"), 4),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "Motor"), 5),
          new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "BulletproofGlass"), 5),
        }
      },
    };

    public Dictionary<string, Color> ColorDictionary = new Dictionary<string, Color>()
    {
      { "ALICEBLUE", Color.AliceBlue },
      { "ANTIQUEWHITE", Color.AntiqueWhite },
      { "AQUA", Color.Aqua },
      { "AQUAMARINE", Color.Aquamarine },
      { "AZURE", Color.Azure },
      { "BEIGE", Color.Beige },
      { "BISQUE", Color.Bisque },
      { "BLACK", Color.Black },
      { "BLANCHEDALMOND", Color.BlanchedAlmond },
      { "BLUE", Color.Blue },
      { "BLUEVIOLET", Color.BlueViolet },
      { "BROWN", Color.Brown },
      { "BURLYWOOD", Color.BurlyWood },
      { "CADETBLUE", Color.CadetBlue },
      { "CHARTREUSE", Color.Chartreuse },
      { "CHOCOLATE", Color.Chocolate },
      { "CORAL", Color.Coral },
      { "CORNFLOWERBLUE", Color.CornflowerBlue },
      { "CORNSILK", Color.Cornsilk },
      { "CRIMSON", Color.Crimson },
      { "CYAN", Color.Cyan },
      { "DARKGREEN", Color.DarkGreen },
      { "DARKBLUE", Color.DarkBlue },
      { "DARKCYAN", Color.DarkCyan },
      { "DARKGOLDENROD", Color.DarkGoldenrod },
      { "DARKGRAY", Color.DarkGray },
      { "DARKKHAKI", Color.DarkKhaki },
      { "DARKMAGENTA", Color.DarkMagenta },
      { "DARKOLIVEGREEN", Color.DarkOliveGreen },
      { "DARKORANGE", Color.DarkOrange },
      { "DARKORCHID", Color.DarkOrchid },
      { "DARKRED", Color.DarkRed },
      { "DARKSALMON", Color.DarkSalmon },
      { "DARKSEAGREEN", Color.DarkSeaGreen },
      { "DARKSLATEBLUE", Color.DarkSlateBlue },
      { "DARKSLATEGRAY", Color.DarkSlateGray },
      { "DARKTURQUOISE", Color.DarkTurquoise },
      { "DARKVIOLET", Color.DarkViolet },
      { "DEEPPINK", Color.DeepPink },
      { "DEEPSKYBLUE", Color.DeepSkyBlue },
      { "DIMGRAY", Color.DimGray },
      { "DODGERBLUE", Color.DodgerBlue },
      { "FIREBRICK", Color.Firebrick },
      { "FLORALWHITE", Color.FloralWhite },
      { "FORESTGREEN", Color.ForestGreen },
      { "FUCHSIA", Color.Fuchsia },
      { "GAINSBORO", Color.Gainsboro },
      { "GHOSTWHITE", Color.GhostWhite },
      { "GOLD", Color.Gold },
      { "GOLDENROD", Color.Goldenrod },
      { "GRAY", Color.Gray },
      { "GREEN", Color.Green },
      { "GREENYELLOW", Color.GreenYellow },
      { "HONEYDEW", Color.Honeydew },
      { "HOTPINK", Color.HotPink },
      { "INDIANRED", Color.IndianRed },
      { "INDIGO", Color.Indigo },
      { "IVORY", Color.Ivory },
      { "KHAKI", Color.Khaki },
      { "LAVENDER", Color.Lavender },
      { "LAVENDERBLUSH", Color.LavenderBlush },
      { "LAWNGREEN", Color.LawnGreen },
      { "LEMONCHIFFON", Color.LemonChiffon },
      { "LIGHTBLUE", Color.LightBlue },
      { "LIGHTCORAL", Color.LightCoral },
      { "LIGHTCYAN", Color.LightCyan },
      { "LIGHTGOLDENRODYELLOW", Color.LightGoldenrodYellow },
      { "LIGHTGRAY", Color.LightGray },
      { "LIGHTGREEN", Color.LightGreen },
      { "LIGHTPINK", Color.LightPink },
      { "LIGHTSALMON", Color.LightSalmon },
      { "LIGHTSEAGREEN", Color.LightSeaGreen },
      { "LIGHTSKYBLUE", Color.LightSkyBlue },
      { "LIGHTSLATEGRAY", Color.LightSlateGray },
      { "LIGHTSTEELBLUE", Color.LightSteelBlue },
      { "LIGHTYELLOW", Color.LightYellow },
      { "LIME", Color.Lime },
      { "LIMEGREEN", Color.LimeGreen },
      { "LINEN", Color.Linen },
      { "MAGENTA", Color.Magenta },
      { "MAROON", Color.Maroon },
      { "MEDIUMAQUAMARINE", Color.MediumAquamarine },
      { "MEDIUMBLUE", Color.MediumBlue },
      { "MEDIUMORCHID", Color.MediumOrchid },
      { "MEDIUMPURPLE", Color.MediumPurple },
      { "MEDIUMSEAGREEN", Color.MediumSeaGreen },
      { "MEDIUMSLATEBLUE", Color.MediumSlateBlue },
      { "MEDIUMSPRINGGREEN", Color.MediumSpringGreen },
      { "MEDIUMTURQUOISE", Color.MediumTurquoise },
      { "MEDIUMVIOLETRED", Color.MediumVioletRed },
      { "MIDNIGHTBLUE", Color.MidnightBlue },
      { "MINTCREAM", Color.MintCream },
      { "MISTYROSE", Color.MistyRose },
      { "MOCCASIN", Color.Moccasin },
      { "NAVAJOWHITE", Color.NavajoWhite },
      { "NAVY", Color.Navy },
      { "OLDLACE", Color.OldLace },
      { "OLIVE", Color.Olive },
      { "OLIVEDRAB", Color.OliveDrab },
      { "ORANGE", Color.Orange },
      { "ORANGERED", Color.OrangeRed },
      { "ORCHID", Color.Orchid },
      { "PALEGOLDENROD", Color.PaleGoldenrod },
      { "PALEGREEN", Color.PaleGreen },
      { "PALETURQUOISE", Color.PaleTurquoise },
      { "PALEVIOLETRED", Color.PaleVioletRed },
      { "PAPAYAWHIP", Color.PapayaWhip },
      { "PEACHPUFF", Color.PeachPuff },
      { "PERU", Color.Peru },
      { "PINK", Color.Pink },
      { "PLUM", Color.Plum },
      { "POWDERBLUE", Color.PowderBlue },
      { "PURPLE", Color.Purple },
      { "RED", Color.Red },
      { "ROSYBROWN", Color.RosyBrown },
      { "ROYALBLUE", Color.RoyalBlue },
      { "SADDLEBROWN", Color.SaddleBrown },
      { "SALMON", Color.Salmon },
      { "SANDYBROWN", Color.SandyBrown },
      { "SEAGREEN", Color.SeaGreen },
      { "SEASHELL", Color.SeaShell },
      { "SIENNA", Color.Sienna },
      { "SILVER", Color.Silver },
      { "SKYBLUE", Color.SkyBlue },
      { "SLATEBLUE", Color.SlateBlue },
      { "SLATEGRAY", Color.SlateGray },
      { "SNOW", Color.Snow },
      { "SPRINGGREEN", Color.SpringGreen },
      { "STEELBLUE", Color.SteelBlue },
      { "TAN", Color.Tan },
      { "TEAL", Color.Teal },
      { "THISTLE", Color.Thistle },
      { "TOMATO", Color.Tomato },
      { "TRANSPARENT", Color.Transparent },
      { "TURQUOISE", Color.Turquoise },
      { "VIOLET", Color.Violet },
      { "WHEAT", Color.Wheat },
      { "WHITE", Color.White },
      { "WHITESMOKE", Color.WhiteSmoke },
      { "YELLOW", Color.Yellow },
      { "YELLOWGREEN", Color.YellowGreen },
    };

    public Dictionary<BotType, string> BotDescriptions = new Dictionary<BotType, string>()
    {
      { BotType.Repair, "A simple helper bot. The Repair Bot is capable of manuevering around a grid and collecting dropped components to be placed in a container, as well as performing light welding duty for any blocks that may need it. It will pull components from containers on the same grid as needed." },
      { BotType.Scavenger, "The Scavenger Bot will follow you around and periodically may find items of interest in the environment." },
      { BotType.Crew, "The Crew Bot performs certain functions based on the block it is assigned to, such as ammo reloading, battery life extension, and more!" },
      //{ BotType.Medic, "The Healer bot is well versed in the art of salves and poultices capable of mending all but the most grievous of wounds. Salves apply a healing over time effect and are applied automatically while the bot is active." },
      { BotType.Combat, "The latest in combat technology, the Combat Bot is equipped with a rapid fire rifle and a titanium exoskeleton capable of taking on small arms fire with ease. It will follow you and attack any nearby hostiles." },
    };

    public Dictionary<MyStringId, string> BotModelDict = new Dictionary<MyStringId, string>(MyStringId.Comparer)
    {
      { MyStringId.GetOrCompute("Default"), "Default" },
      //{ MyStringId.GetOrCompute("RoboDog"), "RoboDog" },
      //{ MyStringId.GetOrCompute("DroneBot"), "Drone_Bot" },
      //{ MyStringId.GetOrCompute("TargetBot"), "Target_Dummy" },
      //{ MyStringId.GetOrCompute("AstronautMale"), "Default_Astronaut" },
      //{ MyStringId.GetOrCompute("AstronautFemale"), "Default_Astronaut_Female" },
    };

    public enum BotType { Repair, Scavenger, Combat, Crew };

    [Flags]
    public enum AllowedTypes : byte
    {
      None = 0x0,
      Repair = 0x1,
      Scavenger = 0x2,
      Combat = 0x4,
      Crew = 0x8,
    }

    public readonly MyStringId MODEL_DEFAULT = MyStringId.GetOrCompute("Default");
    public List<MyStringId> BotModelList = new List<MyStringId>();

    public HashSet<long> EconomyGrids { get; protected set; } = new HashSet<long>();
    public HashSet<string> KnownLootContainerIds { get; protected set; } = new HashSet<string>();
    public HashSet<MyDefinitionId> AllCoreWeaponDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> NpcSafeCoreWeaponDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> CatwalkBlockDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> SlopeBlockDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> SlopedHalfBlockDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> RampBlockDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> HalfStairBlockDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> HalfStairMirroredDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> LadderBlockDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> PassageBlockDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> PassageIntersectionDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> ArmorPanelMiscDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> ArmorPanelAllDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> ScaffoldBlockDefinitions { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public HashSet<MyDefinitionId> GratedCatwalkExpansionBlocks { get; protected set; } = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    public SortedDictionary<string, KeyValuePair<string, bool>> IgnoreTypeDictionary { get; protected set; } = new SortedDictionary<string, KeyValuePair<string, bool>>();
    public Dictionary<MyStringHash, string> AnimationControllerDictionary { get; protected set; } = new Dictionary<MyStringHash, string>(MyStringHash.Comparer); // char subtype to controller subtype
    public Dictionary<MyStringHash, string> SubtypeToSkeletonDictionary { get; protected set; } = new Dictionary<MyStringHash, string>(MyStringHash.Comparer); // char subtype to skeleton type
    public Dictionary<MyDefinitionId, List<MyTuple<int, MyTuple<MyDefinitionId, string, string, bool>>>> NpcSafeCoreWeaponMagazines = new Dictionary<MyDefinitionId, List<MyTuple<int, MyTuple<MyDefinitionId, string, string, bool>>>>(MyDefinitionId.Comparer);

    public ConcurrentDictionary<long, float> PlayerFollowDistanceDict = new ConcurrentDictionary<long, float>(); // player ident to follow distance
    public ConcurrentDictionary<MyItemType, MyObjectBuilder_PhysicalObject> ItemOBDict = new ConcurrentDictionary<MyItemType, MyObjectBuilder_PhysicalObject>();
    public ConcurrentDictionary<MyItemType, MyItemInfo> ItemInfoDict = new ConcurrentDictionary<MyItemType, MyItemInfo>();
    public ConcurrentDictionary<MyDefinitionId, List<MyItemType>> AcceptedItemDict = new ConcurrentDictionary<MyDefinitionId, List<MyItemType>>(MyDefinitionId.Comparer);
    public ConcurrentDictionary<long, IMyGps> BotGPSDictionary = new ConcurrentDictionary<long, IMyGps>(); // Bot EntityId to GPS
    public ConcurrentDictionary<long, IMyPlayer> Players = new ConcurrentDictionary<long, IMyPlayer>();
    public ConcurrentDictionary<long, HealthInfoStat> PlayerToHealthBars = new ConcurrentDictionary<long, HealthInfoStat>(); // player ident to health info stat
    public ConcurrentDictionary<long, float> PlayerToRepairRadius = new ConcurrentDictionary<long, float>(); // player ident to repair bot search radius in meters
    public ConcurrentDictionary<long, BotBase> Bots = new ConcurrentDictionary<long, BotBase>(); // bot char entity id to botbase
    public ConcurrentDictionary<long, List<BotBase>> PlayerToHelperDict = new ConcurrentDictionary<long, List<BotBase>>();
    public ConcurrentDictionary<long, ParticleInfoClient> ParticleDictionary = new ConcurrentDictionary<long, ParticleInfoClient>(); // bot id to particle info
    public ConcurrentDictionary<long, ParticleInfoBase> ParticleInfoDict = new ConcurrentDictionary<long, ParticleInfoBase>(); // bot id to particle info server
    public ConcurrentDictionary<string, MyObjectBuilder_Component> ComponentBuilderDict = new ConcurrentDictionary<string, MyObjectBuilder_Component>();
    public ConcurrentDictionary<long, Factory.BotInfo> FactoryBotInfoDict = new ConcurrentDictionary<long, Factory.BotInfo>();
    public ConcurrentDictionary<string, MySoundPair> SoundPairDict = new ConcurrentDictionary<string, MySoundPair>();
    public ConcurrentDictionary<long, List<long>> PlayerToHelperIdentity = new ConcurrentDictionary<long, List<long>>(); // player identity id to bot entity id
    public ConcurrentDictionary<long, List<long>> PlayerToActiveHelperIds = new ConcurrentDictionary<long, List<long>>(); // player identity to active bot entity id
    public ConcurrentDictionary<long, IMyFaction> BotFactions = new ConcurrentDictionary<long, IMyFaction>(); // player faction id to bot faction
    public ConcurrentDictionary<long, Vector3D> BotToSeatRelativePosition = new ConcurrentDictionary<long, Vector3D>();
    public ConcurrentDictionary<long, MyOwnershipShareModeEnum> BotToSeatShareMode = new ConcurrentDictionary<long, MyOwnershipShareModeEnum>();
    public ConcurrentDictionary<long, TimeSpan> DoorsToClose = new ConcurrentDictionary<long, TimeSpan>(); // door entityId to timespan when opened
    public ConcurrentDictionary<long, CubeGridMap> GridGraphDict = new ConcurrentDictionary<long, CubeGridMap>();
    public ConcurrentDictionary<ulong, VoxelGridMap> VoxelGraphDict;
    public ConcurrentDictionary<long, long> EntityToIdentityDict = new ConcurrentDictionary<long, long>(2, 100);
    public ConcurrentDictionary<long, AiSession.ControlInfo> BotToControllerInfoDict = new ConcurrentDictionary<long, ControlInfo>(); // bot entity id to controller info
    public ConcurrentQueue<GridBase> MapInitQueue = new ConcurrentQueue<GridBase>();
    public ConcurrentStack<MyEntity3DSoundEmitter> SoundEmitters = new ConcurrentStack<MyEntity3DSoundEmitter>();
    public ConcurrentStack<Vector3D[]> CornerArrayStack = new ConcurrentStack<Vector3D[]>();

    public MyConcurrentPool<List<IMyCharacter>> CharacterListPool = new MyConcurrentPool<List<IMyCharacter>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<IMyCharacter>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<VoxelUpdateItem> VoxelUpdateItemPool = new MyConcurrentPool<VoxelUpdateItem>
    (
      defaultCapacity: 100,
      expectedAllocations: 100,
      activator: () => new VoxelUpdateItem(),
      deactivator: (x) => x = null
    );

    public MyConcurrentPool<MyQueue<VoxelUpdateItem>> VoxelUpdateQueuePool = new MyConcurrentPool<MyQueue<VoxelUpdateItem>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new MyQueue<VoxelUpdateItem>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<VoxelUpdateItem>> VoxelUpdateListPool = new MyConcurrentPool<List<VoxelUpdateItem>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<VoxelUpdateItem>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<BotStatus>> BotStatusListPool = new MyConcurrentPool<List<BotStatus>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<BotStatus>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<BotStatus> BotStatusPool = new MyConcurrentPool<BotStatus>
    (
      defaultCapacity: 100,
      clear: (x) => x.Reset(),
      expectedAllocations: 100,
      activator: () => new BotStatus(),
      deactivator: (x) => { x.Reset(); x = null; }
    );

    public MyConcurrentPool<ObstacleWorkData> ObstacleWorkDataPool = new MyConcurrentPool<ObstacleWorkData>
    (
      defaultCapacity: 100,
      expectedAllocations: 100,
      activator: () => new ObstacleWorkData(),
      deactivator: (x) => x = null
    );

    public MyConcurrentPool<ApiWorkData> ApiWorkDataPool = new MyConcurrentPool<ApiWorkData>
    (
      defaultCapacity: 100,
      expectedAllocations: 100,
      activator: () => new ApiWorkData(),
      deactivator: (x) =>  x = null
    );

    public MyConcurrentPool<Dictionary<string, int>> MissingCompsDictPool = new MyConcurrentPool<Dictionary<string, int>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new Dictionary<string, int>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<InventoryCache> InvCachePool = new MyConcurrentPool<InventoryCache>
    (
      defaultCapacity: 100,
      expectedAllocations: 100,
      activator: () => new InventoryCache(),
      deactivator: (x) => { x.Close(); x = null; }
    );

    public MyConcurrentPool<HashSet<Vector3I>> LocalVectorHashPool = new MyConcurrentPool<HashSet<Vector3I>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new HashSet<Vector3I>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<CubeGridMap>> GridMapListPool = new MyConcurrentPool<List<CubeGridMap>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<CubeGridMap>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<MyLineSegmentOverlapResult<MyEntity>>> OverlapResultListPool = new MyConcurrentPool<List<MyLineSegmentOverlapResult<MyEntity>>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<MyLineSegmentOverlapResult<MyEntity>>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<HashSet<long>> GridCheckHashPool = new MyConcurrentPool<HashSet<long>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new HashSet<long>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<string>> StringListPool = new MyConcurrentPool<List<string>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<string>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<MySoundPair>> SoundListPool = new MyConcurrentPool<List<MySoundPair>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<MySoundPair>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<MyEntity>> EntListPool = new MyConcurrentPool<List<MyEntity>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<MyEntity>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<IHitInfo>> HitListPool = new MyConcurrentPool<List<IHitInfo>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<IHitInfo>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<Vector3I>> LineListPool = new MyConcurrentPool<List<Vector3I>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<Vector3I>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<MyVoxelBase>> VoxelMapListPool = new MyConcurrentPool<List<MyVoxelBase>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<MyVoxelBase>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<IMyCubeGrid>> GridGroupListPool = new MyConcurrentPool<List<IMyCubeGrid>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<IMyCubeGrid>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<List<IMySlimBlock>> SlimListPool = new MyConcurrentPool<List<IMySlimBlock>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      expectedAllocations: 100,
      activator: () => new List<IMySlimBlock>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public MyConcurrentPool<RepairWorkData> RepairWorkPool = new MyConcurrentPool<RepairWorkData>
    (
      defaultCapacity: 1000,
      expectedAllocations: 1000,
      activator: () => new RepairWorkData(),
      deactivator: (n) => { n = null; }
    );

    public MyConcurrentPool<PathWorkData> PathWorkPool = new MyConcurrentPool<PathWorkData>
    (
      defaultCapacity: 1000,
      expectedAllocations: 1000,
      activator: () => new PathWorkData(),
      deactivator: (n) => { n = null; }
    );

    public MyConcurrentPool<GraphWorkData> GraphWorkPool = new MyConcurrentPool<GraphWorkData>
    (
      defaultCapacity: 1000,
      expectedAllocations: 1000,
      activator: () => new GraphWorkData(),
      deactivator: (n) => { n = null; }
    );

    public MyConcurrentPool<Node> NodePool = new MyConcurrentPool<Node>
    (
      defaultCapacity: 10000,
      clear: (x) => x.Reset(),
      expectedAllocations: 10000,
      activator: () => new Node(),
      deactivator: (n) => { n.Reset(); n = null; }
    );

    public MyConcurrentPool<TempNode> TempNodePool = new MyConcurrentPool<TempNode>
    (
      defaultCapacity: 10000,
      clear: (x) => x.Reset(),
      expectedAllocations: 10000,
      activator: () => new TempNode(),
      deactivator: (n) => { n.Reset(); n = null; }
    );

    public MyConcurrentPool<Queue<Vector3I>> LocalVectorQueuePool = new MyConcurrentPool<Queue<Vector3I>>
    (
      defaultCapacity: 100,
      clear: (q) => q.Clear(),
      expectedAllocations: 100,
      activator: () => new Queue<Vector3I>(),
      deactivator: (q) => { q.Clear(); q = null; }
    );

    public static ConcurrentStack<MyStorageData> StorageStack = new ConcurrentStack<MyStorageData>();
    public static List<string> AllowedBotRoles = new List<string>(5);

    public static Vector3I[] DirArray = new Vector3I[]
    {
      Vector3I.Up,
      Vector3I.Down,
      Vector3I.Left,
      Vector3I.Right,
      Vector3I.Forward,
      Vector3I.Backward
    };

    //public Dictionary<string, int> AnimationTimeDictionary = new Dictionary<string, int>(); // TODO: Try and find a reference to the duration of animations
    public Stack<RemoteBotAPI.SpawnData> SpawnDataStack = new Stack<RemoteBotAPI.SpawnData>();
    public Stack<FutureBotAPI> FutureBotAPIStack = new Stack<FutureBotAPI>();
    public Queue<FutureBotAPI> FutureBotAPIQueue = new Queue<FutureBotAPI>();
    public Queue<FutureBot> FutureBotQueue = new Queue<FutureBot>();
    public HashSet<string> PendingBotRespawns = new HashSet<string>();
    public HashSet<long> AnalyzeHash = new HashSet<long>();
    public HashSet<long> HealingHash = new HashSet<long>();
    public Dictionary<MyDefinitionId, MyDefinitionBase> AllGameDefinitions = new Dictionary<MyDefinitionId, MyDefinitionBase>(MyDefinitionId.Comparer);
    public List<MyDefinitionId> ScavengerItemList = new List<MyDefinitionId>();
    public List<IMyUseObject> UseObjectsAPI = new List<IMyUseObject>();
    public List<IMySlimBlock> GridSeatsAPI = new List<IMySlimBlock>();
    public List<MyConsumableItemDefinition> ConsumableItemList = new List<MyConsumableItemDefinition>();

    public List<Sandbox.ModAPI.Ingame.MyInventoryItemFilter> EmptySorterCache = new List<Sandbox.ModAPI.Ingame.MyInventoryItemFilter>();
    public List<Sandbox.ModAPI.Ingame.MyInventoryItemFilter> FactorySorterCache = new List<Sandbox.ModAPI.Ingame.MyInventoryItemFilter>()
    {
      new Sandbox.ModAPI.Ingame.MyInventoryItemFilter(new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_CrewBotMaterial")),
      new Sandbox.ModAPI.Ingame.MyInventoryItemFilter(new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_CombatBotMaterial")),
      new Sandbox.ModAPI.Ingame.MyInventoryItemFilter(new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_RepairBotMaterial")),
      new Sandbox.ModAPI.Ingame.MyInventoryItemFilter(new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_ScavengerBotMaterial")),
    };

    Stack<WeaponInfo> _weaponInfoStack = new Stack<WeaponInfo>(20);
    Stack<IconInfo> _iconInfoStack = new Stack<IconInfo>(20);
    Stack<HealthInfo> _healthInfoStack = new Stack<HealthInfo>(20);
    Stack<PathCollection> _pathCollections = new Stack<PathCollection>(10);
    ConcurrentCachingList<ControlInfo> _controllerInfo = new ConcurrentCachingList<ControlInfo>();
    ConcurrentCachingList<ControlInfo> _pendingControllerInfo = new ConcurrentCachingList<ControlInfo>();
    ConcurrentDictionary<long, byte> _botEntityIds = new ConcurrentDictionary<long, byte>();
    ConcurrentDictionary<long, IconInfo> _botSpeakers = new ConcurrentDictionary<long, IconInfo>();
    ConcurrentDictionary<long, IconInfo> _botAnalyzers = new ConcurrentDictionary<long, IconInfo>();
    ConcurrentDictionary<long, HealthInfo> _healthBars = new ConcurrentDictionary<long, HealthInfo>();
    ConcurrentDictionary<long, IconInfo> _botHealings = new ConcurrentDictionary<long, IconInfo>();
    List<long> _iconAddList = new List<long>();
    List<WeaponInfo> _weaponFireList = new List<WeaponInfo>();
    List<BotBase> _robots = new List<BotBase>(10);
    List<IMyUseObject> _useObjList = new List<IMyUseObject>();
    List<IMyPlayer> _tempPlayers = new List<IMyPlayer>(16);
    List<IMyPlayer> _tempPlayersAsync = new List<IMyPlayer>(16);
    List<IMyCharacter> _botCharsToClose = new List<IMyCharacter>();
    List<MyKeys> _keyPresses = new List<MyKeys>();
    Dictionary<long, long> _newPlayerIds = new Dictionary<long, long>(); // bot identityId to bot entityId
    HashSet<long> _iconRemovals = new HashSet<long>();
    HashSet<long> _hBarRemovals = new HashSet<long>();
    HashSet<long> _controlBotIds = new HashSet<long>();
    HashSet<long> _botsToClose = new HashSet<long>();
    MyQueue<MyTuple<long, string, int>> _prefabsToCheck = new MyQueue<MyTuple<long, string, int>>();

    MyObjectBuilderType[] _ignoreTypes = new MyObjectBuilderType[]
    {
      typeof(MyObjectBuilder_DebugSphere1),
      typeof(MyObjectBuilder_DebugSphere2),
      typeof(MyObjectBuilder_DebugSphere3)
    };

    MyDefinitionId[] _validSlopedBlockDefs = new MyDefinitionId[]
    {
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlope2Base"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlope2Tip"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorSlope2Base"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorSlope2Tip"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorSlope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlope2Base"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_Concrete_Slope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_ReinforcedConcrete_Slope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_Concrete_Half_Block_Slope"),
      new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_ReinforcedConcrete_Half_Block_Slope"),
      new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeDiagonalLCDPanel"),
    };
  }
}
