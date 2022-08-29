﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.ConfigData;

using ProtoBuf;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class SettingRequestPacket : PacketBase
  {
    public SettingRequestPacket() { }
    public override bool Received(NetworkHandler netHandler)
    {
      List<SerializableBotPrice> prices = new List<SerializableBotPrice>();
      foreach (var kvp in AiSession.Instance.BotComponents)
      {
        var reqs = kvp.Value;
        long credits;
        AiSession.Instance.BotPrices.TryGetValue(kvp.Key, out credits);
        
        var serialPrice = new SerializableBotPrice(kvp.Key, credits, reqs);
        prices.Add(serialPrice);
      }

      var pkt = new AdminPacket(AiSession.Instance.MaxBots, AiSession.Instance.MaxHelpers, AiSession.Instance.ModSaveData.MaxBotProjectileDistance, AiSession.Instance.AllowMusic, prices);
      netHandler.SendToPlayer(pkt, SenderId);

      return false;
    }
  }
}
