using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Short-step drag law for the runtime current adapter (plan §13.2 B1).
    /// Water and air act on the same frozen input state and are solved in
    /// one linear step; the adapter applies only the difference between the
    /// flow and no-flow evaluations, so a zero field is exactly the stock
    /// path. The offline stand compiles this implementation directly.
    /// </summary>
    internal sealed class CurrentSurfaceDragStep
    {
        private readonly float uniformQuadratic;
        private readonly float[] directionalQuadratic;
        private readonly float linear;

        public CurrentSurfaceDragStep(float linear, float uniformQuadratic, float[] directionalQuadratic)
        {
            this.linear = linear;
            this.uniformQuadratic = uniformQuadratic;
            this.directionalQuadratic = directionalQuadratic;
        }

        internal static Vector3 SceneWind(Vector3 forward, float knots)
        {
            if (knots <= 0f || float.IsNaN(knots) || float.IsInfinity(knots))
                return Vector3.zero;
            forward.y = 0f;
            return forward.normalized * (knots * (1852f / 3600f));
        }

        internal void Difference(Vector3 velocity, Vector3 water, Vector3 wind, float wet, float dry,
            float exposure, float stepPerMass, Quaternion inverseHull, float blend,
            out Vector3 waterDelta, out Vector3 airDelta)
        {
            var still = velocity;
            float exposedDry = dry * exposure;
            Advance(ref velocity, water, wind, wet, exposedDry, stepPerMass, inverseHull, blend,
                out waterDelta, out airDelta);
            Advance(ref still, Vector3.zero, Vector3.zero, wet, exposedDry, stepPerMass, inverseHull, blend,
                out Vector3 stillWater, out Vector3 stillAir);
            waterDelta -= stillWater;
            airDelta -= stillAir;
        }

        public void Advance(ref Vector3 world, Vector3 water, Vector3 wind, float waterWeight,
            float airWeight, float stepPerMass, Quaternion inverseHull, float directionalBlend,
            out Vector3 waterForce, out Vector3 airForce)
        {
            Vector3 relativeWater = world - water;
            Vector3 relativeAir = world - wind;
            float waterRate = Rate(relativeWater, waterWeight, inverseHull, directionalBlend);
            float airRate = Rate(relativeAir, airWeight, inverseHull, directionalBlend);

            // Freeze both rates at the same initial state and solve the
            // resulting linear step together. No sequential drag, no
            // world-speed limiter, no overshoot past the medium.
            float factor = 1f / (1f + stepPerMass * (waterRate + airRate));
            waterForce = relativeWater * (-waterRate * factor);
            airForce = relativeAir * (-airRate * factor);
            world += (waterForce + airForce) * stepPerMass;
        }

        private float Rate(Vector3 relative, float weight, Quaternion inverseHull, float directionalBlend)
        {
            if (weight <= 0f)
                return 0f;
            float speed = relative.magnitude;
            float quadratic = uniformQuadratic;
            if (directionalQuadratic != null && speed > 1e-5f)
            {
                Vector3 axis = inverseHull * (relative / speed);
                float value = axis.x * directionalQuadratic[axis.x > 0f ? 0 : 1]
                    + axis.y * directionalQuadratic[axis.y > 0f ? 2 : 3]
                    + axis.z * directionalQuadratic[axis.z > 0f ? 4 : 5];
                quadratic = Mathf.Lerp(directionalQuadratic[4], value, directionalBlend);
            }
            return weight * Mathf.Max(0f, linear + speed * quadratic);
        }
    }
}
