
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.Models;
using VRage.ModAPI;
using VRage.Render.Particles;

using VRageMath;
using AiEnabled.Networking;
using AiEnabled.Support;

namespace AiEnabled.Projectiles
{
  public class ProjectileInfo
  {
    Stack<BotProjectile> _projectileStack = new Stack<BotProjectile>(50);
    List<BotProjectile> _activeProjectiles = new List<BotProjectile>(50);

    Stack<MuzzleEffect> _effectStack = new Stack<MuzzleEffect>(50);
    List<MuzzleEffect> _activeWeaponEffects = new List<MuzzleEffect>(50);
    List<IHitInfo> _hitList = new List<IHitInfo>();

    readonly double _tanAngleDeviation = Math.Tan(MathHelper.ToRadians(1));
    double _timeStep => 0.01666666753590107 * (MyAPIGateway.Physics?.SimulationRatio ?? 1);

    public void Add(IMyCharacter bot, IMyEntity target, float rand, float damage, WeaponInfo info)
    {
      var gunObj = bot?.EquippedTool as IMyHandheldGunObject<MyGunBase>;
      var gun = gunObj?.GunBase;
      if (gun == null || target == null || target.MarkedForClose)
      {
        //AiSession.Instance.Logger.Log($"ProjectileInfo.Add: Gun or Target was bad, returning!");
        return;
      }

      Vector3D muzzlePos;
      if (MyAPIGateway.Multiplayer.MultiplayerActive)
      {
        // because GunBase.GetMuzzleWorldPosition is a zero vector on DS :/
        muzzlePos = bot.WorldAABB.Center + Vector3D.Rotate(new Vector3D(0.2, 0.5, -0.6), bot.WorldMatrix);
      }
      else
        muzzlePos = gun.GetMuzzleWorldPosition();

      var targetPos = target.WorldAABB.Center;
      var cube = target as IMyCubeBlock;
      if (cube != null)
      {
        if (cube is IMyAirtightHangarDoor)
          targetPos += cube.WorldMatrix.Down * cube.CubeGrid.GridSize;
        else if (cube.BlockDefinition.SubtypeName == "LargeBlockGate")
          targetPos += cube.WorldMatrix.Down * cube.CubeGrid.GridSize * 0.5;
      }

      var targetVec = targetPos - muzzlePos;
      var perpVec = Vector3D.CalculatePerpendicularVector(targetVec);
      var addVecLength = rand * _tanAngleDeviation * targetVec.Length();
      var direction = Vector3D.Normalize(targetVec + perpVec * addVecLength);

      var projectile = _projectileStack.Count > 0 ? _projectileStack.Pop() : new BotProjectile();
      projectile.Init(muzzlePos, direction, bot, target, damage, gun, _hitList);
      _activeProjectiles.Add(projectile);

      if (MyAPIGateway.Session.Player != null)
      {
        var effect = _effectStack.Count > 0 ? _effectStack.Pop() : new MuzzleEffect();
        effect.Start(gun, bot);
        _activeWeaponEffects.Add(effect);
      }
    }

    public void Update()
    {
      if (MyAPIGateway.Session.Player != null)
      {
        for (int i = _activeWeaponEffects.Count - 1; i >= 0; i--)
        {
          var effect = _activeWeaponEffects[i];
          if (!effect.Update())
          {
            effect.Stop();
            _effectStack.Push(effect);
            _activeWeaponEffects.RemoveAtFast(i);
          }
        }
      }

      var step = _timeStep;
      for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
      {
        var projectile = _activeProjectiles[i];
        if (projectile.Update(step))
        {
          _projectileStack.Push(projectile);
          _activeProjectiles.RemoveAtFast(i);
        }
      }
    }

    public void Close()
    {
      _activeWeaponEffects?.Clear();
      _activeProjectiles?.Clear();
      _projectileStack?.Clear();
      _effectStack?.Clear();
      _hitList?.Clear();

      _activeWeaponEffects = null;
      _activeProjectiles = null;
      _projectileStack = null;
      _effectStack = null;
      _hitList = null;
    }
  }
}
