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

    public MessagePacket() { }

    public MessagePacket(string msg)
    {
      Message = msg;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      AiSession.Instance.ShowMessage(Message);
      return false;
    }
  }
}
