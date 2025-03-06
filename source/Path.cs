// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class PathNode : Node
{
    public int start, finish, substrate;   // Bitmasks for start/end/allowed path cells
    public byte value;                      // Value to set for path cells
    bool inertia, longest, edges, vertices; // Path generation options

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Load the starting position types (as bitmask)
        string startSymbols = xelem.Get<string>("from");
        start = grid.Wave(startSymbols);

        // Get color/value to draw path with (default to first start symbol)
        value = grid.values[xelem.Get("color", startSymbols[0])];

        // Load destination position types
        finish = grid.Wave(xelem.Get<string>("to"));

        // Path generation options
        inertia = xelem.Get("inertia", false);   // Prefer continuing in same direction
        longest = xelem.Get("longest", false);   // Choose longest path (vs shortest)
        edges = xelem.Get("edges", false);       // Allow diagonal movement (in-plane)
        vertices = xelem.Get("vertices", false); // Allow 3D diagonal movement

        // Allowed substrate cells where path can go through
        substrate = grid.Wave(xelem.Get<string>("on"));
        return true;
    }

    public override void Reset() { }

    public override bool Go()
    {
        // Queue for breadth-first search from destination
        Queue<(int, int, int, int)> frontier = new();

        // Track potential starting positions
        List<(int x, int y, int z)> startPositions = new();

        // Distance from destination for each cell (-1 = unreachable)
        int[] generations = AH.Array1D(grid.state.Length, -1);
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;

        // Initialize data structures
        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    int i = x + y * MX + z * MX * MY;
                    generations[i] = -1;  // Default to unreachable

                    byte s = grid.state[i];
                    if ((start & 1 << s) != 0) startPositions.Add((x, y, z));  // Add to start candidates
                    if ((finish & 1 << s) != 0)  // Add destinations to frontier
                    {
                        generations[i] = 0;  // Distance 0 from destination
                        frontier.Enqueue((0, x, y, z));  // Add to BFS queue
                    }
                }

        // Bail if no valid start or end points
        if (!startPositions.Any() || !frontier.Any()) return false;

        // Helper function to add a cell to the frontier
        void push(int t, int x, int y, int z)
        {
            int i = x + y * MX + z * MX * MY;
            byte v = grid.state[i];
            if (generations[i] == -1 && ((substrate & 1 << v) != 0 || (start & 1 << v) != 0))
            {
                if ((substrate & 1 << v) != 0) frontier.Enqueue((t, x, y, z));
                generations[i] = t;  // Set distance from destination
            }
        };

        // Run breadth-first search from destination
        while (frontier.Any())
        {
            var (t, x, y, z) = frontier.Dequeue();
            // Check all allowed neighbors
            foreach (var (dx, dy, dz) in Directions(x, y, z, MX, MY, MZ, edges, vertices))
                push(t + 1, x + dx, y + dy, z + dz);
        }

        // Check if any start point is reachable from destination
        if (!startPositions.Where(p => generations[p.x + p.y * MX + p.z * MX * MY] > 0).Any()) return false;

        // Random number generator for this path
        Random localRandom = new(ip.random.Next());
        double min = MX * MY * MZ, max = -2;
        (int, int, int) argmin = (-1, -1, -1), argmax = (-1, -1, -1);

        // Find shortest and longest paths
        foreach (var p in startPositions)
        {
            int g = generations[p.x + p.y * MX + p.z * MX * MY];
            if (g == -1) continue;  // Skip unreachable
            double dg = g;
            double noise = 0.1 * localRandom.NextDouble();  // Random tiebreaker

            if (dg + noise < min)  // Track minimum
            {
                min = dg + noise;
                argmin = p;
            }

            if (dg + noise > max)  // Track maximum
            {
                max = dg + noise;
                argmax = p;
            }
        }

        // Choose starting point based on longest/shortest preference
        var (penx, peny, penz) = longest ? argmax : argmin;

        // Get initial direction toward destination
        var (dirx, diry, dirz) = Direction(penx, peny, penz, 0, 0, 0, generations, localRandom);
        penx += dirx;
        peny += diry;
        penz += dirz;

        // Draw the path from start to destination
        while (generations[penx + peny * MX + penz * MX * MY] != 0)  // Until we reach destination
        {
            // Set cell to path value
            grid.state[penx + peny * MX + penz * MX * MY] = value;
            ip.changes.Add((penx, peny, penz));  // Track for rendering

            // Get next direction (potentially with inertia)
            (dirx, diry, dirz) = Direction(penx, peny, penz, dirx, diry, dirz, generations, localRandom);
            penx += dirx;
            peny += diry;
            penz += dirz;
        }
        return true;
    }

    // Choose next direction based on distance field and optional inertia
    (int, int, int) Direction(int x, int y, int z, int dx, int dy, int dz, int[] generations, Random random)
    {
        List<(int x, int y, int z)> candidates = new();
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        int g = generations[x + y * MX + z * MX * MY];  // Current distance

        // Helper to add valid move directions
        void add(int DX, int DY, int DZ)
        {
            // Only add directions that move closer to destination
            if (generations[x + DX + (y + DY) * MX + (z + DZ) * MX * MY] == g - 1)
                candidates.Add((DX, DY, DZ));
        };

        // Handle case of 4-connected grid (no diagonals)
        if (!vertices && !edges)
        {
            // Check for inertia - continue in same direction if possible
            if (dx != 0 || dy != 0 || dz != 0)
            {
                int cx = x + dx, cy = y + dy, cz = z + dz;
                if (inertia && cx >= 0 && cy >= 0 && cz >= 0 && cx < MX && cy < MY && cz < MZ &&
                    generations[cx + cy * MX + cz * MX * MY] == g - 1)
                    return (dx, dy, dz);  // Continue in same direction
            }

            // Check all orthogonal neighbors
            if (x > 0) add(-1, 0, 0);
            if (x < MX - 1) add(1, 0, 0);
            if (y > 0) add(0, -1, 0);
            if (y < MY - 1) add(0, 1, 0);
            if (z > 0) add(0, 0, -1);
            if (z < MZ - 1) add(0, 0, 1);

            return candidates.Random(random);  // Pick random valid direction
        }
        else  // Handle case with diagonals
        {
            // Get all valid directions based on connectivity
            foreach (var p in Directions(x, y, z, MX, MY, MZ, edges, vertices)) add(p.x, p.y, p.z);
            (int, int, int) result = (-1, -1, -1);

            // Apply inertia if requested and we have a previous direction
            if (inertia && (dx != 0 || dy != 0 || dz != 0))
            {
                double maxScalar = -4;
                foreach (var c in candidates)
                {
                    double noise = 0.1 * random.NextDouble();  // Small random tiebreaker

                    // Calculate cosine similarity (dot product) to favor direction close to previous
                    double cos = (c.x * dx + c.y * dy + c.z * dz) /
                        Math.Sqrt((c.x * c.x + c.y * c.y + c.z * c.z) * (dx * dx + dy * dy + dz * dz));

                    if (cos + noise > maxScalar)
                    {
                        maxScalar = cos + noise;
                        result = c;
                    }
                }
            }
            else result = candidates.Random(random);  // Pick random valid direction

            return result;
        }
    }

    // Get all valid neighboring directions based on connectivity
    static List<(int x, int y, int z)> Directions(int x, int y, int z, int MX, int MY, int MZ, bool edges, bool vertices)
    {
        List<(int, int, int)> result = new();
        if (MZ == 1)  // 2D case
        {
            // Orthogonal neighbors
            if (x > 0) result.Add((-1, 0, 0));
            if (x < MX - 1) result.Add((1, 0, 0));
            if (y > 0) result.Add((0, -1, 0));
            if (y < MY - 1) result.Add((0, 1, 0));

            // Add diagonal neighbors if edges enabled
            if (edges)
            {
                if (x > 0 && y > 0) result.Add((-1, -1, 0));
                if (x > 0 && y < MY - 1) result.Add((-1, 1, 0));
                if (x < MX - 1 && y > 0) result.Add((1, -1, 0));
                if (x < MX - 1 && y < MY - 1) result.Add((1, 1, 0));
            }
        }
        else  // 3D case
        {
            // Orthogonal neighbors
            if (x > 0) result.Add((-1, 0, 0));
            if (x < MX - 1) result.Add((1, 0, 0));
            if (y > 0) result.Add((0, -1, 0));
            if (y < MY - 1) result.Add((0, 1, 0));
            if (z > 0) result.Add((0, 0, -1));
            if (z < MZ - 1) result.Add((0, 0, 1));

            // Add 2D diagonals if edges enabled
            if (edges)
            {
                // XY plane diagonals
                if (x > 0 && y > 0) result.Add((-1, -1, 0));
                if (x > 0 && y < MY - 1) result.Add((-1, 1, 0));
                if (x < MX - 1 && y > 0) result.Add((1, -1, 0));
                if (x < MX - 1 && y < MY - 1) result.Add((1, 1, 0));

                // XZ plane diagonals
                if (x > 0 && z > 0) result.Add((-1, 0, -1));
                if (x > 0 && z < MZ - 1) result.Add((-1, 0, 1));
                if (x < MX - 1 && z > 0) result.Add((1, 0, -1));
                if (x < MX - 1 && z < MZ - 1) result.Add((1, 0, 1));

                // YZ plane diagonals
                if (y > 0 && z > 0) result.Add((0, -1, -1));
                if (y > 0 && z < MZ - 1) result.Add((0, -1, 1));
                if (y < MY - 1 && z > 0) result.Add((0, 1, -1));
                if (y < MY - 1 && z < MZ - 1) result.Add((0, 1, 1));
            }

            // Add 3D diagonals if vertices enabled
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

/*
=== SUMMARY ===

The PathNode class is a pathfinding algorithm that creates paths between designated start and end points in a grid. Unlike normal pathfinding that just finds the shortest route, it offers many customization options.

Think of it like planning a hiking trail through a landscape:

1. Pathfinding Process:
   - Uses breadth-first search starting from destinations to create a "distance field"
   - Identifies valid starting points and picks one (shortest or longest path)
   - Traces the path by always moving to a cell closer to the destination
   - Draws the path by changing cell values

2. Key Features:
   - Can find shortest or longest paths ("as the crow flies" vs. "scenic route")
   - Supports "inertia" to prefer continuing in the same direction (creating straighter paths)
   - Can restrict paths to only travel on specific "substrate" cell types
   - Supports different connectivity options:
     * Basic: Only orthogonal moves (up/down/left/right)
     * Edges: Adds 2D diagonal moves
     * Vertices: Adds full 3D diagonal moves

3. Applications:
   - Creating road/river networks in procedural landscapes
   - Generating dungeon corridors between rooms
   - Creating wiring paths in circuit designs
   - Drawing connective features that follow natural-looking paths

The algorithm is an enhanced version of Dijkstra's algorithm, with the twist that it can optimize for longest paths and path smoothness rather than just shortest distance.
*/