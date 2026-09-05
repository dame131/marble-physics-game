using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MarbleSupportGraph : MonoBehaviour
{
    public enum NeighborLayout { HexOddRowOffset, HexEvenRowOffset, Orthogonal4 }

    [Header("Grid")]
    [SerializeField, Min(1)] private int rows = 30;
    [SerializeField, Min(1)] private int columns = 20;
    [SerializeField] private NeighborLayout neighborLayout = NeighborLayout.HexOddRowOffset;
    [SerializeField, Min(1)] private int ceilingAnchorRows = 1;

    [Header("Matching")]
    [SerializeField, Min(2)] private int minimumMatchCount = 3;

    [Header("Attached Marble Physics")]
    [SerializeField] private bool forceKinematicOnRegister = true;

    [Header("Released Marble Physics")]
    [SerializeField] private bool enableGravityOnRelease = true;
    [SerializeField] private bool clearVelocityOnRelease = true;
    [SerializeField] private RigidbodyInterpolation fallingInterpolation = RigidbodyInterpolation.Interpolate;
    [SerializeField] private CollisionDetectionMode fallingCollisionDetection = CollisionDetectionMode.ContinuousDynamic;

    [Header("Matched Marble Handling")]
    [SerializeField] private bool autoDisableMatchedMarbles = true;

    [Header("Validation")]
    [SerializeField] private bool warnIfSphereColliderMissing = true;

    public event Action<Rigidbody> MarbleMatched;
    public event Action<Rigidbody> MarbleReleased;
    public event Action<int> MatchCleared;
    public event Action<int> UnsupportedMarblesReleased;

    private sealed class MarbleNode
    {
        public Rigidbody Body;
        public int Row;
        public int Column;
        public int MatchId;
        public bool ExplicitAnchor;
        public bool InGrid;
    }

    private MarbleNode[,] grid;
    private readonly Dictionary<Rigidbody, MarbleNode> nodeByBody = new Dictionary<Rigidbody, MarbleNode>(512);
    private readonly Queue<MarbleNode> bfsQueue = new Queue<MarbleNode>(512);
    private readonly List<MarbleNode> nodeScratch = new List<MarbleNode>(512);
    private int[,] visitMarks;
    private int visitGeneration = 1;
    private bool initialized;

    private void Awake() => Initialize();

    private void OnValidate()
    {
        rows = Mathf.Max(1, rows);
        columns = Mathf.Max(1, columns);
        ceilingAnchorRows = Mathf.Clamp(ceilingAnchorRows, 1, rows);
        minimumMatchCount = Mathf.Max(2, minimumMatchCount);
    }

    public void Initialize()
    {
        if (initialized) return;
        grid = new MarbleNode[rows, columns];
        visitMarks = new int[rows, columns];
        nodeByBody.Clear();
        bfsQueue.Clear();
        nodeScratch.Clear();
        initialized = true;
    }

    public void ResetGraph()
    {
        EnsureInitialized();
        Array.Clear(grid, 0, grid.Length);
        Array.Clear(visitMarks, 0, visitMarks.Length);
        nodeByBody.Clear();
        bfsQueue.Clear();
        nodeScratch.Clear();
        visitGeneration = 1;
    }

    public bool RegisterMarble(Rigidbody body, int row, int column, int matchId, bool explicitCeilingAnchor = false)
    {
        EnsureInitialized();
        if (body == null)
        {
            Debug.LogError($"{nameof(MarbleSupportGraph)}: Cannot register a null Rigidbody.", this);
            return false;
        }
        if (!IsInsideGrid(row, column))
        {
            Debug.LogError($"{nameof(MarbleSupportGraph)}: Grid position [{row}, {column}] is outside {rows}x{columns}.", body);
            return false;
        }
        if (grid[row, column] != null)
        {
            Debug.LogError($"{nameof(MarbleSupportGraph)}: Grid cell [{row}, {column}] is already occupied.", body);
            return false;
        }
        if (nodeByBody.ContainsKey(body))
        {
            Debug.LogError($"{nameof(MarbleSupportGraph)}: Rigidbody {body.name} is already registered.", body);
            return false;
        }
        if (warnIfSphereColliderMissing)
        {
            SphereCollider sphere = body.GetComponent<SphereCollider>() ?? body.GetComponentInChildren<SphereCollider>();
            if (sphere == null)
                Debug.LogWarning($"{nameof(MarbleSupportGraph)}: {body.name} has no SphereCollider.", body);
        }

        MarbleNode node = new MarbleNode
        {
            Body = body,
            Row = row,
            Column = column,
            MatchId = matchId,
            ExplicitAnchor = explicitCeilingAnchor,
            InGrid = true
        };

        grid[row, column] = node;
        nodeByBody.Add(body, node);
        if (forceKinematicOnRegister) ConfigureAsAttached(body);
        return true;
    }

    public bool AttachMarbleAndResolve(Rigidbody body, int row, int column, int matchId, bool explicitCeilingAnchor = false)
    {
        if (!RegisterMarble(body, row, column, matchId, explicitCeilingAnchor)) return false;
        return TryResolveMatch(body);
    }

    public bool TryResolveMatch(Rigidbody seedBody)
    {
        EnsureInitialized();
        if (seedBody == null || !nodeByBody.TryGetValue(seedBody, out MarbleNode seed) || !seed.InGrid) return false;
        FindMatchingCluster(seed, nodeScratch);
        if (nodeScratch.Count < minimumMatchCount) return false;

        int directMatchCount = nodeScratch.Count;
        ClearMatchedNodes(nodeScratch);
        int releasedCount = RunAnchoringCheckAndReleaseUnsupported();
        MatchCleared?.Invoke(directMatchCount);
        UnsupportedMarblesReleased?.Invoke(releasedCount);
        return true;
    }

    public int ClearMarblesAndCheckSupport(IReadOnlyList<Rigidbody> marblesToClear)
    {
        EnsureInitialized();
        if (marblesToClear == null || marblesToClear.Count == 0) return 0;
        nodeScratch.Clear();
        for (int i = 0; i < marblesToClear.Count; i++)
        {
            Rigidbody body = marblesToClear[i];
            if (body == null) continue;
            if (!nodeByBody.TryGetValue(body, out MarbleNode node) || !node.InGrid) continue;
            nodeScratch.Add(node);
        }
        if (nodeScratch.Count == 0) return 0;
        int directClearCount = nodeScratch.Count;
        ClearMatchedNodes(nodeScratch);
        int releasedCount = RunAnchoringCheckAndReleaseUnsupported();
        MatchCleared?.Invoke(directClearCount);
        UnsupportedMarblesReleased?.Invoke(releasedCount);
        return releasedCount;
    }

    private void FindMatchingCluster(MarbleNode seed, List<MarbleNode> results)
    {
        results.Clear();
        bfsQueue.Clear();
        BeginVisitPass();
        MarkVisited(seed);
        bfsQueue.Enqueue(seed);
        int targetMatchId = seed.MatchId;

        while (bfsQueue.Count > 0)
        {
            MarbleNode current = bfsQueue.Dequeue();
            results.Add(current);
            VisitNeighbors(current.Row, current.Column, neighbor =>
            {
                if (neighbor == null || !neighbor.InGrid || neighbor.MatchId != targetMatchId || IsVisited(neighbor)) return;
                MarkVisited(neighbor);
                bfsQueue.Enqueue(neighbor);
            });
        }
    }

    public int RunAnchoringCheckAndReleaseUnsupported()
    {
        EnsureInitialized();
        bfsQueue.Clear();
        BeginVisitPass();

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                MarbleNode node = grid[row, column];
                if (node == null || !node.InGrid || !IsAnchor(node) || IsVisited(node)) continue;
                MarkVisited(node);
                bfsQueue.Enqueue(node);
            }
        }

        while (bfsQueue.Count > 0)
        {
            MarbleNode current = bfsQueue.Dequeue();
            VisitNeighbors(current.Row, current.Column, neighbor =>
            {
                if (neighbor == null || !neighbor.InGrid || IsVisited(neighbor)) return;
                MarkVisited(neighbor);
                bfsQueue.Enqueue(neighbor);
            });
        }

        nodeScratch.Clear();
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                MarbleNode node = grid[row, column];
                if (node != null && node.InGrid && !IsVisited(node)) nodeScratch.Add(node);
            }

        int releaseCount = nodeScratch.Count;
        for (int i = 0; i < releaseCount; i++) ReleaseUnsupportedNode(nodeScratch[i]);
        return releaseCount;
    }

    private void ClearMatchedNodes(List<MarbleNode> matchedNodes)
    {
        int count = matchedNodes.Count;
        for (int i = 0; i < count; i++)
        {
            MarbleNode node = matchedNodes[i];
            if (node != null && node.InGrid) RemoveNodeFromGraph(node);
        }

        for (int i = 0; i < count; i++)
        {
            MarbleNode node = matchedNodes[i];
            Rigidbody body = node?.Body;
            if (body == null) continue;
            MarbleMatched?.Invoke(body);
            if (autoDisableMatchedMarbles && body.gameObject.activeSelf) body.gameObject.SetActive(false);
        }
    }

    private void ReleaseUnsupportedNode(MarbleNode node)
    {
        if (node == null || !node.InGrid) return;
        Rigidbody body = node.Body;
        RemoveNodeFromGraph(node);
        if (body == null) return;
        if (clearVelocityOnRelease)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.detectCollisions = true;
        body.interpolation = fallingInterpolation;
        body.collisionDetectionMode = fallingCollisionDetection;
        body.useGravity = enableGravityOnRelease;
        body.isKinematic = false;
        body.WakeUp();
        MarbleReleased?.Invoke(body);
    }

    private static void ConfigureAsAttached(Rigidbody body)
    {
        if (body == null) return;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
        body.Sleep();
    }

    private void RemoveNodeFromGraph(MarbleNode node)
    {
        if (node == null || !node.InGrid) return;
        if (IsInsideGrid(node.Row, node.Column) && grid[node.Row, node.Column] == node)
            grid[node.Row, node.Column] = null;
        node.InGrid = false;
        if (node.Body != null) nodeByBody.Remove(node.Body);
    }

    private void VisitNeighbors(int row, int column, Action<MarbleNode> visitor)
    {
        switch (neighborLayout)
        {
            case NeighborLayout.HexOddRowOffset:
                VisitHexNeighbors(row, column, true, visitor);
                break;
            case NeighborLayout.HexEvenRowOffset:
                VisitHexNeighbors(row, column, false, visitor);
                break;
            case NeighborLayout.Orthogonal4:
                VisitCell(row - 1, column, visitor);
                VisitCell(row + 1, column, visitor);
                VisitCell(row, column - 1, visitor);
                VisitCell(row, column + 1, visitor);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void VisitHexNeighbors(int row, int column, bool oddRowsShiftRight, Action<MarbleNode> visitor)
    {
        VisitCell(row, column - 1, visitor);
        VisitCell(row, column + 1, visitor);
        bool rowIsOdd = (row & 1) != 0;
        bool shiftedRight = oddRowsShiftRight ? rowIsOdd : !rowIsOdd;

        if (shiftedRight)
        {
            VisitCell(row - 1, column, visitor);
            VisitCell(row - 1, column + 1, visitor);
            VisitCell(row + 1, column, visitor);
            VisitCell(row + 1, column + 1, visitor);
        }
        else
        {
            VisitCell(row - 1, column - 1, visitor);
            VisitCell(row - 1, column, visitor);
            VisitCell(row + 1, column - 1, visitor);
            VisitCell(row + 1, column, visitor);
        }
    }

    private void VisitCell(int row, int column, Action<MarbleNode> visitor)
    {
        if (IsInsideGrid(row, column)) visitor(grid[row, column]);
    }

    public void GetNeighborBodies(Rigidbody body, List<Rigidbody> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        if (body == null || !nodeByBody.TryGetValue(body, out MarbleNode node)) return;
        VisitNeighbors(node.Row, node.Column, neighbor =>
        {
            if (neighbor != null && neighbor.InGrid && neighbor.Body != null) results.Add(neighbor.Body);
        });
    }

    public bool TryGetMarble(int row, int column, out Rigidbody body)
    {
        EnsureInitialized();
        body = null;
        if (!IsInsideGrid(row, column)) return false;
        MarbleNode node = grid[row, column];
        if (node == null || !node.InGrid || node.Body == null) return false;
        body = node.Body;
        return true;
    }

    public bool TryGetGridPosition(Rigidbody body, out int row, out int column)
    {
        EnsureInitialized();
        row = -1;
        column = -1;
        if (body == null || !nodeByBody.TryGetValue(body, out MarbleNode node) || !node.InGrid) return false;
        row = node.Row;
        column = node.Column;
        return true;
    }

    public bool IsCellOccupied(int row, int column)
    {
        EnsureInitialized();
        if (!IsInsideGrid(row, column)) return false;
        MarbleNode node = grid[row, column];
        return node != null && node.InGrid;
    }

    public int AttachedMarbleCount => nodeByBody.Count;
    public int Rows => rows;
    public int Columns => columns;
    public int MinimumMatchCount => minimumMatchCount;

    public bool SetExplicitAnchor(Rigidbody body, bool isAnchor)
    {
        EnsureInitialized();
        if (body == null || !nodeByBody.TryGetValue(body, out MarbleNode node)) return false;
        node.ExplicitAnchor = isAnchor;
        return true;
    }

    private bool IsAnchor(MarbleNode node) => node.ExplicitAnchor || node.Row < ceilingAnchorRows;

    private void BeginVisitPass()
    {
        if (visitGeneration == int.MaxValue)
        {
            Array.Clear(visitMarks, 0, visitMarks.Length);
            visitGeneration = 1;
            return;
        }
        visitGeneration++;
    }

    private bool IsVisited(MarbleNode node) => visitMarks[node.Row, node.Column] == visitGeneration;
    private void MarkVisited(MarbleNode node) => visitMarks[node.Row, node.Column] = visitGeneration;
    private bool IsInsideGrid(int row, int column) => row >= 0 && row < rows && column >= 0 && column < columns;
    private void EnsureInitialized() { if (!initialized) Initialize(); }
}
