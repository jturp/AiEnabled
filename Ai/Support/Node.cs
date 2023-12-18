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
  public enum NodeType : byte
  {
    None = 0,
    Grid = 1,
    Ladder = 2,
    GridPlanet = 4,
    GridAdditional = 8,
    GridUnderground = 16,
    Ground = 32,
    Water = 64,
    Tunnel = 128
  }

  public class Node : IEqualityComparer<Node>, IComparer<Node>
  {
    public Vector3I Position;
    public Vector3 Offset;
    public int BlockedMask;
    public int TempBlockedMask;
    public byte MovementCost = 1;
    public byte AddedMovementCost = 0;
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
    public bool IsSpaceNode(GridBase gBase) => _isSpaceNode ?? IsNodeInSpace(gBase);
    public void ResetTempBlocked() => TempBlockedMask = 0;

    public Node() { }

    public Node(Vector3I position, Vector3 surfaceOffset, GridBase gBase, MyCubeGrid grid = null, IMySlimBlock block = null)
    {
      Position = position;
      Offset = surfaceOffset;
      NodeType = NodeType.None;
      BlockedMask = 0;

      if (grid != null)
      {
        NodeType |= NodeType.Grid;

        if (block != null)
        {
          _ref = block;

          if (AiSession.Instance.LadderBlockDefinitions.Contains(block.BlockDefinition.Id))
          {
            NodeType |= NodeType.Ladder;
            MovementCost++;
          }
          else if (block.FatBlock is IMyDoor)
          {
            MovementCost++;

            if (block.FatBlock is IMyAirtightHangarDoor)
              MovementCost++;
          }
        }
        else
        {
          _ref = grid;
        }
      }

      if (IsGridNode)
        CalculateMovementCost(gBase);
    }

    public Node(Vector3I position, Vector3 surfaceOffset, GridBase gBase, NodeType nType, int blockMask, MyCubeGrid grid = null, IMySlimBlock block = null)
    {
      Position = position;
      Offset = surfaceOffset;
      NodeType = nType;
      BlockedMask = blockMask;
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
            MovementCost++;
          }
          else if (block.FatBlock is IMyDoor)
          {
            MovementCost++;

            if (block.FatBlock is IMyAirtightHangarDoor)
              MovementCost++;
          }
        }
        else
        {
          _ref = grid;
        }
      }

      if (IsGridNode)
        CalculateMovementCost(gBase);
    }

    public void CalculateMovementCost(GridBase gBase)
    {
      if (IsAirNode || IsWaterNode)
        MovementCost++;

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

    public void Update(Node other, Vector3 surfaceOffset)
    {
      Offset = surfaceOffset;
      Position = other.Position;
      BlockedMask = other.BlockedMask;
      NodeType = other.NodeType;
      MovementCost = other.MovementCost;
      _isSpaceNode = other._isSpaceNode;
      _ref = other._ref;
    }

    public void Update(Vector3I position, Vector3 surfaceOffset, GridBase gBase, NodeType nType, int blockMask, MyCubeGrid grid = null, IMySlimBlock block = null)
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
            MovementCost++;
          }
          else if (block.FatBlock is IMyDoor)
          {
            MovementCost++;

            if (block.FatBlock is IMyAirtightHangarDoor)
              MovementCost++;
          }
        }
        else
        {
          _ref = grid;
        }
      }

      if (IsGridNode)
        CalculateMovementCost(gBase);
    }

    public void SetNodeType(NodeType nType)
    {
      NodeType |= nType;
    }

    public void RemoveNodeType(NodeType nType)
    {
      NodeType &= ~nType;
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
