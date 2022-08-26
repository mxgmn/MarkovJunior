// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Xml.Linq;
using System.Collections.Generic;

/// <summary>
/// <para>
/// A 'map' node replaces the currently-active grid with a new one, which may
/// have a different size. When a 'map' node is executed, patterns in the input
/// are rewritten to the output grid using the node's rewrite rules, then the
/// program execution continues using the output grid.
/// </para>
/// <para>
/// When a rewrite rule is matched in the input grid, it is rewritten in the
/// output grid at a position determined by this node's scale factors, which
/// are specified as fractions. In case the scaled coordinates in the output
/// grid are not integers, they are rounded down. Unlike <see cref="RuleNode">other
/// nodes which use rewrite rules</see>, the input and output patterns wrap
/// around the grid boundaries.
/// </para>
/// </summary>
class MapNode : Branch
{
    /// <summary>The output grid, which may have a different size to the input grid.</summary>
    public Grid newgrid;
    
    /// <summary>The rewrite rules belonging to this node.</summary>
    public Rule[] rules;
    
    /// <summary>The numerator of the x axis scale factor.</summary>
    int NX;
    
    /// <summary>The numerator of the y axis scale factor.</summary>
    int NY;
    
    /// <summary>The numerator of the z axis scale factor.</summary>
    int NZ;
    
    /// <summary>The denominator of the x axis scale factor.</summary>
    int DX;
    
    /// <summary>The denominator of the y axis scale factor.</summary>
    int DY;
    
    /// <summary>The denominator of the z axis scale factor.</summary>
    int DZ;

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
        
        // parses a fraction from a string
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

        // base.Load expects `parentSymmetry`, not `symmetry`
        if (!base.Load(xelem, parentSymmetry, newgrid)) return false;
        bool[] symmetry = SymmetryHelper.GetSymmetry(grid.MZ == 1, xelem.Get<string>("symmetry", null), parentSymmetry);

        List<Rule> ruleList = new();
        foreach (XElement xrule in xelem.Elements("rule"))
        {
            Rule rule = Rule.Load(xrule, grid, newgrid);
            rule.original = true;
            if (rule == null) return false;
            rule.original = true;
            foreach (Rule r in rule.Symmetries(symmetry, grid.MZ == 1)) ruleList.Add(r);
        }
        rules = ruleList.ToArray();
        return true;
    }

    /// <summary>
    /// Determines whether the rule's input pattern matches in this grid at the
    /// given position. If the input pattern is not in-bounds, it wraps around
    /// the grid edges.
    /// </summary>
    /// <seealso cref="Grid.Matches(Rule, int, int, int)"/>
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

    /// <summary>
    /// Applies a rewrite rule at the given position in the grid. If the output
    /// pattern is not in-bounds, it wraps around the grid edges.
    /// </summary>
    /// <seealso cref="OneNode.Apply(Rule, int, int, int)"/>
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
        // if the input grid has already been mapped to the output grid, then behave like a sequence node
        if (n >= 0) return base.Go();
        
        // mapping happens when n = -1
        newgrid.Clear();
        foreach (Rule rule in rules)
            for (int z = 0; z < grid.MZ; z++) for (int y = 0; y < grid.MY; y++) for (int x = 0; x < grid.MX; x++)
                        if (Matches(rule, x, y, z, grid.state, grid.MX, grid.MY, grid.MZ))
                            Apply(rule, x * NX / DX, y * NY / DY, z * NZ / DZ, newgrid.state, newgrid.MX, newgrid.MY, newgrid.MZ);
        
        // set the output grid to be the currently active one
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
