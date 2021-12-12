using System.Collections.Generic;

using Sandbox.Game.Entities;

using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Ai.Support
{
  public class Node : NodeBase
  {
    public Node(Vector3I position, MyCubeGrid grid, IMySlimBlock block, Vector3D? surfacePos = null)
      : base(position, grid, block, surfacePos) { }

    //public void CheckNeighbors(GridBase gBase)
    //{
    //  foreach (var dirVec in gBase.GetBlockedNodeEdges(this))
    //  {
    //    if (BlockedEdges == null)
    //      BlockedEdges = new List<Vector3I>();

    //    if (!BlockedEdges.Contains(dirVec))
    //      BlockedEdges.Add(dirVec);
    //  }
    //}

    public bool AddBlockage(Vector3I dirVec)
    {
      if (BlockedEdges == null)
        BlockedEdges = new List<Vector3I>();

      if (BlockedEdges.Contains(dirVec))
        return false;

      BlockedEdges.Add(dirVec);
      return true;
    }
  }
}
