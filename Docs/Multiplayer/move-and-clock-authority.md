# Multiplayer move and clock authority

Design notes for networked play, which is not implemented. Nothing in the shipped game depends on
any of this; it is written down so the decisions do not have to be made twice.

The `ChessTheBetrayal.Network` assembly exists but holds only `RequestRematchAction`, a stub.

## Moves are decided by the server

The client sends what the player wants to do. The server decides whether it is legal. The client is
never trusted to report an outcome, only to request one.

1. Client calls `RequestMove()`, which sends a ServerRpc.
2. Server validates against its own `BoardState` — the authority.
3. Server broadcasts a ClientRpc carrying the confirmed `MoveCommand`.
4. Every client fires `OnMoveConfirmed` and updates its visuals.

Betrayal needs the same treatment and is easier to get wrong, because its sub-phases look like
client state. Retribution must be validated server-side: whether a Retribution succeeded or failed
is the server's finding, not the client's report. A skip request is an ordinary client request —
before calling `ResolveVoluntaryDefection`, the server confirms `CurrentPhase == RetributionPending`
against its own board, then broadcasts the outcome.

## The clock

The server keeps the only authoritative `ChessClock` and drives `Tick(deltaMs)` from its own time
source. Client clocks are for display and never decide an outcome.

Every `MoveCommand` already carries `WhiteRemainingMsAtMove` and `BlackRemainingMsAtMove`. The
server checks the submitting client's stamp against its own value within a lag-compensation window
of half a second, and rejects any move submitted after server-side expiry outright.

**The clock does not pause during a Betrayal.** Deciding how to execute retribution costs the
player their own time, which is the point — the pressure is what makes the choice cost something.
A player who flags while deciding loses.

Increments are granted by `ChessClock.OnMoveMade()` after the turn has transitioned. Initiating a
Betrayal does not earn one.

## Sketch

An early shape for the executor, kept because it records which calls were expected to cross the
wire rather than how they should be written.

```csharp
public class NetworkMoveExecutor : NetworkBehaviour, IMoveExecutor
{
    public event Action<MoveCommand> OnMoveConfirmed;
    public event Action<Vector2Int, Vector2Int> OnMoveRejected;
    public event Action<Vector2Int> OnPromotionRequired;

    public void RequestMove(Vector2Int from, Vector2Int to) { /* RequestMoveServerRpc(...) */ }
    public void RequestPromotion(ChessPieceType type)       { /* RequestPromotionServerRpc(...) */ }
    public void RequestRetributionSkip()                    { /* RequestRetributionSkipServerRpc(); */ }

    [ServerRpc(RequireOwnership = false)]
    private void RequestMoveServerRpc(int fx, int fy, int tx, int ty, ServerRpcParams rpcParams = default)
    {
        // Validate against the server's own board here.
    }

    [ClientRpc]
    private void ConfirmMoveClientRpc(/* MoveCommandNetData */)
    {
        // Rehydrate into a MoveCommand and raise OnMoveConfirmed.
    }
}
```
