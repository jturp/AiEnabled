using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiEnabled.Utilities
{
  public class AiEPool<T> where T : class, new()
  {
    ConcurrentStack<T> _stack;
    Action<T> _clear;
    Action<T> _deactivator;
    Func<T> _activator;

    public AiEPool(int defaultCapacity = 10, Action<T> clear = null, Action<T> deactivator = null, Func<T> activator = null)
    {
      _stack = new ConcurrentStack<T>();
      _clear = clear;
      _deactivator = deactivator;
      _activator = activator ?? new Func<T>(() => new T());

      if (defaultCapacity > 0) 
      {
        for (int i = 0; i < defaultCapacity; i++) 
        {
          _stack.Push(new T());
        }
      }
    }

    public T Get()
    {
      T result;
      if (_stack.TryPop(out result))
        return result;

      return _activator();
    }

    public void Return(ref T instance)
    {
      _clear?.Invoke(instance);
      _stack.Push(instance);

      instance = null;
    }

    public void Clean()
    {
      if (_stack != null)
      {
        if (_deactivator != null)
        {
          T instance;
          while (_stack.TryPop(out instance))
          {
            _deactivator.Invoke(instance);
          }
        }
        else
        {
          _stack.Clear();
        }
      }

      _activator = null;
      _deactivator = null;
      _clear = null;
      _stack = null;
    }
  }
}
