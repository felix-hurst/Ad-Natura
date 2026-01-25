public static class Pixelation
{
    public const float DEFAULT_PIXEL_SIZE = 0.0625f;

    public static List<Vector2> PixelateCutEdges(List<Vector2> cut_edge, float pixel_size = DEFAULT_PIXEL_SIZE)
    {
        if (pixel_size <= 0f)
        {
            if (pixel_size <= 0f)
                throw new ArgumentException("pixelSize must be > 0");
        }

        // Get pixel-aligned start and end points
        Vector2 start = SnapToGrid(cut_edge[0], pixel_size);
        Vector2 end = SnapToGrid(cut_edge[1], pixel_size);

        // Add starting point to the list of final points
        List<Vector2> points = new List<Vector2>();
        points.Add(start);


        // Constants required for the algorithm
        float delta_x = end.x - start.x;
        float delta_y = end.y - start.y;
        float decision = 2 * delta_y - 2 * delta_x;
        float x_direction = delta_x >= 0 ? pixel_size : -pixel_size;
        float y_direction = delta_y >= 0 ? pixel_size : -pixel_size;

        // If slope is less than or equal to 1, normal algorithm, else flip x and y in calculations
        bool x_dominant = Mathf.Abs(delta_x) >= Mathf.Abs(delta_y);
        
        // Bresenham's line algorithm adapted for pixelation
        if (x_dominant)
        {
            // Number of steps to take
            int steps = Mathf.FloorToInt(Mathf.Abs(delta_x) / pixel_size);

            // Start at the start
            float x = start.x;
            float y = start.y;

            // Decision parameter for steps in x direction
            float param = 2 * Mathf.Abs(delta_y) - Mathf.Abs(delta_x);

            for (int i = 0; i < steps; i++)
            {
                // Step only along x-axis, one point to add
                if (param < 0)
                {
                    x += x_direction;
                    points.Add(new Vector2(x, y));
                    param += 2 * Mathf.Abs(delta_y);
                }
                // Step along both axes, two points to add (no diagonal movement)
                else
                {
                    x += x_direction;
                    points.Add(new Vector2(x, y));
                    y += y_direction;
                    points.Add(new Vector2(x, y));
                    param += 2 * Mathf.Abs(delta_y) - 2 * Mathf.Abs(delta_x);
                }
            }
        }
        else
        {
            // Number of steps to take
            int steps = Mathf.FloorToInt(Mathf.Abs(delta_y) / pixel_size);

            // Start at the start
            float x = start.x;
            float y = start.y;

            // Decision parameter for steps
            float param = 2 * Mathf.Abs(delta_x) - Mathf.Abs(delta_y);

            for (int i = 0; i < steps; i++)
            {
                // Step only along y-axis, one point to add
                if (param < 0)
                {
                    y += y_direction;
                    points.Add(new Vector2(x, y));
                    param += 2 * Mathf.Abs(delta_x);
                }
                // Step along both axes, two points to add (no diagonal movement)
                else
                {
                    y += y_direction;
                    points.Add(new Vector2(x, y));
                    x += x_direction;
                    points.Add(new Vector2(x, y));
                    param += 2 * Mathf.Abs(delta_x) - 2 * Mathf.Abs(delta_y);
                }
            }
        }

        // Ensure the end point is included
        if (points[points.Count - 1] != end)
        {
            points.Add(end);
        }
        return points;
    }

    public static Vector2 SnapToGrid(Vector2 point, float pixel_size = DEFAULT_PIXEL_SIZE)
    {
        // Snap point to nearest lower pixel grid intersection, floor as per MG
        float x = Mathf.Floor(point.x / pixel_size) * pixel_size;
        float y = Mathf.Floor(point.y / pixel_size) * pixel_size;
        return new Vector2(x, y);
    }

    public static Vector2 IdentifyCutEdges(List<Vector2> edge, Vector2 entry, Vector2 exit)
    {
        List<List<int>> segments = new List<List<int>>();
        List<int> current_segment = new List<int>();

        for (int i = 0; i < edge.Count; i++)
        {
            current_segment.Add(i);
            current_segment.Add(i+1 % edge.Count);

            if (Vector2.Distance(edge[current_segment[0]], entry) < DEFAULT_PIXEL_SIZE && Vector2.Distance(edge[current_segment[1]], exit) < DEFAULT_PIXEL_SIZE)
            {
                segments.Add(current_segment);
                current_segment.Clear();
            }
            else if (Vector2.Distance(edge[current_segment[0]], exit) < DEFAULT_PIXEL_SIZE && Vector2.Distance(edge[current_segment[1]], entry) < DEFAULT_PIXEL_SIZE)
            {
                segments.Add(current_segment);
                current_segment.Clear();
            }
            else
            {
                current_segment.Clear();
            }
        }
    }
}