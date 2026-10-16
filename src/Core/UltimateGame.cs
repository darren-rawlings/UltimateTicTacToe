using System;
using System.Collections.Generic;
using System.Numerics;

namespace UltimateTicTacToe.Core;

/// <summary>X moves first; against the computer, that's you.</summary>
public enum Mark
{
	X,
	O,
}

/// <summary>What a move did: whether it won its small board, and where the opponent must play next.</summary>
public sealed record MoveResult(Mark Mover, int Board, int Cell, bool WonBoard, bool BoardFull, int NextBoard, bool GameOver);

/// <summary>
/// Ultimate tic-tac-toe: nine small boards in a 3×3 big board. Your move's cell (0-8) sends your
/// opponent to the small board in the same position; if that board is already won or full, they
/// may play in any open board. Three in a row in a small board wins it; three won boards in a row
/// win the game. Each small board is two 9-bit masks, which keeps the computer's playouts fast.
/// </summary>
public sealed class UltimateGame
{
	public const int Any = -1;

	/// <summary>The eight three-in-a-rows of a 3×3 grid, as 9-bit masks.</summary>
	public static readonly int[] Lines = { 0b000000111, 0b000111000, 0b111000000, 0b001001001, 0b010010010, 0b100100100, 0b100010001, 0b001010100 };

	private readonly int[] _x = new int[9];
	private readonly int[] _o = new int[9];
	private readonly Stack<(int Board, int Cell, int Next, int WonX, int WonO, int Dead, Mark Turn, bool Over, Mark? Winner)> _history = new();

	/// <summary>Small boards won by X, by O, and drawn (full with no winner), as 9-bit masks.</summary>
	public int WonX { get; private set; }
	public int WonO { get; private set; }
	public int Dead { get; private set; }

	public Mark Turn { get; private set; }

	/// <summary>The board the next move must be in, or <see cref="Any"/>.</summary>
	public int NextBoard { get; private set; }

	public bool IsOver { get; private set; }
	public Mark? Winner { get; private set; }

	public UltimateGame() => Reset();

	private UltimateGame(UltimateGame other)
	{
		Array.Copy(other._x, _x, 9);
		Array.Copy(other._o, _o, 9);
		WonX = other.WonX;
		WonO = other.WonO;
		Dead = other.Dead;
		Turn = other.Turn;
		NextBoard = other.NextBoard;
		IsOver = other.IsOver;
		Winner = other.Winner;
	}

	public UltimateGame Clone() => new(this);

	public void Reset()
	{
		Array.Clear(_x);
		Array.Clear(_o);
		WonX = WonO = Dead = 0;
		Turn = Mark.X;
		NextBoard = Any;
		IsOver = false;
		Winner = null;
		_history.Clear();
	}

	public int MovesMade => _history.Count;

	public Mark? LastMover => _history.Count == 0 ? null : _history.Peek().Turn;

	public static Mark Opponent(Mark mark) => mark == Mark.X ? Mark.O : Mark.X;

	public Mark? At(int board, int cell)
	{
		int bit = 1 << cell;
		return (_x[board] & bit) != 0 ? Mark.X : (_o[board] & bit) != 0 ? Mark.O : null;
	}

	/// <summary>Who won a small board, if anyone.</summary>
	public Mark? BoardWinner(int board) => (WonX & (1 << board)) != 0 ? Mark.X : (WonO & (1 << board)) != 0 ? Mark.O : null;

	public bool IsBoardClosed(int board) => ((WonX | WonO | Dead) & (1 << board)) != 0;

	/// <summary>The small boards the next move may be in.</summary>
	public IEnumerable<int> PlayableBoards()
	{
		if (IsOver)
			yield break;
		if (NextBoard != Any)
		{
			yield return NextBoard;
			yield break;
		}
		for (int board = 0; board < 9; board++)
		{
			if (!IsBoardClosed(board))
				yield return board;
		}
	}

	public bool CanPlay(int board, int cell) =>
		!IsOver && (NextBoard == Any || NextBoard == board) && !IsBoardClosed(board) && ((_x[board] | _o[board]) & (1 << cell)) == 0;

	/// <summary>Every legal move as board * 9 + cell.</summary>
	public List<int> LegalMoves()
	{
		var moves = new List<int>(81);
		foreach (int board in PlayableBoards())
		{
			int free = ~(_x[board] | _o[board]) & 0x1ff;
			while (free != 0)
			{
				int cell = BitOperations.TrailingZeroCount(free);
				free &= free - 1;
				moves.Add(board * 9 + cell);
			}
		}
		return moves;
	}

	public MoveResult Play(int board, int cell)
	{
		if (!CanPlay(board, cell))
			throw new InvalidOperationException($"Can't play board {board} cell {cell}.");
		_history.Push((board, cell, NextBoard, WonX, WonO, Dead, Turn, IsOver, Winner));
		var mover = Turn;
		bool won = Apply(board, cell);
		bool full = (_x[board] | _o[board]) == 0x1ff;
		return new MoveResult(mover, board, cell, won, full, NextBoard, IsOver);
	}

	/// <summary>Places a mark and updates everything. Returns true if it won the small board.</summary>
	internal bool Apply(int board, int cell)
	{
		var mover = Turn;
		ref int own = ref mover == Mark.X ? ref _x[board] : ref _o[board];
		own |= 1 << cell;
		bool won = HasLine(own);
		if (won)
		{
			if (mover == Mark.X)
				WonX |= 1 << board;
			else
				WonO |= 1 << board;
			if (HasLine(mover == Mark.X ? WonX : WonO))
			{
				IsOver = true;
				Winner = mover;
			}
		}
		else if ((_x[board] | _o[board]) == 0x1ff)
		{
			Dead |= 1 << board;
		}
		if (!IsOver && (WonX | WonO | Dead) == 0x1ff)
		{
			// Every board closed with no line of boards: most boards wins, else a draw.
			IsOver = true;
			int x = BitOperations.PopCount((uint)WonX), o = BitOperations.PopCount((uint)WonO);
			Winner = x == o ? null : x > o ? Mark.X : Mark.O;
		}
		NextBoard = IsBoardClosed(cell) ? Any : cell;
		Turn = Opponent(mover);
		return won;
	}

	public bool Undo()
	{
		if (_history.Count == 0)
			return false;
		var h = _history.Pop();
		int bit = 1 << h.Cell;
		_x[h.Board] &= ~bit;
		_o[h.Board] &= ~bit;
		NextBoard = h.Next;
		WonX = h.WonX;
		WonO = h.WonO;
		Dead = h.Dead;
		Turn = h.Turn;
		IsOver = h.Over;
		Winner = h.Winner;
		return true;
	}

	public static bool HasLine(int mask)
	{
		foreach (int line in Lines)
		{
			if ((mask & line) == line)
				return true;
		}
		return false;
	}

	/// <summary>The three-in-a-row a mask contains (for highlighting), or 0.</summary>
	public static int WinningLine(int mask)
	{
		foreach (int line in Lines)
		{
			if ((mask & line) == line)
				return line;
		}
		return 0;
	}
}
