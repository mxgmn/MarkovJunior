// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

/// <summary>
/// A 'path' node draws paths between specific colors in the grid. On each
/// execution step, a 'start' cell is chosen, and a shortest path to an 'end'
/// cell is drawn via 'substrate' cells. The node is inapplicable if no such
/// path can be drawn.
/// </summary>
class PathNode : Node
{
    /// <summary>A bitmask of the colors of 'start' cells, i.e. where a path can start.</summary>
    public int start;
    
    /// <summary>A bitmask of the colors of 'end' cells, i.e. where a path can end.</summary>
    public int finish;
    
    /// <summary>A bitmask of the 'substrate' colors, i.e. where a path can go through.</summary>
    public int substrate;
    
    /// <summary>The color of a path when it is drawn to the grid.</summary>
    public byte value;
    
    /// <summary>
    /// If <c>true</c>, the path-finding algorithm will greedily avoid changing
    /// direction, when this does not lead to a longer path; i.e. the 'pen' has
    /// inertia. Otherwise, when multiple directions lead to equal shortest
    /// paths, one is chosen at random independently of previous steps.
    /// </summary>
    bool inertia;
    
    /// <summary>
    /// If <c>true</c>, the start position furthest from an end position will
    /// be chosen when finding a path. Otherwise, the start position nearest to
    /// an end position will be chosen.
    /// </summary>
    bool longest;
    
    /// <summary>If <c>true</c>, paths may include diagonal steps in two dimensions.</summary>
    bool edges;
    
    /// <summary>If <c>true</c>, paths may include diagonal steps in three dimensions.</summary>
    bool vertices;

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
        // compute a distance field; generations[x + y * MX + z * MX * MY] will
        // be the distance from (x, y, z) to an end cell, or -1 if no path exists
        
        // initialise the data structures for computing the distance field
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

        // if there are no start positions or end positions, this node is inapplicable
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
        
        // compute the distance field by breadth-first search
        while (frontier.Any())
        {
            var (t, x, y, z) = frontier.Dequeue();
            foreach (var (dx, dy, dz) in Directions(x, y, z, MX, MY, MZ, edges, vertices)) push(t + 1, x + dx, y + dy, z + dz);
        }
        
        // if there are no start positions which can reach an end position via a path, this node is inapplicable
        if (!startPositions.Where(p => generations[p.x + p.y * MX + p.z * MX * MY] > 0).Any()) return false;
        
        // choose a start position with the minimum (or maximum) distance from an end position
        Random localRandom = new(ip.random.Next());
        double min = MX * MY * MZ, max = -2;
        (int, int, int) argmin = (-1, -1, -1), argmax = (-1, -1, -1);

        foreach (var p in startPositions)
        {
            int g = generations[p.x + p.y * MX + p.z * MX * MY];
            if (g == -1) continue;
            double dg = g;
            // a small noise term to differentiate start positions at equal distances
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
        
        // advance one step in the path before we start drawing the path
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

    /// <summary>
    /// Chooses a direction to continue drawing the path in.
    /// </summary>
    /// <param name="x">The current x position of the 'pen'.</param>
    /// <param name="y">The current y position of the 'pen'.</param>
    /// <param name="z">The current z position of the 'pen'.</param>
    /// <param name="dx">The delta x from the previous step.</param>
    /// <param name="dy">The delta y from the previous step.</param>
    /// <param name="dz">The delta z from the previous step.</param>
    /// <param name="generations">The distance field.</param>
    /// <param name="random"><inheritdoc cref="Interpreter.random" path="/summary"/></param>
    /// <returns>A tuple (dx, dy, dz).</returns>
    (int, int, int) Direction(int x, int y, int z, int dx, int dy, int dz, int[] generations, Random random)
    {
        List<(int x, int y, int z)> candidates = new();
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        int g = generations[x + y * MX + z * MX * MY];
        
        // adds a direction to the candidates list, if it can lead to a shortest path
        void add(int DX, int DY, int DZ)
        {
            if (generations[x + DX + (y + DY) * MX + (z + DZ) * MX * MY] == g - 1) candidates.Add((DX, DY, DZ));
        };

        if (!vertices && !edges)
        {
            // no diagonal moves allowed
            if (inertia && (dx != 0 || dy != 0 || dz != 0))
            {
                // continue in the same direction if possible
                int cx = x + dx, cy = y + dy, cz = z + dz;
                if (cx >= 0 && cy >= 0 && cz >= 0 && cx < MX && cy < MY && cz < MZ && generations[cx + cy * MX + cz * MX * MY] == g - 1)
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
            // diagonal moves are allowed
            foreach (var p in Directions(x, y, z, MX, MY, MZ, edges, vertices)) add(p.x, p.y, p.z);
            (int, int, int) result = (-1, -1, -1);

            if (inertia && (dx != 0 || dy != 0 || dz != 0))
            {
                // choose a direction minimising the angle with the previous direction
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

    /// <summary>
    /// Returns a list of neighbours of a given grid cell.
    /// </summary>
    /// <param name="x">The x coordinate of the grid cell.</param>
    /// <param name="y">The y coordinate of the grid cell.</param>
    /// <param name="z">The z coordinate of the grid cell.</param>
    /// <param name="MX"><inheritdoc cref="Grid.MX" path="/summary"/></param>
    /// <param name="MY"><inheritdoc cref="Grid.MY" path="/summary"/></param>
    /// <param name="MZ"><inheritdoc cref="Grid.MZ" path="/summary"/></param>
    /// <param name="edges"><inheritdoc cref="PathNode.edges" path="/summary"/></param>
    /// <param name="vertices"><inheritdoc cref="PathNode.vertices" path="/summary"/></param>
    /// <returns></returns>
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
