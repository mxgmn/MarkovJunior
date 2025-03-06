// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

abstract class RuleNode : Node
{
    public Rule[] rules;              // Array of rules that this node manages
    public int counter, steps;        // Counter tracks how many times this node has run, steps is the maximum limit

    protected List<(int, int, int, int)> matches;   // List of matches as (rule_index, x, y, z)
    protected int matchCount, lastMatchedTurn;      // Number of current matches and tracking for incremental matching
    protected bool[][] matchMask;                   // Tracks where rules have already been matched

    protected int[][] potentials;     // Potential values for fields or backward potential computation
    public Field[] fields;            // Distance fields used to guide rule application
    protected Observation[] observations;  // Configuration for observed states (constraints)
    protected double temperature;     // Controls randomness in rule selection

    protected bool search, futureComputed;  // Flags for search-based execution mode
    protected int[] future;                 // Target/future state for observations
    protected byte[][] trajectory;          // Sequence of states for reaching target

    int limit;                        // Limit for search depth
    double depthCoefficient;          // Controls how search prioritizes rule applications

    public bool[] last;               // Tracks which rules were applied in the last step

    // Loads node configuration from XML
    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Parse symmetry settings (which rotations/reflections to use for rules)
        string symmetryString = xelem.Get<string>("symmetry", null);
        bool[] symmetry = SymmetryHelper.GetSymmetry(grid.MZ == 1, symmetryString, parentSymmetry);
        if (symmetry == null)
        {
            Interpreter.WriteLine($"unknown symmetry {symmetryString} at line {xelem.LineNumber()}");
            return false;
        }

        // Load rules from XML and generate their symmetrical variants
        List<Rule> ruleList = new();
        XElement[] xrules = xelem.Elements("rule").ToArray();
        XElement[] ruleElements = xrules.Length > 0 ? xrules : new XElement[] { xelem };
        foreach (XElement xrule in ruleElements)
        {
            Rule rule = Rule.Load(xrule, grid, grid);
            if (rule == null) return false;
            rule.original = true;

            // Each rule can override the node's symmetry settings
            string ruleSymmetryString = xrule.Get<string>("symmetry", null);
            bool[] ruleSymmetry = SymmetryHelper.GetSymmetry(grid.MZ == 1, ruleSymmetryString, symmetry);
            if (ruleSymmetry == null)
            {
                Interpreter.WriteLine($"unknown symmetry {ruleSymmetryString} at line {xrule.LineNumber()}");
                return false;
            }

            // Add all symmetrical variants of this rule to the list
            foreach (Rule r in rule.Symmetries(ruleSymmetry, grid.MZ == 1)) ruleList.Add(r);
        }
        rules = ruleList.ToArray();
        last = new bool[rules.Length];

        // Maximum number of iterations (0 means unlimited)
        steps = xelem.Get("steps", 0);

        // Load field configurations for guidance
        temperature = xelem.Get("temperature", 0.0);
        var xfields = xelem.Elements("field");
        if (xfields.Any())
        {
            fields = new Field[grid.C];
            foreach (XElement xfield in xfields)
            {
                char c = xfield.Get<char>("for");
                if (grid.values.TryGetValue(c, out byte value)) fields[value] = new Field(xfield, grid);
                else
                {
                    Interpreter.WriteLine($"unknown field value {c} at line {xfield.LineNumber()}");
                    return false;
                }
            }
            potentials = AH.Array2D(grid.C, grid.state.Length, 0);
        }

        // Load observation configurations (constraints on final state)
        var xobservations = xelem.Elements("observe");
        if (xobservations.Any())
        {
            observations = new Observation[grid.C];
            foreach (var x in xobservations)
            {
                byte value = grid.values[x.Get<char>("value")];
                observations[value] = new Observation(x.Get("from", grid.characters[value]), x.Get<string>("to"), grid);
            }

            // Configure search mode for satisfying observations
            search = xelem.Get("search", false);
            if (search)
            {
                limit = xelem.Get("limit", -1);
                depthCoefficient = xelem.Get("depthCoefficient", 0.5);
            }
            else potentials = AH.Array2D(grid.C, grid.state.Length, 0);
            future = new int[grid.state.Length];
        }

        return true;
    }

    // Resets the node to initial state
    override public void Reset()
    {
        lastMatchedTurn = -1;
        counter = 0;
        futureComputed = false;

        for (int r = 0; r < last.Length; r++) last[r] = false;
    }

    // Adds a rule match to the list of potential rule applications
    protected virtual void Add(int r, int x, int y, int z, bool[] maskr)
    {
        // Mark this position as matched for this rule to avoid duplicates
        maskr[x + y * grid.MX + z * grid.MX * grid.MY] = true;

        // Add the match to the list
        var match = (r, x, y, z);
        if (matchCount < matches.Count) matches[matchCount] = match;
        else matches.Add(match);
        matchCount++;
    }

    // Main execution method - finds and applies rules
    public override bool Go()
    {
        // Reset tracking of which rules were applied
        for (int r = 0; r < last.Length; r++) last[r] = false;

        // Check if we've reached the maximum number of iterations
        if (steps > 0 && counter >= steps) return false;

        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;

        // If using observations, compute the future/target state first time
        if (observations != null && !futureComputed)
        {
            // Calculate what the future state should look like based on observations
            if (!Observation.ComputeFutureSetPresent(future, grid.state, observations)) return false;
            else
            {
                futureComputed = true;
                if (search)
                {
                    // Use search algorithm to find a sequence of rule applications to reach the target
                    trajectory = null;
                    int TRIES = limit < 0 ? 1 : 20;
                    for (int k = 0; k < TRIES && trajectory == null; k++) trajectory = Search.Run(grid.state, future, rules, grid.MX, grid.MY, grid.MZ, grid.C, this is AllNode, limit, depthCoefficient, ip.random.Next());
                    if (trajectory == null) Console.WriteLine("SEARCH RETURNED NULL");
                }
                else Observation.ComputeBackwardPotentials(potentials, future, MX, MY, MZ, rules);
            }
        }

        // Incremental matching: only check positions affected by the last turn
        if (lastMatchedTurn >= 0)
        {
            for (int n = ip.first[lastMatchedTurn]; n < ip.changes.Count; n++)
            {
                var (x, y, z) = ip.changes[n];
                byte value = grid.state[x + y * MX + z * MX * MY];

                // Try to match each rule at positions affected by this value
                for (int r = 0; r < rules.Length; r++)
                {
                    Rule rule = rules[r];
                    bool[] maskr = matchMask[r];
                    (int x, int y, int z)[] shifts = rule.ishifts[value];

                    // Check all possible positions where this value is used in the rule
                    for (int l = 0; l < shifts.Length; l++)
                    {
                        var (shiftx, shifty, shiftz) = shifts[l];
                        int sx = x - shiftx;
                        int sy = y - shifty;
                        int sz = z - shiftz;

                        // Skip if the rule pattern goes outside grid boundaries
                        if (sx < 0 || sy < 0 || sz < 0 || sx + rule.IMX > MX || sy + rule.IMY > MY || sz + rule.IMZ > MZ) continue;
                        int si = sx + sy * MX + sz * MX * MY;

                        // Add match if not already matched and pattern matches
                        if (!maskr[si] && grid.Matches(rule, sx, sy, sz)) Add(r, sx, sy, sz, maskr);
                    }
                }
            }
        }
        // Full matching: check rules across the entire grid
        else
        {
            matchCount = 0;
            for (int r = 0; r < rules.Length; r++)
            {
                Rule rule = rules[r];
                bool[] maskr = matchMask?[r];

                // Skip through grid in rule-sized chunks for efficiency
                for (int z = rule.IMZ - 1; z < MZ; z += rule.IMZ)
                    for (int y = rule.IMY - 1; y < MY; y += rule.IMY)
                        for (int x = rule.IMX - 1; x < MX; x += rule.IMX)
                        {
                            // Try to match the rule starting from positions where the value matches
                            var shifts = rule.ishifts[grid.state[x + y * MX + z * MX * MY]];
                            for (int l = 0; l < shifts.Length; l++)
                            {
                                var (shiftx, shifty, shiftz) = shifts[l];
                                int sx = x - shiftx;
                                int sy = y - shifty;
                                int sz = z - shiftz;
                                if (sx < 0 || sy < 0 || sz < 0 || sx + rule.IMX > MX || sy + rule.IMY > MY || sz + rule.IMZ > MZ) continue;

                                if (grid.Matches(rule, sx, sy, sz)) Add(r, sx, sy, sz, maskr);
                            }
                        }
            }
        }

        // Compute/update fields if needed
        if (fields != null)
        {
            bool anysuccess = false, anycomputation = false;
            for (int c = 0; c < fields.Length; c++)
            {
                Field field = fields[c];
                // Compute field if it's the first iteration or field requires recomputation
                if (field != null && (counter == 0 || field.recompute))
                {
                    bool success = field.Compute(potentials[c], grid);
                    // If an essential field fails, abort execution
                    if (!success && field.essential) return false;
                    anysuccess |= success;
                    anycomputation = true;
                }
            }
            // If fields were computed but none succeeded, abort execution
            if (anycomputation && !anysuccess) return false;
        }

        return true;
    }
}

/*
========== SUMMARY ==========

This code implements a rule-based pattern matching and transformation system for procedural generation or constraint solving. Think of it like a smart "find and replace" system that works on 2D or 3D grids.

Imagine you're editing a document with a sophisticated "find and replace" tool that can:
1. Look for complex patterns (not just simple text)
2. Apply transformations based on certain conditions
3. Use a "heat map" to guide which replacements to make first
4. Try to reach a specific end result

Here's what the RuleNode class does in simple terms:

1. Rule Management: It loads and organizes a set of transformation rules from XML configuration.
   - Rules can have symmetrical variants (rotations and reflections)
   - Rules can have different priorities or probabilities

2. Pattern Matching: It efficiently finds all places in the grid where rules could be applied.
   - Uses an optimization where it only checks areas affected by recent changes
   - Uses precomputed "shifts" to quickly find potential match locations

3. Field Guidance: It can use "distance fields" (like heat maps) to guide the rule application.
   - These fields measure distance from certain elements
   - They help determine which rule applications are most desirable

4. Goal-Directed Behavior: It can work backward from a desired end state.
   - "Observations" define constraints on what the final result should look like
   - Can use search algorithms to find a sequence of rule applications to reach the goal

The abstract class needs to be extended by concrete implementations (likely OneNode and AllNode mentioned in the code) that determine exactly how to select and apply the matched rules.

This system could be used for procedural level generation in games, texture synthesis, solving constraint satisfaction problems, or modeling complex systems with local rules (like cellular automata).
*/