// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class PathNode : Node
{
    public int start, finish, substrate;
    public byte value;
    bool inertia, longest, edges, vertices;

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        string startSymbols = xelem.Get<string>("from");
        start = grid.Wave(startSymbols);
        value = grid.values[xelem.Get("color", startSymbols[0])];
        finish = grid.Wave(xelem.Get<string>("to"));
        inertia = xelem.Get("inertia", false);
        longest = xelem.Get("longest", false);
        edges = xelem.Get("edges", false);
        vertices = xelem.Get("vertices", false);
        substrate = grid.Wave(xelem.Get<string>("on"));
        return true;
    }

    public override void Reset() { }
    public override bool Go()
    {
        Queue<(int, int, int, int)> frontier = new();
        List<(int x, int y, int z)> startPositions = new();
        int[] generations = AH.Array1D(grid.state.Length, -1);
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;

        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    int i = x + y * MX + z * MX * MY;
                    generations[i] = -1;

                    byte s = grid.state[i];
                    if ((start & 1 << s) != 0) startPositions.Add((x, y, z));
                    if ((finish & 1 << s) != 0)
                    {
                        generations[i] = 0;
                        frontier.Enqueue((0, x, y, z));
                    }
                }

        if (!startPositions.Any() || !frontier.Any()) return false;

        void push(int t, int x, int y, int z)
        {
            int i = x + y * MX + z * MX * MY;
            byte v = grid.state[i];
            if (generations[i] == -1 && ((substrate & 1 << v) != 0 || (start & 1 << v) != 0))
            {
                if ((substrate & 1 << v) != 0) frontier.Enqueue((t, x, y, z));
                generations[i] = t;
            }
        };

        while (frontier.Any())
        {
            var (t, x, y, z) = frontier.Dequeue();
            foreach (var (dx, dy, dz) in Directions(x, y, z, MX, MY, MZ, edges, vertices)) push(t + 1, x + dx, y + dy, z + dz);
        }

        if (!startPositions.Where(p => generations[p.x + p.y * MX + p.z * MX * MY] > 0).Any()) return false;
        
        Random localRandom = new(ip.random.Next());
        double min = MX * MY * MZ, max = -2;
        (int, int, int) argmin = (-1, -1, -1), argmax = (-1, -1, -1);

        foreach (var p in startPositions)
        {
            int g = generations[p.x + p.y * MX + p.z * MX * MY];
            if (g == -1) continue;
            double dg = g;
            double noise = 0.1 * localRandom.NextDouble();

            if (dg + noise < min)
            {
                min = dg + noise;
                argmin = p;
            }

            if (dg + noise > max)
            {
                max = dg + noise;
                argmax = p;
            }
        }

        var (penx, peny, penz) = longest ? argmax : argmin;
        var (dirx, diry, dirz) = Direction(penx, peny, penz, 0, 0, 0, generations, localRandom);
        penx += dirx;
        peny += diry;
        penz += dirz;

        while (generations[penx + peny * MX + penz * MX * MY] != 0)
        {
            grid.state[penx + peny * MX + penz * MX * MY] = value;
            ip.changes.Add((penx, peny, penz));
            (dirx, diry, dirz) = Direction(penx, peny, penz, dirx, diry, dirz, generations, localRandom);
            penx += dirx;
            peny += diry;
            penz += dirz;
        }
        return true;
    }

    (int, int, int) Direction(int x, int y, int z, int dx, int dy, int dz, int[] generations, Random random)
    {
        List<(int x, int y, int z)> candidates = new();
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        int g = generations[x + y * MX + z * MX * MY];

        void add(int DX, int DY, int DZ)
        {
            if (generations[x + DX + (y + DY) * MX + (z + DZ) * MX * MY] == g - 1) candidates.Add((DX, DY, DZ));
        };

        if (!vertices && !edges)
        {
            if (dx != 0 || dy != 0 || dz != 0)
            {
                int cx = x + dx, cy = y + dy, cz = z + dz;
                if (inertia && cx >= 0 && cy >= 0 && cz >= 0 && cx < MX && cy < MY && cz < MZ && generations[cx + cy * MX + cz * MX * MY] == g - 1)
                    return (dx, dy, dz);
            }

            if (x > 0) add(-1, 0, 0);
            if (x < MX - 1) add(1, 0, 0);
            if (y > 0) add(0, -1, 0);
            if (y < MY - 1) add(0, 1, 0);
            if (z > 0) add(0, 0, -1);
            if (z < MZ - 1) add(0, 0, 1);

            return candidates.Random(random);
        }
        else
        {
            foreach (var p in Directions(x, y, z, MX, MY, MZ, edges, vertices)) add(p.x, p.y, p.z);
            (int, int, int) result = (-1, -1, -1);

            if (inertia && (dx != 0 || dy != 0 || dz != 0))
            {
                double maxScalar = -4;
                foreach (var c in candidates)
                {
                    double noise = 0.1 * random.NextDouble();
                    double cos = (c.x * dx + c.y * dy + c.z * dz) / Math.Sqrt((c.x * c.x + c.y * c.y + c.z * c.z) * (dx * dx + dy * dy + dz * dz));

                    if (cos + noise > maxScalar)
                    {
                        maxScalar = cos + noise;
                        result = c;
                    }
                }
            }
            else result = candidates.Random(random);

            return result;
        }
    }

    static List<(int x, int y, int z)> Directions(int x, int y, int z, int MX, int MY, int MZ, bool edges, bool vertices)
    {
        List<(int, int, int)> result = new();
        if (MZ == 1)
        {
            if (x > 0) result.Add((-1, 0, 0));
            if (x < MX - 1) result.Add((1, 0, 0));
            if (y > 0) result.Add((0, -1, 0));
            if (y < MY - 1) result.Add((0, 1, 0));

            if (edges)
            {
                if (x > 0 && y > 0) result.Add((-1, -1, 0));
                if (x > 0 && y < MY - 1) result.Add((-1, 1, 0));
                if (x < MX - 1 && y > 0) result.Add((1, -1, 0));
                if (x < MX - 1 && y < MY - 1) result.Add((1, 1, 0));
            }
        }
        else
        {
            if (x > 0) result.Add((-1, 0, 0));
            if (x < MX - 1) result.Add((1, 0, 0));
            if (y > 0) result.Add((0, -1, 0));
            if (y < MY - 1) result.Add((0, 1, 0));
            if (z > 0) result.Add((0, 0, -1));
            if (z < MZ - 1) result.Add((0, 0, 1));

            if (edges)
            {
                if (x > 0 && y > 0) result.Add((-1, -1, 0));
                if (x > 0 && y < MY - 1) result.Add((-1, 1, 0));
                if (x < MX - 1 && y > 0) result.Add((1, -1, 0));
                if (x < MX - 1 && y < MY - 1) result.Add((1, 1, 0));

                if (x > 0 && z > 0) result.Add((-1, 0, -1));
                if (x > 0 && z < MZ - 1) result.Add((-1, 0, 1));
                if (x < MX - 1 && z > 0) result.Add((1, 0, -1));
                if (x < MX - 1 && z < MZ - 1) result.Add((1, 0, 1));

                if (y > 0 && z > 0) result.Add((0, -1, -1));
                if (y > 0 && z < MZ - 1) result.Add((0, -1, 1));
                if (y < MY - 1 && z > 0) result.Add((0, 1, -1));
                if (y < MY - 1 && z < MZ - 1) result.Add((0, 1, 1));
            }

            if (vertices)
            {
                if (x > 0 && y > 0 && z > 0) result.Add((-1, -1, -1));
                if (x > 0 && y > 0 && z < MZ - 1) result.Add((-1, -1, 1));
                if (x > 0 && y < MY - 1 && z > 0) result.Add((-1, 1, -1));
                if (x > 0 && y < MY - 1 && z < MZ - 1) result.Add((-1, 1, 1));
                if (x < MX - 1 && y > 0 && z > 0) result.Add((1, -1, -1));
                if (x < MX - 1 && y > 0 && z < MZ - 1) result.Add((1, -1, 1));
                if (x < MX - 1 && y < MY - 1 && z > 0) result.Add((1, 1, -1));
                if (x < MX - 1 && y < MY - 1 && z < MZ - 1) result.Add((1, 1, 1));
            }
        }

        return result;
    }
}
