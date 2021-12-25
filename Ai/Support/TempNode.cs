using Sandbox.Game.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Ai.Support
{
  public class TempNode : Node
  {
    public TempNode() { }

    public TempNode(Vector3I position, Vector3 surfaceOffset, MyCubeGrid grid = null, IMySlimBlock block = null) : base(position, surfaceOffset, grid, block) { }

    public TempNode(Vector3I position, Vector3 surfaceOffset, NodeType nType, int blockMask, MyCubeGrid grid = null, IMySlimBlock block = null) : base(position, surfaceOffset, nType, blockMask, grid, block) { }
  }
}
