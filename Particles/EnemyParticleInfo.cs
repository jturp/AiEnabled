using AiEnabled.Networking;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System.Collections.Generic;

using VRage.Game;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Particles
{
  public class EnemyParticleInfo : ParticleInfoBase
  {
    public string ParticleName;
    public MatrixD ParticleMatrix;
    public int RotationCount;
    public Vector3D Position => Bot?.WorldAABB.Center ?? Vector3D.Zero;
    public MatrixD WorldMatrix => Bot?.WorldMatrix ?? MatrixD.Identity;

    MySkinnedEntity _skinned;
    int _boneIndex;

    public EnemyParticleInfo(IMyCharacter bot, string particleName)
    {
      Bot = bot;
      ParticleName = particleName;
      _skinned = bot as MySkinnedEntity;
      _skinned.AnimationController.FindBone("SE_RigR_Weapon_pin", out _boneIndex);

      /* To Get the proper bone:
       * Dictionary<string, IMyModelDummy> dummies = new Dictionary<string, IMyModelDummy>();
       * var bones = Character.Model.GetDummies(dummies);
       * dummy name goes in the FindBone() call to get the index
       */
    }

    public override void Set(IMyCharacter bot)
    {
      Bot = bot;
      _skinned = bot as MySkinnedEntity;
      _skinned.AnimationController.FindBone("SE_RigR_Weapon_pin", out _boneIndex);
      Stop();
    }

    public override void Update()
    {
      if (_boneIndex < 0)
        return;

      if (Effects.Count == 0)
      {
        var matrix = _skinned.BoneAbsoluteTransforms[_boneIndex];
        ParticleMatrix = matrix * WorldMatrix;

        Vector3D position = Vector3D.Zero;
        MyParticleEffect particle;
        if (MyParticlesManager.TryCreateParticleEffect(ParticleName, ref ParticleMatrix, ref position, uint.MaxValue, out particle))
        {
          particle.UserScale = 0.1f;
          Effects.Add(particle);
          particle.OnDelete += Particle_OnDelete;
        }
      }
      else
      {
        RotationCount++;
        if (RotationCount > 60)
          RotationCount = 0;

        var m = _skinned.BoneAbsoluteTransforms[_boneIndex];
        ParticleMatrix = m * WorldMatrix;

        var rotation = MathHelperD.TwoPi * RotationCount / 60;
        var matrix = MatrixD.CreateRotationY(rotation) * ParticleMatrix;
        Effects[0].WorldMatrix = matrix;
      }
    }

    private void Particle_OnDelete(MyParticleEffect effect)
    {
      effect.OnDelete -= Particle_OnDelete;
      Effects?.Clear();
    }
  }
}
