// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)
using System;
using System.Linq;
using System.Xml.Linq;
abstract class Node
{
    abstract protected bool Load(XElement xelem, bool[] symmetry, Grid grid);  // Load configuration from XML
    abstract public void Reset();  // Reset execution state
    abstract public bool Go();     // Execute one step of this node
    protected Interpreter ip;      // Reference to parent interpreter
    public Grid grid;              // Grid being operated on

    // Factory method to create appropriate node type based on XML element
    public static Node Factory(XElement xelem, bool[] symmetry, Interpreter ip, Grid grid)
    {
        // Validate node type
        if (!nodenames.Contains(xelem.Name.LocalName))
        {
            Interpreter.WriteLine($"unknown node type \"{xelem.Name}\" at line {xelem.LineNumber()}");
            return null;
        }

        // Create specific node type based on element name
        // Pattern matching syntax creates the appropriate subclass
        Node result = xelem.Name.LocalName switch
        {
            "one" => new OneNode(),               // Execute one child at random
            "all" => new AllNode(),               // Execute all children in parallel
            "prl" => new ParallelNode(),          // Similar to all but with different semantics
            "markov" => new MarkovNode(),         // Random transitions between states
            "sequence" => new SequenceNode(),     // Execute children in order
            "path" => new PathNode(),             // Path-specific operations
            "map" => new MapNode(),               // Transform grid using rules
            "convolution" => new ConvolutionNode(), // Image-like convolution
            "convchain" => new ConvChainNode(),   // Convolution chain operations
            "wfc" when xelem.Get<string>("sample", null) != null => new OverlapNode(),  // WFC with sample
            "wfc" when xelem.Get<string>("tileset", null) != null => new TileNode(),    // WFC with tileset
            _ => null
        };

        // Initialize and load node configuration
        result.ip = ip;
        result.grid = grid;
        bool success = result.Load(xelem, symmetry, grid);
        if (!success) return null;
        return result;
    }

    // List of valid node type names for validation
    protected static string[] nodenames = new string[] { "one", "all", "prl", "markov", "sequence", "path", "map", "convolution", "convchain", "wfc" };
}

// Base class for nodes that contain child nodes (composite pattern)
abstract class Branch : Node
{
    public Branch parent;     // Parent branch node
    public Node[] nodes;      // Child nodes
    public int n;             // Current child node index

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Process symmetry settings
        string symmetryString = xelem.Get<string>("symmetry", null);
        bool[] symmetry = SymmetryHelper.GetSymmetry(ip.grid.MZ == 1, symmetryString, parentSymmetry);
        if (symmetry == null)
        {
            Interpreter.WriteLine($"unknown symmetry {symmetryString} at line {xelem.LineNumber()}");
            return false;
        }

        // Create child nodes
        XElement[] xchildren = xelem.Elements(nodenames).ToArray();
        nodes = new Node[xchildren.Length];
        for (int c = 0; c < xchildren.Length; c++)
        {
            // Create child node using factory
            var child = Factory(xchildren[c], symmetry, ip, grid);
            if (child == null) return false;

            // Set parent relationship if child is a branch
            // Special cases: MapNode and WFCNode don't have parents (they create new contexts)
            if (child is Branch branch) branch.parent = branch is MapNode || branch is WFCNode ? null : this;
            nodes[c] = child;
        }
        return true;
    }

    // Execute next child node that hasn't completed
    override public bool Go()
    {
        // Loop through remaining child nodes
        for (; n < nodes.Length; n++)
        {
            Node node = nodes[n];
            // If child is a branch, set it as current in interpreter
            if (node is Branch branch) ip.current = branch;
            // Execute child and return true if it's still running
            if (node.Go()) return true;
        }
        // All children completed, return to parent
        ip.current = ip.current.parent;
        Reset();
        return false;  // This branch is complete
    }

    // Reset all child nodes
    override public void Reset()
    {
        foreach (var node in nodes) node.Reset();
        n = 0;  // Reset current node index
    }
}

// Simple sequence - executes children in order
class SequenceNode : Branch { }

// Markov node - resets to beginning after each step
class MarkovNode : Branch
{
    public MarkovNode() { }

    // Constructor for wrapping a single node
    public MarkovNode(Node child, Interpreter ip) { nodes = new Node[] { child }; this.ip = ip; grid = ip.grid; }

    // Always start from the first child
    public override bool Go()
    {
        n = 0;
        return base.Go();
    }
}

/*
=== SUMMARY ===

This code implements a hierarchical execution tree for the Wave Function Collapse algorithm. Think of it like a recipe book where each node represents a different cooking technique or step.

The Node class hierarchy works like a tree of tasks:

1. At the top is the abstract Node class, which defines what all nodes need to do:
   - Load: Set up the node from XML configuration
   - Reset: Clear execution state
   - Go: Execute one step of the node's task

2. The Factory method works like a node dispatcher:
   - It reads an XML element and creates the right type of node
   - It's similar to how a factory might make different products based on an order form

3. The Branch class is a container for other nodes:
   - It's like a multi-step recipe that contains sub-recipes
   - When executed, it runs through its children in sequence
   - Some special branch types like MapNode create new execution contexts

4. There are specialized node types for different operations:
   - SequenceNode: Runs steps in order (like following a recipe step by step)
   - MarkovNode: Keeps restarting from the beginning (like a loop)
   - Other specialized nodes for pattern matching, transformations, etc.

This structure allows complex procedural generation algorithms to be expressed as hierarchical recipes in XML, with each node performing a specific task and the interpreter following the execution tree to produce the final result.
*/