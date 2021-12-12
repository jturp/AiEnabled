using VRageMath;

namespace AiEnabled.Ai.Support.PriorityQ
{
  public interface IFastPriorityQueueNode
  {
    /// <summary>
    /// The Priority to insert this node at.
    /// Cannot be manually edited - see queue.Enqueue() and queue.UpdatePriority() instead
    /// </summary>
    int Priority { get; set; }

    /// <summary>
    /// Represents the current position in the queue
    /// </summary>
    int QueueIndex { get; set; }

#if DEBUG
    /// <summary>
    /// The queue this node is tied to. Used only for debug builds.
    /// </summary>
    object Queue { get; set; }
#endif
  }

  public struct VectorNode : IFastPriorityQueueNode
  {
    public Vector3I Position;
    public int Priority { get; set; }
    public int QueueIndex { get; set; }
    public object Queue { get; set; }

    public VectorNode(Vector3I pos)
    {
      Position = pos;
      Priority = -1;
      QueueIndex = -1;
      Queue = null;
    }

    public void Update(Vector3I pos)
    {
      Position = pos;
      Priority = -1;
      QueueIndex = -1;
      Queue = null;
    }
  }
}