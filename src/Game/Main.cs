using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Looks;
using Renegade.Play;
using Renegade.Ui;
using UltimateTicTacToe.Core;

namespace UltimateTicTacToe.Game;

/// <summary>
/// Ultimate Tic-Tac-Toe. The small boards you may play in glow; tap a cell to place your mark. Win a
/// small board and its little marks sink away as a giant X or O rises over the whole board with a
/// burst of sparks (the signature "board claim"). Drag sideways to spin the board.
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Ultimate Tic-Tac-Toe";

	/// <summary>Google's test ad units until the real ones exist in AdMob (see the release playbook).</summary>
	private static readonly AdMobConfig AdUnits = new(
		InterstitialUnitId: "ca-app-pub-3310172999423635/1969857018",
		RewardedUnitId: "ca-app-pub-3310172999423635/4416231965");

	private const Mark Human = Mark.X;
	private const float Cell = 0.85f;
	private const float Gap = 0.35f;
	private const float Small = Cell * 3;
	private const float GridHalf = (Small * 3 + Gap * 2) / 2f;
	private const float Half = GridHalf + 0.4f;
	private const float Thickness = 0.4f;

	private readonly UltimateGame _game = new();
	private readonly Scoreboard _wins = new();
	private readonly Node3D?[] _marks = new Node3D?[81];
	private readonly Node3D?[] _bigMarks = new Node3D?[9];
	private readonly MeshInstance3D[] _glows = new MeshInstance3D[9];
	private readonly ImpactBank<Style> _impacts = new(Styles.Impact);
	private readonly ComputerTurn _computer = new();

	private GameSettings _settings = null!;
	private Node3D _rig = null!;
	private Camera3D _camera = null!;
	private SoundPool _sound = null!;
	private GameHud _hud = null!;
	private IAdService _ads = null!;
	private AdSchedule? _adSchedule;
	private SpinControl _spin = null!;
	private MeshInstance3D _slab = null!;
	private StandardMaterial3D _glowMaterial = null!;
	private bool _busy;
	private bool _roundCounted;

	private bool _demo;
	private bool _quitAfterDemo;
	private string? _clip;
	private float _initialSpin;

	private bool VsComputer => _settings.Mode == GameMode.PlayerVsComputer || _demo;

	private bool IsComputerTurn => !_game.IsOver && (_demo || (VsComputer && _game.Turn != Human));

	private PieceSet Colours => Styles.Pieces(_settings.Pieces);

	public override void _Ready()
	{
		DevLog.Tag = "utt";
		AudioWatch.Attach(this, Title);
		_settings = GameSettings.Load(board: Style.Wood, pieces: Style.Glass);
		ReadDeveloperArgs();
		AudioServer.SetBusMute(0, _settings.Muted);
		BuildWorld();

		_sound = new SoundPool();
		AddChild(_sound);
		_adSchedule ??= new AdSchedule(_settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		_ads = AdServices.Create(this, AdUnits, _demo, new PlaceholderAds
		{
			TextColor = theme.Text,
			RewardTitle = "Watch to undo your move",
			ClaimText = "Claim undo",
			SkipText = "Skip (no undo)",
		});

		_hud = new GameHud(theme);
		AddChild(_hud);
		_hud.Build(new HudOptions(Title, "Play in a glowing board  ·  Drag sideways to spin", PiecesLabel: "X & O",
			PieceName: s => $"{Styles.Pieces(s).NameOne} & {Styles.Pieces(s).NameTwo}"), _settings);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.UndoPressed += OnUndoPressed;
		_hud.RestartPressed += OnRestartPressed;
		_hud.SetTutorial(Tutorial.Pages);
		_hud.ModeChosen += (mode, difficulty) =>
		{
			_settings.Mode = mode;
			_settings.Difficulty = difficulty;
			_settings.Save();
			_wins.Reset();
			_ = StartGame();
		};
		_hud.BoardChosen += style =>
		{
			_settings.Board = style;
			_settings.Save();
			_slab.MaterialOverride = Materials.For(Styles.BoardLook(style));
		};
		_hud.PiecesChosen += style =>
		{
			_settings.Pieces = style;
			_settings.Save();
			Rebuild();
		};

		_spin = new SpinControl(_rig, () => !_busy && !_settings.FreezeBoard);
		// Freeze the board also turns it back to where it started.
		_spin.Frozen = () => _settings.FreezeBoard && !_busy;
		_spin.Tapped += OnTapped;
		if (_clip != null)
		{
			_hud.Visible = false;
			_ = PlayClip(_clip);
			return;
		}
		_ = StartGame();
		if (!_demo)
			_hud.OfferTutorialOnce(_settings);
		_hud.ApplyDevFlags();
	}

	// ---------------------------------------------------------------- World

	private static Vector3 BoardCenter(int board) =>
		new((board % 3) * (Small + Gap) + Small / 2 - GridHalf, 0, (board / 3) * (Small + Gap) + Small / 2 - GridHalf);

	private static Vector3 CellCenter(int board, int cell) =>
		BoardCenter(board) + new Vector3((cell % 3 - 1) * Cell, 0, (cell / 3 - 1) * Cell);

	private void BuildWorld()
	{
		_camera = new Camera3D { Position = new Vector3(0, 10.4f, 6.4f), Fov = 54, KeepAspect = Camera3D.KeepAspectEnum.Width };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, -0.4f, 0.3f));
		Stage.Build(this, -Thickness, new Color("1a0f09"));

		_rig = new Node3D();
		AddChild(_rig);
		_rig.RotateY(_initialSpin);
		_slab = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(Half * 2, Thickness, Half * 2) },
			Position = new Vector3(0, -Thickness / 2, 0),
			MaterialOverride = Materials.For(Styles.BoardLook(_settings.Board)),
		};
		_rig.AddChild(_slab);

		// Each small board: a darker inset with its own tic-tac-toe grid.
		var inset = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.02f, 0f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		var line = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.05f, 0.02f, 0.85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		_glowMaterial = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.78f, 0.35f, 0.22f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		for (int board = 0; board < 9; board++)
		{
			var center = BoardCenter(board);
			_rig.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(Small + 0.1f, Small + 0.1f) }, MaterialOverride = inset, Position = center + new Vector3(0, 0.002f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
			for (int i = 1; i <= 2; i++)
			{
				float at = (i - 1.5f) * Cell;
				_rig.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.01f, Small - 0.15f) }, MaterialOverride = line, Position = center + new Vector3(at, 0.005f, 0) });
				_rig.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(Small - 0.15f, 0.01f, 0.05f) }, MaterialOverride = line, Position = center + new Vector3(0, 0.005f, at) });
			}
			_glows[board] = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(Small + 0.22f, Small + 0.22f) }, MaterialOverride = _glowMaterial, Position = center + new Vector3(0, 0.004f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_rig.AddChild(_glows[board]);
		}
		Stage.WarmUp(this, -Thickness, Sparkle.Halo);
	}

	/// <summary>An X (two crossed bars) or an O (a ring), standing on the board.</summary>
	private Node3D MakeMark(Mark mark, float size)
	{
		var node = new Node3D();
		var material = Materials.For(Colours.LookOf(mark == Mark.X));
		float thick = 0.13f * size;
		if (mark == Mark.X)
		{
			foreach (float angle in new[] { 45f, -45f })
				node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.7f * size, thick, thick) }, MaterialOverride = material, RotationDegrees = new Vector3(0, angle, 0), Position = new Vector3(0, thick / 2, 0) });
		}
		else
		{
			node.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.2f * size, OuterRadius = 0.33f * size, Rings = 32, RingSegments = 12 }, MaterialOverride = material, Position = new Vector3(0, 0.065f * size, 0) });
		}
		_rig.AddChild(node);
		return node;
	}

	private static IEnumerable<MeshInstance3D> MeshesOf(Node3D node) => node.GetChildren().OfType<MeshInstance3D>();

	// ---------------------------------------------------------------- Input

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_hud.MenuOpen)
			_spin.HandleInput(@event);
	}

	private void OnTapped(Vector2 screen)
	{
		if (_busy || IsComputerTurn || _game.IsOver)
			return;
		var from = _camera.ProjectRayOrigin(screen);
		var direction = _camera.ProjectRayNormal(screen);
		if (direction.Y > -0.01f)
			return;
		var local = _rig.ToLocal(from + direction * (-from.Y / direction.Y));
		for (int board = 0; board < 9; board++)
		{
			var offset = local - BoardCenter(board);
			if (Mathf.Abs(offset.X) > Small / 2 || Mathf.Abs(offset.Z) > Small / 2)
				continue;
			int column = Mathf.Clamp(Mathf.FloorToInt(offset.X / Cell + 1.5f), 0, 2);
			int row = Mathf.Clamp(Mathf.FloorToInt(offset.Z / Cell + 1.5f), 0, 2);
			int cell = row * 3 + column;
			if (_game.CanPlay(board, cell))
				_ = PlayMove(board, cell);
			return;
		}
	}

	// ---------------------------------------------------------------- Turns

	private async Task PlayMove(int board, int cell)
	{
		_busy = true;
		_spin.Stop();
		_hud.HideHint();
		ShowGlows(false);
		var result = _game.Play(board, cell);
		DevLog.Write($"{result.Mover} plays {board}/{cell}, won board={result.WonBoard}, next={result.NextBoard}, over={result.GameOver}");
		RefreshUi(result.Mover);

		var mark = MakeMark(result.Mover, 1f);
		_marks[board * 9 + cell] = mark;
		var rest = CellCenter(board, cell);
		mark.Position = rest + new Vector3(0, 1.2f, 0);
		mark.RotateY((float)GD.RandRange(-0.2, 0.2));
		var drop = CreateTween();
		drop.TweenProperty(mark, "position", rest, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await ToSignal(drop, Tween.SignalName.Finished);
		Clack(mark.GlobalPosition, 0.7f);

		if (result.WonBoard)
			await ClaimBoard(board, result.Mover);
		else if (result.BoardFull)
			DimBoard(board);
		if (_game.IsOver && !_roundCounted)
		{
			_roundCounted = true;
			if (_game.Winner is { } winner)
			{
				_wins.Win(first: winner == Mark.X);
				await SetWinnerAlight(winner);
			}
		}
		_busy = false;
		RefreshUi();
	}

	/// <summary>
	/// The signature: the board's little marks sink and shrink away while a giant mark in the
	/// winner's colour rises over the whole board, flashing, with a burst of sparks.
	/// </summary>
	private async Task ClaimBoard(int board, Mark winner)
	{
		for (int cell = 0; cell < 9; cell++)
		{
			if (_marks[board * 9 + cell] is not { } small)
				continue;
			var sink = CreateTween();
			sink.TweenProperty(small, "scale", Vector3.One * 0.45f, 0.35f);
		}
		var big = MakeMark(winner, 3.1f);
		_bigMarks[board] = big;
		var rest = BoardCenter(board) + new Vector3(0, 0.1f, 0);
		big.Position = rest + new Vector3(0, -0.5f, 0);
		big.Scale = Vector3.One * 0.3f;
		foreach (var mesh in MeshesOf(big))
			mesh.MaterialOverlay = Sparkle.Halo;
		var rise = CreateTween().SetParallel();
		rise.TweenProperty(big, "position", rest, 0.45f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		rise.TweenProperty(big, "scale", Vector3.One, 0.45f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		Sparkle.Burst(_rig, rest + new Vector3(0, 0.4f, 0));
		_sound.Play(_impacts.Random(_settings.Pieces), big.GlobalPosition, Styles.GainDb(_settings.Pieces) + 4f, 0.8f);
		_hud.SetStatus(_game.IsOver ? "" : $"{winner} · {NameOf(winner)} {(NameOf(winner) == "You" ? "take" : "takes")} a board!", ColorOf(winner));
		await ToSignal(rise, Tween.SignalName.Finished);
		foreach (var mesh in MeshesOf(big))
			mesh.MaterialOverlay = null;
		await Wait(0.25f);
	}

	private void DimBoard(int board)
	{
		for (int cell = 0; cell < 9; cell++)
		{
			if (_marks[board * 9 + cell] is { } small)
				CreateTween().TweenProperty(small, "scale", Vector3.One * 0.7f, 0.3f);
		}
	}

	private void Clack(Vector3 at, float strength)
	{
		_sound.PlayImpact(_impacts.Random(_settings.Pieces), at, strength, Styles.GainDb(_settings.Pieces));
		_sound.PlayImpact(_impacts.Random(_settings.Board), at, strength * 0.6f, Styles.GainDb(_settings.Board));
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_spin.Process(dt);
		// The playable boards breathe gently.
		float t = Time.GetTicksMsec() / 1000f;
		_glowMaterial.AlbedoColor = _glowMaterial.AlbedoColor with { A = 0.14f + 0.1f * (0.5f + 0.5f * Mathf.Sin(t * 3f)) };
		if (_busy || !IsComputerTurn)
			return;
		if (!_computer.Thinking)
		{
			var player = new ComputerPlayer(_demo ? Difficulty.Medium : _settings.Difficulty);
			var snapshot = _game.Clone();
			_computer.MinThinkTime = _demo ? 0.15f : 0.6f;
			_computer.Start(() => player.ChooseMove(snapshot), $"computer ({player.Difficulty})");
			RefreshUi();
			return;
		}
		if (_computer.Poll(dt) is { } move && _game.CanPlay(move / 9, move % 9))
			_ = PlayMove(move / 9, move % 9);
	}

	// ---------------------------------------------------------------- Games, undo, ads

	private void OnNewGamePressed()
	{
		if (_busy)
			return;
		bool adDue = _adSchedule!.GameFinished();
		_settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		_settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			_ads.ShowInterstitial(() => _ = StartGame());
		}
		else
		{
			_ = StartGame();
		}
	}

	private async Task StartGame()
	{
		_computer.Cancel();
		PutOutFire();
		_busy = true;
		_roundCounted = false;
		_game.Reset();
		Rebuild();
		await Wait(0.2f);
		_busy = false;
		RefreshUi();
	}

	/// <summary>Removes every mark and puts back the ones the rules have (after undo or a colour change).</summary>
	private void Rebuild()
	{
		foreach (var node in _marks.Concat(_bigMarks))
			node?.QueueFree();
		System.Array.Clear(_marks);
		System.Array.Clear(_bigMarks);
		for (int board = 0; board < 9; board++)
		{
			for (int cell = 0; cell < 9; cell++)
			{
				if (_game.At(board, cell) is not { } mark)
					continue;
				var node = MakeMark(mark, 1f);
				node.Position = CellCenter(board, cell);
				_marks[board * 9 + cell] = node;
				if (_game.IsBoardClosed(board))
					node.Scale = Vector3.One * (_game.BoardWinner(board) != null ? 0.45f : 0.7f);
			}
			if (_game.BoardWinner(board) is { } winner)
			{
				var big = MakeMark(winner, 3.1f);
				big.Position = BoardCenter(board) + new Vector3(0, 0.1f, 0);
				_bigMarks[board] = big;
			}
		}
	}

	private void OnRestartPressed()
	{
		if (_busy)
			return;
		if (_game.IsOver || _game.MovesMade == 0)
		{
			_ = StartGame();
			return;
		}
		_hud.AskRestart(VsComputer && !_demo, countLoss =>
		{
			if (countLoss)
				_wins.Win(first: Human != Mark.X);
			_ = StartGame();
		}, then => _ads.ShowRewarded(rewarded: then, closed: () => { }));
	}

	// ---------------------------------------------------------------- The winner's fire

	private readonly List<Flames> _fires = new();

	/// <summary>
	/// The three giant marks of the winning line catch fire one after another; the winner's other
	/// giant marks smoulder (glow without flames, which keeps it light on a phone).
	/// </summary>
	private async Task SetWinnerAlight(Mark winner)
	{
		int won = winner == Mark.X ? _game.WonX : _game.WonO;
		int line = UltimateGame.WinningLine(won);
		var boards = Enumerable.Range(0, 9).Where(b => (line & (1 << b)) != 0)
			.Concat(Enumerable.Range(0, 9).Where(b => (won & (1 << b)) != 0 && (line & (1 << b)) == 0));
		int lit = 0;
		foreach (int board in boards)
		{
			if (_bigMarks[board] is not { } big)
				continue;
			bool inLine = (line & (1 << board)) != 0;
			_fires.Add(Flames.SetAlight(_rig, big.Position + new Vector3(0, 0.2f, 0), MeshesOf(big), radius: 0.85f, size: 2.2f, withLight: lit == 0, particles: inLine));
			lit++;
			if (inLine)
				await Wait(0.25f);
		}
		_hud.SetStatus(NameOf(winner) == "You" ? $"{winner} · You win!" : $"{winner} · {NameOf(winner)} wins!", ColorOf(winner));
		await Wait(0.4f);
	}

	private void PutOutFire()
	{
		foreach (var fire in _fires)
			fire.Extinguish();
		_fires.Clear();
		foreach (var big in _bigMarks)
		{
			if (big == null)
				continue;
			foreach (var mesh in MeshesOf(big))
				mesh.MaterialOverlay = null;
		}
	}

	// ---------------------------------------------------------------- Tutorial clips

	/// <summary>
	/// Plays one scripted scene for the tutorial (with --clip=name; Record-Clips.ps1 records it),
	/// then quits. The HUD is hidden so the clip shows only the board.
	/// </summary>
	private async Task PlayClip(string clip)
	{
		_settings.Mode = GameMode.PlayerVsPlayer;
		_settings.Muted = false;
		_game.Reset();
		Rebuild();
		await Wait(0.5f);
		switch (clip)
		{
			case "send":
				// Each move sends the other player to the matching small board.
				foreach (var (board, cell) in new[] { (4, 0), (0, 8), (8, 4), (4, 2) })
				{
					ShowGlows(true);
					await Wait(1.1f);
					await PlayMove(board, cell);
				}
				ShowGlows(true);
				await Wait(1.4f);
				break;
			case "claim":
			{
				// A real game up to the first chance to win a small board, then that move on camera.
				var computer = new ComputerPlayer(Difficulty.Medium, seed: 7);
				while (!WinsBoardNext(out _))
				{
					int m = computer.ChooseMove(_game);
					_game.Play(m / 9, m % 9);
				}
				Rebuild();
				ShowGlows(true);
				await Wait(1.2f);
				WinsBoardNext(out int move);
				await PlayMove(move / 9, move % 9);
				ShowGlows(true);
				await Wait(1.8f);
				break;
			}
			case "end":
			{
				// Self-play until a game ends with a winner, replayed so the last few moves show.
				var moves = new List<int>();
				for (int seed = 1; ; seed++)
				{
					var trial = new UltimateGame();
					var computer = new ComputerPlayer(Difficulty.Medium, seed: seed);
					moves.Clear();
					while (!trial.IsOver)
					{
						int m = computer.ChooseMove(trial);
						moves.Add(m);
						trial.Play(m / 9, m % 9);
					}
					if (trial.Winner != null)
						break;
				}
				for (int i = 0; i < moves.Count - 3; i++)
					_game.Play(moves[i] / 9, moves[i] % 9);
				Rebuild();
				await Wait(0.6f);
				for (int i = moves.Count - 3; i < moves.Count; i++)
				{
					await PlayMove(moves[i] / 9, moves[i] % 9);
					await Wait(0.4f);
				}
				await Wait(2.6f);
				break;
			}
		}
		GetTree().Quit();
	}

	/// <summary>True if the side to move can win a small board right now (and which move does it).</summary>
	private bool WinsBoardNext(out int move)
	{
		foreach (int m in _game.LegalMoves())
		{
			if (_game.Clone().Play(m / 9, m % 9).WonBoard)
			{
				move = m;
				return true;
			}
		}
		move = -1;
		return false;
	}

	private bool CanUndo => VsComputer && !_demo && !_busy && _game.MovesMade > 0 && (_game.IsOver || _game.Turn == Human);

	private void OnUndoPressed()
	{
		if (!CanUndo)
			return;
		DevLog.Write("ad: rewarded (undo)");
		_hud.SetUndoVisible(false);
		_ads.ShowRewarded(rewarded: UndoLastTurn, closed: () => RefreshUi());
	}

	private void UndoLastTurn()
	{
		_computer.Cancel();
		PutOutFire();
		if (_game.IsOver && _roundCounted && _game.Winner is { } winner)
			_wins.Unwin(first: winner == Mark.X);
		_roundCounted = false;
		while (_game.LastMover == UltimateGame.Opponent(Human))
			_game.Undo();
		if (_game.LastMover == Human)
			_game.Undo();
		Rebuild();
		RefreshUi();
	}

	// ---------------------------------------------------------------- UI

	private string NameOf(Mark mark) => VsComputer && !_demo
		? (mark == Human ? "You" : "Computer")
		: Colours.NameOf(mark == Mark.X);

	private Color ColorOf(Mark mark) => Colours.UiColorOf(mark == Mark.X);

	private void ShowGlows(bool show)
	{
		var playable = show ? _game.PlayableBoards().ToHashSet() : new HashSet<int>();
		for (int board = 0; board < 9; board++)
			_glows[board].Visible = playable.Contains(board);
	}

	private void RefreshUi(Mark? mover = null)
	{
		int x = System.Numerics.BitOperations.PopCount((uint)_game.WonX), o = System.Numerics.BitOperations.PopCount((uint)_game.WonO);
		_hud.SetScore($"X  {NameOf(Mark.X)}  {x}   —   {o}  {NameOf(Mark.O)}  O");
		_hud.SetNewGameVisible(_game.IsOver && !_busy);
		_hud.SetUndoVisible(CanUndo);
		_hud.SetRestartVisible(!_game.IsOver);
		ShowGlows(!_busy && !_game.IsOver && (!IsComputerTurn || _demo));
		if (_game.IsOver)
		{
			if (_busy)
				return;
			string wins = $"(wins {_wins.First}–{_wins.Second})";
			string text = _game.Winner switch
			{
				null => $"A draw! {wins}",
				{ } w when NameOf(w) == "You" => $"{w} · You win! {wins}",
				{ } w => $"{w} · {NameOf(w)} wins {wins}",
			};
			_hud.SetStatus(text, _game.Winner is { } side ? ColorOf(side) : Colors.White);
			if (_demo && _quitAfterDemo)
				GetTree().CreateTimer(1.5).Timeout += () => GetTree().Quit();
			return;
		}
		var turn = mover ?? _game.Turn;
		string where = _game.NextBoard == UltimateGame.Any ? ": play anywhere" : "";
		// The mark comes first: players think of themselves as X or O.
		string status = mover == null && IsComputerTurn && !_demo ? $"{turn} · Computer is thinking…"
			: NameOf(turn) == "You" ? $"{turn} · Your turn{where}" : $"{turn} · {NameOf(turn)}'s turn{where}";
		_hud.SetStatus(status, ColorOf(turn));
	}

	private SignalAwaiter Wait(float seconds) => ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	// ---------------------------------------------------------------- Developer flags

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo` (the
	/// computer plays both sides), `--quit-after-demo`, `--games-since-ad=4`, `--board=Wood`,
	/// `--pieces=Glass`, `--spin=30` (degrees). Style flags aren't saved.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					_demo = true;
					_settings.Muted = false;
					break;
				case "--quit-after-demo":
					_quitAfterDemo = true;
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--board" when System.Enum.TryParse(value, out Style board):
					_settings.Board = board;
					break;
				case "--pieces" when System.Enum.TryParse(value, out Style pieces):
					_settings.Pieces = pieces;
					break;
				case "--spin":
					_initialSpin = Mathf.DegToRad(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
					break;
				case "--clip":
					_clip = value;
					break;
			}
		}
	}
}
