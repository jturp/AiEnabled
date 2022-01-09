using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;

using ParallelTasks;

using VRage.Game.ModAPI;

namespace AiEnabled.Parallel
{
  public class RepairWorkData : WorkData
  {
    public IMySlimBlock Block;
    public BotBase Bot;

    public RepairWorkData() { }

    public RepairWorkData(IMySlimBlock block, BotBase bot)
    {
      Block = block;
      Bot = bot;
    }
  }
}
