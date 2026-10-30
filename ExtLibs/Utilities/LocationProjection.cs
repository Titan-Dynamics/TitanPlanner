using System;
using System.Collections.Generic;
using System.Text;

namespace MissionPlanner.Utilities
{
    public class LocationProjection
    {
        /// <summary>
        /// Where a location moving at a velocity (north, east, down in m/s, as MAVLink reports it) is at now.
        /// </summary>
        public static PointLatLngAlt Project(PointLatLngAlt location, Vector3 velocity, DateTime locationtime, DateTime now)
        {
            var deltatime = now - locationtime;
            var bearing = Math.Atan2(velocity.y, velocity.x) * (180 / Math.PI);
            var dist = velocity * deltatime.TotalSeconds;
            var newpos = location.newpos(bearing, Math.Sqrt(dist.x * dist.x + dist.y * dist.y));
            // down is positive
            newpos.Alt -= dist.z;
            return newpos;
        }
    }
}
