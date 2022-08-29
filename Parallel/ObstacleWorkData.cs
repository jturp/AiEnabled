using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ParallelTasks;

using Sandbox.Game.Entities;

using VRage.Game.Entity;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Parallel
{
  public class ObstacleWorkData : WorkData
  {
    public List<IMySlimBlock> Blocks;
    public List<MyEntity> Entities;

    public ObstacleWorkData() { }

    public ObstacleWorkData(List<IMySlimBlock> list)
    {
      Blocks = list;
    }
  }
}
