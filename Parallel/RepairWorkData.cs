using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ParallelTasks;

using VRage.Game.ModAPI;

namespace AiEnabled.ModFiles.Parallel
{
  public class RepairWorkData : WorkData
  {
    public IMySlimBlock Block;
    public IMyCharacter Bot;

    public RepairWorkData() { }

    public RepairWorkData(IMySlimBlock block, IMyCharacter bot)
    {
      Block = block;
      Bot = bot;
    }
  }
}
