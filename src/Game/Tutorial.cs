using System.Collections.Generic;
using Renegade.Ui;

namespace UltimateTicTacToe.Game;

/// <summary>The guided tour. Clips are recorded from the game with --clip (see Main.PlayClip).</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Nine games in one",
			"The big board is a tic-tac-toe grid of nine small tic-tac-toe boards. X moves first, then you take turns. The line at the bottom always says which mark is yours to play.",
			"res://tutorial/send.ogv"),
		new TutorialPage("Where you play sends them",
			"The square you pick inside a small board sends your opponent to the matching small board. Top-left square? They must play in the top-left board. The glowing boards are where you can play.",
			"res://tutorial/send.ogv"),
		new TutorialPage("Win a small board",
			"Three in a row on a small board claims it: a giant mark rises over it. If you're sent to a board that's already won or full, you may play in any open board.",
			"res://tutorial/claim.ogv"),
		new TutorialPage("Win the big board",
			"Claim three small boards in a row on the big grid to win the game, and your giant marks catch fire.",
			"res://tutorial/end.ogv"),
		new TutorialPage("Tips",
			"Think about where each move sends your opponent: don't send them somewhere they can win. Drag sideways to spin the board, or freeze it in Settings. Against the computer, Undo (ad) takes a move back.",
			"res://tutorial/claim.ogv"),
	};
}
