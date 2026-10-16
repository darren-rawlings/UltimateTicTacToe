using System;
using System.Collections.Generic;
using System.Diagnostics;
using Renegade.Core;

namespace UltimateTicTacToe.Core;

/// <summary>
/// The computer opponent: Monte Carlo tree search, which plays thousands of quick random games from
/// the current position and grows a tree towards the moves that win most often. It's the standard
/// approach for this game, where good positions are hard to score by hand. The levels differ in
/// thinking time; every level takes an immediate game win when there is one.
/// </summary>
public sealed class ComputerPlayer
{
	private readonly Random _random;

	public Difficulty Difficulty { get; }

	public ComputerPlayer(Difficulty difficulty, int? seed = null)
	{
		Difficulty = difficulty;
		_random = seed is { } s ? new Random(s) : new Random();
	}

	private int BudgetMs => Difficulty switch
	{
		Difficulty.Easy => 15,
		Difficulty.Medium => 120,
		_ => 450,
	};

	/// <summary>Returns board * 9 + cell.</summary>
	public int ChooseMove(UltimateGame game)
	{
		var moves = game.LegalMoves();
		if (moves.Count == 0)
			throw new InvalidOperationException("No legal moves.");
		if (moves.Count == 1)
			return moves[0];
		// Easy also blunders now and then, so it's beatable.
		if (Difficulty == Difficulty.Easy && _random.NextDouble() < 0.3)
			return moves[_random.Next(moves.Count)];

		foreach (int move in moves)
		{
			var next = game.Clone();
			next.Apply(move / 9, move % 9);
			if (next.IsOver && next.Winner == game.Turn)
				return move;
		}
		return Search(game);
	}

	private sealed class Node
	{
		public readonly int Move;
		public readonly Node? Parent;
		public readonly List<Node> Children = new();
		public List<int>? Untried;
		public int Visits;
		public double Wins;

		public Node(int move, Node? parent)
		{
			Move = move;
			Parent = parent;
		}
	}

	private int Search(UltimateGame root)
	{
		var me = root.Turn;
		var tree = new Node(-1, null) { Untried = root.LegalMoves() };
		var clock = Stopwatch.StartNew();
		int iterations = 0;
		while (clock.ElapsedMilliseconds < BudgetMs || iterations < 50)
		{
			iterations++;
			var game = root.Clone();
			var node = tree;
			// Selection: follow the most promising child (UCB1) while every move has been tried.
			while (node.Untried!.Count == 0 && node.Children.Count > 0)
			{
				node = Select(node);
				game.Apply(node.Move / 9, node.Move % 9);
			}
			// Expansion: try one new move.
			if (node.Untried.Count > 0 && !game.IsOver)
			{
				int pick = _random.Next(node.Untried.Count);
				int move = node.Untried[pick];
				node.Untried.RemoveAt(pick);
				game.Apply(move / 9, move % 9);
				var child = new Node(move, node) { Untried = game.IsOver ? new List<int>() : game.LegalMoves() };
				node.Children.Add(child);
				node = child;
			}
			// Playout: random moves to the end.
			while (!game.IsOver)
			{
				var legal = game.LegalMoves();
				int move = legal[_random.Next(legal.Count)];
				game.Apply(move / 9, move % 9);
			}
			// Backpropagation: each node scores the result for the player who made its move.
			for (var n = node; n != null; n = n.Parent)
			{
				n.Visits++;
				if (n.Parent == null)
					continue;
				var mover = MoverAt(n, me);
				n.Wins += game.Winner == null ? 0.5 : game.Winner == mover ? 1 : 0;
			}
		}
		Node best = tree.Children[0];
		foreach (var child in tree.Children)
		{
			if (child.Visits > best.Visits)
				best = child;
		}
		return best.Move;
	}

	/// <summary>Who made the move leading to this node: the root's player at odd depths.</summary>
	private static Mark MoverAt(Node node, Mark rootPlayer)
	{
		int depth = 0;
		for (var n = node; n.Parent != null; n = n.Parent)
			depth++;
		return depth % 2 == 1 ? rootPlayer : UltimateGame.Opponent(rootPlayer);
	}

	private static Node Select(Node node)
	{
		Node best = node.Children[0];
		double bestScore = double.MinValue;
		double logVisits = Math.Log(node.Visits);
		foreach (var child in node.Children)
		{
			double score = child.Wins / child.Visits + 1.41 * Math.Sqrt(logVisits / child.Visits);
			if (score > bestScore)
			{
				bestScore = score;
				best = child;
			}
		}
		return best;
	}
}
