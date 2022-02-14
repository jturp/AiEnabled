
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
using AiEnabled.Bots;
using VRage.Utils;
using Sandbox.Common.ObjectBuilders;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage;
using AiEnabled.Utilities;
using Sandbox.Definitions;

namespace AiEnabled.Projectiles
{
  public class ProjectileInfo
  {
    internal class WeaponEffect
    {
      internal MyParticleEffect ParticleEffect;
      internal MyWeaponDefinition.MyWeaponEffect Effect;

      internal void Set(MyParticleEffect pEff, MyWeaponDefinition.MyWeaponEffect wEff)
      {
        ParticleEffect = pEff;
        Effect = wEff;
      }
    }

    internal Stack<WeaponEffect> WeaponEffects = new Stack<WeaponEffect>(50);
    Stack<BotProjectile> _projectileStack = new Stack<BotProjectile>(50);
    List<BotProjectile> _activeProjectiles = new List<BotProjectile>(50);

    Stack<MuzzleEffect> _effectStack = new Stack<MuzzleEffect>(50);
    List<MuzzleEffect> _activeWeaponEffects = new List<MuzzleEffect>(50);
    List<IHitInfo> _hitList = new List<IHitInfo>();

    double _timeStep => 0.01666666753590107 * (MyAPIGateway.Physics?.SimulationRatio ?? 1);

    public void Add(IMyCharacter bot, IMyEntity target, WeaponInfo info)
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
      else if (info.LeadTargets && target.Physics.LinearVelocity.LengthSquared() > 0 && !Vector3D.IsZero(target.Physics.LinearVelocity - bot.Physics.LinearVelocity, 0.1))
      {
        var ammoDef = gun.CurrentAmmoDefinition as MyProjectileAmmoDefinition;
        var ammoSpeed = ammoDef?.DesiredSpeed ?? 300;
        targetPos = GetInterceptPoint(ammoSpeed, bot, target);
      }
 
      var targetVec = targetPos - muzzlePos;
      var perpVec = Vector3D.CalculatePerpendicularVector(targetVec);
      var addVecLength = info.GetRandom() * info.ShotDeviationAngleTan * targetVec.Length();
      var direction = Vector3D.Normalize(targetVec + perpVec * addVecLength);

      var projectile = _projectileStack.Count > 0 ? _projectileStack.Pop() : new BotProjectile();
      projectile.Init(muzzlePos, direction, bot, target, info.Damage, gun, _hitList, null);
      _activeProjectiles.Add(projectile);

      if (MyAPIGateway.Session.Player != null && gun.MuzzleFlashLifeSpan > 0)
      {
        var effect = _effectStack.Count > 0 ? _effectStack.Pop() : new MuzzleEffect();
        effect.Start(gun, bot);
        _activeWeaponEffects.Add(effect);
      }
    }

    public void AddMissileForBot(BotBase botBase, IMyEntity target)
    {
      var bot = botBase.Character;
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

      var ammoDef = gun.CurrentAmmoDefinition as MyMissileAmmoDefinition;
      var ammoSpeed = ammoDef?.DesiredSpeed ?? 200;

      var targetPos = target.WorldAABB.Center;
      var cube = target as IMyCubeBlock;
      if (cube != null)
      {
        if (cube is IMyAirtightHangarDoor)
          targetPos += cube.WorldMatrix.Down * cube.CubeGrid.GridSize;
        else if (cube.BlockDefinition.SubtypeName == "LargeBlockGate")
          targetPos += cube.WorldMatrix.Down * cube.CubeGrid.GridSize * 0.5;
      }
      else if (botBase.ShouldLeadTargets && target.Physics.LinearVelocity.LengthSquared() > 0 && !Vector3D.IsZero(target.Physics.LinearVelocity - bot.Physics.LinearVelocity, 0.1))
      {
        targetPos = GetInterceptPoint(ammoSpeed, bot, target);
      }

      var targetVec = targetPos - muzzlePos;
      var perpVec = Vector3D.CalculatePerpendicularVector(targetVec);
      var addVecLength = MyUtils.GetRandomFloat(0, 1) * botBase._shotAngleDeviationTan * targetVec.Length();
      var direction = Vector3D.Normalize(targetVec + perpVec * addVecLength);
      var addedVelocity = VectorUtils.Project(bot.Physics.LinearVelocity, bot.WorldMatrix.Right);
      //var weaponSubtype = botBase.ToolSubtype.StartsWith("Basic") ? "BasicHandHeldLauncherGun" : "AdvancedHandHeldLauncherGun";

      var weaponItemDef = gunObj.PhysicalItemDefinition as MyWeaponItemDefinition;
      var missileAmmo = gun.CurrentAmmoDefinition as MyMissileAmmoDefinition;

      var ob = new MyObjectBuilder_Missile()
      {
        SubtypeName = "Missile200mm",
        AmmoMagazineId = gun.CurrentAmmoMagazineId,
        EntityId = 0,
        PersistentFlags = MyPersistentEntityFlags2.Enabled | MyPersistentEntityFlags2.InScene,
        LauncherId = bot.EquippedTool.EntityId,
        OriginEntity = bot.EquippedTool.EntityId,
        Owner = gunObj.OwnerId,
        LinearVelocity = addedVelocity + direction * ammoSpeed,
        PositionAndOrientation = new MyPositionAndOrientation(muzzlePos, direction, Vector3D.CalculatePerpendicularVector(direction)),
        WeaponDefinitionId = weaponItemDef.WeaponDefinitionId,
      };

      var missile = MyEntities.CreateFromObjectBuilderAndAdd(ob, true);
      gun.ConsumeAmmo();

      var projectile = _projectileStack.Count > 0 ? _projectileStack.Pop() : new BotProjectile();
      projectile.Init(muzzlePos, direction, bot, target, missileAmmo.MissileExplosionDamage, gun, _hitList, missile);
      _activeProjectiles.Add(projectile);

      if (MyAPIGateway.Session.Player != null && gun.MuzzleFlashLifeSpan > 0)
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

    Vector3D GetInterceptPoint(double ammoSpeed, IMyCharacter bot, IMyEntity tgt)
    {
      // Setup the equation
      var botPos = bot.WorldAABB.Center;
      var tgtPos = tgt.WorldAABB.Center;
      var botVel = (Vector3D)bot.Physics.LinearVelocity;
      var tgtVel = (Vector3D)tgt.Physics.LinearVelocity;

      var displacementVector = tgtPos - botPos;
      var relativeVelocity = tgtVel - botVel;
      var cosTheta = VectorUtils.GetCosineAngleBetween(-displacementVector, tgtVel) * Math.Sign(tgtVel.Dot(-displacementVector));
      if (cosTheta == 0)
        cosTheta = 1;

      double a = ammoSpeed * ammoSpeed - relativeVelocity.LengthSquared();
      double b = -2 * relativeVelocity.Dot(displacementVector) * cosTheta;
      double c = displacementVector.LengthSquared();

      // t = (-b +/- sqrt(b² - 4ac)) / 2a

      var discr = b * b - 4 * a * c;

      if (discr < 0)
      {
        // no real roots
        return tgtPos + tgtVel * (displacementVector.Length() / ammoSpeed);
      }

      var denom = 2 * a;

      if (discr > 0)
      {
        // two real roots
        var t1 = (-b + discr) / denom;
        var t2 = (-b - discr) / denom;

        double t3;
        if (t1 > 0 && t2 > 0)
        {
          t3 = t1 < t2 ? t1 : t2;
        }
        else if (t1 > 0)
        {
          t3 = t1;
        }
        else if (t2 > 0)
        {
          t3 = t2;
        }
        else
        {
          t3 = displacementVector.Length() / ammoSpeed;
        }

        return tgtPos + tgtVel * t3;
      }

      // one real root
      var t = -b / denom;
      return tgtPos + tgtVel * t;
    }

    public void Close()
    {
      WeaponEffects?.Clear();
      _activeWeaponEffects?.Clear();
      _activeProjectiles?.Clear();
      _projectileStack?.Clear();
      _effectStack?.Clear();
      _hitList?.Clear();

      WeaponEffects = null;
      _activeWeaponEffects = null;
      _activeProjectiles = null;
      _projectileStack = null;
      _effectStack = null;
      _hitList = null;
    }
  }
}
