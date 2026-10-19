using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.Scene3D
{
    public enum DemStatus
    {
        /// <summary>Every sample came from elevation data (or is known ocean).</summary>
        Ready,

        /// <summary>Some elevation data is still downloading or unavailable; heights were not filled.</summary>
        Pending
    }

    /// <summary>
    /// Samples SRTM heights for a whole tile grid at once: each 1x1 degree cell is fetched once per tile
    /// rather than once per vertex, and missing cells are queued for download instead of being skipped.
    /// </summary>
    public static class ElevationSampler
    {
        /// <summary>Cells (by south-west corner) the 3D map has loaded, released again when it closes.</summary>
        static readonly HashSet<(int lat, int lng)> _usedCells = new HashSet<(int lat, int lng)>();

        /// <summary>
        /// Frees the SRTM cells the terrain has loaded (26 MB each). The 3D map looks much further than anything
        /// else, so without this they would stay in memory after it closes.
        /// </summary>
        public static void ReleaseCells()
        {
            (int lat, int lng)[] cells;
            lock (_usedCells)
            {
                cells = _usedCells.ToArray();
                _usedCells.Clear();
            }

            foreach (var cell in cells)
                srtm.ReleaseCell(cell.lat + 0.5, cell.lng + 0.5);
        }

        /// <summary>
        /// Fills heights[j * cols + i] for every (rowLats[j], colLngs[i]). Returns Pending without touching
        /// the output if any cell the grid needs is not available yet.
        /// </summary>
        public static DemStatus Sample(double[] rowLats, double[] colLngs, float[] heights)
        {
            // Tile edges often lie exactly on a degree line (zoom 9 edges at 0/45/90/135 degrees, the equator),
            // where floor() would pick the neighbouring SRTM cell. Nudge every sample a hair (~0.1 mm) into
            // the tile so edge samples come from the same cell as the rest of the tile.
            rowLats = NudgeInward(rowLats);
            colLngs = NudgeInward(colLngs);

            int rows = rowLats.Length;
            int cols = colLngs.Length;

            var cells = new Dictionary<long, short[,]>();
            bool pending = false;

            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    long key = CellKey(rowLats[j], colLngs[i]);
                    if (cells.ContainsKey(key))
                        continue;

                    short[,] data;
                    var status = srtm.GetCell(rowLats[j], colLngs[i], out data);
                    if (status == srtm.CellStatus.Pending || status == srtm.CellStatus.Missing)
                        pending = true;
                    if (status == srtm.CellStatus.Loaded)
                    {
                        lock (_usedCells)
                            _usedCells.Add(((int)Math.Floor(rowLats[j]), (int)Math.Floor(colLngs[i])));
                    }

                    // Ocean cells keep a null grid and sample as sea level.
                    cells[key] = status == srtm.CellStatus.Loaded ? data : null;
                }
            }

            if (pending)
                return DemStatus.Pending;

            for (int j = 0; j < rows; j++)
            {
                double lat = rowLats[j];
                for (int i = 0; i < cols; i++)
                {
                    double lng = colLngs[i];
                    var data = cells[CellKey(lat, lng)];
                    double alt = data == null ? 0 : srtm.SampleCell(data, lat, lng);
                    heights[j * cols + i] = double.IsNaN(alt) ? float.NaN : (float)alt;
                }
            }

            FillVoids(heights, cols, rows);
            return DemStatus.Ready;
        }

        static double[] NudgeInward(double[] values)
        {
            const double epsilon = 1e-9;
            double middle = (values[0] + values[values.Length - 1]) / 2;
            var nudged = new double[values.Length];
            for (int k = 0; k < values.Length; k++)
                nudged[k] = values[k] + (values[k] < middle ? epsilon : values[k] > middle ? -epsilon : 0);
            return nudged;
        }

        static long CellKey(double lat, double lng)
        {
            return (long)Math.Floor(lat) * 1000 + (long)Math.Floor(lng);
        }

        /// <summary>Replaces SRTM voids with the mean of the valid samples so a void never drops a tile.</summary>
        static void FillVoids(float[] heights, int cols, int rows)
        {
            double sum = 0;
            int count = 0;
            for (int k = 0; k < heights.Length; k++)
            {
                if (!float.IsNaN(heights[k]))
                {
                    sum += heights[k];
                    count++;
                }
            }

            if (count == heights.Length)
                return;

            float fill = count > 0 ? (float)(sum / count) : 0f;
            for (int k = 0; k < heights.Length; k++)
            {
                if (float.IsNaN(heights[k]))
                    heights[k] = fill;
            }
        }

        /// <summary>
        /// Approximate heights for a tile from an ancestor's grid, used until real elevation data arrives.
        /// </summary>
        public static void SampleFromAncestor(TileId tile, int gridN, TileId ancestor, float[] ancestorHeights,
            int ancestorGridN, float[] heights)
        {
            int cols = gridN + 1;
            int ancestorCols = ancestorGridN + 1;
            double scale = 1.0 / (1 << (tile.Z - ancestor.Z));

            for (int j = 0; j <= gridN; j++)
            {
                double v = ((tile.Y + (double)j / gridN) * scale - ancestor.Y) * ancestorGridN;
                for (int i = 0; i <= gridN; i++)
                {
                    double u = ((tile.X + (double)i / gridN) * scale - ancestor.X) * ancestorGridN;
                    heights[j * cols + i] = Bilinear(ancestorHeights, ancestorCols, u, v);
                }
            }
        }

        static float Bilinear(float[] grid, int cols, double u, double v)
        {
            int max = cols - 1;
            u = Math.Max(0, Math.Min(max, u));
            v = Math.Max(0, Math.Min(max, v));
            int i0 = Math.Min((int)u, max - 1);
            int j0 = Math.Min((int)v, max - 1);
            double fu = u - i0;
            double fv = v - j0;

            double a = grid[j0 * cols + i0];
            double b = grid[j0 * cols + i0 + 1];
            double c = grid[(j0 + 1) * cols + i0];
            double d = grid[(j0 + 1) * cols + i0 + 1];

            double top = a + (b - a) * fu;
            double bottom = c + (d - c) * fu;
            return (float)(top + (bottom - top) * fv);
        }
    }
}
