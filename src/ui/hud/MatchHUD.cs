using Godot;
using FPSGame.Gamemodes.TDM;
using FPSGame.Entities.Player.Controllers;
using FPSGame.UI.HUD.Minimap;
using FPSGame.UI.HUD.Killfeed;
using FPSGame.UI.HUD.Compass;
using FPSGame.UI.HUD.Reticle;
using FPSGame.UI.HUD.Scorestreaks;

namespace FPSGame.UI.HUD;

/// <summary>
/// Master HUD controller.
/// Drives the tactical minimap, compass ribbon, kill feed, health status, weapon & ammo display,
/// segmented scorestreaks ladder, dynamic hitmarkers, and match announcements.
/// </summary>
public partial class MatchHUD : Control
{
    public static MatchHUD? Instance { get; private set; }

    private Label _clockLabel = null!;
    private Label _alphaScoreLabel = null!;
    private Label _bravoScoreLabel = null!;
    private Control _centerBannerContainer = null!;
    private Label _bannerTitleLabel = null!;
    private Label _bannerCountdownLabel = null!;
    private Label _fpsLabel = null!;

    // Health
    private Label _healthValueLabel = null!;
    private ProgressBar _healthProgressBar = null!;

    // Weapon & Ammo
    private Label _weaponNameLabel = null!;
    private Label _fireModeLabel = null!;
    private Label _ammoMagLabel = null!;
    private Label _ammoReserveLabel = null!;
    private Label _lethalLabel = null!;
    private Label _tacticalLabel = null!;

    // Modular Sub-Widgets
    public MinimapWidget Minimap { get; private set; } = null!;
    public KillFeedWidget KillFeed { get; private set; } = null!;
    public CompassWidget Compass { get; private set; } = null!;
    public ReticleWidget Reticle { get; private set; } = null!;
    public ScorestreakWidget Streaks { get; private set; } = null!;

    private float _fpsUpdateTimer;
    private const float FpsUpdateInterval = 0.25f;
    private int _lastFps = -1;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        _clockLabel = GetNode<Label>("%ClockLabel");
        _alphaScoreLabel = GetNode<Label>("%AlphaScoreLabel");
        _bravoScoreLabel = GetNode<Label>("%BravoScoreLabel");
        _centerBannerContainer = GetNode<Control>("%CenterBannerContainer");
        _bannerTitleLabel = GetNode<Label>("%BannerTitleLabel");
        _bannerCountdownLabel = GetNode<Label>("%BannerCountdownLabel");
        _fpsLabel = GetNode<Label>("%FpsLabel");

        _healthValueLabel = GetNode<Label>("%HealthValueLabel");
        _healthProgressBar = GetNode<ProgressBar>("%HealthProgressBar");

        _weaponNameLabel = GetNode<Label>("%WeaponNameLabel");
        _fireModeLabel = GetNode<Label>("%FireModeLabel");
        _ammoMagLabel = GetNode<Label>("%AmmoMagLabel");
        _ammoReserveLabel = GetNode<Label>("%AmmoReserveLabel");
        _lethalLabel = GetNode<Label>("%LethalLabel");
        _tacticalLabel = GetNode<Label>("%TacticalLabel");

        Minimap = GetNode<MinimapWidget>("%MinimapWidget");
        KillFeed = GetNode<KillFeedWidget>("%KillFeedWidget");
        Compass = GetNode<CompassWidget>("%CompassWidget");
        Reticle = GetNode<ReticleWidget>("%ReticleWidget");
        Streaks = GetNode<ScorestreakWidget>("%ScorestreaksWidget");

        _interactionContainer = GetNodeOrNull<Control>("%InteractionContainer");
        _interactionLabel = GetNodeOrNull<Label>("%InteractionLabel");
        _interactionProgressBar = GetNodeOrNull<ProgressBar>("%InteractionProgressBar");
        if (_interactionContainer != null) _interactionContainer.Visible = false;

        int initialFps = Mathf.RoundToInt((float)Engine.GetFramesPerSecond());
        _fpsLabel.Text = $"{initialFps} FPS";
        _lastFps = initialFps;

        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.StateChanged += OnMatchStateChanged;
            MatchManager.Instance.WarmupTick += OnWarmupTick;
            MatchManager.Instance.MatchTimerTick += OnMatchTimerTick;
            MatchManager.Instance.ScoresUpdated += OnScoresUpdated;
            MatchManager.Instance.MatchFinished += OnMatchFinished;

            OnMatchStateChanged(MatchManager.Instance.CurrentState);
            OnScoresUpdated(MatchManager.Instance.TeamAlphaKills, MatchManager.Instance.TeamBravoKills);
        }
    }

    public void BindLocalPlayer(PlayerController player)
    {
        Minimap.BindLocalPlayer(player);
        Compass.BindLocalPlayer(player);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.StateChanged -= OnMatchStateChanged;
            MatchManager.Instance.WarmupTick -= OnWarmupTick;
            MatchManager.Instance.MatchTimerTick -= OnMatchTimerTick;
            MatchManager.Instance.ScoresUpdated -= OnScoresUpdated;
            MatchManager.Instance.MatchFinished -= OnMatchFinished;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Throttled FPS polling (4 Hz)
        _fpsUpdateTimer += dt;
        if (_fpsUpdateTimer >= FpsUpdateInterval)
        {
            _fpsUpdateTimer = 0.0f;
            int currentFps = Mathf.RoundToInt((float)Engine.GetFramesPerSecond());
            if (currentFps != _lastFps)
            {
                _lastFps = currentFps;
                _fpsLabel.Text = $"{currentFps} FPS";
            }
        }

        if (_announcementTimer > 0.0f)
        {
            _announcementTimer -= dt;
            if (_announcementTimer <= 0.0f && MatchManager.Instance?.CurrentState == MatchState.InProgress)
            {
                _centerBannerContainer.Visible = false;
            }
        }
    }

    public void UpdateHealth(float current, float max)
    {
        int curHp = Mathf.Max(0, Mathf.RoundToInt(current));
        int maxHp = Mathf.RoundToInt(max);
        _healthValueLabel.Text = $"{curHp} / {maxHp}";
        _healthProgressBar.MaxValue = max;
        _healthProgressBar.Value = current;

        float ratio = current / max;
        if (ratio > 0.6f)
        {
            _healthValueLabel.Modulate = new Color(0.35f, 0.9f, 0.5f);
        }
        else if (ratio > 0.3f)
        {
            _healthValueLabel.Modulate = new Color(1.0f, 0.85f, 0.2f);
        }
        else
        {
            _healthValueLabel.Modulate = new Color(1.0f, 0.3f, 0.3f);
        }
    }

    public void UpdateCompass(float yawDegrees)
    {
        Compass.SetHeading(yawDegrees);
    }

    public void UpdateWeapon(string name, string fireMode, int currentMag, int reserveAmmo)
    {
        string displayName = name switch
        {
            "LMG" => "FN MINIMI",
            "MGL" => "M32 MGL",
            _ => name
        };
        _weaponNameLabel.Text = displayName;
        _fireModeLabel.Text = $"[V] {fireMode.ToUpperInvariant()}";
        _ammoMagLabel.Text = currentMag.ToString();
        _ammoReserveLabel.Text = reserveAmmo.ToString();
    }

    public void UpdateEquipment(int fragCount, int flashCount)
    {
        _tacticalLabel.Text = flashCount.ToString();
        _lethalLabel.Text = fragCount.ToString();
    }

    public void UpdateScorestreakProgress(int currentStreakKills)
    {
        Streaks.SetStreakKills(currentStreakKills);
    }

    public void UpdateScorestreakProgress(int currentStreakKills, int uavCount, int carePackageCount, int airstrikeCount, int bombingRunCount = 0)
    {
        Streaks.SetStreakData(currentStreakKills, uavCount, carePackageCount, airstrikeCount, bombingRunCount);
    }

    private Control? _interactionContainer;
    private Label? _interactionLabel;
    private ProgressBar? _interactionProgressBar;
    private float _announcementTimer;

    public void ShowInteractionPrompt(string text, float progress)
    {
        EnsureInteractionUI();
        if (_interactionContainer == null) return;

        _interactionContainer.Visible = true;
        if (_interactionLabel != null) _interactionLabel.Text = text;
        if (_interactionProgressBar != null) _interactionProgressBar.Value = Mathf.Clamp(progress * 100.0f, 0.0f, 100.0f);
    }

    public void HideInteractionPrompt()
    {
        if (_interactionContainer != null)
        {
            _interactionContainer.Visible = false;
        }
    }

    public void ShowAnnouncement(string message, float duration = 2.2f)
    {
        _bannerTitleLabel.Text = message;
        _bannerCountdownLabel.Text = "";
        _centerBannerContainer.Visible = true;
        _announcementTimer = duration;
    }

    private void EnsureInteractionUI()
    {
        if (_interactionContainer != null) return;
        _interactionContainer = GetNodeOrNull<Control>("%InteractionContainer");
        _interactionLabel = GetNodeOrNull<Label>("%InteractionLabel");
        _interactionProgressBar = GetNodeOrNull<ProgressBar>("%InteractionProgressBar");
    }

    public void ShowHitmarker(bool isHeadshot, bool isKill)
    {
        Reticle.FlashHitmarker(isHeadshot, isKill);
    }

    public void SetRespawning(bool isRespawning, float secondsRemaining = 0.0f)
    {
    }

    private void OnMatchStateChanged(MatchState state)
    {
        switch (state)
        {
            case MatchState.Warmup:
                _centerBannerContainer.Visible = true;
                _bannerTitleLabel.Text = "MATCH STARTING IN";
                _bannerCountdownLabel.Text = "5";
                break;

            case MatchState.InProgress:
                _centerBannerContainer.Visible = false;
                break;

            case MatchState.Ended:
                _centerBannerContainer.Visible = true;
                break;
        }
    }

    private void OnWarmupTick(float secondsRemaining)
    {
        if (MatchManager.Instance?.CurrentState == MatchState.Warmup)
        {
            int displaySeconds = Mathf.CeilToInt(secondsRemaining);
            _bannerCountdownLabel.Text = displaySeconds > 0 ? displaySeconds.ToString() : "GO!";
            _clockLabel.Text = "00:00";
        }
    }

    private void OnMatchTimerTick(float secondsRemaining)
    {
        int minutes = Mathf.FloorToInt(secondsRemaining / 60.0f);
        int seconds = Mathf.FloorToInt(secondsRemaining % 60.0f);
        _clockLabel.Text = $"{minutes:D2}:{seconds:D2}";
    }

    private void OnScoresUpdated(int alphaKills, int bravoKills)
    {
        _alphaScoreLabel.Text = $"ALPHA  {alphaKills} / {MatchManager.TargetKillsToWin}";
        _bravoScoreLabel.Text = $"BRAVO  {bravoKills} / {MatchManager.TargetKillsToWin}";
    }

    private void OnMatchFinished(int winningTeam)
    {
        _centerBannerContainer.Visible = true;
        _bannerTitleLabel.Text = "MATCH COMPLETE";

        if (winningTeam == 1)
        {
            _bannerCountdownLabel.Text = "TEAM ALPHA VICTORY";
            _bannerCountdownLabel.Modulate = new Color(0.3f, 0.65f, 1.0f);
        }
        else if (winningTeam == 2)
        {
            _bannerCountdownLabel.Text = "TEAM BRAVO VICTORY";
            _bannerCountdownLabel.Modulate = new Color(1.0f, 0.35f, 0.35f);
        }
        else
        {
            _bannerCountdownLabel.Text = "DRAW MATCH";
            _bannerCountdownLabel.Modulate = Colors.Yellow;
        }
    }
}
