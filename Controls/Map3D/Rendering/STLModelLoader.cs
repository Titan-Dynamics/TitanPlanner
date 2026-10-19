using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Handles loading and parsing STL model files for 3D visualization.
    /// Supports both embedded resources based on MAV_TYPE and custom file paths.
    /// </summary>
    public class STLModelLoader
    {
        /// <summary>
        /// Models parsed (or being parsed) in the background, shared by every 3D map: parsing a multi-megabyte ASCII
        /// STL took about a second, which used to be spent on the UI thread when the map first drew the vehicle.
        /// </summary>
        private static readonly Dictionary<string, Task<Tuple<List<float>, List<float>>>> _parsedModels =
            new Dictionary<string, Task<Tuple<List<float>, List<float>>>>();

        private List<float> _vertices;
        private List<float> _normals;
        private bool _isLoaded = false;
        private MAVLink.MAV_TYPE? _loadedMavType = null;
        private string _customSTLPath = "";

        public List<float> Vertices => _vertices;
        public List<float> Normals => _normals;
        public int VertexCount => _vertices?.Count / 3 ?? 0;
        public bool IsLoaded => _isLoaded;
        public MAVLink.MAV_TYPE? LoadedMavType => _loadedMavType;

        /// <summary>
        /// Set when <see cref="EnsureLoaded"/> returned false only because the model is still being parsed.
        /// </summary>
        public bool IsPending { get; private set; }

        public string CustomSTLPath
        {
            get => _customSTLPath;
            set
            {
                if (_customSTLPath != value)
                {
                    _customSTLPath = value;
                    _isLoaded = false; // Force reload when path changes
                }
            }
        }

        /// <summary>
        /// Starts parsing the current vehicle's model in the background, so it is ready by the time the map draws.
        /// </summary>
        public void Preload()
        {
            try
            {
                ModelTask(CurrentMavType(), UsingCustomSTL);
            }
            catch
            {
                // EnsureLoaded reports problems when the model is actually needed.
            }
        }

        /// <summary>
        /// Loads the STL model if needed, based on current MAV_TYPE or custom path.
        /// Returns true if the model is ready to render; false with <see cref="IsPending"/> set while it is still
        /// being parsed in the background.
        /// </summary>
        public bool EnsureLoaded()
        {
            IsPending = false;
            MAVLink.MAV_TYPE currentMavType = CurrentMavType();

            // Check if we need to reload due to vehicle type change (only when using default STL)
            bool usingCustomSTL = UsingCustomSTL;
            if (_isLoaded && !usingCustomSTL && _loadedMavType.HasValue && _loadedMavType.Value != currentMavType)
            {
                // Vehicle type changed, force reload
                _isLoaded = false;
            }

            if (_isLoaded) return true;

            try
            {
                var task = ModelTask(currentMavType, usingCustomSTL);
                if (!task.IsCompleted)
                {
                    IsPending = true;
                    return false;
                }

                // Throws the parse's exception, if it had one.
                var parsed = task.GetAwaiter().GetResult();
                if (parsed == null)
                {
                    MessageBox.Show("STL resource not found for vehicle type", "STL Load Error");
                    return false;
                }

                _vertices = parsed.Item1;
                _normals = parsed.Item2;
                if (!usingCustomSTL)
                    _loadedMavType = currentMavType;
                _isLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading STL: " + ex.Message, "STL Load Error");
                return false;
            }
        }

        private bool UsingCustomSTL => !string.IsNullOrEmpty(_customSTLPath) && File.Exists(_customSTLPath);

        private static MAVLink.MAV_TYPE CurrentMavType()
        {
            try
            {
                return MainV2.comPort?.MAV?.aptype ?? MAVLink.MAV_TYPE.FIXED_WING;
            }
            catch
            {
                return MAVLink.MAV_TYPE.FIXED_WING;
            }
        }

        /// <summary>
        /// The parse of the model for this vehicle, started on a worker thread the first time it is asked for. The
        /// vehicle parameters are read here, on the caller's thread; the task only reads the STL and parses it.
        /// </summary>
        private Task<Tuple<List<float>, List<float>>> ModelTask(MAVLink.MAV_TYPE mavType, bool usingCustomSTL)
        {
            string path = _customSTLPath;
            bool quadPlane = !usingCustomSTL && mavType == MAVLink.MAV_TYPE.FIXED_WING && IsQuadPlaneByParam();
            string key = usingCustomSTL
                ? "file:" + path + "|" + File.GetLastWriteTimeUtc(path).Ticks
                : "type:" + mavType + (quadPlane ? "|quadplane" : "");

            lock (_parsedModels)
            {
                Task<Tuple<List<float>, List<float>>> task;
                if (!_parsedModels.TryGetValue(key, out task))
                {
                    task = Task.Run(() =>
                    {
                        string stlContent = usingCustomSTL ? File.ReadAllText(path) : GetDefaultSTL(mavType, quadPlane);
                        return string.IsNullOrEmpty(stlContent) ? null : ParseSTL(stlContent);
                    });
                    _parsedModels[key] = task;
                }

                return task;
            }
        }

        /// <summary>
        /// Forces a reload of the STL model on next EnsureLoaded() call.
        /// </summary>
        public void Invalidate()
        {
            _isLoaded = false;
        }

        /// <summary>
        /// Checks if the current vehicle is a quadplane via Q_ENABLE parameter.
        /// Some quadplanes report as FIXED_WING but have Q_ENABLE=1.
        /// </summary>
        private bool IsQuadPlaneByParam()
        {
            try
            {
                var param = MainV2.comPort?.MAV?.param;
                if (param != null && param.ContainsKey("Q_ENABLE"))
                {
                    return param["Q_ENABLE"].Value == 1.0;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Gets the default embedded STL content based on MAV_TYPE.
        /// Uses specific models for different vehicle types, with fallbacks.
        /// quadPlane is the Q_ENABLE check for quadplanes that report as FIXED_WING, made by the caller.
        /// </summary>
        private static string GetDefaultSTL(MAVLink.MAV_TYPE mavType, bool quadPlane)
        {
            string stl = null;

            // Check for quadplane via Q_ENABLE parameter (some quadplanes report as FIXED_WING)
            if (quadPlane)
            {
                stl = MissionPlanner.Properties.Resources.quadplane_stl;
                if (string.IsNullOrEmpty(stl)) stl = MissionPlanner.Properties.Resources.plane_stl;
                return stl;
            }

            switch (mavType)
            {
                // Quadrotor (most common copter)
                case MAVLink.MAV_TYPE.QUADROTOR:
                    stl = MissionPlanner.Properties.Resources.quadrotor_stl;
                    if (string.IsNullOrEmpty(stl)) stl = MissionPlanner.Properties.Resources.copter_stl;
                    break;

                // Hexarotor
                case MAVLink.MAV_TYPE.HEXAROTOR:
                    stl = MissionPlanner.Properties.Resources.hexarotor_stl;
                    if (string.IsNullOrEmpty(stl)) stl = MissionPlanner.Properties.Resources.copter_stl;
                    break;

                // Octorotor
                case MAVLink.MAV_TYPE.OCTOROTOR:
                    stl = MissionPlanner.Properties.Resources.octorotor_stl;
                    if (string.IsNullOrEmpty(stl)) stl = MissionPlanner.Properties.Resources.copter_stl;
                    break;

                // Tricopter
                case MAVLink.MAV_TYPE.TRICOPTER:
                    stl = MissionPlanner.Properties.Resources.tricopter_stl;
                    if (string.IsNullOrEmpty(stl)) stl = MissionPlanner.Properties.Resources.copter_stl;
                    break;

                // Coaxial
                case MAVLink.MAV_TYPE.COAXIAL:
                    stl = MissionPlanner.Properties.Resources.coaxial_stl;
                    if (string.IsNullOrEmpty(stl)) stl = MissionPlanner.Properties.Resources.copter_stl;
                    break;

                // Helicopter
                case MAVLink.MAV_TYPE.HELICOPTER:
                    stl = MissionPlanner.Properties.Resources.helicopter_stl;
                    if (string.IsNullOrEmpty(stl)) stl = MissionPlanner.Properties.Resources.copter_stl;
                    break;

                // Dodecarotor (12 motors)
                case MAVLink.MAV_TYPE.DODECAROTOR:
                    stl = MissionPlanner.Properties.Resources.dodecarotor_stl;
                    if (string.IsNullOrEmpty(stl)) stl = MissionPlanner.Properties.Resources.copter_stl;
                    break;

                // VTOL / Quadplane types (via MAV_TYPE)
                case MAVLink.MAV_TYPE.VTOL_DUOROTOR:
                case MAVLink.MAV_TYPE.VTOL_QUADROTOR:
                case MAVLink.MAV_TYPE.VTOL_TILTROTOR:
                case MAVLink.MAV_TYPE.VTOL_RESERVED2:
                case MAVLink.MAV_TYPE.VTOL_RESERVED3:
                case MAVLink.MAV_TYPE.VTOL_RESERVED4:
                case MAVLink.MAV_TYPE.VTOL_RESERVED5:
                    stl = MissionPlanner.Properties.Resources.quadplane_stl;
                    if (string.IsNullOrEmpty(stl)) stl = MissionPlanner.Properties.Resources.plane_stl;
                    break;

                // Ground rover (car)
                case MAVLink.MAV_TYPE.GROUND_ROVER:
                    stl = MissionPlanner.Properties.Resources.rover_stl;
                    break;

                // Surface boat
                case MAVLink.MAV_TYPE.SURFACE_BOAT:
                    stl = MissionPlanner.Properties.Resources.boat_stl;
                    break;

                // Submarine
                case MAVLink.MAV_TYPE.SUBMARINE:
                    stl = MissionPlanner.Properties.Resources.sub_stl;
                    break;
            }

            // Final fallback to plane
            if (string.IsNullOrEmpty(stl))
                stl = MissionPlanner.Properties.Resources.plane_stl;

            return stl;
        }

        /// <summary>
        /// Parses ASCII STL content into vertices and normals.
        /// Keeps the STL’s original origin and applies a -90 degree Z rotation.
        /// </summary>
        private static Tuple<List<float>, List<float>> ParseSTL(string stlContent)
        {
            var vertices = new List<float>();
            var normals = new List<float>();

            var lines = stlContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            float nx = 0, ny = 0, nz = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("facet normal"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    nx = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    ny = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                    nz = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (trimmed.StartsWith("vertex"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    float vx = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    float vy = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    float vz = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);

                    vertices.Add(vx);
                    vertices.Add(vy);
                    vertices.Add(vz);

                    normals.Add(nx);
                    normals.Add(ny);
                    normals.Add(nz);
                }
            }

            // STL is in millimeters - conversion to meters happens in Map3D.DrawPlane()

            // Rotate STL -90 degrees around Z axis (swap X and Y, negate new Y)
            RotateModel(vertices, normals);
            return Tuple.Create(vertices, normals);
        }

        /// <summary>
        /// Rotates the model -90 degrees around Z axis.
        /// </summary>
        private static void RotateModel(List<float> vertices, List<float> normals)
        {
            for (int i = 0; i < vertices.Count; i += 3)
            {
                float x = vertices[i];
                float y = vertices[i + 1];
                vertices[i] = y;
                vertices[i + 1] = -x;
            }

            for (int i = 0; i < normals.Count; i += 3)
            {
                float normX = normals[i];
                float normY = normals[i + 1];
                normals[i] = normY;
                normals[i + 1] = -normX;
            }
        }
    }
}
