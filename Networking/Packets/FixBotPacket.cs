using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots;
using AiEnabled.Utilities;

using ProtoBuf;

using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game.Entity;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class FixBotPacket : PacketBase
  {
    [ProtoMember(1)] public long PlayerId;

    public FixBotPacket() { }

    public FixBotPacket(long playerId)
    {
      PlayerId = playerId;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      IMyPlayer player;
      List<BotBase> helpers;
      if (AiSession.Instance.Players.TryGetValue(PlayerId, out player) && player?.Character != null
        && AiSession.Instance.PlayerToHelperDict.TryGetValue(PlayerId, out helpers) && helpers?.Count > 0)
      {
        var owner = player.Character;
        var ownerPos = owner.WorldAABB.Center;

        for (int i = 0; i < helpers.Count; i++)
        {
          var helper = helpers[i];
          if (helper?.Character == null || helper.IsDead)
            continue;

          var bot = helper.Character;

          if (Vector3D.DistanceSquared(bot.WorldAABB.Center, ownerPos) < 100)
            continue;

          var seat = bot.Parent as IMyCockpit;
          if (seat != null)
            seat.RemovePilot();

          Vector3D botPos = ownerPos + (owner.WorldMatrix.Forward * 5) + owner.WorldMatrix.Up + (owner.WorldMatrix.Left * i);

          float _;
          var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(botPos, out _);
          if (nGrav.LengthSquared() > 0)
          {
            var planet = MyGamePruningStructure.GetClosestPlanet(botPos);
            if (planet != null)
            {
              while (GridBase.PointInsideVoxel(botPos, planet))
                botPos += owner.WorldMatrix.Up;
            }
          }

          bot.Physics.SetSpeeds(Vector3.Zero, Vector3.Zero);
          bot.SetPosition(botPos);

          helper.UseAPITargets = false;
          helper.NeedsTransition = false;
          helper._pathCollection?.CleanUp(true);

          if (helper.Target != null)
          {
            helper.Target.RemoveTarget();
            helper.SetTarget();

            helper.Target.Update();
            var actualPosition = helper.Target.CurrentActualPosition;
            helper.StartCheckGraph(ref actualPosition, true);
          }
        }
      }

      return false;
    }
  }
}
