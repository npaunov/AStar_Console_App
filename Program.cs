using System;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        // Set the grid size
        int width = 30, height = 30;

        // Create a 2D grid (0 = free, 1 = obstacle)
        int[,] grid = new int[height, width];

        // Number of obstacles to randomly place in the grid
        int obstacleCount = 100;
        // Random number generator for obstacle and position selection
        var rand = new Random();

        // Helper function to find a random free cell (not an obstacle)
        (int x, int y) RandomFreeCell()
        {
            int x, y;
            do
            {
                x = rand.Next(width);   // Random column
                y = rand.Next(height);  // Random row
            } while (grid[y, x] == 1); // Repeat if cell is an obstacle
            return (x, y);
        }

        // Select random start and goal positions (not the same, not on obstacles)
        var start = RandomFreeCell();
        var goal = RandomFreeCell();
        while (goal == start)
        {
            goal = RandomFreeCell();
        }

        // For testing, set fixed start and goal positions
        start = (0, 0);
        goal = (29, 29);

        // Place obstacles randomly, making sure not to overwrite start or goal
        int placed = 0;
        while (placed < obstacleCount)
        {
            int x = rand.Next(width);
            int y = rand.Next(height);
            // Only place obstacle if cell is not start, goal, or already an obstacle
            if ((x, y) != start && (x, y) != goal && grid[y, x] == 0)
            {
                grid[y, x] = 1; // Mark as obstacle
                placed++;
            }
        }

        // Start measuring the time taken for the A* search
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Run the A* search algorithm to find the path
        var path = AStarSearch(grid, start, goal);
        // Stop the timer
        sw.Stop();

        // Print column markers (A, B, C, ...)
        Console.Write("   "); // Padding for row numbers
        for (int x = 0; x < width; x++)
        {
            char colMark = (char)('A' + x); // Convert column index to letter
            Console.Write($"{colMark} ");
        }
        Console.WriteLine();

        // Print the grid row by row
        for (int y = 0; y < height; y++)
        {
            // Print row marker (1, 2, 3, ...)
            Console.Write($"{y + 1,2} "); // Right-aligned, 2 spaces

            for (int x = 0; x < width; x++)
            {
                // Print different symbols for start, goal, obstacles, path, and empty cells
                if ((x, y) == start)
                    Console.Write("S "); // Start cell
                else if ((x, y) == goal)
                    Console.Write("G "); // Goal cell
                else if (grid[y, x] == 1)
                    Console.Write("@ "); // Obstacle
                else if (path.Contains((x, y)))
                    Console.Write("* "); // Path cell
                else
                    Console.Write(". "); // Empty cell
            }
            Console.WriteLine(); // Newline after each row
        }

        // --- Reporting Section ---
        Console.WriteLine();
        // Print start and goal positions with both marker and coordinates
        Console.WriteLine($"Start: {CoordToMarker(start)} ({start.x},{start.y})");
        Console.WriteLine($"Goal:  {CoordToMarker(goal)} ({goal.x},{goal.y})");
        Console.WriteLine();

        // If a valid path was found, print the path details
        if (path.Count > 0 && path[0] == start && path[^1] == goal)
        {
            Console.WriteLine("A* Path found!");
            Console.WriteLine($"Path length: {path.Count}");
            Console.WriteLine("Path sequence (from start to goal):");
            // Print the path as a sequence of grid markers
            for (int i = 0; i < path.Count; i++)
            {
                var step = path[i];
                Console.Write($"{CoordToMarker(step)}");
                if (i < path.Count - 1)
                    Console.Write(" -> ");
            }
            Console.WriteLine();
        }
        else
        {
            // If no path was found, print a message
            Console.WriteLine("No path found from start to goal.");
        }

        // Print the time taken for the search in minutes, seconds, and milliseconds
        Console.WriteLine();
        Console.WriteLine($"Search time: {sw.Elapsed.Minutes}m {sw.Elapsed.Seconds}s {sw.Elapsed.Milliseconds}ms");

        // Helper function to convert (x, y) to grid marker (e.g., C3)
        static string CoordToMarker((int x, int y) coord)
        {
            char col = (char)('A' + coord.x); // Convert x to column letter
            int row = coord.y + 1;            // Convert y to row number (1-based)
            return $"{col}{row}";
        }
    }

    /// <summary>
    /// Performs the A* search algorithm on a 2D grid with diagonal movement.
    /// </summary>
    /// <param name="grid">2D grid (0 = free, 1 = obstacle)</param>
    /// <param name="start">Start position (x, y)</param>
    /// <param name="goal">Goal position (x, y)</param>
    /// <returns>List of positions from start to goal, or empty if no path</returns>
    static List<(int, int)> AStarSearch(int[,] grid, (int, int) start, (int, int) goal)
    {
        // The open set, sorted by F = G + H (total estimated cost)
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

        // Main A* search loop
        while (openSet.Count > 0)
        {
            // Get the node in openSet with the lowest F score
            var current = openSet.Min.Item3;
            // If we've reached the goal, reconstruct and return the path
            if (current == goal)
                return ReconstructPath(cameFrom, current);

            // Remove the current node from the open set
            openSet.Remove(openSet.Min);

            // Check all valid neighbors (8 directions)
            foreach (var (neighbor, moveCost) in GetNeighborsWithCost(grid, current))
            {
                // Calculate tentative G score (cost from start to neighbor)
                double tentativeG = gScore[current] + moveCost;
                // If this path to neighbor is better than any previous one
                if (!gScore.TryGetValue(neighbor, out double g) || tentativeG < g)
                {
                    // Record the best path so far to this neighbor
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    // Calculate heuristic (estimated cost to goal)
                    double h = Heuristic(neighbor, goal);
                    // Add neighbor to open set with updated F and H
                    openSet.Add((tentativeG + h, h, neighbor));
                }
            }
        }
        // If no path is found, return an empty list
        return new List<(int, int)>();
    }

    /// <summary>
    /// Returns a list of valid neighbor positions (8 directions) and their move cost.
    /// </summary>
    /// <param name="grid">2D grid (0 = free, 1 = obstacle)</param>
    /// <param name="pos">Current position (x, y)</param>
    /// <returns>List of tuples: (neighbor position, move cost)</returns>
    static List<((int, int), double)> GetNeighborsWithCost(int[,] grid, (int, int) pos)
    {
        var neighbors = new List<((int, int), double)>();
        // Directions: N, NE, E, SE, S, SW, W, NW
        int[] dx = { 0, 1, 1, 1, 0, -1, -1, -1 };
        int[] dy = { -1, -1, 0, 1, 1, 1, 0, -1 };
        for (int dir = 0; dir < 8; dir++)
        {
            int nx = pos.Item1 + dx[dir]; // Neighbor x
            int ny = pos.Item2 + dy[dir]; // Neighbor y
            // Check if neighbor is within grid bounds and not an obstacle
            if (nx >= 0 && ny >= 0 && nx < grid.GetLength(1) && ny < grid.GetLength(0) && grid[ny, nx] == 0)
            {
                // Diagonal move if both dx and dy are not zero, else straight
                double cost = (dx[dir] != 0 && dy[dir] != 0) ? Math.Sqrt(2) : 1.0;
                neighbors.Add(((nx, ny), cost));
            }
        }
        return neighbors;
    }

    /// <summary>
    /// Octile distance heuristic for 8-directional movement.
    /// </summary>
    /// <param name="a">First position (x, y)</param>
    /// <param name="b">Second position (x, y)</param>
    /// <returns>Estimated cost from a to b</returns>
    static double Heuristic((int, int) a, (int, int) b)
    {
        int dx = Math.Abs(a.Item1 - b.Item1); // Horizontal distance
        int dy = Math.Abs(a.Item2 - b.Item2); // Vertical distance
        double D = 1.0;                       // Cost for straight move
        double D2 = Math.Sqrt(2);             // Cost for diagonal move
        // Octile distance formula
        return D * (dx + dy) + (D2 - 2 * D) * Math.Min(dx, dy);
    }

    /// <summary>
    /// Reconstructs the path from start to goal using the cameFrom map.
    /// </summary>
    /// <param name="cameFrom">Dictionary mapping each node to its parent</param>
    /// <param name="current">Goal position</param>
    /// <returns>List of positions from start to goal</returns>
    static List<(int, int)> ReconstructPath(Dictionary<(int, int), (int, int)> cameFrom, (int, int) current)
    {
        var path = new List<(int, int)> { current }; // Start with goal
        // Walk backwards from goal to start
        while (cameFrom.TryGetValue(current, out var prev))
        {
            current = prev;
            path.Add(current);
        }
        path.Reverse(); // Reverse to get path from start to goal
        return path;
    }
}
