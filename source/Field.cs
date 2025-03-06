// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
class Field
{
    // Control flags for field behavior
    public bool recompute, inversed, essential;

    // Bitmasks representing sets of values
    public int zero, substrate;

    // Constructor that initializes a field from XML configuration
    public Field(XElement xelem, Grid grid)
    {
        // Parse configuration flags
        recompute = xelem.Get("recompute", false);
        essential = xelem.Get("essential", false);

        // Get the substrate (the cells this field applies to)
        string on = xelem.Get<string>("on");
        substrate = grid.Wave(on); // Converts string of symbols to a bitmask

        // Get source values that will have zero potential
        string zeroSymbols = xelem.Get<string>("from", null);
        if (zeroSymbols != null) inversed = true; // "from" means we're inverting the field
        else zeroSymbols = xelem.Get<string>("to"); // "to" means normal field direction

        // Convert zero symbols to bitmask
        zero = grid.Wave(zeroSymbols);
    }

    // Computes potential values for each cell in the grid
    // Potential is the "distance" from cells with zero potential
    // Returns false if no cells have zero potential (field can't be computed)
    public bool Compute(int[] potential, Grid grid)
    {
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;

        // Queue for breadth-first search, storing (distance, x, y, z)
        var front = new Queue<(int, int, int, int)>();

        // Initialize coordinates for traversing the grid
        int ix = 0, iy = 0, iz = 0;

        // Initialize all potentials to -1 (unvisited) and find starting cells
        for (int i = 0; i < grid.state.Length; i++)
        {
            potential[i] = -1;
            byte value = grid.state[i];

            // If this cell's value is in our "zero" set, it's a starting point
            if ((zero & 1 << value) != 0)
            {
                potential[i] = 0;
                front.Enqueue((0, ix, iy, iz));
            }

            // Increment coordinates, wrapping around at grid boundaries
            ix++;
            if (ix == MX)
            {
                ix = 0; iy++;
                if (iy == MY) { iy = 0; iz++; }
            }
        }

        // If no starting cells found, field can't be computed
        if (!front.Any()) return false;

        // Breadth-first search to compute potentials
        while (front.Any())
        {
            var (t, x, y, z) = front.Dequeue();
            var neighbors = Neighbors(x, y, z, MX, MY, MZ);

            // Check all neighbors of the current cell
            for (int n = 0; n < neighbors.Count; n++)
            {
                var (nx, ny, nz) = neighbors[n];
                int i = nx + ny * grid.MX + nz * grid.MX * grid.MY;
                byte v = grid.state[i];

                // If neighbor unvisited and in the substrate, assign it potential t+1
                if (potential[i] == -1 && (substrate & 1 << v) != 0)
                {
                    front.Enqueue((t + 1, nx, ny, nz));
                    potential[i] = t + 1;
                }
            }
        }

        return true;
    }

    // Gets the 6-connected neighbors (up, down, left, right, front, back)
    // within the grid boundaries
    static List<(int, int, int)> Neighbors(int x, int y, int z, int MX, int MY, int MZ)
    {
        List<(int, int, int)> result = new();
        if (x > 0) result.Add((x - 1, y, z));         // Left neighbor
        if (x < MX - 1) result.Add((x + 1, y, z));    // Right neighbor
        if (y > 0) result.Add((x, y - 1, z));         // Down neighbor
        if (y < MY - 1) result.Add((x, y + 1, z));    // Up neighbor
        if (z > 0) result.Add((x, y, z - 1));         // Back neighbor
        if (z < MZ - 1) result.Add((x, y, z + 1));    // Front neighbor
        return result;
    }

    // Calculates the potential change for a rule applied at a specific position
    // Returns null if any new value's potential can't be determined
    public static int? DeltaPointwise(byte[] state, Rule rule, int x, int y, int z, Field[] fields, int[][] potentials, int MX, int MY)
    {
        int sum = 0;
        int dz = 0, dy = 0, dx = 0;

        // Iterate through each cell in the rule's pattern
        for (int di = 0; di < rule.input.Length; di++)
        {
            byte newValue = rule.output[di];

            // If this cell changes value (0xff means "don't change")
            if (newValue != 0xff && (rule.input[di] & 1 << newValue) == 0)
            {
                // Calculate grid position for this cell
                int i = x + dx + (y + dy) * MX + (z + dz) * MX * MY;

                // Get potential for the new value
                int newPotential = potentials[newValue][i];
                if (newPotential == -1) return null; // Can't determine potential

                byte oldValue = state[i];
                int oldPotential = potentials[oldValue][i];

                // Basic contribution to sum is the difference in potentials
                sum += newPotential - oldPotential;

                // Handle special field inversions
                if (fields != null)
                {
                    // If old value's field is inversed, add twice its potential
                    Field oldField = fields[oldValue];
                    if (oldField != null && oldField.inversed) sum += 2 * oldPotential;

                    // If new value's field is inversed, subtract twice its potential
                    Field newField = fields[newValue];
                    if (newField != null && newField.inversed) sum -= 2 * newPotential;
                }
            }

            // Move to next cell in the rule's pattern, wrapping at boundaries
            dx++;
            if (dx == rule.IMX)
            {
                dx = 0; dy++;
                if (dy == rule.IMY) { dy = 0; dz++; }
            }
        }

        return sum;
    }
}

/*
========== SUMMARY ==========

This code implements a "Field" system for a grid-based simulation, similar to how a heat map or distance field works. 

Imagine a grid where some cells are marked as "sources" (with zero potential) and the Field calculates how far each other cell is from the nearest source. This distance is called the "potential" of the cell.

Here's how it works in simple terms:

1. Field Setup: The Field is configured using XML, specifying:
   - Which cells to consider (the "substrate")
   - Which cells are sources (have zero potential)
   - Whether the field is "inversed" (treats "from" differently than "to")

2. Potential Calculation: Starting from the source cells, the code uses a breadth-first search (like ripples spreading in water) to calculate the potential of each cell. This is similar to finding the shortest path in a maze.

3. Rule Evaluation: The DeltaPointwise method helps evaluate how a rule would change the total potential if applied. It:
   - Calculates the difference between old and new potentials
   - Handles special cases for inversed fields
   - Returns null if any necessary potential can't be determined

This system is likely part of a larger procedural generation or constraint solving algorithm, where rules are chosen to minimize or maximize field potentials. Think of it like water flowing downhill - the algorithm might prefer rules that decrease the total potential.

The Field class basically creates a landscape of values (potentials) that guide the algorithm's decisions, similar to how a GPS calculates the distance to your destination from different routes.
*/