namespace Touge.Vehicle.Physics
{
    /// <summary>
    /// Anti-roll bar for one axle.
    ///
    /// The bar is a torsion spring linking the two wheels on an axle. When the body rolls, one side
    /// compresses more than the other and the bar resists that difference:
    ///
    ///     F = antiRollStiffness * (leftCompressionRatio - rightCompressionRatio)
    ///
    /// applied as extra load on the more-compressed (outside) wheel and an equal reduction on the
    /// less-compressed (inside) one.
    ///
    /// WHY THIS IS A PRIMARY BALANCE TUNING LEVER:
    /// The bar does not change how much load transfers across the car overall - that is set by mass,
    /// centre-of-mass height and track width. What it changes is how that transfer is DISTRIBUTED
    /// between the two axles. Because tyre grip is sub-linear in load (see
    /// <see cref="TireModel.EffectiveFriction"/>), an axle that takes a larger share of the transfer
    /// loses grip relative to the other. So stiffening the REAR bar makes the rear let go sooner -
    /// more oversteer, easier drift initiation. Stiffening the FRONT bar does the opposite.
    /// </summary>
    public static class AntiRollBar
    {
        /// <summary>
        /// Fold this axle's bar contribution into both wheels' loads. Must be called after both
        /// wheels have run their suspension update and before the loads are applied to the body.
        /// </summary>
        public static void Apply(Wheel left, Wheel right, float stiffness)
        {
            // Using compression RATIO rather than absolute travel keeps the bar rate meaningful when
            // front and rear run different spring travel.
            float compressionDifference = left.CompressionRatio - right.CompressionRatio;
            float force = compressionDifference * stiffness;

            left.ApplyAntiRoll(force);
            right.ApplyAntiRoll(-force);
        }
    }
}
