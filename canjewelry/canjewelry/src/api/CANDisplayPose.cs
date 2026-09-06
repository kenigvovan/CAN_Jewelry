using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace canjewelry.src.api
{
    /// <summary>
    /// One step of a display pose. <see cref="op"/> is "scale", "rotate" or "translate".
    /// </summary>
    public class CANDisplayPoseStep
    {
        public string op;
        public float x;
        public float y;
        public float z;
        /// <summary>Uniform factor for "scale"; ignored by the other operations.</summary>
        public float scale = 1f;
    }

    /// <summary>
    /// A chain of mesh transforms applied when an item sits on a jeweler set.
    ///
    /// Deliberately a list of steps rather than a single ModelTransform: the existing poses are
    /// scale -> translate -> rotate -> translate chains that do not collapse into one transform.
    ///
    /// Rotation is given in DEGREES here, while the mesh API takes radians — JSON authors should
    /// not have to write fractions of pi.
    /// </summary>
    public class CANDisplayPose
    {
        public CANDisplayPoseStep[] steps;

        public void Apply(MeshData mesh, Vec3f origin)
        {
            if (mesh == null || steps == null) return;

            foreach (var step in steps)
            {
                if (step?.op == null) continue;

                switch (step.op.ToLowerInvariant())
                {
                    case "scale":
                        mesh.Scale(origin, step.scale, step.scale, step.scale);
                        break;
                    case "rotate":
                        mesh.Rotate(origin,
                            step.x * GameMath.DEG2RAD,
                            step.y * GameMath.DEG2RAD,
                            step.z * GameMath.DEG2RAD);
                        break;
                    case "translate":
                        mesh.Translate(step.x, step.y, step.z);
                        break;
                }
            }
        }
    }
}
