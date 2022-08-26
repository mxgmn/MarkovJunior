// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

/// <summary>
/// <para>
/// Computes a distance field; given bitmasks for the 'zero' and 'substrate'
/// colors, the distance field potentials are the shortest distances from each
/// cell to a zero, via orthogonally-adjacent substrate cells. The potential of
/// a cell is -1 if it is not a zero or substrate, or if it has no path to a
/// zero via the substrate; a value of -1 represents an infinite potential.
/// </para>
/// <para>
/// Each field is associated with a <see cref="RuleNode">RuleNode</see> and a
/// color. The field potentials are used to compute 'scores' for grid states so
/// that rule applications can be biased towards minimising the 'score'.
/// </para>
/// </summary>
class Field
{
    /// <summary>
    /// If true, the distance field should be recomputed on each execution step
    /// of the <see cref="RuleNode">RuleNode</see> associated with this field;
    /// otherwise, it will only be computed on the first execution step after
    /// the node is reset.
    /// </summary>
    public bool recompute;
    
    /// <summary>
    /// If true, the distance field's sign is inverted when used to calculate
    /// the 'score' for a grid state.
    /// </summary>
    public bool inversed;
    
    /// <summary>
    /// If true, then the <see cref="RuleNode">RuleNode</see> associated with
    /// this field is inapplicable when the grid has no 'zeroes'.
    /// </summary>
    public bool essential;
    
    /// <summary>
    /// A bitmask of the 'zero' colors for this distance field. The potentials
    /// are shortest distances to a 'zero' via 'substrate' cells.
    /// </summary>
    public int zero;
    
    /// <summary>
    /// A bitmask of the 'substrate' colors for this distance field. The
    /// potentials are shortest distances to a 'zero' via 'substrate' cells.
    /// </summary>
    public int substrate;

    public Field(XElement xelem, Grid grid)
    {
        recompute = xelem.Get("recompute", false);
        essential = xelem.Get("essential", false);
        string on = xelem.Get<string>("on");
        substrate = grid.Wave(on);

        string zeroSymbols = xelem.Get<string>("from", null);
        if (zeroSymbols != null) inversed = true;
        else zeroSymbols = xelem.Get<string>("to");
        zero = grid.Wave(zeroSymbols);
    }

    /// <summary>
    /// Computes the distance field for the given grid, as a flat array.
    /// </summary>
    /// <param name="potential">The array which the distance field potentials will be written to.</param>
    /// <param name="grid">The grid state for which the distance field will be computed.</param>
    /// <returns><c>true</c> if the grid has any 'zeroes', otherwise <c>false</c>.</returns>
    public bool Compute(int[] potential, Grid grid)
    {
        // compute the distance field by breadth-first search
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        var front = new Queue<(int, int, int, int)>();

        // initialise the potentials array and enqueue the zeroes
        int ix = 0, iy = 0, iz = 0;
        for (int i = 0; i < grid.state.Length; i++)
        {
            potential[i] = -1;
            byte value = grid.state[i];
            if ((zero & 1 << value) != 0)
            {
                potential[i] = 0;
                front.Enqueue((0, ix, iy, iz));
            }

            ix++;
            if (ix == MX)
            {
                ix = 0; iy++;
                if (iy == MY) { iy = 0; iz++; }
            }
        }
        
        // return false if there are no zeroes
        if (!front.Any()) return false;
        
        // BFS loop
        while (front.Any())
        {
            var (t, x, y, z) = front.Dequeue();
            var neighbors = Neighbors(x, y, z, MX, MY, MZ);
            for (int n = 0; n < neighbors.Count; n++)
            {
                var (nx, ny, nz) = neighbors[n];
                int i = nx + ny * grid.MX + nz * grid.MX * grid.MY;
                byte v = grid.state[i];
                if (potential[i] == -1 && (substrate & 1 << v) != 0)
                {
                    front.Enqueue((t + 1, nx, ny, nz));
                    potential[i] = t + 1;
                }
            }
        }

        return true;
    }
    
    /// <summary>
    /// Returns a list of the orthogonal neighbours of the cell (x, y, z).
    /// </summary>
    static List<(int, int, int)> Neighbors(int x, int y, int z, int MX, int MY, int MZ)
    {
        List<(int, int, int)> result = new();

        if (x > 0) result.Add((x - 1, y, z));
        if (x < MX - 1) result.Add((x + 1, y, z));
        if (y > 0) result.Add((x, y - 1, z));
        if (y < MY - 1) result.Add((x, y + 1, z));
        if (z > 0) result.Add((x, y, z - 1));
        if (z < MZ - 1) result.Add((x, y, z + 1));

        return result;
    }

    /// <summary>
    /// Computes the hypothetical change in 'score' for the grid state, if the
    /// given rule would be applied at the given position. A <c>null</c> return
    /// value is equivalent to an infinite increase in 'score', indicating that
    /// this rule should not be applied at this position.
    /// </summary>
    public static int? DeltaPointwise(byte[] state, Rule rule, int x, int y, int z, Field[] fields, int[][] potentials, int MX, int MY)
    {
        int sum = 0;
        int dz = 0, dy = 0, dx = 0;
        for (int di = 0; di < rule.input.Length; di++)
        {
            byte newValue = rule.output[di];
            // check if this change to the grid would break the match
            if (newValue != 0xff && (rule.input[di] & 1 << newValue) == 0)
            {
                int i = x + dx + (y + dy) * MX + (z + dz) * MX * MY;
                int newPotential = potentials[newValue][i];
                if (newPotential == -1) return null;

                byte oldValue = state[i];
                int oldPotential = potentials[oldValue][i];
                // oldPotential cannot be -1, because newPotential is not -1,
                // and a solvable state can't result from applying a rule to an
                // unsolvable state
                sum += newPotential - oldPotential;
                
                // flip the signs of the contributions from inverted fields
                if (fields != null)
                {
                    Field oldField = fields[oldValue];
                    if (oldField != null && oldField.inversed) sum += 2 * oldPotential;
                    Field newField = fields[newValue];
                    if (newField != null && newField.inversed) sum -= 2 * newPotential;
                }
            }

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
