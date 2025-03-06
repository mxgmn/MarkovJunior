// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Xml.Linq;
using System.Collections.Generic;

class MapNode : Branch
{
    public Grid newgrid;      // Output grid that this node creates
    public Rule[] rules;      // Set of transformation rules to apply
    int NX, NY, NZ;           // Numerators for scaling factors in each dimension
    int DX, DY, DZ;           // Denominators for scaling factors in each dimension

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Parse the scale attribute which defines how the input grid maps to the output grid
        string scalestring = xelem.Get<string>("scale", null);
        if (scalestring == null)
        {
            Interpreter.WriteLine($"scale should be specified in map node");
            return false;  // Scale is required
        }
        string[] scales = scalestring.Split(' ');
        if (scales.Length != 3)
        {
            Interpreter.WriteLine($"scale attribute \"{scalestring}\" should have 3 components separated by space");
            return false;  // Need scale for all three dimensions
        }

        // Helper function to parse scaling factors like "2" or "3/2"
        static (int numerator, int denominator) readScale(string s)
        {
            if (!s.Contains('/')) return (int.Parse(s), 1);  // Simple integer scale
            else
            {
                string[] nd = s.Split('/');
                return (int.Parse(nd[0]), int.Parse(nd[1]));  // Fractional scale
            }
        };

        // Parse scaling factors for each dimension
        (NX, DX) = readScale(scales[0]);  // X-axis (width) scaling
        (NY, DY) = readScale(scales[1]);  // Y-axis (height) scaling
        (NZ, DZ) = readScale(scales[2]);  // Z-axis (depth) scaling

        // Create a new grid with scaled dimensions
        newgrid = Grid.Load(xelem, grid.MX * NX / DX, grid.MY * NY / DY, grid.MZ * NZ / DZ);
        if (newgrid == null) return false;  // Exit if grid creation failed

        // Call the parent class's Load method with the new grid
        if (!base.Load(xelem, parentSymmetry, newgrid)) return false;

        // Parse symmetry settings for rule generation
        bool[] symmetry = SymmetryHelper.GetSymmetry(grid.MZ == 1, xelem.Get<string>("symmetry", null), parentSymmetry);

        // Load and process all rules
        List<Rule> ruleList = new();
        foreach (XElement xrule in xelem.Elements("rule"))
        {
            // Create the base rule from XML
            Rule rule = Rule.Load(xrule, grid, newgrid);
            rule.original = true;  // Mark as an original (not symmetry-generated) rule
            if (rule == null) return false;  // Exit if rule loading failed

            // Add all symmetry variants of the rule
            foreach (Rule r in rule.Symmetries(symmetry, grid.MZ == 1)) ruleList.Add(r);
        }
        rules = ruleList.ToArray();  // Convert to array for better performance
        return true;
    }

    // Check if a rule's input pattern matches at a specific position in the grid
    static bool Matches(Rule rule, int x, int y, int z, byte[] state, int MX, int MY, int MZ)
    {
        // Check each cell in the input pattern
        for (int dz = 0; dz < rule.IMZ; dz++) for (int dy = 0; dy < rule.IMY; dy++) for (int dx = 0; dx < rule.IMX; dx++)
                {
                    // Calculate position with wrapping
                    int sx = x + dx;
                    int sy = y + dy;
                    int sz = z + dz;

                    if (sx >= MX) sx -= MX;  // Wrap around on X axis
                    if (sy >= MY) sy -= MY;  // Wrap around on Y axis
                    if (sz >= MZ) sz -= MZ;  // Wrap around on Z axis

                    // Get the allowed values for this position in the rule
                    int inputWave = rule.input[dx + dy * rule.IMX + dz * rule.IMX * rule.IMY];

                    // Check if the current state is allowed by the rule
                    // If not, the rule doesn't match here
                    if ((inputWave & (1 << state[sx + sy * MX + sz * MX * MY])) == 0) return false;
                }

        return true;  // All cells matched the pattern
    }

    // Apply a rule's output pattern at a specific position in the grid
    static void Apply(Rule rule, int x, int y, int z, byte[] state, int MX, int MY, int MZ)
    {
        // For each cell in the output pattern
        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    // Calculate position with wrapping
                    int sx = x + dx;
                    int sy = y + dy;
                    int sz = z + dz;

                    if (sx >= MX) sx -= MX;  // Wrap around on X axis
                    if (sy >= MY) sy -= MY;  // Wrap around on Y axis
                    if (sz >= MZ) sz -= MZ;  // Wrap around on Z axis

                    // Get the output value for this position
                    byte output = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];

                    // Apply if not the special "do not change" value (0xff/255)
                    if (output != 0xff) state[sx + sy * MX + sz * MX * MY] = output;
                }
    }

    // Main execution method
    override public bool Go()
    {
        if (n >= 0) return base.Go();  // If already executed, go to child nodes

        // Clear the output grid
        newgrid.Clear();

        // Process all rules at all positions
        foreach (Rule rule in rules)
            for (int z = 0; z < grid.MZ; z++) for (int y = 0; y < grid.MY; y++) for (int x = 0; x < grid.MX; x++)
                        // Check if rule matches at this position
                        if (Matches(rule, x, y, z, grid.state, grid.MX, grid.MY, grid.MZ))
                            // If it matches, apply the rule's output at the scaled position
                            Apply(rule, x * NX / DX, y * NY / DY, z * NZ / DZ, newgrid.state, newgrid.MX, newgrid.MY, newgrid.MZ);

        // Set the new grid as the current grid in the interpreter
        ip.grid = newgrid;
        n++;  // Mark as executed
        return true;
    }

    // Reset execution state
    override public void Reset()
    {
        base.Reset();
        n = -1;  // Mark as not executed
    }
}

/*
=== SUMMARY ===

The MapNode class is like a transformation engine that applies pattern-matching rules to convert one grid into another, potentially at a different scale.

Think of it as a photo filter that looks for specific patterns and replaces them with new ones, except it can also resize the image at the same time. Here's what it does:

1. It takes an input grid and creates a new output grid (potentially with different dimensions)
   - The scaling can be different in each dimension (X, Y, Z)
   - It can even do fractional scaling like "3/2" (3 output cells for every 2 input cells)

2. It loads a set of transformation rules, each with:
   - An input pattern - what to look for
   - An output pattern - what to replace it with

3. When executed, it:
   - Scans the entire input grid for patterns that match any rule
   - Whenever it finds a match, it applies the corresponding output pattern to the new grid
   - The output positions are scaled according to the defined scaling factors

4. It handles symmetry automatically:
   - Rules can generate variations (rotations, reflections) based on symmetry settings
   - This saves having to manually specify every possible orientation of a pattern

This node is useful for procedural generation tasks like:
- Converting a low-resolution sketch into detailed terrain
- Translating abstract patterns into concrete visual elements
- Creating multi-scale procedural content where large-scale structure influences small-scale details
*/