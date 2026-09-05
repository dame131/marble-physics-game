using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class MarbleShooter : MonoBehaviour
{
    public enum HexOffsetLayout { OddRowsShiftRight, EvenRowsShiftRight }

    [Serializable]
    public sealed class MarbleStyle
    {
        public int matchId;
        public bool active = true;
        public Material material;
    }

    private sealed class LoadedMarble
    {
        public Rigidbody body;
        public int matchId;
    }

    [Header("Core")]
    [SerializeField] private MarbleSupportGraph supportGraph;
    [SerializeField] private MarbleBasketsAndPool marblePool;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Transform launchOrigin;
    [SerializeField] private Transform alternateMarbleAnchor;
    [SerializeField] private Transform gridOrigin;

    [Header("Styles")]
    [SerializeField] private List<MarbleStyle> marbleStyles = new List<MarbleStyle>();

    [Header("Hex Grid")]
    [SerializeField] private HexOffsetLayout hexLayout = HexOffsetLayout.OddRowsShiftRight;
    [SerializeField, Min(0.001f)] private float horizontalSpacing = 1f;
    [SerializeField, Min(0.001f)] private float verticalSpacing = 0.8660254f;

    [Header("Shot")]
    [SerializeField, Min(0.1f)] private float launchSpeed = 18f;
    [SerializeField, Range(0f, 1f)] private float minimumUpwardDot = 0.08f;
    [SerializeField] private bool allowSpacebarSwap = true;

    [Header("Trajectory")]
    [SerializeField] private LineRenderer trajectoryLine;
    [SerializeField] private Material trajectoryMaterial;
    [SerializeField, Range(0, 2)] private int maximumWallReflections = 2;
    [SerializeField, Min(1f)] private float trajectoryDistance = 50f;
    [SerializeField, Min(0.0001f)] private float raySurfaceOffset = 0.01f;
    [SerializeField, Min(0.001f)] private float trajectoryWidth = 0.035f;
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private LayerMask marbleMask;
    [SerializeField] private LayerMask ceilingMask;

    [Header("Swap")]
    [SerializeField, Min(0.01f)] private float swapDuration = 0.09f;
    [SerializeField] private AnimationCurve swapCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public event Action<Rigidbody, int> MarbleFired;
    public event Action<Rigidbody, int, int, int> MarbleAttached;
    public event Action MarbleSwapped;

    private LoadedMarble currentMarble;
    private LoadedMarble alternateMarble;
    private bool aiming;
    private bool swapping;
    private bool shotInFlight;
    private Coroutine swapRoutine;
    private readonly Vector3[] trajectoryPoints = new Vector3[5];
    private readonly Vector2Int[] neighborScratch = new Vector2Int[6];
    private static readonly Color LaserRed = new Color(1f, 0.02f, 0f, 1f);

    private void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        ConfigureTrajectory();
    }

    private void Start()
    {
        EnsureTwoMarblesLoaded();
        PositionLoadedMarbles();
    }

    private void Update()
    {
        if (allowSpacebarSwap && Input.GetKeyDown(KeyCode.Space)) SwapMarbles();
        HandleInput();
    }

    private void OnDisable()
    {
        aiming = false;
        HideTrajectory();
        if (swapRoutine != null) StopCoroutine(swapRoutine);
        swapRoutine = null;
        swapping = false;
    }

    private void HandleInput()
    {
        if (swapping || shotInFlight || currentMarble == null) return;

        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                if (IsPointerOverUI(t.fingerId)) return;
                aiming = true;
                UpdateAim(t.position);
            }
            else if ((t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary) && aiming) UpdateAim(t.position);
            else if (t.phase == TouchPhase.Ended && aiming) ReleaseAim(t.position);
            else if (t.phase == TouchPhase.Canceled) CancelAim();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (IsPointerOverUI()) return;
            aiming = true;
            UpdateAim(Input.mousePosition);
        }
        if (Input.GetMouseButton(0) && aiming) UpdateAim(Input.mousePosition);
        if (Input.GetMouseButtonUp(0) && aiming) ReleaseAim(Input.mousePosition);
    }

    private void UpdateAim(Vector2 screenPosition)
    {
        if (TryGetAimDirection(screenPosition, out Vector3 direction)) DrawTrajectory(direction);
        else HideTrajectory();
    }

    private void ReleaseAim(Vector2 screenPosition)
    {
        if (!TryGetAimDirection(screenPosition, out Vector3 direction)) { CancelAim(); return; }
        aiming = false;
        HideTrajectory();
        Fire(direction);
    }

    private void CancelAim() { aiming = false; HideTrajectory(); }

    private bool TryGetAimDirection(Vector2 screenPosition, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (aimCamera == null || launchOrigin == null || gridOrigin == null) return false;

        Plane plane = new Plane(gridOrigin.forward, launchOrigin.position);
        Ray screenRay = aimCamera.ScreenPointToRay(screenPosition);
        if (!plane.Raycast(screenRay, out float enter)) return false;

        Vector3 raw = screenRay.GetPoint(enter) - launchOrigin.position;
        raw = Vector3.ProjectOnPlane(raw, gridOrigin.forward);
        if (raw.sqrMagnitude < 0.0001f) return false;
        raw.Normalize();
        if (Vector3.Dot(raw, launchOrigin.up) < minimumUpwardDot) return false;
        direction = raw;
        return true;
    }

    private void DrawTrajectory(Vector3 direction)
    {
        if (trajectoryLine == null) return;
        int mask = wallMask.value | marbleMask.value | ceilingMask.value;
        Vector3 origin = launchOrigin.position;
        float remaining = trajectoryDistance;
        int pointCount = 1;
        int reflections = 0;
        trajectoryPoints[0] = origin;

        while (pointCount < trajectoryPoints.Length)
        {
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, remaining, mask, QueryTriggerInteraction.Ignore))
            {
                trajectoryPoints[pointCount++] = origin + direction * remaining;
                break;
            }

            trajectoryPoints[pointCount++] = hit.point;
            remaining -= hit.distance;
            int layer = hit.collider.gameObject.layer;
            if (LayerInMask(layer, marbleMask) || LayerInMask(layer, ceilingMask)) break;
            if (!LayerInMask(layer, wallMask) || reflections >= maximumWallReflections) break;

            direction = Vector3.Reflect(direction, hit.normal).normalized;
            origin = hit.point + direction * raySurfaceOffset;
            reflections++;
        }

        trajectoryLine.positionCount = pointCount;
        for (int i = 0; i < pointCount; i++) trajectoryLine.SetPosition(i, trajectoryPoints[i]);
        trajectoryLine.enabled = true;
    }

    private void ConfigureTrajectory()
    {
        if (trajectoryLine == null) return;
        trajectoryLine.useWorldSpace = true;
        trajectoryLine.startWidth = trajectoryWidth;
        trajectoryLine.endWidth = trajectoryWidth;
        trajectoryLine.startColor = LaserRed;
        trajectoryLine.endColor = LaserRed;
        trajectoryLine.numCapVertices = 4;
        trajectoryLine.numCornerVertices = 4;
        if (trajectoryMaterial != null) trajectoryLine.sharedMaterial = trajectoryMaterial;
        HideTrajectory();
    }

    private void HideTrajectory()
    {
        if (trajectoryLine == null) return;
        trajectoryLine.enabled = false;
        trajectoryLine.positionCount = 0;
    }

    private void Fire(Vector3 direction)
    {
        if (shotInFlight || currentMarble == null || currentMarble.body == null) return;
        LoadedMarble projectile = currentMarble;
        currentMarble = null;
        Rigidbody body = projectile.body;
        shotInFlight = true;

        body.transform.SetParent(null, true);
        body.transform.SetPositionAndRotation(launchOrigin.position, launchOrigin.rotation);
        SetColliders(body, true);
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.detectCollisions = true;
        body.useGravity = false;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        MarbleProjectileRelay relay = body.GetComponent<MarbleProjectileRelay>();
        if (relay == null) relay = body.gameObject.AddComponent<MarbleProjectileRelay>();
        relay.Initialize(this, projectile.matchId);

        body.velocity = direction.normalized * launchSpeed;
        body.WakeUp();
        MarbleFired?.Invoke(body, projectile.matchId);
        AdvanceLoadedMarbles();
    }

    internal void HandleProjectileCollision(MarbleProjectileRelay relay, Collision collision)
    {
        if (relay == null || relay.HasSettled || collision == null) return;
        Collider other = collision.collider;
        int layer = other.gameObject.layer;
        bool hitMarble = LayerInMask(layer, marbleMask);
        bool hitCeiling = LayerInMask(layer, ceilingMask);
        if (!hitMarble && !hitCeiling) return;

        Rigidbody projectile = relay.Body;
        int row = -1, column = -1;
        bool found = false;

        if (hitMarble && other.attachedRigidbody != null &&
            supportGraph.TryGetGridPosition(other.attachedRigidbody, out int hitRow, out int hitColumn))
            found = TryClosestOpenNeighbor(hitRow, hitColumn, projectile.position, out row, out column);

        if (!found && hitCeiling) found = TryNearestOpenTop(projectile.position, out row, out column);
        if (!found) found = TryNearestAttachable(projectile.position, out row, out column);
        if (!found) return;

        relay.MarkSettled();
        AttachProjectile(projectile, relay.MatchId, row, column);
    }

    private void AttachProjectile(Rigidbody body, int matchId, int row, int column)
    {
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.position = GetCellWorldPosition(row, column);
        SetColliders(body, true);

        bool matched = supportGraph.AttachMarbleAndResolve(body, row, column, matchId);
        bool attached = supportGraph.TryGetGridPosition(body, out _, out _);
        if (!matched && !attached)
        {
            marblePool.ReturnMarble(body);
            shotInFlight = false;
            EnsureTwoMarblesLoaded();
            return;
        }

        MarbleAttached?.Invoke(body, matchId, row, column);
        shotInFlight = false;
        EnsureTwoMarblesLoaded();
    }

    public void SwapMarbles()
    {
        if (swapping || shotInFlight || aiming || currentMarble?.body == null || alternateMarble?.body == null) return;
        if (swapRoutine != null) StopCoroutine(swapRoutine);
        swapRoutine = StartCoroutine(SwapRoutine());
    }

    private IEnumerator SwapRoutine()
    {
        swapping = true;
        LoadedMarble oldCurrent = currentMarble;
        LoadedMarble oldAlternate = alternateMarble;
        currentMarble = oldAlternate;
        alternateMarble = oldCurrent;

        Vector3 a0 = oldCurrent.body.position;
        Vector3 b0 = oldAlternate.body.position;
        float elapsed = 0f;
        while (elapsed < swapDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = swapCurve.Evaluate(Mathf.Clamp01(elapsed / swapDuration));
            oldCurrent.body.position = Vector3.Lerp(a0, alternateMarbleAnchor.position, t);
            oldAlternate.body.position = Vector3.Lerp(b0, launchOrigin.position, t);
            yield return null;
        }

        PositionLoadedMarbles();
        swapping = false;
        swapRoutine = null;
        MarbleSwapped?.Invoke();
    }

    private void AdvanceLoadedMarbles()
    {
        currentMarble = alternateMarble;
        alternateMarble = CreateRandomLoadedMarble();
        PositionLoadedMarbles();
    }

    private void EnsureTwoMarblesLoaded()
    {
        if (currentMarble?.body == null) currentMarble = CreateRandomLoadedMarble();
        if (alternateMarble?.body == null) alternateMarble = CreateRandomLoadedMarble();
    }

    private LoadedMarble CreateRandomLoadedMarble()
    {
        MarbleStyle style = GetRandomActiveStyle();
        if (style == null || marblePool == null || launchOrigin == null) return null;
        Rigidbody body = marblePool.RentMarble(launchOrigin.position, launchOrigin.rotation);
        if (body == null) return null;
        ApplyStyle(body, style);
        PrepareForLauncher(body);
        return new LoadedMarble { body = body, matchId = style.matchId };
    }

    private MarbleStyle GetRandomActiveStyle()
    {
        int count = 0;
        for (int i = 0; i < marbleStyles.Count; i++) if (marbleStyles[i] != null && marbleStyles[i].active) count++;
        if (count == 0) return null;
        int pick = UnityEngine.Random.Range(0, count);
        for (int i = 0; i < marbleStyles.Count; i++)
        {
            MarbleStyle s = marbleStyles[i];
            if (s == null || !s.active) continue;
            if (pick-- == 0) return s;
        }
        return null;
    }

    private static void ApplyStyle(Rigidbody body, MarbleStyle style)
    {
        if (style.material == null) return;
        Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = style.material;
    }

    private static void PrepareForLauncher(Rigidbody body)
    {
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
        body.detectCollisions = false;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        SetColliders(body, false);
        body.Sleep();
    }

    private void PositionLoadedMarbles()
    {
        if (currentMarble?.body != null) currentMarble.body.transform.SetPositionAndRotation(launchOrigin.position, launchOrigin.rotation);
        if (alternateMarble?.body != null) alternateMarble.body.transform.SetPositionAndRotation(alternateMarbleAnchor.position, alternateMarbleAnchor.rotation);
    }

    private bool TryClosestOpenNeighbor(int centerRow, int centerColumn, Vector3 position, out int resultRow, out int resultColumn)
    {
        resultRow = resultColumn = -1;
        float best = float.PositiveInfinity;
        int count = FillNeighbors(centerRow, centerColumn);
        for (int i = 0; i < count; i++)
        {
            int r = neighborScratch[i].x, c = neighborScratch[i].y;
            if (!ValidCell(r, c) || supportGraph.IsCellOccupied(r, c)) continue;
            float d = (GetCellWorldPosition(r, c) - position).sqrMagnitude;
            if (d >= best) continue;
            best = d; resultRow = r; resultColumn = c;
        }
        return resultRow >= 0;
    }

    private bool TryNearestOpenTop(Vector3 position, out int resultRow, out int resultColumn)
    {
        resultRow = 0; resultColumn = -1;
        float best = float.PositiveInfinity;
        for (int c = 0; c < supportGraph.Columns; c++)
        {
            if (supportGraph.IsCellOccupied(0, c)) continue;
            float d = (GetCellWorldPosition(0, c) - position).sqrMagnitude;
            if (d < best) { best = d; resultColumn = c; }
        }
        return resultColumn >= 0;
    }

    private bool TryNearestAttachable(Vector3 position, out int resultRow, out int resultColumn)
    {
        resultRow = resultColumn = -1;
        float best = float.PositiveInfinity;
        for (int r = 0; r < supportGraph.Rows; r++)
            for (int c = 0; c < supportGraph.Columns; c++)
            {
                if (supportGraph.IsCellOccupied(r, c) || !CellCanAttach(r, c)) continue;
                float d = (GetCellWorldPosition(r, c) - position).sqrMagnitude;
                if (d < best) { best = d; resultRow = r; resultColumn = c; }
            }
        return resultRow >= 0;
    }

    private bool CellCanAttach(int row, int column)
    {
        if (row == 0) return true;
        int count = FillNeighbors(row, column);
        for (int i = 0; i < count; i++)
        {
            int r = neighborScratch[i].x, c = neighborScratch[i].y;
            if (ValidCell(r, c) && supportGraph.IsCellOccupied(r, c)) return true;
        }
        return false;
    }

    private int FillNeighbors(int row, int column)
    {
        bool shiftedRight = hexLayout == HexOffsetLayout.OddRowsShiftRight ? (row & 1) != 0 : (row & 1) == 0;
        int i = 0;
        neighborScratch[i++] = new Vector2Int(row, column - 1);
        neighborScratch[i++] = new Vector2Int(row, column + 1);
        if (shiftedRight)
        {
            neighborScratch[i++] = new Vector2Int(row - 1, column);
            neighborScratch[i++] = new Vector2Int(row - 1, column + 1);
            neighborScratch[i++] = new Vector2Int(row + 1, column);
            neighborScratch[i++] = new Vector2Int(row + 1, column + 1);
        }
        else
        {
            neighborScratch[i++] = new Vector2Int(row - 1, column - 1);
            neighborScratch[i++] = new Vector2Int(row - 1, column);
            neighborScratch[i++] = new Vector2Int(row + 1, column - 1);
            neighborScratch[i++] = new Vector2Int(row + 1, column);
        }
        return i;
    }

    private Vector3 GetCellWorldPosition(int row, int column)
    {
        bool shiftedRight = hexLayout == HexOffsetLayout.OddRowsShiftRight ? (row & 1) != 0 : (row & 1) == 0;
        float x = column * horizontalSpacing + (shiftedRight ? horizontalSpacing * 0.5f : 0f);
        float y = row * verticalSpacing;
        return gridOrigin.position + gridOrigin.right * x - gridOrigin.up * y;
    }

    private bool ValidCell(int r, int c) => r >= 0 && r < supportGraph.Rows && c >= 0 && c < supportGraph.Columns;
    private static bool LayerInMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;

    private static void SetColliders(Rigidbody body, bool enabled)
    {
        Collider[] colliders = body.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = enabled;
    }

    private static bool IsPointerOverUI(int fingerId = -1)
    {
        if (EventSystem.current == null) return false;
        return fingerId >= 0 ? EventSystem.current.IsPointerOverGameObject(fingerId) : EventSystem.current.IsPointerOverGameObject();
    }
}

[DisallowMultipleComponent]
public sealed class MarbleProjectileRelay : MonoBehaviour
{
    private MarbleShooter owner;
    public int MatchId { get; private set; }
    public bool HasSettled { get; private set; }
    public Rigidbody Body { get; private set; }

    public void Initialize(MarbleShooter shooter, int matchId)
    {
        owner = shooter;
        MatchId = matchId;
        Body = GetComponent<Rigidbody>();
        HasSettled = false;
    }

    public void MarkSettled() => HasSettled = true;

    private void OnCollisionEnter(Collision collision)
    {
        if (owner != null && !HasSettled) owner.HandleProjectileCollision(this, collision);
    }
}
