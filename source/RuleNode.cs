// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

/// <summary>
/// Base class for AST nodes which have <see cref="Rule">rewrite rules</see>
/// as children.
/// </summary>
abstract class RuleNode : Node
{
    /// <summary>The rewrite rules belonging to this node.</summary>
    public Rule[] rules;
    
    /// <summary>
    /// The number of times this node has been executed since it was last reset.
    /// </summary>
    public int counter;
    
    /// <summary>
    /// The maximum number of times this node may be executed, between resets.
    /// If <c>steps</c> is zero, then there is no maximum.
    /// </summary>
    public int steps;

    /// <summary>
    /// <para>
    /// A list of (r, x, y, z) tuples where rule <c>r</c> matches in the grid
    /// at position (x, y, z). The list contains all current matches but may
    /// also contain some stale matches.
    /// </para>
    /// <para>
    /// To avoid excessive allocation and deallocation, the list is never
    /// shortened; the <see cref="RuleNode.matchCount">matchCount</see> field
    /// is the list's 'true' length. All current matches occur before that
    /// index, and unless this is a <see cref="OneNode">OneNode</see>, no stale
    /// matches do.
    /// </para>
    /// </summary>
    protected List<(int, int, int, int)> matches;
    
    /// <summary>
    /// <para>
    /// The 'true' length of the <see cref="RuleNode.matches">matches</see> list.
    /// The actual list length may be greater than <c>matchCount</c>, but all
    /// current matches occur before this index in that list.
    /// </para>
    /// <para>
    /// If this is a <see cref="OneNode">OneNode</see>, then the list may also
    /// contain some stale matches before this index, so this field is only an
    /// upper bound for the number of current matches.
    /// </para>
    /// </summary>
    protected int matchCount;
    
    /// <summary>
    /// The last turn at which the <see cref="RuleNode.matches">matches</see>
    /// list was up-to-date. Used to keep track of which changes in the grid
    /// should be re-scanned for matches. <c>lastMatchedTurn</c> is -1 if this
    /// node has been reset since the last full-grid scan.
    /// </summary>
    protected int lastMatchedTurn;
    
    /// <summary>
    /// Maps each rule to a flat array of flags indicating membership in the
    /// <see cref="RuleNode.matches">matches</see> list. <c>matchMask[r][x + y * MX + z * MX * MY]</c>
    /// is true if and only if (r, x, y, z) is in the list before index
    /// <see cref="RuleNode.matchCount">matchCount</see>.
    /// </summary>
    protected bool[][] matchMask;

    /// <summary>
    /// If this node has any <see cref="Field">fields</see> or <see cref="Observation">observations</see>,
    /// then <c>potentials</c> maps each color to an array of potentials which
    /// are computed by the field or observation associated with that color.
    /// Roughly, <c>potentials[c][x + y * MX + z * MX * MY]</c> measures how
    /// 'good' or 'bad' it is for color <c>c</c> to be at position (x, y, z) in
    /// the grid.
    /// </summary>
    protected int[][] potentials;
    
    /// <summary>
    /// Maps each color to the <see cref="Field">field</see> associated with
    /// that color for this node, if any. It is <c>null</c> if this node has no
    /// fields.
    /// </summary>
    public Field[] fields;
    
    /// <summary>
    /// Maps each color to the <see cref="Observation">observation</see>
    /// associated with that color for this node, if any. It is <c>null</c> if
    /// this node has no observations.
    /// </summary>
    protected Observation[] observations;
    
    /// <summary>
    /// If this node has any <see cref="Field">fields</see> or <see cref="Observation">observations</see>,
    /// then the temperature controls how much this rule tries to minimise the
    /// 'score' of the grid state according to the <see cref="RuleNode.potentials">potentials</see>.
    /// A temperature of zero means that when the node is executed, it will
    /// always apply rules in a way which minimises the 'score'; higher
    /// temperatures increase the probability of choosing "less optimal"
    /// matches when rules are applied.
    /// </summary>
    protected double temperature;

    /// <summary>
    /// <para>
    /// If true, this node will try to achieve the goal determined by its
    /// <see cref="Observation">observations</see> using A* search; otherwise,
    /// the observations will be used to compute backwards <see cref="RuleNode.potentials">potentials</see>,
    /// in order to bias the application of rewrite rules towards the goal.
    /// </para>
    /// <para>
    /// This flag is irrelevant if there are no observations.
    /// </para>
    /// </summary>
    protected bool search;
    
    /// <summary>
    /// Indicates whether the trajectory or backwards potentials determined by
    /// this node's <see cref="Observation">observations</see> have already
    /// been computed. This flag is irrelevant if there are no observations.
    /// </summary>
    protected bool futureComputed;
    
    /// <summary>
    /// If this node has any <see cref="Observation">observations</see>, then
    /// this array represents the future goal determined by those observations.
    /// Each element is a bitmask of colors, and the goal is reached when every
    /// cell in the grid matches the corresponding bitmask.
    /// </summary>
    protected int[] future;
    
    /// <summary>
    /// If not null, this array holds a sequence of states found by <see cref="Search">search</see>.
    /// When this node is executed, a precomputed state in the trajectory will
    /// be copied to the grid, instead of applying rewrite rules as usual.
    /// </summary>
    protected byte[][] trajectory;

    /// <summary>
    /// The maximum number of board states to be considered in a <see cref="Search">search</see>.
    /// If negative, then there is no maximum.
    /// </summary>
    int limit;
    
    /// <summary>
    /// Used to interpolate between breadth-first and depth-first search. If
    /// negative, the search is purely depth-first.
    /// </summary>
    double depthCoefficient;

    /// <summary>
    /// Maps each rule to a boolean flag, indicating whether that rule was
    /// applied on the last execution step.
    /// </summary>
    public bool[] last;

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        string symmetryString = xelem.Get<string>("symmetry", null);
        bool[] symmetry = SymmetryHelper.GetSymmetry(grid.MZ == 1, symmetryString, parentSymmetry);
        if (symmetry == null)
        {
            Interpreter.WriteLine($"unknown symmetry {symmetryString} at line {xelem.LineNumber()}");
            return false;
        }

        List<Rule> ruleList = new();
        XElement[] xrules = xelem.Elements("rule").ToArray();
        // if this element has no <rule> elements, treat this element itself as a rule
        XElement[] ruleElements = xrules.Length > 0 ? xrules : new XElement[] { xelem };
        foreach (XElement xrule in ruleElements)
        {
            Rule rule = Rule.Load(xrule, grid, grid);
            if (rule == null) return false;
            rule.original = true;

            string ruleSymmetryString = xrule.Get<string>("symmetry", null);
            bool[] ruleSymmetry = SymmetryHelper.GetSymmetry(grid.MZ == 1, ruleSymmetryString, symmetry);
            if (ruleSymmetry == null)
            {
                Interpreter.WriteLine($"unknown symmetry {ruleSymmetryString} at line {xrule.LineNumber()}");
                return false;
            }
            foreach (Rule r in rule.Symmetries(ruleSymmetry, grid.MZ == 1)) ruleList.Add(r);
        }
        rules = ruleList.ToArray();
        last = new bool[rules.Length];

        steps = xelem.Get("steps", 0);

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

        var xobservations = xelem.Elements("observe");
        if (xobservations.Any())
        {
            observations = new Observation[grid.C];
            foreach (var x in xobservations)
            {
                byte value = grid.values[x.Get<char>("value")];
                observations[value] = new Observation(x.Get("from", grid.characters[value]), x.Get<string>("to"), grid);
            }

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

    override public void Reset()
    {
        lastMatchedTurn = -1;
        counter = 0;
        futureComputed = false;

        for (int r = 0; r < last.Length; r++) last[r] = false;
    }

    /// <summary>
    /// This method is called whenever one of this node's rules is matched in
    /// the grid.
    /// </summary>
    /// <param name="r">The index of the rule in the <see cref="RuleNode.rules">rules</see> array.</param>
    /// <param name="x">The x coordinate of the match.</param>
    /// <param name="y">The y coordinate of the match.</param>
    /// <param name="z">The z coordinate of the match.</param>
    /// <param name="maskr">The <see cref="RuleNode.matchMask">matchMask</see> array for the given rule.</param>
    protected virtual void Add(int r, int x, int y, int z, bool[] maskr)
    {
        maskr[x + y * grid.MX + z * grid.MX * grid.MY] = true;

        var match = (r, x, y, z);
        if (matchCount < matches.Count) matches[matchCount] = match;
        else matches.Add(match);
        matchCount++;
    }

    public override bool Go()
    {
        for (int r = 0; r < last.Length; r++) last[r] = false;

        // есть вариант вернуть false на том же ходу, на котором мы достигли предела
        // there is an option to return false on the same turn on which we reached the limit
        if (steps > 0 && counter >= steps) return false;

        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        if (observations != null && !futureComputed)
        {
            // compute the future state based on the current grid state and observations
            // if some color is observed but not present in the grid, then this node is inapplicable
            if (!Observation.ComputeFutureSetPresent(future, grid.state, observations)) return false;
            else
            {
                futureComputed = true;
                if (search)
                {
                    // compute a trajectory by A* search; if successful, the trajectory states will be successively copied to the grid
                    trajectory = null;
                    int TRIES = limit < 0 ? 1 : 20;
                    for (int k = 0; k < TRIES && trajectory == null; k++) trajectory = Search.Run(grid.state, future, rules, grid.MX, grid.MY, grid.MZ, grid.C, this is AllNode, limit, depthCoefficient, ip.random.Next());
                    if (trajectory == null) Console.WriteLine("SEARCH RETURNED NULL");
                }
                else
                {
                    // otherwise compute potentials to bias application of rewrite rules towards the future goal
                    Observation.ComputeBackwardPotentials(potentials, future, MX, MY, MZ, rules);
                }
            }
        }
        
        // update the list of matches, and match masks
        if (lastMatchedTurn >= 0)
        {
            // matches are computed up to lastMatchedTurn, only need to scan for changes
            for (int n = ip.first[lastMatchedTurn]; n < ip.changes.Count; n++)
            {
                var (x, y, z) = ip.changes[n];
                byte value = grid.state[x + y * MX + z * MX * MY];
                for (int r = 0; r < rules.Length; r++)
                {
                    Rule rule = rules[r];
                    bool[] maskr = matchMask[r];
                    (int x, int y, int z)[] shifts = rule.ishifts[value];
                    for (int l = 0; l < shifts.Length; l++)
                    {
                        var (shiftx, shifty, shiftz) = shifts[l];
                        int sx = x - shiftx;
                        int sy = y - shifty;
                        int sz = z - shiftz;

                        if (sx < 0 || sy < 0 || sz < 0 || sx + rule.IMX > MX || sy + rule.IMY > MY || sz + rule.IMZ > MZ) continue;
                        int si = sx + sy * MX + sz * MX * MY;

                        // maskr is used to avoid adding the match if it has already been added
                        if (!maskr[si] && grid.Matches(rule, sx, sy, sz)) Add(r, sx, sy, sz, maskr);
                    }
                }
            }
        }
        else
        {
            // matches have not been computed since this node was last reset; scan whole grid
            matchCount = 0;
            for (int r = 0; r < rules.Length; r++)
            {
                Rule rule = rules[r];
                bool[] maskr = matchMask?[r];
                // look at lattice points spaced (IMX, IMY, IMZ) apart
                for (int z = rule.IMZ - 1; z < MZ; z += rule.IMZ)
                    for (int y = rule.IMY - 1; y < MY; y += rule.IMY)
                        for (int x = rule.IMX - 1; x < MX; x += rule.IMX)
                        {
                            // use ishifts to find matches near lattice points
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

        // recompute any distance fields as required
        if (fields != null)
        {
            bool anysuccess = false, anycomputation = false;
            for (int c = 0; c < fields.Length; c++)
            {
                Field field = fields[c];
                if (field != null && (counter == 0 || field.recompute))
                {
                    bool success = field.Compute(potentials[c], grid);
                    if (!success && field.essential) return false;
                    anysuccess |= success;
                    anycomputation = true;
                }
            }
            if (anycomputation && !anysuccess) return false;
        }

        return true;
    }
}
