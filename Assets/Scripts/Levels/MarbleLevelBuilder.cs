using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MarbleLevelBuilder : MonoBehaviour
{
    public enum LevelSource { Texture, IntegerArray }

    [Serializable]
    public sealed class PixelMatchMapping
    {
        public Color32 pixelColor = Color.white;
        public int matchId;
    }

    private struct SpawnCell
    {
        public int row, column, matchId;
        public SpawnCell(int r, int c, int id) { row = r; column = c; matchId = id; }
    }

    [Header("Core")]
    [SerializeField] private MarbleSupportGraph supportGraph;
    [SerializeField] private MarbleBasketsAndPool marblePool;
    [SerializeField] private MarbleShooter shooter;
    [SerializeField] private Transform gridOrigin;
    [SerializeField] private Transform boardRoot;

    [Header("Source")]
    [SerializeField] private LevelSource levelSource = LevelSource.Texture;
    [SerializeField] private Texture2D levelTexture;
    [SerializeField] private bool textureTopIsBoardTop = true;
    [SerializeField] private int emptyIntegerValue = -1;

    [Header("Texture Mapping")]
    [SerializeField] private List<PixelMatchMapping> pixelMappings = new List<PixelMatchMapping>();
    [SerializeField, Range(0, 255)] private int transparentAlphaThreshold = 5;
    [SerializeField, Range(0, 32)] private int blackEmptyThreshold = 4;
    [SerializeField, Range(0, 32)] private int colorMatchTolerance = 2;

    [Header("Styles")]
    [SerializeField] private List<MarbleShooter.MarbleStyle> marbleStyles = new List<MarbleShooter.MarbleStyle>();

    [Header("Hex Grid - Must Match Shooter")]
    [SerializeField] private MarbleShooter.HexOffsetLayout hexLayout = MarbleShooter.HexOffsetLayout.OddRowsShiftRight;
    [SerializeField, Min(0.001f)] private float horizontalSpacing = 1f;
    [SerializeField, Min(0.001f)] private float verticalSpacing = 0.8660254f;

    public event Action LevelBuildStarted;
    public event Action<int> LevelBuilt;
    public event Action BoardCleared;

    private int[,] runtimeLayout;
    private readonly HashSet<Rigidbody> activeLevelMarbles = new HashSet<Rigidbody>();
    private readonly List<Rigidbody> clearScratch = new List<Rigidbody>(512);
    private readonly List<SpawnCell> spawnScratch = new List<SpawnCell>(1024);
    private readonly Dictionary<int, Material> materialByMatchId = new Dictionary<int, Material>(16);
    private bool clearing;

    public int ActiveLevelMarbleCount => activeLevelMarbles.Count;

    private void Awake()
    {
        if (boardRoot == null)
        {
            GameObject go = new GameObject("Marble Level Board");
            go.transform.SetParent(transform, false);
            boardRoot = go.transform;
        }
    }

    private void OnEnable()
    {
        if (shooter != null) shooter.MarbleAttached += OnShooterMarbleAttached;
        if (marblePool != null) marblePool.MarbleReturnedToPool += OnMarbleReturnedToPool;
    }

    private void OnDisable()
    {
        if (shooter != null) shooter.MarbleAttached -= OnShooterMarbleAttached;
        if (marblePool != null) marblePool.MarbleReturnedToPool -= OnMarbleReturnedToPool;
    }

    public void SetIntegerLayout(int[,] layout)
    {
        if (layout == null) { runtimeLayout = null; return; }
        runtimeLayout = (int[,])layout.Clone();
        levelSource = LevelSource.IntegerArray;
    }

    public void SetTextureLevel(Texture2D texture)
    {
        levelTexture = texture;
        levelSource = LevelSource.Texture;
    }

    public bool BuildLevel() => levelSource == LevelSource.Texture ? BuildLevel(levelTexture) : BuildLevel(runtimeLayout);

    public bool BuildLevel(Texture2D texture)
    {
        if (texture == null || !BuildStyleCache()) return false;
        spawnScratch.Clear();

        Color32[] pixels;
        try { pixels = texture.GetPixels32(); }
        catch (UnityException e)
        {
            Debug.LogError($"Texture {texture.name} must have Read/Write enabled. {e.Message}", texture);
            return false;
        }

        if (!ValidateDimensions(texture.height, texture.width)) return false;
        for (int row = 0; row < texture.height; row++)
        {
            int y = textureTopIsBoardTop ? texture.height - 1 - row : row;
            for (int col = 0; col < texture.width; col++)
            {
                Color32 px = pixels[y * texture.width + col];
                if (IsEmptyPixel(px)) continue;
                if (!TryMapPixel(px, out int id) || !materialByMatchId.ContainsKey(id))
                {
                    Debug.LogError($"Unmapped level pixel at [{row},{col}] = ({px.r},{px.g},{px.b},{px.a}).", texture);
                    return false;
                }
                spawnScratch.Add(new SpawnCell(row, col, id));
            }
        }

        return SpawnPlan();
    }

    public bool BuildLevel(int[,] layout)
    {
        if (layout == null || !BuildStyleCache()) return false;
        runtimeLayout = (int[,])layout.Clone();
        levelSource = LevelSource.IntegerArray;
        spawnScratch.Clear();

        int rows = layout.GetLength(0), cols = layout.GetLength(1);
        if (!ValidateDimensions(rows, cols)) return false;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int id = layout[r, c];
                if (id == emptyIntegerValue) continue;
                if (!materialByMatchId.ContainsKey(id))
                {
                    Debug.LogError($"No active MarbleStyle for match ID {id} at [{r},{c}].", this);
                    return false;
                }
                spawnScratch.Add(new SpawnCell(r, c, id));
            }

        return SpawnPlan();
    }

    private bool SpawnPlan()
    {
        LevelBuildStarted?.Invoke();
        ClearBoard();

        for (int i = 0; i < spawnScratch.Count; i++)
        {
            SpawnCell cell = spawnScratch[i];
            Rigidbody body = marblePool.RentMarble(GetCellWorldPosition(cell.row, cell.column), gridOrigin.rotation, boardRoot);
            if (body == null) { ClearBoard(); return false; }

            ApplyMaterial(body, materialByMatchId[cell.matchId]);
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
            body.detectCollisions = true;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            SetColliders(body, true);
            activeLevelMarbles.Add(body);

            if (!supportGraph.RegisterMarble(body, cell.row, cell.column, cell.matchId, cell.row == 0))
            {
                ClearBoard();
                return false;
            }
        }

        LevelBuilt?.Invoke(activeLevelMarbles.Count);
        return true;
    }

    public void ClearBoard()
    {
        if (clearing || supportGraph == null || marblePool == null) return;
        clearing = true;
        clearScratch.Clear();
        foreach (Rigidbody body in activeLevelMarbles) if (body != null) clearScratch.Add(body);
        supportGraph.ResetGraph();
        for (int i = 0; i < clearScratch.Count; i++) marblePool.ReturnMarble(clearScratch[i]);
        activeLevelMarbles.Clear();
        clearScratch.Clear();
        clearing = false;
        BoardCleared?.Invoke();
    }

    public Vector3 GetCellWorldPosition(int row, int column)
    {
        bool shiftedRight = hexLayout == MarbleShooter.HexOffsetLayout.OddRowsShiftRight ? (row & 1) != 0 : (row & 1) == 0;
        float x = column * horizontalSpacing + (shiftedRight ? horizontalSpacing * 0.5f : 0f);
        float y = row * verticalSpacing;
        return gridOrigin.position + gridOrigin.right * x - gridOrigin.up * y;
    }

    private bool BuildStyleCache()
    {
        materialByMatchId.Clear();
        for (int i = 0; i < marbleStyles.Count; i++)
        {
            MarbleShooter.MarbleStyle style = marbleStyles[i];
            if (style == null || !style.active || style.material == null) continue;
            if (materialByMatchId.ContainsKey(style.matchId))
            {
                Debug.LogError($"Duplicate active match ID {style.matchId}.", this);
                return false;
            }
            materialByMatchId.Add(style.matchId, style.material);
        }
        return materialByMatchId.Count > 0;
    }

    private bool TryMapPixel(Color32 pixel, out int matchId)
    {
        int tolerance2 = colorMatchTolerance * colorMatchTolerance;
        for (int i = 0; i < pixelMappings.Count; i++)
        {
            PixelMatchMapping m = pixelMappings[i];
            if (m == null) continue;
            int dr = pixel.r - m.pixelColor.r;
            int dg = pixel.g - m.pixelColor.g;
            int db = pixel.b - m.pixelColor.b;
            if (dr * dr + dg * dg + db * db <= tolerance2)
            {
                matchId = m.matchId;
                return true;
            }
        }
        matchId = -1;
        return false;
    }

    private bool IsEmptyPixel(Color32 p)
    {
        return p.a <= transparentAlphaThreshold || (p.r <= blackEmptyThreshold && p.g <= blackEmptyThreshold && p.b <= blackEmptyThreshold);
    }

    private bool ValidateDimensions(int rows, int cols)
    {
        if (supportGraph == null || gridOrigin == null) return false;
        if (rows > supportGraph.Rows || cols > supportGraph.Columns)
        {
            Debug.LogError($"Level {rows}x{cols} exceeds graph {supportGraph.Rows}x{supportGraph.Columns}.", this);
            return false;
        }
        return rows > 0 && cols > 0;
    }

    private void OnShooterMarbleAttached(Rigidbody body, int matchId, int row, int column)
    {
        if (body == null || !supportGraph.TryGetGridPosition(body, out _, out _)) return;
        activeLevelMarbles.Add(body);
        if (boardRoot != null) body.transform.SetParent(boardRoot, true);
    }

    private void OnMarbleReturnedToPool(Rigidbody body)
    {
        if (body != null) activeLevelMarbles.Remove(body);
    }

    private static void ApplyMaterial(Rigidbody body, Material material)
    {
        Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = material;
    }

    private static void SetColliders(Rigidbody body, bool enabled)
    {
        Collider[] colliders = body.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = enabled;
    }
}
