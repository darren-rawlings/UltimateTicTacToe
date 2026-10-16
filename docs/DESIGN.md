# Ultimate Tic-Tac-Toe: design

Store title: **Ultimate Tic-Tac-Toe** (HUD shows "Ultimate TTT"). Package `com.renegadegames.ultimatetictactoe`.

## The game
Nine small tic-tac-toe boards in a 3×3 big board. The cell you play in sends your opponent to the
small board in the same position; if that board is won or full, they may play anywhere. Three in
a row wins a small board; three won boards in a row win the game. If every board is closed with
no line, the most boards won wins (otherwise a draw).

## Feel
- 3D marks: X as two crossed bars, O as a ring, in the chosen colours (Ruby & Sapphire glass by
  default), dropped onto a board in the chosen material.
- The small boards you may play in glow and breathe; the status says "play anywhere" when free.
- **Signature effect, board claims:** winning a small board sinks its little marks and raises a
  giant mark over the whole board with a flash and a burst of sparks.
- Drag sideways to spin the board.

## Modes
Player vs player; vs computer via Monte Carlo tree search: Easy (15 ms, and some random moves),
Medium (120 ms), Hard (450 ms). Every level takes an immediate win.

## Ads
Between-games interstitial every 5th game; Undo (ad) vs the computer.

## Later
A rule variant where a sent-to won board is still playable; game replay.
