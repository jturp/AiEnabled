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
    public NodeType NodeType;
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
    public bool IsBlocked(Vector3I pos) => (BlockedMask & GetBlockedMask(pos)) != 0;

    public Node() { }

    public Node(Vector3I position, Vector3 surfaceOffset, MyCubeGrid grid = null, IMySlimBlock block = null)
    {
      Position = position;
      Offset = surfaceOffset;
      NodeType = NodeType.None;
      BlockedMask = 0;

      if (block != null)
        _ref = block;
      else
        _ref = grid;

      if (grid != null)
      {
        NodeType |= NodeType.Grid;

        if (block != null && AiSession.Instance.LadderBlockDefinitions.Contains(block.BlockDefinition.Id))
          NodeType |= NodeType.Ladder;
      }
    }

    public Node(Vector3I position, Vector3 surfaceOffset, NodeType nType, int blockMask, MyCubeGrid grid = null, IMySlimBlock block = null)
    {
      Position = position;
      Offset = surfaceOffset;
      NodeType = nType;
      BlockedMask = blockMask;

      if (block != null)
        _ref = block;
      else
        _ref = grid;

      if (grid != null)
      {
        NodeType |= NodeType.Grid;

        if (block != null && AiSession.Instance.LadderBlockDefinitions.Contains(block.BlockDefinition.Id))
          NodeType |= NodeType.Ladder;
      }
    }

    public void Update(Vector3I position, Vector3 surfaceOffset, NodeType nType, int blockMask, MyCubeGrid grid = null, IMySlimBlock block = null)
    {
      Position = position;
      Offset = surfaceOffset;
      BlockedMask = blockMask;
      NodeType = nType;

      if (block != null)
        _ref = block;
      else
        _ref = grid;

      if (grid != null)
      {
        NodeType |= NodeType.Grid;

        if (block != null && AiSession.Instance.LadderBlockDefinitions.Contains(block.BlockDefinition.Id))
          NodeType |= NodeType.Ladder;
      }
    }

    public void SetNodeType(NodeType nType)
    {
      NodeType |= nType;
    }

    public void RemoveNodeType(NodeType nType)
    {
      NodeType &= ~nType;
    }

    public bool SetBlocked(Vector3I pos)
    {
      var mask = GetBlockedMask(pos);

      if ((BlockedMask & mask) == 0)
      {
        BlockedMask |= mask;
        return true;
      }

      return false;
    }

    int GetBlockedMask(Vector3I pos)
    {
      var rel = pos - Position + 1;
      return 1 << (rel.X * 9 + rel.Y * 3 + rel.Z);
    }

    public bool IsSpaceNode(GridBase gBase)
    {
      if ((NodeType & NodeType.Ground) > 0)
        return false;

      var worldPos = gBase.LocalToWorld(Position) + Offset;

      float _;
      var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPos, out _);
      return gravity.LengthSquared() <= 0;
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
