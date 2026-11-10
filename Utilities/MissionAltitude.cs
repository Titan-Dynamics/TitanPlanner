namespace MissionPlanner.Utilities
{
    /// <summary>
    /// A mission altitude by its frame: what a waypoint's altitude number means as a height above sea level.
    /// </summary>
    public static class MissionAltitude
    {
        /// <summary>Whether the frame's altitude is above the ground under the waypoint.</summary>
        public static bool FollowsTerrain(MAVLink.MAV_FRAME frame)
        {
            return frame == MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT || frame == MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT_INT;
        }

        /// <summary>
        /// The height above sea level an altitude in the frame flies at: the altitude itself (GLOBAL), above the ground
        /// under it (TERRAIN, counted from home when the ground isn't known, groundAsl NaN), or above home (the others).
        /// </summary>
        public static double ToAsl(double alt, MAVLink.MAV_FRAME frame, double homeAsl, double groundAsl)
        {
            switch (frame)
            {
                case MAVLink.MAV_FRAME.GLOBAL:
                case MAVLink.MAV_FRAME.GLOBAL_INT:
                    return alt;

                case MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT:
                case MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT_INT:
                    return (double.IsNaN(groundAsl) ? homeAsl : groundAsl) + alt;

                default:
                    return homeAsl + alt;
            }
        }
    }
}
