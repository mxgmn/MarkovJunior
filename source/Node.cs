// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;

/// <summary>
/// Base class for nodes in the abstract syntax tree (AST) of a MarkovJunior
/// program. Nodes are stateful while the interpreter executes the program.
/// </summary>
abstract class Node
{
    /// <summary>
    /// Loads the parameters for this AST node from an XML element. The loading
    /// may fail if the XML data is invalid, or a referenced resource file
    /// cannot be loaded.
    /// </summary>
    /// <param name="xelem">The XML element.</param>
    /// <param name="symmetry">The parent node's symmetry group, which this node will inherit if it is not overridden.</param>
    /// <param name="grid"><inheritdoc cref="Node.grid" path="/summary"/></param>
    /// <returns><c>true</c> if the loading was successful, otherwise <c>false</c>.</returns>
    abstract protected bool Load(XElement xelem, bool[] symmetry, Grid grid);
    
    /// <summary>
    /// Resets this node to its initial state.
    /// </summary>
    abstract public void Reset();
    
    /// <summary>
    /// Executes one step and returns <c>true</c>, or returns <c>false</c> if
    /// this node is not currently applicable.
    /// </summary>
    abstract public bool Go();

    protected Interpreter ip;
    
    /// <summary>The input grid for this node.</summary>
    public Grid grid;

    /// <summary>
    /// Creates an AST node from an XML element. The loading may fail if the
    /// XML data is invalid, or a referenced resource file cannot be loaded.
    /// </summary>
    /// <param name="xelem">The XML element.</param>
    /// <param name="symmetry">The parent node's symmetry group, which the new node will inherit if it is not overridden.</param>
    /// <param name="ip">The interpreter which will interpret the AST.</param>
    /// <param name="grid">The input grid for the new node.</param>
    /// <returns>The new AST node, or <c>null</c> if the loading fails.</returns>
    public static Node Factory(XElement xelem, bool[] symmetry, Interpreter ip, Grid grid)
    {
        if (!nodenames.Contains(xelem.Name.LocalName))
        {
            Interpreter.WriteLine($"unknown node type \"{xelem.Name}\" at line {xelem.LineNumber()}");
            return null;
        }

        Node result = xelem.Name.LocalName switch
        {
            "one" => new OneNode(),
            "all" => new AllNode(),
            "prl" => new ParallelNode(),
            "markov" => new MarkovNode(),
            "sequence" => new SequenceNode(),
            "path" => new PathNode(),
            "map" => new MapNode(),
            "convolution" => new ConvolutionNode(),
            "convchain" => new ConvChainNode(),
            "wfc" when xelem.Get<string>("sample", null) != null => new OverlapNode(),
            "wfc" when xelem.Get<string>("tileset", null) != null => new TileNode(),
            _ => null
        };

        result.ip = ip;
        result.grid = grid;
        bool success = result.Load(xelem, symmetry, grid);

        if (!success) return null;
        return result;
    }

    protected static string[] nodenames = new string[] { "one", "all", "prl", "markov", "sequence", "path", "map", "convolution", "convchain", "wfc" };
}

/// <summary>
/// Base class for AST nodes which have other AST nodes as children.
/// </summary>
abstract class Branch : Node
{
    /// <summary>
    /// The parent of this AST node, or <c>null</c> if this is a root node,
    /// <see cref="MapNode">'map' node</see> or <see cref="WFCNode">WFC node</see>.
    /// </summary>
    public Branch parent;
    
    /// <summary>The children of this AST node.</summary>
    public Node[] nodes;
    
    /// <summary>The index of the currently active child node. May be -1 if any preprocessing needs to be done.</summary>
    public int n;

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        string symmetryString = xelem.Get<string>("symmetry", null);
        bool[] symmetry = SymmetryHelper.GetSymmetry(ip.grid.MZ == 1, symmetryString, parentSymmetry);
        if (symmetry == null)
        {
            Interpreter.WriteLine($"unknown symmetry {symmetryString} at line {xelem.LineNumber()}");
            return false;
        }

        XElement[] xchildren = xelem.Elements(nodenames).ToArray();
        nodes = new Node[xchildren.Length];
        for (int c = 0; c < xchildren.Length; c++)
        {
            // if this is a MapNode which replaces the grid, then `grid` has already been changed before base.Load was called
            var child = Factory(xchildren[c], symmetry, ip, grid);
            if (child == null) return false;
            // MapNode/WFCNode not have parents; the program terminates when these nodes complete
            if (child is Branch branch) branch.parent = branch is MapNode || branch is WFCNode ? null : this;
            nodes[c] = child;
        }
        return true;
    }

    override public bool Go()
    {
        for (; n < nodes.Length; n++)
        {
            Node node = nodes[n];
            if (node is Branch branch) ip.current = branch;
            if (node.Go()) return true;
        }
        ip.current = ip.current.parent;
        Reset();
        return false;
    }

    override public void Reset()
    {
        foreach (var node in nodes) node.Reset();
        n = 0;
    }
}

/// <summary>
/// A sequence node fully executes each of its children until they are not
/// applicable, in sequence.
/// </summary>
class SequenceNode : Branch { }

/// <summary>
/// A Markov node repeatedly executes its first applicable child node, until
/// none of its children are applicable.
/// </summary>
class MarkovNode : Branch
{
    public MarkovNode() { }
    public MarkovNode(Node child, Interpreter ip) { nodes = new Node[] { child }; this.ip = ip; grid = ip.grid; }

    public override bool Go()
    {
        // a Markov node always searches for an applicable node from the start of the array of children
        n = 0;
        return base.Go();
    }
}
