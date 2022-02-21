using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;

using System.Collections.Generic;

using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;

using VRageMath;

namespace AiEnabled.Projectiles
{
  internal class MuzzleEffect
  {
    public int Duration;
    MyGunBase _gun;
    IMyEntity _tool;
    readonly List<ProjectileInfo.WeaponEffect> _effects = new List<ProjectileInfo.WeaponEffect>();

    public void Start(MyGunBase gun, IMyCharacter bot)
    {
      Duration = gun.MuzzleFlashLifeSpan;
      //gun.CreateEffects(MyWeaponDefinition.WeaponEffectAction.Shoot);

      _gun = gun;
      _tool = bot.EquippedTool;
      _tool.OnMarkForClose += EquippedTool_OnMarkForClose;
      _tool.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
      _tool.NeedsWorldMatrix = true;

      var renderId =  gun.IsUserControllableGunBlock ? _tool.Render.GetRenderObjectID() : uint.MaxValue;
      _effects.Clear();

      for (int i = 0; i < gun.WeaponDefinition.WeaponEffects.Length; i++)
      {
        var eff = gun.WeaponDefinition.WeaponEffects[i];
        if (eff == null || eff.Action != MyWeaponDefinition.WeaponEffectAction.Shoot)
          continue;

        MyParticleEffect particle;
        var matrix = gun.GetMuzzleWorldMatrix();
        var position = gun.GetMuzzleWorldPosition();
        if (MyParticlesManager.TryCreateParticleEffect(eff.Particle, ref matrix, ref position, renderId, out particle) && particle.Loop)
        {
          var effect = AiSession.Instance.Projectiles.WeaponEffects.Count > 0 ? AiSession.Instance.Projectiles.WeaponEffects.Pop() : new ProjectileInfo.WeaponEffect();
          effect.Set(particle, eff);

          _effects.Add(effect);
        }
      }
    }

    private void EquippedTool_OnMarkForClose(IMyEntity obj)
    {
      obj.OnMarkForClose -= EquippedTool_OnMarkForClose;
      Stop();
    }

    public bool Update()
    {
      if (_gun == null)
        return false;

      _tool.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
      _tool.NeedsWorldMatrix = true;

      var matrix = _gun.GetMuzzleWorldMatrix();
      //var position = _gun.GetMuzzleWorldPosition();

      for (int i = 0; i < _effects.Count; i++)
      {
        var weaponEffect = _effects[i];
        var pEff = weaponEffect.ParticleEffect;
        var def = weaponEffect.Effect;
        pEff.UserBirthMultiplier = MathHelper.Clamp(pEff.UserBirthMultiplier - def.ParticleBirthDecrease, def.ParticleBirthMin, def.ParticleBirthMax);
        pEff.WorldMatrix = matrix;
      }

      //_gun.UpdateEffectPositions();
      //_gun.UpdateEffects();

      Duration--;
      return Duration > 0;
    }

    public void Stop()
    {
      if (_tool != null)
        _tool.OnMarkForClose -= EquippedTool_OnMarkForClose;

      for (int i = 0; i < _effects.Count; i++)
      {
        var eff = _effects[i];
        eff.ParticleEffect.Stop();
        AiSession.Instance.Projectiles.WeaponEffects.Push(eff);
      }

      _effects.Clear();

      if (_gun != null)
      {
        _gun.RemoveOldEffects();
      }
    }
  }
}
