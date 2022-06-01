// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;

abstract class Node
{
    abstract protected bool Load(XElement xelem, bool[] symmetry, Grid grid);
    abstract public void Reset();
    abstract public bool Go();

    protected Interpreter ip;
    public Grid grid;

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

abstract class Branch : Node
{
    public Branch parent;
    public Node[] nodes;
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
            var child = Factory(xchildren[c], symmetry, ip, grid);
            if (child == null) return false;
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

class SequenceNode : Branch { }
class MarkovNode : Branch
{
    public MarkovNode() { }
    public MarkovNode(Node child, Interpreter ip) { nodes = new Node[] { child }; this.ip = ip; grid = ip.grid; }

    public override bool Go()
    {
        n = 0;
        return base.Go();
    }
}
