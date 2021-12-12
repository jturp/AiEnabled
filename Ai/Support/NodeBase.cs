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
  public abstract class NodeBase : IEqualityComparer<NodeBase>, IComparer<NodeBase>
  {
    public Vector3I Position;
    public Vector3D? SurfacePosition;
    public List<Vector3I> BlockedEdges;
    public MyCubeGrid Grid;
    public IMySlimBlock Block;
    public bool IsGridNode;
    public bool IsLadder;
    public bool IsGridNodePlanetTile;
    public bool IsGridNodeAdditional;
    public bool IsGridNodeUnderGround;
    public bool IsGroundNode;
    public bool IsWaterNode;
    public bool IsAirNode;
    public bool IsSpaceNode;
    public bool IsTunnelNode;

    public NodeBase(Vector3I position, MyCubeGrid grid = null, IMySlimBlock block = null, Vector3D? surfacePos = null)
    {
      Position = position;
      IsGridNode = grid != null;
      SurfacePosition = surfacePos;
      Grid = grid;
      Block = block;
      IsLadder = block != null && AiSession.Instance.LadderBlockDefinitions.Contains(block.BlockDefinition.Id);
    }

    public int Compare(NodeBase x, NodeBase y)
    {
      return x.Position.CompareTo(y.Position);
    }

    public bool Equals(NodeBase x, NodeBase y)
    {
      return x.Position == y.Position;
    }

    public int GetHashCode(NodeBase nb)
    {
      return nb.Position.GetHashCode();
    }

    public override bool Equals(object obj)
    {
      var nb = obj as NodeBase;
      if (nb == null)
        return false;

      return this.Position == nb.Position;
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
