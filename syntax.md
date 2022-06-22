## Grid
New grids are created either at root, or with `wfc` or `map` nodes. Example: [MazeMap](models/MazeMap.xml) has 2 grids. Grids have one required attribute `values` and one optional attribute `folder`.

`values="BRGUY"` says that black, red, green, blue and yellow values are possible on the grid, and the starting value is black.

`folder="DungeonGrowth"` says that the interpreter should look for rule files in the [DungeonGrowth](resources/rules/DungeonGrowth/) folder.

For root grids, there is one more optional boolean attribute called `origin`, equal `False` by default. If origin is set `True`, the interpreter creates a pixel at the center of the grid with the value equal to the second value in `values`. For example, `values="YRBAN" origin="True"` creates a yellow screen with a red dot in the center.



## Symmetry
With most nodes, you can specify the `symmetry` attribute. Possible symmetry values are listed in [SymmetryHelper.cs](source/SymmetryHelper.cs). By default, the largest symmetry group is used. Example: in [Flowers](models/Flowers.xml) we need flowers growing vertically and not side-to-side, so we specify that rules should only be mirrored along the x axis.



## Rules
Rule attributes:
* `in="BBB/BWB"` - specifies input part of a rule.
* `out="RR DA FR"` - specifies output part of a rule.
* `fin="filename"` - loads input from `filename.png` or `filename.vox`.
* `fout="filename"` - loads output from `filename.png` or `filename.vox`.
* `file="filename"` - loads a glued input + output box from file. Example: [Circuit](models/Circuit.xml).
* `p` - the probability that the rule will be applied. Equals `1.0` by default. Example: in [Apartemazements](models/Apartemazements.xml) only 25% of ceiling locations are converted into light sources.

Slashes `/` are y-separators, spaces ` ` are z-separators.

If a file is referenced, the `legend` should be specified. Legend lists used values in a scanline order. See example in [DungeonGrowth](models/DungeonGrowth.xml).



## Rulenodes
There are 3 kinds of rulenodes:
1. `one` nodes, also called `(exists)` nodes.
2. `all` nodes, also called `{forall}` nodes.
3. `prl` (parallel) nodes are similar to `all` nodes, but they are applied independently of each other, they don't care about rule overlaps. Often executing a `prl` node leads to exactly the same result as with an `all` node, but `prl` is more performant.

Rulenode attributes:
* `steps="60"` - limits node execution to 60 steps. See an example in [River](models/River.xml).



## Unions
See examples of union use in [DungeonGrowth](models/DungeonGrowth.xml).



## Inference
Inference in MarkovJunior allows to impose constraints on the future state, and generate only those runs that lead to the constrained future. Inference is triggered by putting `observe` elements inside rulenodes (inside `one` or `all` nodes, to be precise). Observe elements have 3 attributes: `value`, `from`, `to`.

For example, `<observe value="W" to="BR"/>` means that squares that are currently white should become black or red after a chain of rule applications. `<observe value="I" from="B" to="W"/>` means that squares that are currently indigo are turned black immediately, and then should become white after a chain of rule applications.

In [SokobanLevel1](models/SokobanLevel1.xml) we say that the goal `I`-squares should become white - this would mean that the puzzle is solved. We also help the inference engine by explicitly saying that the current black, white and red squares should *not* be white in the end. Since we don't have `I` in the ruleset, we say that current indigo squares should be treated as black by setting `from="B"`.

Rulenodes with inference have a boolean `search` attribute, false by default. If search is set false, the interpreter follows the rule propagation field greedily. If search is set true, the interpreter searches the state graph using the rule propagation field as a heuristic.

If search is set false, the interpreter can be made to follow the goal more strictly or less strictly by varying the floating point `temperature` parameter. If search is set true, the attributes are:
1. Integer `limit` attribute sets the maximum number of states searched. By default, the number of states is not limited.
2. Floating point `depthCoefficient` attribute [interpolates](https://github.com/mxgmn/MarkovJunior/blob/4e64162f00203f5b5753af100af0dab8d72ce805/source/Search.cs#L269) between breadth-first search and depth-first search.

See examples of inference use in [MultiSokoban9](models/MultiSokoban9.xml), [SokobanLevel1](models/SokobanLevel1.xml), [StairsPath](models/StairsPath.xml), [KnightPatrol](models/KnightPatrol.xml), [CrossCountry](models/CrossCountry.xml), [RegularPath](models/RegularPath.xml), [DiagonalPath](models/DiagonalPath.xml), [EuclideanPath](models/EuclideanPath.xml), [BishopParity](models/BishopParity.xml), [SnellLaw](models/SnellLaw.xml), [SequentialSokoban](models/SequentialSokoban.xml), [CompleteSAW](models/CompleteSAW.xml), [CompleteSAWSmart](models/CompleteSAWSmart.xml), [Island](models/Island.xml).



## Map
See examples of `map` node use in [MazeMap](models/MazeMap.xml), [MarchingSquares](models/MarchingSquares.xml), [OddScale](models/OddScale.xml), [OddScale3D](models/OddScale3D.xml), [SeaVilla](models/SeaVilla.xml), [ModernHouse](models/ModernHouse.xml), [CarmaTower](models/CarmaTower.xml).



## Path
See examples of `path` node use in [BasicDijkstraFill](models/BasicDijkstraFill.xml), [BasicDijkstraDungeon](models/BasicDijkstraDungeon.xml), [BernoulliPercolation](models/BernoulliPercolation.xml), [Percolation](models/Percolation.xml), [Circuit](models/Circuit.xml), [DungeonGrowth](models/DungeonGrowth.xml).



## Convolution
See examples of `convolution` node use in [GameOfLife](models/GameOfLife.xml), [Cave](models/Cave.xml), [ConnectedCaves](models/ConnectedCaves.xml), [ConstrainedCaves](models/ConstrainedCaves.xml), [OpenCave](models/OpenCave.xml), [OpenCave3D](models/OpenCave3D.xml), [Counting](models/Counting.xml), [CarmaTower](models/CarmaTower.xml).



## WaveFunctionCollapse
See examples of tile WFC in [TileDungeon](models/TileDungeon.xml), [Knots2D](models/Knots2D.xml), [Knots3D](models/Knots3D.xml), [Surface](models/Surface.xml), [EscherSurface](models/EscherSurface.xml), [PillarsOfEternity](models/PillarsOfEternity.xml), [Apartemazements](models/Apartemazements.xml), [SeaVilla](models/SeaVilla.xml), [ModernHouse](models/ModernHouse.xml).

See examples of overlap WFC in [WaveFlowers](models/WaveFlowers.xml), [WaveBrickWall](models/WaveBrickWall.xml), [WaveDungeon](models/WaveDungeon.xml).



## ConvChain
See examples of `convchain` node use in [ChainMaze](models/ChainMaze.xml), [ChainDungeon](models/ChainDungeon.xml), [ChainDungeonMaze](models/ChainDungeonMaze.xml).



## Questions and Answers
**Q:** How to make a loop? How to make a sequence repeat?<br/>
**A:** To make a sequence repeat, put a `sequence` node inside a `markov` node or inside another `sequence` node. Examples of this: [MultiHeadedWalk](models/MultiHeadedWalk.xml), [HamiltonianPath](models/HamiltonianPath.xml), [SelectLargeCaves](models/SelectLargeCaves.xml), [SelectLongKnots](models/SelectLongKnots.xml), [FireNoise](models/FireNoise.xml), [SmartSAW](models/SmartSAW.xml), [FindLongCycle](models/FindLongCycle.xml). Counters in markov/sequence nodes are not supported right now. Instead, you may want to repeat the sequence until some node is matched.
