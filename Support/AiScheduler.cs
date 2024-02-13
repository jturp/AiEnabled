using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Collections;
using ParallelTasks;
using Sandbox.ModAPI;
using AiEnabled.Utilities;

namespace AiEnabled.Support
{
  public class AiScheduler
  {
    internal class FutureActionItem
    {
      internal Action Action;
      internal int TickDelay;

      internal void Clear()
      {
        Action = null;
        TickDelay = 0;
      }
    }

    AiEPool<FutureActionItem> _actionPool = new AiEPool<FutureActionItem>
    (
      defaultCapacity: 25, 
      clear: f => f.Clear(), 
      deactivator: f => f.Clear(),
      activator: () => new FutureActionItem()
    );

    MyQueue<FutureActionItem> _futureActions = new MyQueue<FutureActionItem>();
    List<FutureActionItem> _actionsToAdd = new List<FutureActionItem>();

    public void UpdateAndExecuteJobs()
    {
      for (int i = 0; i < _actionsToAdd.Count; i++)
      {
        _futureActions.Enqueue(_actionsToAdd[i]);
      }

      _actionsToAdd.Clear();

      for (int i = 0; i < _futureActions.Count; i++)
      {
        var future = _futureActions.Dequeue();
        future.TickDelay--;
        if (future.TickDelay <= 0)
        {
          future.Action?.Invoke();
          _actionPool?.Return(ref future);
        }
        else
        {
          _futureActions.Enqueue(future);
        }
      }
    }

    public void Schedule(Action callback, int delay = 1)
    {
      var future = _actionPool.Get();
      future.Action = callback;
      future.TickDelay = delay;
      _actionsToAdd.Add(future);
    }

    public void Close()
    {
      _actionPool?.Clean();
      _futureActions?.Clear();
      _actionsToAdd?.Clear();

      _actionPool = null;
      _futureActions = null;
      _actionsToAdd = null;
    }
  }
}
