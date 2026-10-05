using System;
using System.Collections.Generic;
using BannerlordSceneToolkit;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace PrefabCreatorTool.Core
{
    // Blender's Object > Transform > Randomize, cut down to what this engine can actually do:
    // rotation only. Scale is out - GameEntity exposes no scale setter (scale is baked in from
    // the prefab XML at instantiation, a limitation LivePrefabSwapper already documents) - and
    // position jitter is already covered by the Pile Generator's own scatter.
    //
    // Each entity gets its OWN independent random rotation, drawn per axis from
    // [-maxDegrees, +maxDegrees], applied about the entity's OWN origin - so a pile's pieces
    // each spin individually instead of the group swinging around a shared pivot, which is the
    // entire point of randomizing (breaking up the repetition of a Distribute run or a pile
    // whose pieces came out too uniform). Axis caps are world axes: Z is yaw (the one that
    // matters for scattered props on the ground), X/Y lean pieces over - useful for debris,
    // usually left at 0 for anything meant to stand upright.
    //
    // Order per entity: yaw (Z) first, then the X/Y leans, each about the entity's current
    // origin. For the small angles X/Y are typically capped at, order is barely visible; it is
    // fixed so repeated runs at least fail predictably if a cap is typed wrong.
    public static class RandomizeRotation
    {
        public class Result
        {
            public int Rotated;
            public int Failed;
        }

        private static bool Alive(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        public static Result Apply(List<GameEntity> targets, float maxXDegrees, float maxYDegrees, float maxZDegrees)
        {
            var result = new Result();
            if (targets == null) return result;

            var rng = new Random();

            foreach (var entity in targets)
            {
                if (!Alive(entity)) continue;
                try
                {
                    var frame = entity.GetGlobalFrame();
                    var rotation = frame.rotation;

                    // Z first (see class comment), then X, then Y. An axis capped at 0 draws
                    // nothing and applies nothing.
                    if (maxZDegrees > 0.0001f) rotation = RotateBasis(rotation, new Vec3(0f, 0f, 1f, 0f), NextAngle(rng, maxZDegrees));
                    if (maxXDegrees > 0.0001f) rotation = RotateBasis(rotation, new Vec3(1f, 0f, 0f, 0f), NextAngle(rng, maxXDegrees));
                    if (maxYDegrees > 0.0001f) rotation = RotateBasis(rotation, new Vec3(0f, 1f, 0f, 0f), NextAngle(rng, maxYDegrees));

                    var newFrame = new MatrixFrame(rotation, frame.origin);
                    entity.SetGlobalFrame(ref newFrame, true);
                    EditorFrameSync.Sync(entity);
                    result.Rotated++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    Log.Warn($"[Randomize] rotate failed on '{entity.Name}': {ex.Message}");
                }
            }

            return result;
        }

        private static float NextAngle(Random rng, float maxDegrees) =>
            (float)(rng.NextDouble() * 2.0 - 1.0) * maxDegrees;

        // Rodrigues' rotation of each basis vector about a world axis - rotating the basis but
        // not the origin is what spins an entity in place. Same formula PrefabSwapperTool's
        // PrefabDistributor uses; duplicated as three lines of math rather than reaching across
        // tools for a private helper.
        private static Mat3 RotateBasis(Mat3 rotation, Vec3 axisUnit, float angleDegrees)
        {
            return new Mat3(
                RotateVector(rotation.s, axisUnit, angleDegrees),
                RotateVector(rotation.f, axisUnit, angleDegrees),
                RotateVector(rotation.u, axisUnit, angleDegrees));
        }

        private static Vec3 RotateVector(Vec3 v, Vec3 axisUnit, float angleDegrees)
        {
            float rad = angleDegrees * ((float)Math.PI / 180f);
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            var cross = new Vec3(
                axisUnit.y * v.z - axisUnit.z * v.y,
                axisUnit.z * v.x - axisUnit.x * v.z,
                axisUnit.x * v.y - axisUnit.y * v.x,
                0f);
            var dot = Vec3.DotProduct(axisUnit, v);
            return v * cos + cross * sin + axisUnit * (dot * (1f - cos));
        }
    }
}
