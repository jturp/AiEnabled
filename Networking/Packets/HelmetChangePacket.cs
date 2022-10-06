using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;

using VRage.Game.ModAPI;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class HelmetChangePacket : PacketBase
  {
    [ProtoMember(1)] readonly long _botEntityId;

    public HelmetChangePacket() { }

    public HelmetChangePacket(long id)
    {
      _botEntityId = id;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      var bot = MyEntities.GetEntityById(_botEntityId) as IMyCharacter;
      if (bot != null)
      {
        var oxyComp = bot.Components.Get<MyCharacterOxygenComponent>();
        oxyComp?.SwitchHelmet();
      }

      return false;
    }
  }
}
