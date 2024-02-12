﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;

using ProtoBuf;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  internal class ResetMapPacket : PacketBase
  {
    [ProtoMember(1)] public long MapId;

    public ResetMapPacket() { }

    public ResetMapPacket(long id)
    {
      MapId = id;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      CubeGridMap map = null;
      if (AiSession.Instance?.GridGraphDict?.TryGetValue(MapId, out map) == true)
      {
        if (map?.IsValid == true)
        {
          map.GraphLocked = false;
          map.Init();
        }
        else
        {
          map?.Close();
          AiSession.Instance.GridGraphDict.Remove(MapId);
        }
      }

      return false;
    }
  }
}
