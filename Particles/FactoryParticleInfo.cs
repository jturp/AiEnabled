using AiEnabled.Networking;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System.Collections.Generic;

using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Particles
{
  public class FactoryParticleInfo : ParticleInfoBase
  {
    public IMyTerminalBlock Block;
    public int RotationTicksX, RotationTicksZ;
    public MatrixD ParticleMatrix1, ParticleMatrix2;
    public MyEntity3DSoundEmitter SoundEmitter;
    public MySoundPair SoundPair;

    public FactoryParticleInfo(IMyCharacter bot, IMyTerminalBlock block)
    {
      Bot = bot;
      Block = block;

      if (!AiSession.Instance.SoundPairDict.TryGetValue("BlockAssemblerProcess", out SoundPair))
      {
        SoundPair = new MySoundPair("BlockAssemblerProcess");
        AiSession.Instance.SoundPairDict["BlockAssemblerProcess"] = SoundPair;
      }

      if (AiSession.Instance.PlayerData == null || AiSession.Instance.PlayerData.BotVolumeModifier > 0)
      {
        var volMulti = AiSession.Instance.PlayerData?.BotVolumeModifier ?? 1f;
        SoundEmitter = AiSession.Instance.GetEmitter((MyEntity)block);
        SoundEmitter.VolumeMultiplier = volMulti;
        SoundEmitter.PlaySound(SoundPair);
      }
    }

    public override void Set(IMyCharacter bot)
    {
      Stop();
      Bot = bot;
      RotationTicksX = RotationTicksZ = 0;

      if (SoundEmitter != null)
      {
        SoundEmitter.Entity = (MyEntity)Block;

        if (AiSession.Instance.PlayerData == null || AiSession.Instance.PlayerData.BotVolumeModifier > 0)
        {
          var volMulti = AiSession.Instance.PlayerData?.BotVolumeModifier ?? 1f;
          SoundEmitter.VolumeMultiplier = volMulti;
          SoundEmitter.PlaySound(SoundPair);
        }
      }
    }

    public override void Stop()
    {
      base.Stop();
      SoundEmitter?.StopSound(false);
    }

    public override void Close()
    {
      base.Close();

      if (SoundEmitter != null)
      {
        SoundEmitter.VolumeMultiplier = 1f;
        SoundEmitter.Cleanup();
        AiSession.Instance.ReturnEmitter(SoundEmitter);
      }
    }

    public override void Update()
    {
      if (Effects.Count == 0)
      {
        RotationTicksX = RotationTicksZ = 0;
        var position = Bot.WorldAABB.Center;
        ParticleMatrix1 = ParticleMatrix2 = Block.WorldMatrix;
        ParticleMatrix1.Translation = position;
        ParticleMatrix2.Translation = position + Bot.WorldMatrix.Up * 0.25;

        MyParticleEffect particle0, particle1, particle2;
        if (MyParticlesManager.TryCreateParticleEffect("ShipWelderArc", ref ParticleMatrix2, ref position, uint.MaxValue, out particle0))
        {
          Effects.Add(particle0);
        }

        if (MyParticlesManager.TryCreateParticleEffect("ShipWelderArc", ref ParticleMatrix2, ref position, uint.MaxValue, out particle1))
        {
          Effects.Add(particle1);
        }

        if (MyParticlesManager.TryCreateParticleEffect("Damage_Electrical_Damaged", ref ParticleMatrix1, ref position, uint.MaxValue, out particle2))
        {
          particle2.UserScale = 0.2f;
          particle2.UserColorMultiplier = new Vector4(0.25f);
          Effects.Add(particle2);
        }
      }
      else if (Effects.Count > 0)
      {
        RotationTicksX++;
        if (RotationTicksX > 60)
        {
          RedoElectricParticle();
          RotationTicksX = 0;
          RotationTicksZ++;
        }

        var rotationX = MathHelperD.TwoPi * RotationTicksX / 60;
        var rotationZ = MathHelperD.TwoPi * RotationTicksZ / 30;
        var xRotation = MatrixD.CreateRotationX(rotationX);
        var position = Bot.WorldAABB.Center + Bot.WorldMatrix.Up * 0.25;

        var matrix = ParticleMatrix2;
        matrix.Translation = position;

        var p1Matrix = MatrixD.CreateRotationZ(rotationZ) * matrix;
        p1Matrix = xRotation * p1Matrix;
        p1Matrix.Translation += p1Matrix.Forward * 0.5;
        Effects[0].WorldMatrix = p1Matrix;

        if (Effects.Count > 1)
        {
          p1Matrix = MatrixD.CreateRotationZ(-rotationZ) * matrix;
          p1Matrix = xRotation * p1Matrix;
          p1Matrix.Translation += p1Matrix.Backward * 0.5;
          Effects[1].WorldMatrix = p1Matrix;

          if (Effects.Count > 2)
          {
            ParticleMatrix1 = Block.WorldMatrix;
            ParticleMatrix1.Translation = position;
            Effects[2].WorldMatrix = ParticleMatrix1;
          }
        }
      }
    }

    void RedoElectricParticle()
    {
      if (Effects.Count > 2)
      {
        var p = Effects[2];
        if (p != null)
        {
          p.StopEmitting();
          p.Autodelete = true;
          MyParticlesManager.RemoveParticleEffect(p);
        }

        Effects.RemoveAtFast(2);
      }

      var position = Bot.WorldAABB.Center;
      ParticleMatrix1 = Block.WorldMatrix;
      ParticleMatrix1.Translation = position;

      MyParticleEffect particle;
      if (MyParticlesManager.TryCreateParticleEffect("Damage_Electrical_Damaged", ref ParticleMatrix1, ref position, uint.MaxValue, out particle))
      {
        particle.UserScale = 0.2f;
        particle.UserColorMultiplier = new Vector4(0.25f);
        Effects.Add(particle);
      }
    }
  }
}
