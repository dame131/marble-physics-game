using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class GameManager : MonoBehaviour
{
    public enum GameState { Booting, Loading, Playing, Won, Lost, Complete, Error }

    [Serializable]
    public sealed class TextureLevel
    {
        public string levelName = "Level";
        public Texture2D layout;
        [Min(1)] public int startingShots = 30;
    }

    [Header("Core Systems")]
    [SerializeField] private MarbleSupportGraph supportGraph;
    [SerializeField] private MarbleShooter shooter;
    [SerializeField] private MarbleBasketsAndPool basketsAndPool;
    [SerializeField] private MarblePixyDust pixyDust;
    [SerializeField] private MarbleLevelBuilder levelBuilder;

    [Header("Levels")]
    [SerializeField] private List<TextureLevel> levels = new List<TextureLevel>();
    [SerializeField, Min(0)] private int startingLevelIndex = 0;

    [Header("Flow")]
    [SerializeField] private bool loadFirstLevelOnStart = true;
    [SerializeField] private bool resetScoreOnNewGame = true;
    [SerializeField] private bool autoAdvanceOnWin = true;
    [SerializeField, Min(0f)] private float winTransitionDelay = 1.5f;
    [SerializeField] private bool loopLevelsAfterFinalStage = false;

    public event Action<int> OnScoreChanged;
    public event Action<int> OnShotsChanged;
    public event Action OnLevelWon;
    public event Action OnLevelLost;
    public event Action<int> OnLevelChanged;
    public event Action<GameState> OnGameStateChanged;

    private GameState state = GameState.Booting;
    private int currentLevelIndex = -1;
    private int shotsRemaining;
    private int currentScore;
    private bool shotResolutionPending;
    private Rigidbody activeProjectile;
    private bool outcomeCheckRequested;
    private Coroutine transitionRoutine;
    private int[,] runtimeLevelLayout;
    private int runtimeLevelShots;

    public GameState State => state;
    public int CurrentLevelIndex => currentLevelIndex;
    public int ShotsRemaining => shotsRemaining;
    public int CurrentScore => currentScore;
    public int RemainingAttachedMarbles => supportGraph != null ? supportGraph.AttachedMarbleCount : 0;
    public bool IsPlaying => state == GameState.Playing;

    private void Awake()
    {
        if (supportGraph != null) supportGraph.Initialize();
        if (basketsAndPool != null) currentScore = basketsAndPool.TotalScore;
    }

    private void OnEnable()
    {
        if (shooter != null)
        {
            shooter.MarbleFired += OnMarbleFired;
            shooter.MarbleAttached += OnMarbleAttached;
        }
        if (basketsAndPool != null)
        {
            basketsAndPool.ScoreChanged += OnPoolScoreChanged;
            basketsAndPool.MarbleReturnedToPool += OnMarbleReturnedToPool;
        }
        if (supportGraph != null)
        {
            supportGraph.MarbleMatched += OnGraphBodyChanged;
            supportGraph.MarbleReleased += OnGraphBodyChanged;
            supportGraph.MatchCleared += OnGraphCountChanged;
            supportGraph.UnsupportedMarblesReleased += OnGraphCountChanged;
        }
        if (levelBuilder != null)
        {
            levelBuilder.LevelBuilt += OnLevelBuilt;
            levelBuilder.BoardCleared += RequestOutcomeCheck;
        }
    }

    private void OnDisable()
    {
        if (shooter != null)
        {
            shooter.MarbleFired -= OnMarbleFired;
            shooter.MarbleAttached -= OnMarbleAttached;
        }
        if (basketsAndPool != null)
        {
            basketsAndPool.ScoreChanged -= OnPoolScoreChanged;
            basketsAndPool.MarbleReturnedToPool -= OnMarbleReturnedToPool;
        }
        if (supportGraph != null)
        {
            supportGraph.MarbleMatched -= OnGraphBodyChanged;
            supportGraph.MarbleReleased -= OnGraphBodyChanged;
            supportGraph.MatchCleared -= OnGraphCountChanged;
            supportGraph.UnsupportedMarblesReleased -= OnGraphCountChanged;
        }
        if (levelBuilder != null)
        {
            levelBuilder.LevelBuilt -= OnLevelBuilt;
            levelBuilder.BoardCleared -= RequestOutcomeCheck;
        }
    }

    private void Start()
    {
        if (resetScoreOnNewGame && basketsAndPool != null) basketsAndPool.ResetScore();
        currentScore = basketsAndPool != null ? basketsAndPool.TotalScore : 0;
        OnScoreChanged?.Invoke(currentScore);
        OnShotsChanged?.Invoke(shotsRemaining);

        if (loadFirstLevelOnStart && levels.Count > 0)
            LoadLevel(Mathf.Clamp(startingLevelIndex, 0, levels.Count - 1));
    }

    private void LateUpdate()
    {
        if (!outcomeCheckRequested) return;
        outcomeCheckRequested = false;
        EvaluateOutcome();
    }

    private void OnMarbleFired(Rigidbody body, int matchId)
    {
        if (state != GameState.Playing || shotsRemaining <= 0) return;
        shotsRemaining--;
        activeProjectile = body;
        shotResolutionPending = true;
        OnShotsChanged?.Invoke(shotsRemaining);
    }

    private void OnMarbleAttached(Rigidbody body, int matchId, int row, int column)
    {
        if (activeProjectile == body) activeProjectile = null;
        shotResolutionPending = false;
        if (shotsRemaining <= 0 && shooter != null) shooter.enabled = false;
        RequestOutcomeCheck();
    }

    private void OnMarbleReturnedToPool(Rigidbody body)
    {
        if (activeProjectile == body)
        {
            activeProjectile = null;
            shotResolutionPending = false;
        }
        RequestOutcomeCheck();
    }

    private void OnPoolScoreChanged(int score)
    {
        currentScore = score;
        OnScoreChanged?.Invoke(score);
    }

    private void OnGraphBodyChanged(Rigidbody body) => RequestOutcomeCheck();
    private void OnGraphCountChanged(int count) => RequestOutcomeCheck();
    private void OnLevelBuilt(int count) => RequestOutcomeCheck();
    private void RequestOutcomeCheck() => outcomeCheckRequested = true;

    private void EvaluateOutcome()
    {
        if (state != GameState.Playing || shotResolutionPending || supportGraph == null) return;
        int remaining = supportGraph.AttachedMarbleCount;
        if (remaining <= 0) WinLevel();
        else if (shotsRemaining <= 0) LoseLevel();
    }

    private void WinLevel()
    {
        if (state != GameState.Playing) return;
        SetState(GameState.Won);
        if (shooter != null) shooter.enabled = false;
        OnLevelWon?.Invoke();
        if (autoAdvanceOnWin)
        {
            if (transitionRoutine != null) StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(AdvanceRoutine());
        }
    }

    private IEnumerator AdvanceRoutine()
    {
        float elapsed = 0f;
        while (elapsed < winTransitionDelay)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        transitionRoutine = null;
        NextLevel();
    }

    private void LoseLevel()
    {
        if (state != GameState.Playing) return;
        SetState(GameState.Lost);
        if (shooter != null) shooter.enabled = false;
        OnLevelLost?.Invoke();
    }

    public bool LoadLevel(int levelIndex)
    {
        if (shotResolutionPending || levelIndex < 0 || levelIndex >= levels.Count) return false;
        TextureLevel level = levels[levelIndex];
        if (level == null || level.layout == null || levelBuilder == null) return false;

        StopTransition();
        SetState(GameState.Loading);
        if (shooter != null) shooter.enabled = false;
        shotResolutionPending = false;
        activeProjectile = null;
        outcomeCheckRequested = false;

        if (!levelBuilder.BuildLevel(level.layout)) { SetState(GameState.Error); return false; }

        runtimeLevelLayout = null;
        currentLevelIndex = levelIndex;
        SetShots(level.startingShots);
        SetState(GameState.Playing);
        if (shooter != null) shooter.enabled = true;
        OnLevelChanged?.Invoke(currentLevelIndex);
        RequestOutcomeCheck();
        return true;
    }

    public bool LoadRuntimeLevel(int[,] layout, int startingShots)
    {
        if (layout == null || startingShots <= 0 || shotResolutionPending || levelBuilder == null) return false;

        StopTransition();
        SetState(GameState.Loading);
        if (shooter != null) shooter.enabled = false;
        shotResolutionPending = false;
        activeProjectile = null;
        outcomeCheckRequested = false;
        runtimeLevelLayout = (int[,])layout.Clone();
        runtimeLevelShots = startingShots;

        if (!levelBuilder.BuildLevel(runtimeLevelLayout)) { SetState(GameState.Error); return false; }

        currentLevelIndex = -1;
        SetShots(startingShots);
        SetState(GameState.Playing);
        if (shooter != null) shooter.enabled = true;
        OnLevelChanged?.Invoke(-1);
        RequestOutcomeCheck();
        return true;
    }

    public bool RetryLevel()
    {
        if (shotResolutionPending) return false;
        if (currentLevelIndex >= 0) return LoadLevel(currentLevelIndex);
        return runtimeLevelLayout != null && LoadRuntimeLevel((int[,])runtimeLevelLayout.Clone(), runtimeLevelShots);
    }

    public bool NextLevel()
    {
        StopTransition();
        if (currentLevelIndex < 0) { SetState(GameState.Complete); return false; }
        int next = currentLevelIndex + 1;
        if (next < levels.Count) return LoadLevel(next);
        if (loopLevelsAfterFinalStage && levels.Count > 0) return LoadLevel(0);
        SetState(GameState.Complete);
        if (shooter != null) shooter.enabled = false;
        return false;
    }

    public bool StartNewGame()
    {
        StopTransition();
        if (basketsAndPool != null) basketsAndPool.ResetScore();
        currentScore = basketsAndPool != null ? basketsAndPool.TotalScore : 0;
        OnScoreChanged?.Invoke(currentScore);
        return levels.Count > 0 && LoadLevel(Mathf.Clamp(startingLevelIndex, 0, levels.Count - 1));
    }

    private void SetShots(int value)
    {
        shotsRemaining = Mathf.Max(0, value);
        OnShotsChanged?.Invoke(shotsRemaining);
    }

    private void SetState(GameState newState)
    {
        if (state == newState) return;
        state = newState;
        OnGameStateChanged?.Invoke(state);
    }

    private void StopTransition()
    {
        if (transitionRoutine == null) return;
        StopCoroutine(transitionRoutine);
        transitionRoutine = null;
    }
}
