using System.Linq;
using Renegade.Core;
using UltimateTicTacToe.Core;
using Xunit;

namespace UltimateTicTacToe.Tests;

public class UltimateTests
{
	[Fact]
	public void TheFirstMoveCanBeAnywhereAndSendsTheOpponent()
	{
		var game = new UltimateGame();
		Assert.Equal(81, game.LegalMoves().Count);
		var result = game.Play(4, 2);
		Assert.Equal(2, result.NextBoard);
		Assert.All(game.LegalMoves(), move => Assert.Equal(2, move / 9));
	}

	[Fact]
	public void ThreeInARowWinsASmallBoard()
	{
		var game = new UltimateGame();
		// Each X move sends O back to board 0, where O completes the bottom row (cells 6, 7, 8).
		game.Play(0, 0); game.Play(0, 8);
		game.Play(8, 0); game.Play(0, 7);
		game.Play(7, 0);
		var result = game.Play(0, 6);
		Assert.True(result.WonBoard);
		Assert.Equal(Mark.O, game.BoardWinner(0));
	}

	[Fact]
	public void BeingSentToAClosedBoardFreesTheMove()
	{
		var game = new UltimateGame();
		game.Play(0, 0); game.Play(0, 8);
		game.Play(8, 0); game.Play(0, 7);
		game.Play(7, 0); game.Play(0, 6);
		// O won board 0 and sent X to board 6; X's cell 0 would send O to board 0, which is closed.
		game.Play(6, 0);
		Assert.Equal(UltimateGame.Any, game.NextBoard);
		Assert.True(game.LegalMoves().Select(m => m / 9).Distinct().Count() > 1);
	}

	[Fact]
	public void UndoRestoresTheBoard()
	{
		var game = new UltimateGame();
		game.Play(4, 4);
		game.Play(4, 0);
		Assert.True(game.Undo());
		Assert.True(game.Undo());
		Assert.Equal(81, game.LegalMoves().Count);
		Assert.Equal(Mark.X, game.Turn);
	}

	[Fact]
	public void RandomGamesAlwaysEnd()
	{
		var random = new System.Random(5);
		for (int i = 0; i < 50; i++)
		{
			var game = new UltimateGame();
			while (!game.IsOver)
			{
				var moves = game.LegalMoves();
				int move = moves[random.Next(moves.Count)];
				game.Play(move / 9, move % 9);
			}
		}
	}

	[Fact]
	public void HardBeatsEasy()
	{
		int hardWins = 0;
		for (int i = 0; i < 6; i++)
		{
			var game = new UltimateGame();
			var hard = new ComputerPlayer(Difficulty.Hard, seed: i);
			var easy = new ComputerPlayer(Difficulty.Easy, seed: 20 + i);
			var hardMark = i % 2 == 0 ? Mark.X : Mark.O;
			while (!game.IsOver)
			{
				int move = (game.Turn == hardMark ? hard : easy).ChooseMove(game);
				game.Play(move / 9, move % 9);
			}
			if (game.Winner == hardMark)
				hardWins++;
		}
		Assert.True(hardWins >= 5, $"Hard won only {hardWins} of 6.");
	}
}
