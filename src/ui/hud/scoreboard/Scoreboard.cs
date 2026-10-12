using System.Linq;
using Godot;
using FPSGame.Gamemodes.TDM;
using FPSGame.Network.Core;

namespace FPSGame.UI.HUD.Scoreboard;

/// <summary>
/// Tactical scoreboard overlay toggled with the Tab key.
/// Displays team score comparison, match clock, and player stats (Score, K, D, A, Ping).
/// </summary>
public partial class Scoreboard : Control
{
	private Label _clockLabel = null!;
	private Label _alphaHeaderScoreLabel = null!;
	private Label _bravoHeaderScoreLabel = null!;
	private Tree _alphaTree = null!;
	private Tree _bravoTree = null!;

	public bool IsOpen => Visible;

	public override void _Ready()
	{
		_clockLabel = GetNode<Label>("%ClockLabel");
		_alphaHeaderScoreLabel = GetNode<Label>("%AlphaHeaderScoreLabel");
		_bravoHeaderScoreLabel = GetNode<Label>("%BravoHeaderScoreLabel");
		_alphaTree = GetNode<Tree>("%AlphaTree");
		_bravoTree = GetNode<Tree>("%BravoTree");

		SetupTreeHeaders(_alphaTree);
		SetupTreeHeaders(_bravoTree);

		if (MatchManager.Instance != null)
		{
			MatchManager.Instance.MatchTimerTick += OnTimerTick;
			MatchManager.Instance.ScoresUpdated += OnScoresUpdated;
			MatchManager.Instance.PlayerStatsUpdated += OnPlayerStatsUpdated;
			OnScoresUpdated(MatchManager.Instance.TeamAlphaKills, MatchManager.Instance.TeamBravoKills);
		}

		Visible = false;
	}

	public override void _ExitTree()
	{
		if (MatchManager.Instance != null)
		{
			MatchManager.Instance.MatchTimerTick -= OnTimerTick;
			MatchManager.Instance.ScoresUpdated -= OnScoresUpdated;
			MatchManager.Instance.PlayerStatsUpdated -= OnPlayerStatsUpdated;
		}
	}

	public void Open()
	{
		Visible = true;
		RefreshScoreboardData();
	}

	public void Close()
	{
		Visible = false;
	}

	private void SetupTreeHeaders(Tree tree)
	{
		tree.Columns = 6;
		tree.HideRoot = true;
		tree.ColumnTitlesVisible = true;

		tree.SetColumnTitle(0, "OPERATOR");
		tree.SetColumnTitleAlignment(0, HorizontalAlignment.Left);
		tree.SetColumnExpand(0, true);

		tree.SetColumnTitle(1, "SCORE");
		tree.SetColumnTitleAlignment(1, HorizontalAlignment.Center);
		tree.SetColumnExpand(1, false);
		tree.SetColumnCustomMinimumWidth(1, 65);

		tree.SetColumnTitle(2, "K");
		tree.SetColumnTitleAlignment(2, HorizontalAlignment.Center);
		tree.SetColumnExpand(2, false);
		tree.SetColumnCustomMinimumWidth(2, 45);

		tree.SetColumnTitle(3, "D");
		tree.SetColumnTitleAlignment(3, HorizontalAlignment.Center);
		tree.SetColumnExpand(3, false);
		tree.SetColumnCustomMinimumWidth(3, 45);

		tree.SetColumnTitle(4, "A");
		tree.SetColumnTitleAlignment(4, HorizontalAlignment.Center);
		tree.SetColumnExpand(4, false);
		tree.SetColumnCustomMinimumWidth(4, 45);

		tree.SetColumnTitle(5, "PING");
		tree.SetColumnTitleAlignment(5, HorizontalAlignment.Center);
		tree.SetColumnExpand(5, false);
		tree.SetColumnCustomMinimumWidth(5, 55);
	}

	public void RefreshScoreboardData()
	{
		if (NetworkManager.Instance == null) return;

		_alphaTree.Clear();
		_bravoTree.Clear();

		TreeItem alphaRoot = _alphaTree.CreateItem();
		TreeItem bravoRoot = _bravoTree.CreateItem();

		long myPeerId = Multiplayer.GetUniqueId();
		Color yellowColor = new Color(1.0f, 0.88f, 0.15f);
		Color defaultColor = Colors.White;

		var allPlayers = NetworkManager.Instance.Players.Values
			.OrderByDescending(p => p.Score)
			.ThenByDescending(p => p.Kills)
			.ToList();

		foreach (var p in allPlayers)
		{
			bool isMe = p.PeerId == myPeerId;
			Color rowColor = isMe ? yellowColor : defaultColor;
			string hostTag = p.PeerId == 1 ? " [HOST]" : "";
			string youTag = isMe ? " [YOU]" : "";
			string nameDisplay = $"{p.Name}{hostTag}{youTag}";

			Tree parentTree = p.TeamId == 2 ? _bravoTree : _alphaTree;
			TreeItem parentRoot = p.TeamId == 2 ? bravoRoot : alphaRoot;

			TreeItem row = parentTree.CreateItem(parentRoot);
			row.SetText(0, nameDisplay);
			row.SetTextAlignment(0, HorizontalAlignment.Left);

			row.SetText(1, p.Score.ToString());
			row.SetTextAlignment(1, HorizontalAlignment.Center);

			row.SetText(2, p.Kills.ToString());
			row.SetTextAlignment(2, HorizontalAlignment.Center);

			row.SetText(3, p.Deaths.ToString());
			row.SetTextAlignment(3, HorizontalAlignment.Center);

			row.SetText(4, p.Assists.ToString());
			row.SetTextAlignment(4, HorizontalAlignment.Center);

			row.SetText(5, $"{p.Ping}ms");
			row.SetTextAlignment(5, HorizontalAlignment.Center);

			for (int col = 0; col < 6; col++)
			{
				row.SetCustomColor(col, rowColor);
			}
		}
	}

	private void OnTimerTick(float secondsRemaining)
	{
		if (!Visible) return;

		int minutes = Mathf.FloorToInt(secondsRemaining / 60.0f);
		int seconds = Mathf.FloorToInt(secondsRemaining % 60.0f);
		_clockLabel.Text = $"{minutes:D2}:{seconds:D2}";
	}

	private void OnScoresUpdated(int alphaKills, int bravoKills)
	{
		_alphaHeaderScoreLabel.Text = $"{alphaKills} / {MatchManager.TargetKillsToWin}";
		_bravoHeaderScoreLabel.Text = $"{bravoKills} / {MatchManager.TargetKillsToWin}";

		if (Visible)
		{
			RefreshScoreboardData();
		}
	}

	private void OnPlayerStatsUpdated(long peerId)
	{
		if (Visible)
		{
			RefreshScoreboardData();
		}
	}
}
