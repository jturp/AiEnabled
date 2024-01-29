using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.API;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Ai.Support
{
  [Flags]
  public enum NodeType : ushort
  {
    None = 0x0,
    Grid = 0x1,
    Ladder = 0x2,
    GridPlanet = 0x4,
    GridAdditional = 0x8,
    GridUnderground = 0x10,
    Ground = 0x20,
    Water = 0x40,
    Tunnel = 0x80,
    Door = 0x100,
    Hangar = 0x200,
    Catwalk = 0x400
  }

  public class Node : IEqualityComparer<Node>, IComparer<Node>
  {
    public Vector3I Position;
    public Vector3D Offset;
    public int BlockedMask;
    public int TempBlockedMask;
    public short MovementCost = 1;
    public short AddedMovementCost = 0;
    public NodeType NodeType;
    bool? _isSpaceNode;
    object _ref;

    public MyCubeGrid Grid => (Block?.CubeGrid ?? _ref) as MyCubeGrid;
    public IMySlimBlock Block => _ref as IMySlimBlock;
    public bool IsGridNode => (NodeType & NodeType.Grid) > 0;
    public bool IsLadder => (NodeType & NodeType.Ladder) > 0;
    public bool IsGridNodePlanetTile => (NodeType & NodeType.GridPlanet) > 0;
    public bool IsGridNodeAdditional => (NodeType & NodeType.GridAdditional) > 0;
    public bool IsGridNodeUnderGround => (NodeType & NodeType.GridUnderground) > 0;
    public bool IsGroundNode => (NodeType & NodeType.Ground) > 0;
    public bool IsWaterNode => (NodeType & NodeType.Water) > 0;
    public bool IsAirNode => (NodeType & NodeType.Ground) == 0;
    public bool IsTunnelNode => (NodeType & NodeType.Tunnel) > 0;
    public bool IsDoor => (NodeType & NodeType.Door) > 0;
    public bool IsHangarDoor => (NodeType & NodeType.Hangar) > 0;
    public bool IsVoxelNode => IsGridNodePlanetTile || (NodeType & NodeType.Grid) == 0;
    public bool IsCatwalk => (NodeType & NodeType.Catwalk) > 0;
    public bool IsSpaceNode(GridBase gBase) => _isSpaceNode ?? IsNodeInSpace(gBase);
    public void ResetTempBlocked() => TempBlockedMask = 0;

    public Node() { }

    public void Reset()
    {
      Position = Vector3I.Zero;
      Offset = Vector3D.Zero;
      NodeType = NodeType.None;
      MovementCost = 5;
      AddedMovementCost = 0;
      BlockedMask = 0;
      TempBlockedMask = 0;
      _isSpaceNode = null;
      _ref = null;
    }

    public void CalculateMovementCost(GridBase gBase)
    {
      MovementCost = AiSession.Instance.MovementCostData.MovementCostDict["Base"];

      if (IsWaterNode)
        MovementCost += AiSession.Instance.MovementCostData.MovementCostDict["Water"];
      else if (IsAirNode)
        MovementCost += AiSession.Instance.MovementCostData.MovementCostDict["Air"];

      if (IsVoxelNode)
        MovementCost += AiSession.Instance.MovementCostData.MovementCostDict["Voxel"];

      if (IsTunnelNode)
        MovementCost += AiSession.Instance.MovementCostData.MovementCostDict["Tunnel"];

      if (IsGridNode)
      {
        if (IsDoor)
        {
          if (IsHangarDoor)
            MovementCost += AiSession.Instance.MovementCostData.MovementCostDict["Door"];
          else
            MovementCost += AiSession.Instance.MovementCostData.MovementCostDict["Hangar"];
        }
        else if (IsLadder)
          MovementCost += AiSession.Instance.MovementCostData.MovementCostDict["Ladder"];
        else if (IsCatwalk)
          MovementCost += AiSession.Instance.MovementCostData.MovementCostDict["Catwalk"];

        if (AiSession.Instance.ModSaveData.IncreaseNodeWeightsNearWeapons)
        {
          var gridMap = gBase as CubeGridMap;
          if (gridMap?.WeaponPositions?.Count > 0)
          {
            var grid = gridMap.MainGrid;
            var worldPosition = grid.GridIntegerToWorld(Position) + Offset;
            var hitInfo = AiSession.Instance.CubeGridHitInfo;

            var closestDistance = int.MaxValue;
            foreach (var point in gridMap.WeaponPositions)
            {
              var worldPoint = grid.GridIntegerToWorld(point);
              var line = new LineD(worldPoint, worldPosition);
              bool hitSomething = grid.GetIntersectionWithLine(ref line, ref hitInfo);

              if (!hitSomething || hitInfo.Position == Position)
              {
                var distance = (point - Position).Length();
                if (distance < closestDistance)
                {
                  closestDistance = distance;
                }
              }
            }

            var num = 5 - (closestDistance / 7);
            if (num > 0)
              AddedMovementCost = (byte)(num * num);
          }
        }
      }

      MovementCost = Math.Max((short)1, MovementCost);
    }

    public void Update(Node other, Vector3D surfaceOffset)
    {
      Offset = surfaceOffset;
      Position = other.Position;
      BlockedMask = other.BlockedMask;
      TempBlockedMask = other.TempBlockedMask;
      NodeType = other.NodeType;
      MovementCost = other.MovementCost;
      AddedMovementCost = other.AddedMovementCost;
      _isSpaceNode = other._isSpaceNode;
      _ref = other._ref;
    }

    public void Update(Vector3I position, Vector3D surfaceOffset, GridBase gBase, NodeType nType, int blockMask, MyCubeGrid grid = null, IMySlimBlock block = null)
    {
      Position = position;
      Offset = surfaceOffset;
      BlockedMask = blockMask;
      NodeType = nType;
      MovementCost = 1;
      _isSpaceNode = null;

      if (grid != null)
      {
        NodeType |= NodeType.Grid;

        if (block != null)
        {
          _ref = block;

          if (AiSession.Instance.LadderBlockDefinitions.Contains(block.BlockDefinition.Id))
          {
            NodeType |= NodeType.Ladder;
          }
          else if (block.FatBlock is IMyDoor)
          {
            NodeType |= NodeType.Door;

            if (block.FatBlock is IMyAirtightHangarDoor || block.BlockDefinition.Id.SubtypeName.Contains("Gate"))
              NodeType |= NodeType.Hangar;
          }
          else if (AiSession.Instance.CatwalkBlockDefinitions.Contains(block.BlockDefinition.Id))
            NodeType |= NodeType.Catwalk;
        }
        else
        {
          _ref = grid;
        }
      }

      CalculateMovementCost(gBase);
    }

    public void SetNodeType(NodeType nType, GridBase gBase)
    {
      NodeType |= nType;
      CalculateMovementCost(gBase);
    }

    public void RemoveNodeType(NodeType nType, GridBase gBase)
    {
      NodeType &= ~nType;
      CalculateMovementCost(gBase);
    }

    public bool SetBlocked(Vector3I dir)
    {
      var mask = GetBlockedMask(dir);

      if ((BlockedMask & mask) == 0)
      {
        BlockedMask |= mask;
        return true;
      }

      return false;
    }

    public bool SetBlockedTemp(Vector3I dir)
    {
      var mask = GetBlockedMask(dir);

      if ((TempBlockedMask & mask) == 0)
      {
        BlockedMask |= mask;
        return true;
      }

      return false;
    }

    public bool IsBlocked(Vector3I dir)
    {
      var mask = GetBlockedMask(dir);

      return (BlockedMask & mask) > 0 || (TempBlockedMask & mask) > 0;
    }

    int GetBlockedMask(Vector3I pos)
    {
      var rel = pos - Position + 1;
      return 1 << (rel.X * 9 + rel.Y * 3 + rel.Z);
    }

    bool IsNodeInSpace(GridBase gBase)
    {
      if ((NodeType & NodeType.Ground) > 0)
      {
        _isSpaceNode = false;
        return false;
      }

      var worldPos = gBase.LocalToWorld(Position) + Offset;

      var gridGraph = gBase as CubeGridMap;
      if (gridGraph != null && gridGraph.UnbufferedOBB.Contains(ref worldPos))
      {
        _isSpaceNode = false;
        return false;
      }

      float _;
      var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPos, out _);
      var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(worldPos, 0);
      return nGrav.LengthSquared() <= 0 && aGrav.LengthSquared() <= 0;
    }

    /// <summary>
    /// Debug only
    /// </summary>
    public string GetBlockedEdges(StringBuilder sb)
    {
      sb.Clear();

      if (IsBlocked(Vector3I.Up))
        sb.Append("Up ");

      if (IsBlocked(Vector3I.Down))
        sb.Append("Down ");

      if (IsBlocked(Vector3I.Left))
        sb.Append("Left ");

      if (IsBlocked(Vector3I.Right))
        sb.Append("Right ");

      if (IsBlocked(Vector3I.Forward))
        sb.Append("Forward ");

      if (IsBlocked(Vector3I.Backward))
        sb.Append("Backward");

      return sb.ToString();
    }

    public int Compare(Node x, Node y)
    {
      return x.Position.CompareTo(y.Position);
    }

    public bool Equals(Node x, Node y)
    {
      return x.Position == y.Position;
    }

    public int GetHashCode(Node nb)
    {
      return nb.Position.GetHashCode();
    }

    public override int GetHashCode()
    {
      return Position.GetHashCode();
    }

    public override string ToString()
    {
      return Position.ToString();
    }
  }
}
