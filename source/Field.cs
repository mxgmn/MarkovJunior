// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class Field
{
    public bool recompute, inversed, essential;
    public int zero, substrate;

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

    public bool Compute(int[] potential, Grid grid)
    {
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        var front = new Queue<(int, int, int, int)>();

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

        if (!front.Any()) return false;
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

    public static int? DeltaPointwise(byte[] state, Rule rule, int x, int y, int z, Field[] fields, int[][] potentials, int MX, int MY)
    {
        int sum = 0;
        int dz = 0, dy = 0, dx = 0;
        for (int di = 0; di < rule.input.Length; di++)
        {
            byte newValue = rule.output[di];
            if (newValue != 0xff && (rule.input[di] & 1 << newValue) == 0)
            {
                int i = x + dx + (y + dy) * MX + (z + dz) * MX * MY;
                int newPotential = potentials[newValue][i];
                if (newPotential == -1) return null;

                byte oldValue = state[i];
                int oldPotential = potentials[oldValue][i];
                sum += newPotential - oldPotential;

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
