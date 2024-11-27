using AiEnabled.Ai.Support;
using AiEnabled.Bots;
using ProtoBuf;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class GoToAllPacket : PacketBase
  {
    [ProtoMember(1)] readonly long _playerId;
    [ProtoMember(2)] readonly int _commandDistance;

    public GoToAllPacket() { }

    public GoToAllPacket(long playerId, int commandDistance) 
    {
      _playerId = playerId;
      _commandDistance = commandDistance;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered)
        return false;

      IMyPlayer player;
      if (!AiSession.Instance.Players.TryGetValue(_playerId, out player) || player?.Character == null)
        return false;

      List<BotBase> playerHelpers;
      if (!AiSession.Instance.PlayerToHelperDict.TryGetValue(_playerId, out playerHelpers) || playerHelpers == null || playerHelpers.Count == 0)
        return false;

      var topMost = player.Character.GetTopMostParent();
      var ownerPos = player.Character.WorldAABB.Center;
      var headMatrix = player.Character.GetHeadMatrix(true);
      var headPos = headMatrix.Translation;
      var forwardPos = headPos + headMatrix.Forward * 100;

      var hitList = AiSession.Instance.HitListPool.Get();
      MyAPIGateway.Physics.CastRay(headPos, forwardPos, hitList);

      Vector3D? gotoPos = null;

      for (int i = 0; i < hitList.Count; ++i)
      {
        var hit = hitList[i];
        if (hit?.HitEntity == null)
          continue;

        var hitGrid = hit.HitEntity as IMyCubeGrid;
        if (hitGrid != null)
        {
          if (topMost != null && topMost.EntityId == hitGrid.EntityId)
            continue;

          var pos = hit.Position + hit.Normal * hitGrid.GridSize * 0.2f;
          var localPos = hitGrid.WorldToGridInteger(pos);
          gotoPos = hitGrid.GridIntegerToWorld(localPos);
          break;
        }

        var voxel = (hit.HitEntity as MyVoxelBase)?.RootVoxel;
        if (voxel != null)
        {
          Vector3D upVec = hit.Normal;

          bool onGround;
          var surfacePosition = GridBase.GetClosestSurfacePointFast(hit.Position, upVec, voxel, out onGround);

          if (onGround)
            surfacePosition += upVec * 0.3;
          else
            surfacePosition -= upVec * 1.5;

          gotoPos = surfacePosition;
          break;
        }
      }

      AiSession.Instance.HitListPool.Return(ref hitList);

      if (gotoPos == null)
      {
        var pkt = new MessagePacket("No valid GoTo position found!");
        netHandler.SendToPlayer(pkt, SenderId);
        return false;
      }

      float interference;
      var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(ownerPos, out interference);
      var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(ownerPos, interference);
      var planet = nGrav.LengthSquared() > 0 ? MyGamePruningStructure.GetClosestPlanet(ownerPos) : null;

      Vector3D sendTo = gotoPos.Value;
      Vector3D up;
      double offset = 2.5;

      if (aGrav.LengthSquared() > 0)
        up = -Vector3D.Normalize(aGrav);
      else if (nGrav.LengthSquared() > 0)
        up = -Vector3D.Normalize(nGrav);
      else if (planet != null)
        up = Vector3D.Normalize(sendTo - planet.PositionComp.GetPosition());
      else
        up = player.Character.WorldMatrix.Up;

      var perpVec = MyUtils.GetRandomPerpendicularVector(ref up);
      int numCommanded = 0;

      for (int i = 0; i < playerHelpers.Count; i++)
      {
        var bot = playerHelpers[i];
        if (bot == null || bot.IsDead)
          continue;

        var botPos = bot.GetPosition();

        if (Vector3D.DistanceSquared(botPos, ownerPos) > _commandDistance * _commandDistance)
          continue;

        numCommanded++;

        if (planet != null)
        {
          var surfacePoint = GridBase.GetClosestSurfacePointAboveGround(ref sendTo, up, voxel: planet, checkGrids: true);

          if (surfacePoint != null && Vector3D.DistanceSquared(surfacePoint.Value, sendTo) < 100 * 100)
          {
            sendTo = surfacePoint.Value;
          }
        }

        var pkt = new CommandPacket(bot.Character.EntityId, sendTo);
        AiSession.Instance.Network.SendToServer(pkt);

        sendTo = gotoPos.Value + perpVec * offset;
        offset *= -1;

        if (offset > 0)
          offset += 2.5;
      }

      if (numCommanded > 0)
      {
        var pkt = new MessagePacket($"Sending [{numCommanded}] helpers to waypoint!", "White", ttl:5000);
        netHandler.SendToPlayer(pkt, SenderId);
      }

      return false;
    }
  }
}
