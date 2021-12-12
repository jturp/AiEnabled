using AiEnabled.Bots;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

using VRageMath;

using Sandbox.Definitions;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace AiEnabled.Projectiles
{
  public partial class BotProjectile
  {
    public float Damage;
    public float BlockDamage;
    public bool IsTracer;
    public long WeaponId;
    public double ProjectileSpeed;
    public IMyCharacter Owner;
    public IMyEntity Target;

    public Vector3D Start;
    public Vector3D LastPosition;
    public Vector3D Position;
    public Vector3D Direction;
    float _headShotMultiplier;
    bool _firstCheck;
    Color _trailColor;
    List<IHitInfo> _hitList;

    public void Init(Vector3D start, Vector3D dir, IMyCharacter bot, IMyEntity target, float damage, MyGunBase gun, List<IHitInfo> hitList)
    {
      var ammoDef = gun.CurrentAmmoDefinition as MyProjectileAmmoDefinition;
      _headShotMultiplier = ammoDef == null ? 2 : ammoDef.ProjectileHeadShotDamage / ammoDef.ProjectileHealthDamage;
      _trailColor = Vector3.IsZero(ammoDef.ProjectileTrailColor) ? Color.White : new Color(ammoDef.ProjectileTrailColor);
      _firstCheck = true;
      _hitList = hitList;

      // Keen does damage = DamageModifier * (IsCharacter ? (HeadShot ? HeadShotDamage : HealthDamage) : MassDamage);

      Owner = bot;
      Target = target;
      Damage = damage;
      BlockDamage = ammoDef.ProjectileMassDamage;
      WeaponId = bot.EquippedTool.EntityId;
      Start = start;
      LastPosition = start;
      Position = start;
      Direction = dir;
      ProjectileSpeed = ammoDef.DesiredSpeed;

      if (MyAPIGateway.Session.Player != null)
      {
        IsTracer = MyUtils.GetRandomInt(1, 101) > 30;

        var soundComp = bot.Components?.Get<MyCharacterSoundComponent>();
        if (soundComp != null)
        {
          soundComp.PlayActionSound(gun.ShootSound);
        }
      }
      else
      {
        IsTracer = false;
      }
    }

    public bool Update(double timeStep)
    {
      LastPosition = Position;
      Position += Direction * ProjectileSpeed * timeStep;

      if (Vector3D.DistanceSquared(Start, Position) > 22500)
        return true;

      float tracerLength = IsTracer ? MyUtils.GetRandomFloat(1, 2) : 0f;

      IHitInfo hitInfo = null;
      IMyEntity hitEntity = null;
      Vector3D hitPosition = Vector3D.Zero;

      if (_firstCheck)
      {
        _firstCheck = false;
        LastPosition = Start - Direction;

        _hitList.Clear();
        MyAPIGateway.Physics.CastRay(LastPosition, Position, _hitList, CollisionLayers.CharacterCollisionLayer);

        for (int i = 0; i < _hitList.Count; i++)
        {
          hitInfo = _hitList[i];
          var hit = hitInfo?.HitEntity;
          if (hit == null)
            continue;

          if (hit.EntityId == Owner.EntityId || hit.EntityId == Owner.EquippedTool?.EntityId)
          {
            continue;
          }

          hitEntity = hit;
          hitPosition = hitInfo.Position;
          break;
        }

        if (hitEntity == null)
        {
          if (IsTracer)
          {
            var tracerPos = Position - Direction * tracerLength;
            var clr = _trailColor.ToVector4();
            MyTransparentGeometry.AddLineBillboard(MyStringId.GetOrCompute("ProjectileTrailLine"), clr, tracerPos, (Vector3)Direction, tracerLength, 0.01f, BlendTypeEnum.PostPP);
          }

          return false;
        }
      }
      else if (!MyAPIGateway.Physics.CastRay(LastPosition, Position, out hitInfo, CollisionLayers.CharacterCollisionLayer) || hitInfo?.HitEntity?.Physics == null)
      {
        if (IsTracer)
        {
          var tracerPos = Position - Direction * tracerLength;
          var clr = _trailColor.ToVector4();
          MyTransparentGeometry.AddLineBillboard(MyStringId.GetOrCompute("ProjectileTrailLine"), clr, tracerPos, (Vector3)Direction, tracerLength, 0.01f, BlendTypeEnum.PostPP);
        }

        return false;
      }
      else
      {
        hitEntity = hitInfo.HitEntity;
        hitPosition = hitInfo.Position;
      }

      if (IsTracer)
      {
        var distanceGunToTarget = Vector3D.Distance(Start, hitPosition);
        tracerLength = (float)Math.Min(tracerLength, distanceGunToTarget);
        var tracerPos = hitPosition - Direction * tracerLength;
        var clr = _trailColor.ToVector4();
        MyTransparentGeometry.AddLineBillboard(MyStringId.GetOrCompute("ProjectileTrailLine"), clr, tracerPos, (Vector3)Direction, tracerLength, 0.01f, BlendTypeEnum.PostPP);
      }

      var ent = hitEntity;
      var character = ent as IMyCharacter;
      if (character != null)
      {
        if (AiSession.Instance.IsServer)
        {
          var headMatrix = character.GetHeadMatrix(true);
          var headPosition = headMatrix.Translation + headMatrix.Backward * 0.2;
          var subtype = character.Definition.Id.SubtypeName;

          if (subtype.StartsWith("space_spider", StringComparison.OrdinalIgnoreCase) || subtype.StartsWith("space_wolf", StringComparison.OrdinalIgnoreCase))
            Damage *= 5;
          else if (Vector3D.DistanceSquared(headPosition, hitPosition) < 0.05)
            Damage *= _headShotMultiplier;

          AiSession.Instance.DamageCharacter(Owner.EntityId, character, MyDamageType.Bullet, Damage);
        }

        if (MyAPIGateway.Session.Player != null)
        {
          var position = hitPosition;
          var matrix = character.WorldMatrix;
          matrix.Translation = position;

          string effectName = "MaterialHit_Character";
          string soundName = "WepPlayRifleImpPlay";
          if (string.IsNullOrWhiteSpace(character.DisplayName) || character.IsBot)
          {
            var subtype = character.Definition.Id.SubtypeName;
           
            if (subtype == "Ghost_Bot")
            {
              effectName = "MaterialHit_Ice";
            }
            else if (subtype.StartsWith("space_spider", StringComparison.OrdinalIgnoreCase))
            {
              effectName = "Blood_Spider";
            }
            else if (subtype != "Space_Zombie" && subtype != "Space_Skeleton" 
              && !subtype.StartsWith("space_wolf", StringComparison.OrdinalIgnoreCase) 
              && !subtype.StartsWith("default_astronaut", StringComparison.OrdinalIgnoreCase))
            {
              effectName = "MaterialHit_Metal";
              soundName = "WepPlayRifleImpMetal";
            }
          }

          MyParticleEffect _;
          MyParticlesManager.TryCreateParticleEffect(effectName, ref matrix, ref position, uint.MaxValue, out _);

          var soundComp = character.Components?.Get<MyCharacterSoundComponent>();
          if (soundComp != null)
          {
            // WepPlayRifleImpPlay or WepFemPlayRifleImpPlay

            MySoundPair soundPair;
            if (!AiSession.Instance.SoundPairDict.TryGetValue(soundName, out soundPair))
            {
              soundPair = new MySoundPair(soundName);
              AiSession.Instance.SoundPairDict[soundName] = soundPair;
            }

            soundComp.PlayActionSound(soundPair);
          }
        }

        return true;
      }

      var grid = ent as IMyCubeGrid;
      if (grid == null)
      {
        // In case we hit a door or subpart
        grid = ent?.GetTopMostParent() as IMyCubeGrid;
      }

      if (grid != null && !grid.MarkedForClose)
      {
        var blockPos = grid.WorldToGridInteger(hitPosition);
        var block = grid.GetCubeBlock(blockPos);

        if (block == null)
        {
          hitPosition -= hitInfo.Normal * grid.GridSize * 0.2f;
          blockPos = grid.WorldToGridInteger(hitPosition);
          block = grid.GetCubeBlock(blockPos);

          if (block == null)
          {
            return true;
          }
        }

        var isServer = AiSession.Instance.IsServer;
        if (isServer)
        {
          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(Owner.EntityId, out bot) && bot != null)
            BlockDamage *= bot.DamageModifier;

          block.DoDamage(BlockDamage, MyDamageType.Bullet, true);
        }

        //AiSession.Instance.Logger.Log($"Block damage final = {BlockDamage}");
        block.DoDamage(BlockDamage * 0.5f, MyDamageType.Deformation, isServer);

        if (MyAPIGateway.Session.Player != null)
        {
          var position = hitPosition;
          Matrix m;
          block.Orientation.GetMatrix(out m);
          m.Translation = (Vector3)position;
          MatrixD matrix = m;

          string material, sound;
          var blockDef = block.BlockDefinition.Id;
          if (blockDef.SubtypeName.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0
            || blockDef.SubtypeName.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            material = "MaterialHit_Glass";
            sound = "WepPlayRifleImpGlass";
          }
          else
          {
            var skin = block.SkinSubtypeId.String;
            if (skin == "Wood_Armor")
            {
              material = "MaterialHit_Wood";
              sound = "WepPlayRifleImpWood";
            }
            else if (skin == "Concrete_Armor")
            {
              material = "MaterialHit_Rock";
              sound = "WepPlayRifleImpRock";
            }
            else
            {
              material = "MaterialHit_Metal";
              sound = "WepPlayRifleImpMetal";
            }
          }

          MyParticleEffect _;
          MyParticlesManager.TryCreateParticleEffect(material, ref matrix, ref position, uint.MaxValue, out _);

          MyEntity3DSoundEmitter emitter = AiSession.Instance.GetEmitter();
          emitter.SetPosition(hitPosition);

          MySoundPair soundPair;
          if (!AiSession.Instance.SoundPairDict.TryGetValue(sound, out soundPair))
          {
            soundPair = new MySoundPair(sound);
            AiSession.Instance.SoundPairDict[sound] = soundPair;
          }

          emitter.PlaySound(soundPair);
          AiSession.Instance.ReturnEmitter(emitter);
        }

        return true;
      }

      var voxel = ent as MyVoxelBase;
      if (voxel != null && !MyAPIGateway.Utilities.IsDedicated)
      {
        var position = hitPosition;
        var matrix = MatrixD.Identity;
        matrix.Translation = position;

        MyParticleEffect effect;
        MyParticlesManager.TryCreateParticleEffect("MaterialHit_Sand", ref matrix, ref position, uint.MaxValue, out effect);

        MyEntity3DSoundEmitter emitter = AiSession.Instance.GetEmitter();
        emitter.SetPosition(hitPosition);

        MySoundPair soundPair;
        if (!AiSession.Instance.SoundPairDict.TryGetValue("WepPlayRifleImpSand", out soundPair))
        {
          // WepPlayRifleImpSand
          soundPair = new MySoundPair("WepPlayRifleImpSand");
          AiSession.Instance.SoundPairDict["WepPlayRifleImpSand"] = soundPair;
        }

        emitter.PlaySound(soundPair);
        AiSession.Instance.ReturnEmitter(emitter);
        return true;
      }

      return hitEntity != null;
    }
  }
}
