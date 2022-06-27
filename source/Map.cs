// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Xml.Linq;
using System.Collections.Generic;

class MapNode : Branch
{
    public Grid newgrid;
    public Rule[] rules;
    int NX, NY, NZ, DX, DY, DZ;

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        string scalestring = xelem.Get<string>("scale", null);
        if (scalestring == null)
        {
            Interpreter.WriteLine($"scale should be specified in map node");
            return false;
        }
        string[] scales = scalestring.Split(' ');
        if (scales.Length != 3)
        {
            Interpreter.WriteLine($"scale attribute \"{scalestring}\" should have 3 components separated by space");
            return false;
        }

        static (int numerator, int denominator) readScale(string s)
        {
            if (!s.Contains('/')) return (int.Parse(s), 1);
            else
            {
                string[] nd = s.Split('/');
                return (int.Parse(nd[0]), int.Parse(nd[1]));
            }
        };

        (NX, DX) = readScale(scales[0]);
        (NY, DY) = readScale(scales[1]);
        (NZ, DZ) = readScale(scales[2]);

        newgrid = Grid.Load(xelem, grid.MX * NX / DX, grid.MY * NY / DY, grid.MZ * NZ / DZ);
        if (newgrid == null) return false;

        if (!base.Load(xelem, parentSymmetry, newgrid)) return false;
        bool[] symmetry = SymmetryHelper.GetSymmetry(grid.MZ == 1, xelem.Get<string>("symmetry", null), parentSymmetry);

        List<Rule> ruleList = new();
        foreach (XElement xrule in xelem.Elements("rule"))
        {
            Rule rule = Rule.Load(xrule, grid, newgrid);
            rule.original = true;
            if (rule == null) return false;
            foreach (Rule r in rule.Symmetries(symmetry, grid.MZ == 1)) ruleList.Add(r);
        }
        rules = ruleList.ToArray();
        return true;
    }

    static bool Matches(Rule rule, int x, int y, int z, byte[] state, int MX, int MY, int MZ)
    {
        for (int dz = 0; dz < rule.IMZ; dz++) for (int dy = 0; dy < rule.IMY; dy++) for (int dx = 0; dx < rule.IMX; dx++)
                {
                    int sx = x + dx;
                    int sy = y + dy;
                    int sz = z + dz;

                    if (sx >= MX) sx -= MX;
                    if (sy >= MY) sy -= MY;
                    if (sz >= MZ) sz -= MZ;

                    int inputWave = rule.input[dx + dy * rule.IMX + dz * rule.IMX * rule.IMY];
                    if ((inputWave & (1 << state[sx + sy * MX + sz * MX * MY])) == 0) return false;
                }

        return true;
    }

    static void Apply(Rule rule, int x, int y, int z, byte[] state, int MX, int MY, int MZ)
    {
        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    int sx = x + dx;
                    int sy = y + dy;
                    int sz = z + dz;

                    if (sx >= MX) sx -= MX;
                    if (sy >= MY) sy -= MY;
                    if (sz >= MZ) sz -= MZ;

                    byte output = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];
                    if (output != 0xff) state[sx + sy * MX + sz * MX * MY] = output;
                }
    }

    override public bool Go()
    {
        if (n >= 0) return base.Go();

        newgrid.Clear();
        foreach (Rule rule in rules)
            for (int z = 0; z < grid.MZ; z++) for (int y = 0; y < grid.MY; y++) for (int x = 0; x < grid.MX; x++)
                        if (Matches(rule, x, y, z, grid.state, grid.MX, grid.MY, grid.MZ))
                            Apply(rule, x * NX / DX, y * NY / DY, z * NZ / DZ, newgrid.state, newgrid.MX, newgrid.MY, newgrid.MZ);

        ip.grid = newgrid;
        n++;
        return true;
    }

    override public void Reset()
    {
        base.Reset();
        n = -1;
    }
}
