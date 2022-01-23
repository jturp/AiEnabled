using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

using Sandbox.Game;
using Sandbox.ModAPI;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class RepChangePacket : PacketBase
  {
    [ProtoMember(1)] public long BotIdentityId;
    [ProtoMember(2)] public long BotFactionId;
    [ProtoMember(3)] public long OwnerIdentityId;
    [ProtoMember(4)] public long OwnerFactionId;
    [ProtoMember(5)] public int Reputation;

    public RepChangePacket() { }

    public RepChangePacket(long botId, long botFactionId, long ownerId, long ownerFactionId, int rep)
    {
      BotIdentityId = botId;
      BotFactionId = botFactionId;
      OwnerIdentityId = ownerId;
      OwnerFactionId = ownerFactionId;
      Reputation = rep;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      try
      {
        var ownerFaction = MyAPIGateway.Session.Factions.TryGetFactionById(OwnerFactionId);
        if (ownerFaction == null)
        {
          AiSession.Instance.Logger.Log($"RepChangePacket.Received: OwnerFaction was null for ID {OwnerFactionId}", Utilities.MessageType.WARNING);
          return false;
        }

        var botFaction = MyAPIGateway.Session.Factions.TryGetFactionById(BotFactionId);
        if (botFaction == null)
        {
          AiSession.Instance.Logger.Log($"RepChangePacket.Received: BotFaction was null for ID {BotFactionId}", Utilities.MessageType.WARNING);
          return false;
        }

        if (MyAPIGateway.Session.Factions.AreFactionsEnemies(BotFactionId, OwnerFactionId))
        {
          MyAPIGateway.Session.Factions.ChangeAutoAccept(BotFactionId, BotIdentityId, true, true);
          MyAPIGateway.Session.Factions.ChangeAutoAccept(OwnerFactionId, OwnerIdentityId, ownerFaction.AutoAcceptMember, true);
          MyAPIGateway.Session.Factions.AcceptPeace(OwnerFactionId, BotFactionId);
        }

        MyVisualScriptLogicProvider.SetPlayersFaction(BotIdentityId, botFaction.Tag);
        MyAPIGateway.Session.Factions.SetReputation(BotFactionId, OwnerFactionId, int.MaxValue);
        MyAPIGateway.Session.Factions.SetReputation(OwnerFactionId, BotFactionId, int.MaxValue);
        MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(BotIdentityId, OwnerFactionId, int.MaxValue);
        MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(OwnerIdentityId, BotFactionId, int.MaxValue);

        foreach (var kvp in MyAPIGateway.Session.Factions.Factions)
        {
          if (kvp.Key == OwnerFactionId || kvp.Key == BotFactionId)
            continue;

          var rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(OwnerIdentityId, kvp.Key);
          if (rep == 0)
            continue;

          MyAPIGateway.Session.Factions.SetReputation(BotFactionId, kvp.Key, rep);
          MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(BotIdentityId, kvp.Key, rep);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in RepChangePacket.Received: {ex.Message}\n{ex.StackTrace}", Utilities.MessageType.ERROR);
      }

      return false;
    }
  }
}
