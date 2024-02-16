using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots;

using ProtoBuf;

using Sandbox.ModAPI;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  internal class ResetMapPacket : PacketBase
  {
    [ProtoMember(1)] public long MapId;
    [ProtoMember(2)] public bool ObstaclesOnly;

    public ResetMapPacket() { }

    public ResetMapPacket(long id, bool onlyObstacles = false)
    {
      MapId = id;
      ObstaclesOnly = onlyObstacles;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      CubeGridMap map = null;
      if (AiSession.Instance?.GridGraphDict?.TryGetValue(MapId, out map) == true)
      {
        if (map?.IsValid == true)
        {
          if (ObstaclesOnly && map.MainGrid != null)
          {
            map.ClearTempObstacles();

            List<BotBase> helpers = null;
            var playerId = MyAPIGateway.Players.TryGetIdentityId(SenderId);

            if (playerId > 0 && AiSession.Instance?.PlayerToHelperDict?.TryGetValue(playerId, out helpers) == true && helpers?.Count > 0)
            {
              for (int i = 0; i < helpers.Count; i++)
              {
                try
                {
                  var bot = helpers[i];
                  var gridGraph = bot?._currentGraph as CubeGridMap;
                  if (gridGraph?.MainGrid?.EntityId == map.MainGrid.EntityId)
                  {
                    bot._pathCollection?.ClearObstacles(true);
                  }
                }
                catch { }
              }
            }
          }
          else
          {
            map.GraphLocked = false;
            map.Init();
          }
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
