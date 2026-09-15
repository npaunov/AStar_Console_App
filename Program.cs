using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AStar.Rendering;

class Program
{
    // Where generated artefacts go. The study's outputs live in D:\Save, outside
    // the repository, so nothing generated is ever committed by accident. The
    // repo-relative results/ directory is the fallback, so anyone cloning this
    // can still reproduce the figures without that drive.
    const string PreferredResultsRoot = @"D:\Save\results";

    static void Main(string[] args)
    {
        // Set grid dimensions
        int width = 30, height = 30;

        // Create a 2D grid: 0 = free cell, 99 = obstacle
        int[,] grid = new int[height, width];

        // Create a 2D array for cell movement cost (1 for free cell, 99 for obstacle)
        int[,] cellCost = new int[height, width];

        // Number of obstacles to randomly place in the grid
        int obstacleCount = 300;

        // Random number generator for obstacle and position selection
        var rand = new Random();

        // Set start and goal positions (named tuple for easier access)
        var start = (x: 0, y: 0);
        var goal = (x: width - 1, y: height - 1);

        // Place obstacles randomly, making sure not to overwrite start or goal
        int placed = 0;
        while (placed < obstacleCount)
        {
            int x = rand.Next(width); // Random column
            int y = rand.Next(height); // Random row
            // Only place obstacle if cell is not start, goal, or already an obstacle
            if ((x, y) != start && (x, y) != goal && grid[y, x] == 0)
            {
                grid[y, x] = 99; // Mark as obstacle
                placed++; // Increment placed count
            }
        }

        //Assign weights: 1 for free cells, 99 for obstacles
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                cellCost[y, x] = grid[y, x] == 0 ? 1 : 99;

        //// Assign weights: random 1-9 for free cells, 99 for obstacles
        //for (int y = 0; y < height; y++)
        //    for (int x = 0; x < width; x++)
        //        cellCost[y, x] = grid[y, x] == 0 ? rand.Next(1, 9) : 99;

        // Print the environment once, before the search, if it is small enough to read
        if (ConsoleGridRenderer.CanPrint(width, height))
        {
            Console.WriteLine("Grid with weights (before path):");
            ConsoleGridRenderer.PrintGrid(grid, cellCost, null, null, start, goal, width, height, showPath: false);
        }

        // Time the search and nothing else — no console painting inside the
        // measured region, so this is a measurement of the algorithm.
        // Memory is the allocated-bytes delta across the same region: allocation
        // churn is deterministic, unlike working set. The Stopwatch is built
        // before the first snapshot so its own allocation is not attributed to
        // the search, and both snapshots sit outside the timed region because
        // precise: true is not free.
        var sw = new Stopwatch();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        sw.Start();
        var (path, explored) = AStarSearch(grid, cellCost, start, goal, width, height);
        sw.Stop();
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        // Print the finished grid once, with the path and the explored set
        if (ConsoleGridRenderer.CanPrint(width, height))
        {
            Console.WriteLine("\nGrid with final path:");
            ConsoleGridRenderer.PrintGrid(grid, cellCost, path, explored, start, goal, width, height, showPath: true);
        }

        // Print start and goal positions as coordinates
        Console.WriteLine();
        Console.WriteLine($"Start: {ConsoleGridRenderer.FormatCoord(start)}");
        Console.WriteLine($"Goal:  {ConsoleGridRenderer.FormatCoord(goal)}");
        Console.WriteLine();

        // If a valid path was found, print the path details
        bool success = path.Count > 0 && path[0] == start && path[^1] == goal;
        if (success)
        {
            Console.WriteLine("A* Path found!"); // Path found message
            Console.WriteLine($"Path length: {path.Count}"); // Path length
            Console.WriteLine("Path sequence (from start to goal):");
            ConsoleGridRenderer.PrintPathSequence(path);
        }
        else
        {
            // If no path was found, print a message
            Console.WriteLine("No path found from start to goal.");
        }

        Console.WriteLine($"Expanded nodes: {explored.Count}");

        // Print the time and memory taken for the search
        Console.WriteLine();
        Console.WriteLine($"Search time: {sw.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms");
        Console.WriteLine($"Allocated:   {FormatBytes(allocatedBytes)} ({allocatedBytes.ToString(CultureInfo.InvariantCulture)} bytes)");

        // Render the run as a PNG — this replaces the per-expansion console
        // animation and is the only visualisation that scales past ~50x50.
        string figurePath = WriteFigure(grid, path, explored, start, goal, width, height,
            sw.Elapsed.TotalMilliseconds, allocatedBytes, success, ResolveResultsDirectory(args));
        Console.WriteLine($"Figure written to: {figurePath}");
    }

    // Renders the finished search to an indexed-palette PNG under results/ and
    // returns the full path of the file it wrote.
    static string WriteFigure(
        int[,] grid,
        List<(int, int)> path,
        HashSet<(int, int)> explored,
        (int x, int y) start,
        (int x, int y) goal,
        int width,
        int height,
        double elapsedMs,
        long allocatedBytes,
        bool success,
        string resultsDirectory)
    {
        // The renderer works from a plain obstacle mask; the weight array is a
        // console-view concern only. The density is counted off the mask rather
        // than taken from the requested obstacle count, so the caption always
        // describes the environment that was actually drawn and searched.
        var blocked = new bool[height, width];
        int blockedCells = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (grid[y, x] == 99)
                {
                    blocked[y, x] = true;
                    blockedCells++;
                }

        double densityPercent = 100.0 * blockedCells / (width * height);

        var panel = new PanelData
        {
            Blocked = blocked,
            Start = start,
            Goal = goal,
            Explored = explored,
            Path = success ? path : null,
            Labels = new[]
            {
                "A* OCTILE 8-DIR",
                $"GRID: {width} X {height}",
                $"EXPANDED: {explored.Count}   PATH: {(success ? path.Count : 0)} CELLS",
                // InvariantCulture matters twice over: this machine's locale uses a
                // comma as the decimal separator, and the bitmap font has no comma.
                $"OBSTACLES: {densityPercent.ToString("F1", CultureInfo.InvariantCulture)} %",
                $"TIME: {elapsedMs.ToString("F2", CultureInfo.InvariantCulture)} MS",
                $"MEMORY: {FormatBytes(allocatedBytes)}",
            },
        };

        var image = GridImageRenderer.Render(panel, GridImageRenderer.SuggestScale(width, height));
        string figurePath = Path.Combine(resultsDirectory, "demo", "astar_demo.png");
        PngEncoder.Write(figurePath, image);
        return figurePath;
    }

    // Byte counts in a fixed set of units. InvariantCulture keeps the decimal
    // separator a dot: this machine's locale uses a comma, and the bitmap font
    // has no comma glyph, so a comma would render as a blank on the figure.
    static string FormatBytes(long bytes)
    {
        const double Kilobyte = 1024.0;
        const double Megabyte = Kilobyte * 1024.0;

        if (Math.Abs(bytes) >= Megabyte)
            return (bytes / Megabyte).ToString("F2", CultureInfo.InvariantCulture) + " MB";
        if (Math.Abs(bytes) >= Kilobyte)
            return (bytes / Kilobyte).ToString("F1", CultureInfo.InvariantCulture) + " KB";
        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    // Results never belong in bin/, so that one results directory accumulates
    // across runs. Order of preference: an explicit path given as the first
    // command-line argument, then PreferredResultsRoot if its parent exists,
    // then results/ beside the project file, then the working directory.
    static string ResolveResultsDirectory(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            return args[0];

        string? preferredParent = Path.GetDirectoryName(PreferredResultsRoot);
        if (!string.IsNullOrEmpty(preferredParent) && Directory.Exists(preferredParent))
            return PreferredResultsRoot;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.GetFiles("AStar_Console_App.csproj").Length > 0)
                return Path.Combine(directory.FullName, "results");
            directory = directory.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "results");
    }

    // A* search algorithm for finding the shortest path. Returns the path and
    // the set of expanded nodes; the caller decides how to visualise them.
    static (List<(int, int)> Path, HashSet<(int, int)> Explored) AStarSearch(
        int[,] grid, int[,] cellCost, (int, int) start, (int, int) goal, int width, int height)
    {
        // Open set, sorted by F = G + H (total estimated cost)
        var openSet = new SortedSet<(double, double, (int, int))>(Comparer<(double, double, (int, int))>.Create((a, b) =>
        {
            int cmp = a.Item1.CompareTo(b.Item1); // Compare F
            if (cmp == 0) cmp = a.Item2.CompareTo(b.Item2); // Compare H
            if (cmp == 0) cmp = a.Item3.Item1.CompareTo(b.Item3.Item1); // Compare X
            if (cmp == 0) cmp = a.Item3.Item2.CompareTo(b.Item3.Item2); // Compare Y
            return cmp;
        }));

        // For reconstructing the path: maps each node to its parent
        var cameFrom = new Dictionary<(int, int), (int, int)>();

        // Cost from start to each node
        var gScore = new Dictionary<(int, int), double> { [start] = 0 };

        // Heuristic estimate from start to goal
        var hScore = Heuristic(start, goal);

        // Add the start node to the open set
        openSet.Add((hScore, hScore, start));

        // Set of explored (visited) cells
        var explored = new HashSet<(int, int)>();

        // Main A* search loop
        while (openSet.Count > 0)
        {
            var current = openSet.Min.Item3; // Get node with lowest F score
            explored.Add(current); // Mark as explored

            // If we've reached the goal, reconstruct and return the path
            if (current == goal)
                return (ReconstructPath(cameFrom, current), explored);

            openSet.Remove(openSet.Min); // Remove current node from open set

            // Check all valid neighbors (8 directions)
            foreach (var (neighbor, moveCost) in GetNeighborsWithCost(grid, cellCost, current))
            {
                double tentativeG = gScore[current] + moveCost; // Calculate tentative G score
                // If this path to neighbor is better than any previous one
                if (!gScore.TryGetValue(neighbor, out double g) || tentativeG < g)
                {
                    cameFrom[neighbor] = current; // Record best path so far
                    gScore[neighbor] = tentativeG; // Update G score
                    double h = Heuristic(neighbor, goal); // Calculate heuristic
                    openSet.Add((tentativeG + h, h, neighbor)); // Add neighbor to open set
                }
            }
        }
        // If no path is found, return an empty list
        return (new List<(int, int)>(), explored);
    }

    // Returns a list of valid neighbor positions (8 directions) and their move cost, using cell weights
    static List<((int, int), double)> GetNeighborsWithCost(int[,] grid, int[,] cellCost, (int, int) pos)
    {
        var neighbors = new List<((int, int), double)>();
        // Directions: N, NE, E, SE, S, SW, W, NW
        int[] dx = { 0, 1, 1, 1, 0, -1, -1, -1 };
        int[] dy = { -1, -1, 0, 1, 1, 1, 0, -1 };
        for (int dir = 0; dir < 8; dir++)
        {
            int nx = pos.Item1 + dx[dir]; // Neighbor column
            int ny = pos.Item2 + dy[dir]; // Neighbor row
            // Check if neighbor is within grid bounds and not an obstacle
            if (nx >= 0 && ny >= 0 && nx < grid.GetLength(1) && ny < grid.GetLength(0) && grid[ny, nx] != 99)
            {
                // Diagonal move: multiply cell cost by sqrt(2), else use cell cost
                double cost = cellCost[ny, nx] * ((dx[dir] != 0 && dy[dir] != 0) ? Math.Sqrt(2) : 1.0);
                neighbors.Add(((nx, ny), cost)); // Add neighbor and cost
            }
        }
        return neighbors;
    }

    // Octile distance heuristic for 8-directional movement
    static double Heuristic((int, int) a, (int, int) b)
    {
        int dx = Math.Abs(a.Item1 - b.Item1); // Horizontal distance
        int dy = Math.Abs(a.Item2 - b.Item2); // Vertical distance
        double D = 1.0; // Cost for straight move
        double D2 = Math.Sqrt(2); // Cost for diagonal move
        // Octile distance formula
        return D * (dx + dy) + (D2 - 2 * D) * Math.Min(dx, dy);
    }

    // Reconstructs the path from start to goal using the cameFrom map
    static List<(int, int)> ReconstructPath(Dictionary<(int, int), (int, int)> cameFrom, (int, int) current)
    {
        var path = new List<(int, int)> { current }; // Start with goal
        // Walk backwards from goal to start
        while (cameFrom.TryGetValue(current, out var prev))
        {
            current = prev; // Move to previous cell
            path.Add(current); // Add to path
        }
        path.Reverse(); // Reverse to get path from start to goal
        return path; // Return path list
    }
}
