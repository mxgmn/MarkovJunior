// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Xml.Linq;

class ParallelNode : RuleNode
{
    byte[] newstate;

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        if (!base.Load(xelem, parentSymmetry, grid)) return false;
        newstate = new byte[grid.state.Length];
        return true;
    }

    override protected void Add(int r, int x, int y, int z, bool[] maskr)
    {
        Rule rule = rules[r];
        if (ip.random.NextDouble() > rule.p) return;
        last[r] = true;
        int MX = grid.MX, MY = grid.MY;

        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    byte newvalue = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];
                    int idi = x + dx + (y + dy) * MX + (z + dz) * MX * MY;
                    if (newvalue != 0xff && newvalue != grid.state[idi])
                    {
                        newstate[idi] = newvalue;
                        ip.changes.Add((x + dx, y + dy, z + dz));
                    }
                }
        matchCount++;
    }

    public override bool Go()
    {
        if (!base.Go()) return false;

        for (int n = ip.first[ip.counter]; n < ip.changes.Count; n++)
        {
            var (x, y, z) = ip.changes[n];
            int i = x + y * grid.MX + z * grid.MX * grid.MY;
            grid.state[i] = newstate[i];
        }

        counter++;
        return matchCount > 0;
    }
}
