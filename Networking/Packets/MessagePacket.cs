using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class MessagePacket : PacketBase
  {
    [ProtoMember(1)] public string Message;
    [ProtoMember(2)] public string Color;
    [ProtoMember(3)] public int TimeToLive;

    public MessagePacket() { }

    public MessagePacket(string msg, string color = "Red", int ttl = 2000)
    {
      Message = msg;
      Color = color;
      TimeToLive = ttl;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      AiSession.Instance.ShowMessage(Message, Color, TimeToLive);
      return false;
    }
  }
}
