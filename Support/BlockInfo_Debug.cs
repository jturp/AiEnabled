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
using Direction = VRageMath.Base6Directions.DirectionFlags;
using VRage.Voxels;
using AiEnabled.API;
using AiEnabled.Parallel;
using VRage.Input;
using AiEnabled.ConfigData;
using VRage.Utils;
using VRage;
using System.Runtime.ConstrainedExecution;
using AiEnabled.Utilities;
using ObjectBuilders.SafeZone;
using SpaceEngineers.ObjectBuilders.ObjectBuilders;
using Havok;
using System.IO;
using ProtoBuf;

namespace AiEnabled
{
  public partial class BlockInfo
  {
    void InitBlockInfo_Debug()
    {
      _logger.Info("Init Block Info: Start");

      // Breakdown: Dictionary<BlockDefinitionId, Dictionary<BlockCell, MyTuple<DirectionFlag_Blocked, OffsetDir, OffsetAmount, SpecialHandling, IsGroundNode>>>

      _invalidBlockDirInfo = new Dictionary<MyDefinitionId, Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>>(MyDefinitionId.Comparer)
      {
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRailStraight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Up + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmor_Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmor_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Up + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHalfArmorBlock"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyHalfArmorBlock"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHalfSlopeArmorBlock"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyHalfSlopeArmorBlock"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorRoundSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Up + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorRoundCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Forward }, Vector3.Up + Vector3.Left + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorRoundSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Up + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorRoundCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Forward }, Vector3.Up + Vector3.Left + Vector3.Backward, 0.35f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlope2Base"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlope2Tip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorCorner2Base"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Up + Vector3.Backward, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorCorner2Base"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Up + Vector3.Backward, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorCorner2Tip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorSlope2Base"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeHeavyBlockArmorSlope2Tip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorHalfCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Backward + Vector3.Down + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorHalfCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Backward + Vector3.Down + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorHalfSlopeCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorHalfSlopeCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorHalfSlopedCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Backward + Vector3.Down + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorHalfSlopedCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Backward + Vector3.Down + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorHalfSlopedCornerBase"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Backward, Direction.Left, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorHalfSlopedCornerBase"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Backward, Direction.Left, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlopedCornerTip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorSlopedCornerTip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlopeTransition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Left, Direction.Right }, Vector3.Up + Vector3.Backward, 0.35f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorSlopeTransition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Left, Direction.Right }, Vector3.Up + Vector3.Backward, 0.35f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlopeTransitionMirrored"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Right, Direction.Backward }, Vector3.Up + Vector3.Forward, 0.35f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorSlopeTransitionMirrored"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Right, Direction.Backward }, Vector3.Up + Vector3.Forward, 0.35f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlopeTransitionTip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Left, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorSlopeTransitionTip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Left, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSlopeTransitionTipMirrored"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorSlopeTransitionTipMirrored"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSquareSlopedCornerTip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorSquareSlopedCornerTip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorSquareSlopedCornerTipInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Backward, Direction.Left, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockHeavyArmorSquareSlopedCornerTipInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Backward, Direction.Left, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorPanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorSlopedSidePanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorSlopedPanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Right, Direction.Left }, Vector3.Up + Vector3.Forward, 0.35f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfPanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfCenterPanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Forward, Direction.Backward, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorQuarterPanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedPanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedPanelTipLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedSideBasePanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedSideTipPanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedSideBasePanelLightInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedSideTipPanelLightInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfSlopedPanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Backward, Direction.Down }, Vector3.Up + Vector3.Forward + Vector3.Left, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedPanelLightRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedTipPanelLightRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedPanelLightLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedTipPanelLightLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmorPanelLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmorPanelCornerLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward, Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmorPanelFaceLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmorPanelInvertedCornerLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorPanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorSlopedSidePanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorSlopedPanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Right, Direction.Left }, Vector3.Up + Vector3.Forward, 0.35f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfPanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfCenterPanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Forward, Direction.Backward, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorQuarterPanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedPanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedPanelTipHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedSideBasePanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedSideTipPanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedSideBasePanelHeavyInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1SlopedSideTipPanelHeavyInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmorHalfSlopedPanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Backward, Direction.Down }, Vector3.Up + Vector3.Forward + Vector3.Left, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedPanelHeavyRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedTipPanelHeavyRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedPanelHeavyLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeArmor2x1HalfSlopedTipPanelHeavyLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmorPanelHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmorPanelCornerHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward, Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmorPanelFaceHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRoundArmorPanelInvertedCornerHeavy"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_SensorBlock), "LargeBlockSensor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
        }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_SoundBlock), "LargeBlockSoundBlock"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
        }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "ButtonPanelLarge"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "LargeButtonPanelPedestal"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "LargeBlockRadioAntenna"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,5,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,5,1), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "ControlPanel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
        }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeControlPanelPedestal"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockDesk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward }, Vector3.Backward, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockDeskCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockDeskCornerInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Right }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockDeskChairless"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockDeskChairlessCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockDeskChairlessCornerInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Right }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Kitchen), "LargeBlockKitchen"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "LargeBlockLockerRoom"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Backward + Vector3.Left, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "LargeBlockLockerRoomCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Backward + Vector3.Left, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Planter), "LargeBlockPlanters"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockCouch"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockCouchCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Left }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "LargeBlockLockers"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockBathroomOpen"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockBathroom"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockToilet"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Left, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_VendingMachine), "FoodDispenser"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Jukebox), "Jukebox"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Shower"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Left + Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowWallLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowWallRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "TransparentLCDLarge"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Catwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkStraight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkRailingEnd"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkRailingHalfRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Right }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkRailingHalfLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Right }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkHalf"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkHalfRailing"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Right }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkHalfCenterRailing"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CatwalkHalfOuterRailing"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GratedStairs"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left, Direction.Forward, Direction.Down }, Vector3.Up, 0.4f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GratedHalfStairs"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left, Direction.Forward, Direction.Down }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GratedHalfStairsMirrored"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left, Direction.Forward, Direction.Down }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingStraight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingDouble"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingHalfRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "RailingHalfLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Railing2x1Right"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Railing2x1Left"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ReflectorLight), "RotatingLightLarge"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Freight1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Freight2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Freight3"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_SolarPanel), "LargeBlockColorableSolarPanel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left, Direction.Backward },Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_SolarPanel), "LargeBlockColorableSolarPanelCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left, Direction.Right, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_SolarPanel), "LargeBlockColorableSolarPanelCornerInverted"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left, Direction.Right, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_WindTurbine), "LargeBlockWindTurbineReskin"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,1), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,1), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,1), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,2), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,2), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,2), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,2), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,2), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,3), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,3), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,3), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,3), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,3), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,3), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,3), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,3), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,3), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CryoChamber), "LargeBlockCryoRoom"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "HoloLCDLarge"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_MedicalRoom), "LargeMedicalRoomReskin"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CryoChamber), "LargeBlockHalfBed"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Left }, Vector3.Right, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CryoChamber), "LargeBlockHalfBedOffset"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Left }, Vector3.Right, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeDiagonalLCDPanel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward, Direction.Up, Direction.Down }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Truss"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFrame"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussSlopedFrame"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussSloped"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussAngled"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussHalf"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloorT"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloorX"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloorAngled"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloorAngledInverted"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussFloorHalf"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Up, Direction.Left, Direction.Right }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "TrussLadder"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeCrate"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBarrel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Warhead), "LargeExplosiveBarrel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), ""), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirtightHangarDoor), ""), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirtightSlideDoor), "LargeBlockSlideDoor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_VendingMachine), "VendingMachine"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_StoreBlock), "AtmBlock"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Reactor), "LargeBlockLargeGenerator"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_WindTurbine), "LargeBlockWindTurbine"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_SolarPanel), "LargeBlockSolarPanel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeDeadAstronaut"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
        }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster3"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster9"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster10"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster11"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSignEaster13"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "LargeBlockRadioAntennaDish"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,2), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,3), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,3), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,3), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,3), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,3), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,4), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,4), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,4), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,4), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "LargeBlockGate"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Right, 0.25f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Left, 0.25f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Down + Vector3.Right, 0.25f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Down, 0.25f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right }, Vector3.Down + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "LargeBlockOffsetDoor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "DeadBody01"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "DeadBody02"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "DeadBody03"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "DeadBody04"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "DeadBody05"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "DeadBody06"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AngledInteriorWallA"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AngledInteriorWallB"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeBlockAccessPanel1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeBlockAccessPanel2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "LargeBlockAccessPanel3"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeBlockAccessPanel4"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CameraBlock), "LargeCameraTopMounted"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
        }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign3"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign4"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign5"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign6"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign7"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign8"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign9"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign10"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign11"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign12"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWarningSign13"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_SensorBlock), "LargeBlockSensorReskin"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
        }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_MyProgrammableBlock), "LargeProgrammableBlockReskin"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward, Direction.Up, Direction.Down }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "VerticalButtonPanelLarge"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockConveyorPipeCap"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_OxygenTank), "LargeHydrogenTankIndustrial"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Right }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Left }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Right }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Left }, Vector3.Backward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Assembler), "LargeAssemblerIndustrial"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Forward, 0.25f, false, true) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Refinery), "LargeRefineryIndustrial"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, true) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, true, true) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Zero, 0f, true, true) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, true) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Backward, Direction.Forward }, Vector3.Zero, 0f, true, true) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, true, true) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Up, Direction.Right, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockSlope2x1Base"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockSlope2x1Tip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockHalf"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeGridBeamBlockHalfSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Passage), ""), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage2Wall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeStairs"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Forward, 0.2f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeRamp"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Forward, 0.2f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSteelCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSteelCatwalk2Sides"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSteelCatwalkCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSteelCatwalkPlate"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeCoverWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeCoverWallHalf"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeCoverWallHalfMirrored"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "PassengerSeatLarge"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), ""), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Backward, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "LadderShaft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left, Direction.Backward }, Vector3.Backward, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AirDuctGrate"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeBlockCorner_LCD_1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeBlockCorner_LCD_2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeBlockCorner_LCD_Flat_1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeBlockCorner_LCD_Flat_2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeTextPanel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeLCDPanel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeLCDPanelWide"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "SmallLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
        }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LargeBlockLight_1corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
        }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LargeBlockLight_2corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
        }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_OxygenTank), "LargeHydrogenTank"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Right }, Vector3.Forward + Vector3.Left, 0.3f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Left }, Vector3.Forward + Vector3.Right, 0.3f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Right }, Vector3.Forward + Vector3.Left, 0.3f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Left }, Vector3.Forward + Vector3.Right, 0.3f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Right }, Vector3.Forward + Vector3.Left, 0.3f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Left }, Vector3.Forward + Vector3.Right, 0.3f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Right }, Vector3.Backward + Vector3.Left, 0.3f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Left }, Vector3.Backward + Vector3.Right, 0.3f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Right }, Vector3.Backward + Vector3.Left, 0.3f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Left }, Vector3.Backward + Vector3.Right, 0.3f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Right }, Vector3.Backward + Vector3.Left, 0.3f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Left }, Vector3.Backward + Vector3.Right, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockConveyorCap"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_PistonBase), "LargePistonBase"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_PistonTop), "LargePistonTop"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_MotorStator), "LargeStator"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_MotorAdvancedStator), "LargeAdvancedStator"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_MedicalRoom), "LargeMedicalRoom"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Viewport1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Viewport2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussPillarLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Right + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "StorageShelf1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "StorageShelf2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "StorageShelf3"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockInsetWallSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward, Direction.Up, Direction.Down }, Vector3.Forward + Vector3.Right, 0.275f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockConsoleModule"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockConsoleModuleCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockConsoleModuleInvertedCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Forward }, Vector3.Backward + Vector3.Left, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockConsoleModuleScreens"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "LargeBlockConsoleModuleButtons"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward + Vector3.Left, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "ExtendedWindow"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "ExtendedWindowRailing"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "ExtendedWindowCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "ExtendedWindowEnd"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "ExtendedWindowDome"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Corridor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "CorridorLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CorridorCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CorridorT"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CorridorX"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CorridorWindow"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CorridorDoubleWindow"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CorridorWindowRoof"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "CorridorNarrow"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "CorridorNarrowStowage"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "LargeBlockNarrowDoor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "LargeBlockNarrowDoorHalf"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussPillarDiagonal"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "TrussPillarOffset"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeLCDPanel5x5"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeLCDPanel5x3"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LargeLCDPanel3x3"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesStraight1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesStraight2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesBendUp"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesBendDown"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesStraightEnd1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesStraightEnd2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesStraightDown"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesU"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesT"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_EmissiveBlock), "LargeNeonTubesCircle"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TerminalBlock), "LargeBlockSciFiTerminal"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "LargeSciFiButtonTerminal"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockBarCounter"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockBarCounterCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Right }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "LargeSciFiButtonPanel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Thrust), "LargeBlockLargeAtmosphericThrustSciFi"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Up }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolA"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolB"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolC"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolD"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolE"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolF"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolG"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolH"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolI"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolJ"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolK"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolL"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolM"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolN"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolO"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolP"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolQ"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolR"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolS"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolT"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolU"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolV"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolW"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolX"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolY"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolZ"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol0"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol3"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol4"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol5"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol6"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol7"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol8"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbol9"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolHyphen"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolUnderscore"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolDot"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolApostrophe"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolAnd"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolColon"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolExclamationMark"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeSymbolQuestionMark"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Thrust), "LargeBlockLargeThrust"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Thrust), "LargeBlockLargeHydrogenThrust"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward, Direction.Up }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward, Direction.Up }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Up }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Up }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Thrust), "LargeBlockLargeAtmosphericThrust"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Up }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,4), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Thrust), "LargeBlockLargeFlatAtmosphericThrust"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Up }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Thrust), "LargeBlockLargeFlatAtmosphericThrustDShape"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Up }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_LandingGear), "LargeBlockLandingGear"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_LandingGear), "LargeBlockSmallMagneticPlate"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CameraBlock), "LargeCameraBlock"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "LargeBlockWeaponRack"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "FireCover"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "FireCoverCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfWindow"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfWindowInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfWindowCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfWindowCornerInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfWindowRound"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Embrasure"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFi"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiIntersection"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiGate"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageScifiCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiTjunction"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWindow"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "BridgeWindow1x1Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Up, Direction.Forward }, Vector3.Down, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "BridgeWindow1x1Face"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Left, Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "BridgeWindow1x1FaceInverted"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward, Direction.Down }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LargeLightPanel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Reactor), "LargeBlockLargeGeneratorWarfare2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Up, 0.25f, false, true) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Up + Vector3.Forward, 0.25f, false, true) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Forward, 0.1f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Up }, Vector3.Up * 2 + Vector3.Right, 0.1f, false, true) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right }, Vector3.Left, 0.25f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Backward }, Vector3.Up, 0.25f, false, true) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Forward }, Vector3.Up + Vector3.Backward, 0.25f, false, true) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.1f, false, false) },
            { new Vector3I(1,1,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Backward, 0.1f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirtightHangarDoor), "AirtightHangarDoorWarfare2A"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirtightHangarDoor), "AirtightHangarDoorWarfare2B"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirtightHangarDoor), "AirtightHangarDoorWarfare2C"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "SlidingHatchDoor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "SlidingHatchDoorHalf"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Down, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockStandingCockpit"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_LargeGatlingTurret), ""), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] { Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_LargeMissileTurret), ""), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] { Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_LargeMissileTurret), "LargeCalibreTurret"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_LargeMissileTurret), "LargeBlockMediumCalibreTurret"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,2), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWindowSquare"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeWindowEdge"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Left, Direction.Right }, Vector3.Up, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Right }, Vector3.Forward, 0.2f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Backward, Direction.Left, Direction.Right }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2Inv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right, Direction.Forward }, Vector3.Down, 0.2f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2Face"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Up, 0.2f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2SideLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2SideLeftInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2SideRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2SideRightInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left, Direction.Down, Direction.Backward }, Vector3.Up, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1Face"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1Side"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1SideInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1Inv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2Flat"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x2FlatInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1Flat"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window1x1FlatInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window3x3Flat"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window3x3FlatInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window2x3Flat"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Window2x3FlatInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowRound"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowRoundInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowRoundCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward, Direction.Up }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowRoundCornerInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward, Direction.Up }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowRoundFace"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowRoundFaceInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowRoundInwardsCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "WindowRoundInwardsCornerInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "RoboFactory"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },

        // mod block list
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "SB_ADeck_Straight_door"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CryoChamber), "SB_ADeck_Straight_locker"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Forward, 0.075f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_ADeck_Straight_nolight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_ADeck_Corner_nolight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_ADeck_Straight_Wall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_ADeck_Straight_window"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_ADeck_Corner_Wall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_ADeck_Tsection_nolight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_ADeck_Crosssection_nolight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "SB_ADeck_Straight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "SB_ADeck_Straight_Stairs"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Forward + Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Backward + Vector3.Down, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "SB_BDeck_Straight_door"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_BDeck_Straight_nolight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_BDeck_Corner_nolight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_BDeck_Straight_L"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_BDeck_Corner_Wall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_BDeck_Tsection_nolight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "SB_BDeck_Crosssection_nolight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "SB_BDeck_Straight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },


        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage2Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage2CornerEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage2Intersection"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage2IntersectionEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage2StraightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage2TIntersection"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage2TIntersectionEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWindowEnd"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiPillar"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFi2PillarWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFi3PillarWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiLeftPillar"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiRightPillar"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiRoof"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWallCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiStairs"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiStairsLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiRoofLight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "PassageSciFiVent"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "PassageSciFiVentWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "PassageSciFiCargoAccess"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "PassageSciFiCargoAccessWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageSciFiLCDStraight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageSciFiLCDWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "PassageSciFiDoor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "PassageSciFiWallSideDoor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "PassageSciFiAirlock"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_MedicalRoom), "PassageSciFiMedicalRoom"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "PassageSciFiVentEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "PassageSciFiCargoAccessEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageSciFiLCDStraightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiIntersectionEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageScifiCornerEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiJunctionEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWindowEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWallEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "PassageSciFiVentWallEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "PassageSciFiCargoAccessWallEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageSciFiLCDWallEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWallWindowEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWallWindowLEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWallWindowREnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWallWindowCEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiLeftPillarEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiRightPillarEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiPillarEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFi2PillarWallEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFi3PillarWallEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiRoofEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiFloorEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageSciFiWallCornerEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "PassageSciFiButtonWallEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "PassageSciFiDoorEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "PassageSciFiWallSideDoorEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "PassageSciFiSideDoorEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "PassageSciFiAirlockEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiLightEnclosed"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiRoofLightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiJunctionLightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiCrossLightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageScifiCornerLightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiWallLightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiLeftPillarLightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiRightPillarLightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageSciFiWallCornerLightEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageLux"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },

        // Rotary Airlock mod
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "RotaryAirlock"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "RotaryAirlockCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },

        // mod
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Conveyor), "PassageCargoAccess_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Conveyor), "PassageCargoAccessEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Conveyor), "HalfPassageCargoAccess_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Conveyor), "HalfPassageCargoAccessEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "PassageLadder_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward }, Vector3.Backward, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "PassageLadderEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Backward, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "PassageLadderExit_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward }, Vector3.Backward, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "PassageAirVent"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "PassageAirVentEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "PassageDoor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "PassageDoorSide_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "PassageDoorEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "PassageDoorSideEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "PassageAirlock_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "PassageAirlockEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LCDPassage"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LCDPassage2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageTLCD"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageCornerLCD"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageCornerLCDInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LCDPassageEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "LCDPassage2Enc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageTLCDEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageCornerLCDEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "PassageCornerLCDInvEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "PassageButtonPanel_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "PassageButtonPanelEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Conveyor), "PassageConveyor_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Conveyor), "PassageConveyorEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "PassageCargoAccess_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "PassageCargoAccessEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "HalfPassageDoor_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "HalfPassageDoorEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "HalfPassageDoorL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "HalfPassageDoorR_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "HalfPassageDoorLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "HalfPassageDoorREnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "HalfPassageButtonPanel_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "HalfPassageButtonPanelEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "HalfPassageAirVent"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AirVent), "HalfPassageAirVentEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "HalfPassageLCD"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "HalfPassageLCDEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "HalfPassageLCD2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "HalfPassageLCD2Enc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "PassageTopDoor_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_AdvancedDoor), "PassageTopDoorEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "HalfPassageDoorSide_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "HalfPassageDoorSideEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "HalfPassageCargoAccess_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CargoContainer), "HalfPassageCargoAccessEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassage_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassageWall_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassageWallEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassageEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageTop_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassageGlass_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassageGlassEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageTopEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassageCorner_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassageCornerDiag_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassageCornerEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassageCornerDiagEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassagePillarL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassagePillarWL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassagePillarWLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassagePillarLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassagePillarR_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassagePillarWR_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassagePillarREnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "HalfPassagePillarWREnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassagePillar_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassagePillarEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassagePillar2_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassagePillar2o_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassagePillar3_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassagePillar2Enc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassagePillar2oEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassagePillar3Enc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LightedPassage"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "Passage4WayLightedIntersection_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageTLightedIntersection_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageTVertLighted_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageLLightedIntersection_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LightedPassageGlassSide_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LightedPassageGlass2Side_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LightedPassageTGlass_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LightedPassageStairs_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, true) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Up, 0.85f, false, true) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Down, 0.15f, false, true) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "AVTECH_PassageStairs45L"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Left, 0.5f, false, true) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Right, 0.5f, false, true) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LightedPassageEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "Passage4WayLightedIntersectionEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageTLightedIntersectionEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageLLightedIntersectionEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LightedPassageGlassSideEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LightedPassageGlass2SideEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "LightedPassageTGlassEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "AVTECH_PassageWallL"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "AVTECH_PassageWallLEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageTopL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageGlassL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageGlassLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassageTopLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageCornerL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageCornerDiagL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageCornerLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageCornerDiagLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassagePillarLL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassagePillarGlassLL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassagePillarLLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassagePillarLLGlassEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassagePillarRL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassagePillarGlassRL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassagePillarRLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassagePillarGlassRLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassagePillarL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassagePillarLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassagePillar2L_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassagePillar2oL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassagePillar3L_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassagePillar2LEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassagePillar2oLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "PassagePillar3LEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageWallL_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_InteriorLight), "HalfPassageWallLEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage4WayIntersection_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageTIntersection_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageTVertIntersection_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageLIntersection_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageGlassSide_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageGlass2Side_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageTGlass_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AVTECH_PassageWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageStairs_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, true) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Up, 0.85f, false, true) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Down, 0.15f, false, true) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AVTECH_PassageStairs45"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Left, 0.5f, false, true) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Right, 0.5f, false, true) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "EnclosedPassage"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "Passage4WayIntersectionEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageTIntersectionEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageLIntersectionEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageGlassSideEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageGlass2SideEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PassageTGlassEnc_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AVTECH_PassageWallEnc"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "PassageLadder2_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Backward }, Vector3.Backward, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "PassageLadderEnc2_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Backward, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "PassageLadderExit2_Large"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Backward }, Vector3.Backward, 0.15f, false, false) },
          }
        },

        // Grated Catwalk Expansion mod
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkRaised"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkEnd"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Left + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Right + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfTJunction"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWidthTJunctionBalcony"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWidthBalcony"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWidthCrossoverLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWidthCrossoverRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkStraight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfMixedCrossroad"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkMixedTJunctionLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkMixedTJunctionRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Right + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWallRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Backward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkHalfWallLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkDiagonalBaseRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkDiagonalBaseLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeCatwalkDiagonalWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeCatwalkEndLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Forward + Vector3.Right, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeCatwalkEndRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Right, Direction.Backward }, Vector3.Forward + Vector3.Right, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkCurvedWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkEndLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkEndRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkHalfWidthCornerA"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Forward + Vector3.Right, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkHalfWidthCornerB"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkMixedCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMRoundCatwalkMixedCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Backward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkCornerBaseLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Left + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkCornerBaseRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Right + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkEndLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Right, Direction.Backward }, Vector3.Left + Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkEndRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Right + Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkLCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Left + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkLCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Right * 0.7f + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkMixedCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkMixedCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkSquareEnd"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward, Direction.Backward, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkStraight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkTJunctionBaseLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Left + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkTJunctionBaseRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Right + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkWallOffset"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalkCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalkCrossoverLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalkCrossoverRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalkCrossroad"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthDiagonalCatwalkTJunction"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkAcuteCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkAcuteCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Forward }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkAngledEndLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkAngledEndRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkDiagonalWallLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkDiagonalWallRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkNarrowEndLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkNarrowEndRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkObtuseCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Left + Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkObtuseCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Right + Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkStraightLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkStraightRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkWallLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Right }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkWallRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkWideEndLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSlopeBaseCatwalkWideEndRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkDiagonalTJunction"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Backward + Vector3.Left, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkDiagonalWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkEndLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkEndRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkHalfCornerA"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkHalfCornerB"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkMixedCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkMixedCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkMixedTJunctionLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedCatwalkMixedTJunctionRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkCurvedWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkDiagonalTJunction"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkEndLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkEndRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkHalfCornerA"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkHalfCornerB"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Backward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkMixedCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkMixedCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkMixedTJunctionLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfSlopeInvertedRoundedCatwalkMixedTJunctionRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkCurvedWallLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkCurvedWallRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkDiagonalCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkDiagonalCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkEnd"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkFullCurvedWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkHalfWidthBalcony"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkStraight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfStadiumCatwalkStraightWall"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMQuadrantCatwalk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMQuadrantCatwalkCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Forward + Vector3.Right, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedCatwalkPassageSupported"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedCatwalkPassageSupportedStraight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedCatwalkPassageBaseless"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedCatwalkPassageBaselessStraight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHingeSupportStrut"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHingeSupportStrutLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHingeSupportStrutRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMStairsSupport"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMStairsSupportCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSupportLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSupportRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSupportCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMLadderCap"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMLadderCapCaged"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward, Direction.Right, Direction.Left }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "GCMLadderWithCage"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "GCMSquareCatwalkLadderBottom"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "GCMSquareCatwalkLadderMiddle"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkLadderTop"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "GCMSquareCatwalkEndLadderBottom"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "GCMSquareCatwalkEndLadderMiddle"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkEndLadderTop"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "GCMSquareCatwalkStraightLadderBottom"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward, Direction.Backward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "GCMSquareCatwalkStraightLadderMiddle"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkStraightLadderTop"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "GCMSquareCatwalkWallLadderBottom"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Ladder2), "GCMSquareCatwalkWallLadderMiddle"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMSquareCatwalkWallLadderTop"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedStairs1x2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Forward, 0.2f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedStairsWithGratedSides1x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up , 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedStairsWithGratedSides1x2"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Forward, 0.2f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedStairsWithGratedSides1x1Stackable"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMGratedStairsWithGratedSides1x2Stackable"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Up }, Vector3.Forward, 0.2f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkStraightWithStairsLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkStraightWithStairsRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkBranchingWithStairsLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkBranchingWithStairsRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkWallWithStairsRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthCatwalkWallWithStairsLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, true, true) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsUTurnLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsUTurnRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepBaseLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Backward, Direction.Down }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepBaseRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Right, Direction.Backward, Direction.Down }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepBaseWALLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepBaseWALRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepBaseSTRLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepBaseSTRRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepBaseBRALeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepBaseBRARight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepTipLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward, Direction.Backward, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepTipRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Forward, Direction.Backward, Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepTipWALLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepTipWALRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepTipSTRLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepTipSTRRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Left }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepCornerLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepCornerRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepUTurnLeft"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "GCMHalfWidthStairsSteepUTurnRight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, true, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, true, false) },
          }
        },

        // AQD blocks
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_Concrete_Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Up + Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_Concrete_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward }, Vector3.Backward + Vector3.Left, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_Concrete_Half_Block"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward }, Vector3.Backward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_Concrete_Half_Block_Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_ReinforcedConcrete_Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_ReinforcedConcrete_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_ReinforcedConcrete_Half_Block"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_ReinforcedConcrete_Half_Block_Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Corner_Split_2x1x1_Tip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Forward + Vector3.Right, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Corner_Split_2x1x1_Tip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Forward + Vector3.Right, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_Half_Corner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_Half_Corner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope3x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope3x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope3x1_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Up, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope3x1_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Up, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope3x1_Transition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left,  }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope3x1_TransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left,  }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope3x1_Transition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left,  }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope3x1_TransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left,  }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope4x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.15f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope4x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.15f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope4x1_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Up, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope4x1_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Up, 0.15f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope4x1_Transition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.15f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope4x1_TransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.15f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope4x1_Transition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.1f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope4x1_TransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.1f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope5x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.125f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.375f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope5x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.125f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.25f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.375f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope5x1_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Up, 0.1f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Up, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope5x1_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Up, 0.1f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Up, 0.2f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope5x1_Transition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.15f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.275f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slope5x1_TransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.15f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.275f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope5x1_Transition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.15f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.275f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slope5x1_TransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.15f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left }, Vector3.Up, 0.275f, false, false) },
            { new Vector3I(0,0,4), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Up, 0.5f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Corner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Corner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_HalfPlate"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_HalfPlate"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_HalfSlopeTransition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_HalfSlopeTransition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_HalfSlopeTransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_HalfSlopeTransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Inv_Corner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Inv_Corner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Plate_1x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Plate_1x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Plate_2x1Base"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Plate_2x1Base"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Plate_2x1BaseMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Plate_2x1BaseMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Plate_2x1Tip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Plate_2x1Tip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Plate_2x1TipMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Plate_2x1TipMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Plate_Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Plate_Slope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Plate_Slope2x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Plate_Slope2x1"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Plate_Triangle"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Plate_Triangle"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_RaisedCorner_Inset"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_RaisedCorner_Inset"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_Corner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_Corner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_Half_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_Half_Corner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_InvCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_InvCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_InvCorner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_InvCorner_Split"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_RaisedCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_RaisedCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_SlopeTransition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_SlopeTransition"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_Slab_SlopeTransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_Slab_SlopeTransitionMirror"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_LA_SlabSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "AQD_LG_HA_SlabSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },

        // XL blocks
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_1xFrame"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,2), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,3), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_1xMount"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(1,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_BlockCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_BlockFrame"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(1,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_BlockInvCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_BlockSlope"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(1,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(2,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(3,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(4,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(1,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(2,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(3,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(4,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Down, 1f, false, false) },
            { new Vector3I(0,4,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_BlockSlopedCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,4), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,4), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_BlockSlopedCornerBase"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_BlockSlopedCornerTip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,4), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,4), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_Brace"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_HalfBlockCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_HalfCornerBase"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,1), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,4), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,4), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_HalfCornerBaseInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_HalfCornerTip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,1), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_HalfCornerTipInv"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_HalfSlopeBase"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_HalfSlopeTip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_Hip"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,0,1), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward, Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_Passage"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(4,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "XL_PassageCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,1), MyTuple.Create(new Direction[] {  Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,1), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,1), MyTuple.Create(new Direction[] {  Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,2), MyTuple.Create(new Direction[] {  Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,2), MyTuple.Create(new Direction[] {  Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,3), MyTuple.Create(new Direction[] {  Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,3), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,3), MyTuple.Create(new Direction[] {  Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,3), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,3), MyTuple.Create(new Direction[] {  Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,1,4), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,2,4), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,3,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,3,4), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,4,4), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(3,4,4), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
          }
        },

        // Contact DLC blocks
        {
          new MyDefinitionId(typeof(MyObjectBuilder_PistonTop), "LargePistonTopReskin"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up }, Vector3.Down, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "LargeBlockSmallGate"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Door), "LargeBlockEvenWideDoor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Up, Direction.Right, Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "LargeBlockCompactRadioAntennaReskin"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right, Direction.Forward }, Vector3.Backward, 0.3f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  Direction.Left, Direction.Forward }, Vector3.Backward, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockModularBridgeCockpit"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockModularBridgeRaisedSlopedCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Left, Direction.Forward, Direction.Backward }, Vector3.Up, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockModularBridgeHalfSlopedCorner"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward }, Vector3.Forward + Vector3.Left, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockModularBridgeCorner2x1BaseL"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Right, Direction.Backward, Direction.Forward }, Vector3.Up, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockModularBridgeCorner2x1BaseR"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Down, Direction.Left, Direction.Backward, Direction.Forward }, Vector3.Up, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ButtonPanel), "LargeBlockModularBridgeButtonPanel"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Right, Direction.Forward }, Vector3.Backward, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockModularBridgeEmpty"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Right, Direction.Forward }, Vector3.Backward, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockModularBridgeFloor"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Right, Direction.Forward }, Vector3.Backward, 0.3f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockModularBridgeSideL"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Right }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockModularBridgeSideR"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Left }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ReflectorLight), "LargeBlockFloodlight"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ReflectorLight), "LargeBlockFloodlightAngled"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Forward, 0.25f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ReflectorLight), "LargeBlockFloodlightCornerL"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Forward, 0.35f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_ReflectorLight), "LargeBlockFloodlightCornerR"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Backward }, Vector3.Forward, 0.35f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_LargeMissileTurret), "LargeMissileTurretReskin"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] { Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_LargeGatlingTurret), "LargeGatlingTurretReskin"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,0), MyTuple.Create(new Direction[] { Direction.Up, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,0), MyTuple.Create(new Direction[] { Direction.Right, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,0), MyTuple.Create(new Direction[] { Direction.Left, Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,0), MyTuple.Create(new Direction[] { Direction.Backward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,0), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,1), MyTuple.Create(new Direction[] { Direction.Up, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Right }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,1), MyTuple.Create(new Direction[] { Direction.Down }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,1), MyTuple.Create(new Direction[] { Direction.Down, Direction.Left }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,0,2), MyTuple.Create(new Direction[] { Direction.Up, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,0,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,1,2), MyTuple.Create(new Direction[] { Direction.Right, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,1,2), MyTuple.Create(new Direction[] { Direction.Left, Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(0,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(1,2,2), MyTuple.Create(new Direction[] { Direction.Forward }, Vector3.Zero, 0f, false, false) },
            { new Vector3I(2,2,2), MyTuple.Create(new Direction[] {  }, Vector3.Zero, 0f, false, false) },
          }
        },
        {
          new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "LargeBlockCaptainDesk"), new Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>(Vector3I.Comparer)
          {
            { new Vector3I(0,0,0), MyTuple.Create(new Direction[] {  Direction.Up, Direction.Down, Direction.Left, Direction.Right, Direction.Forward, Direction.Backward }, Vector3.Zero, 0f, false, false) },
          }
        },
      };

      _logger.Info("Init Block Info: End");
      _logger.Info($"Number of block entries: {_invalidBlockDirInfo.Count}");
    }
  }
}
