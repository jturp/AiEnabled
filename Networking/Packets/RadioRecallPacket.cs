﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Utilities;

using ProtoBuf;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game.Entity;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class RadioRecallPacket : PacketBase
  {
    [ProtoMember(1)] readonly long _playerId;
    [ProtoMember(2)] readonly int _commandDistance;

    public RadioRecallPacket() { }

    public RadioRecallPacket(long playerId, int cmdDistance)
    {
      _playerId = playerId;
      _commandDistance = cmdDistance;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      try
      {
        IMyPlayer player;
        if (!AiSession.Instance.Players.TryGetValue(_playerId, out player) || player?.Character == null)
          return false;

        var playerCharacter = player.Character;
        var headMatrix = playerCharacter.GetHeadMatrix(true);
        var halfDistance = _commandDistance / 2;

        var sphere = new BoundingSphereD(headMatrix.Translation + headMatrix.Forward * halfDistance, halfDistance);

        List<MyEntity> entList = AiSession.Instance.EntListPool.Get();
        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList, MyEntityQueryType.Dynamic);

        if (entList.Count > 0)
        {
          // This is how keen does it, but it's less performant

          //var viewMatrix = MatrixD.Invert(headMatrix);
          //var viewPort = MyAPIGateway.Session.Camera.ViewportSize;
          //var aspectRatio = viewPort.X / viewPort.Y;
          //var fovWithZoom = MathHelper.ToRadians(60);
          //var projectionMatrix = MatrixD.CreatePerspectiveFieldOfView(fovWithZoom, aspectRatio, 0.05f, 250f);
          //var viewProjectionMatrix = viewMatrix * projectionMatrix;
          //var frustum = new BoundingFrustumD(viewProjectionMatrix);

          foreach (var ent in entList)
          {
            BotBase bot;
            var ch = ent as IMyCharacter;
            if (ch == null || ch.IsDead || !AiSession.Instance.Bots.TryGetValue(ch.EntityId, out bot) || bot?.Owner?.IdentityId != player.IdentityId)
              continue;

            //if (frustum.Contains(ch.GetPosition()) != ContainmentType.Disjoint)
            if (AiUtils.IsPositionInCone(bot.GetPosition(), headMatrix.Translation, headMatrix.Forward))
              AiSession.Instance.LocalBotAPI.SetBotTarget(ent.EntityId, playerCharacter);
          }
        }

        AiSession.Instance.EntListPool?.Return(ref entList);
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Error in RadioRecallPacket.Received: {ex}");
      }

      return false;
    }
  }
}
