// Multiplayer move handling — not yet implemented.
//
// When this is built, all move validation must happen on the server.
// The client only sends what the player wants to do; the server decides if it's legal.
//
// Rough flow:
//   1. Client calls RequestMove() → sends a ServerRpc
//   2. Server validates against its own BoardState (the authority)
//   3. Server broadcasts a ClientRpc with the confirmed MoveCommand
//   4. All clients fire OnMoveConfirmed and update their visuals
//
// TODO (Betrayal + Network): The Retribution sub-phase must be fully validated server-side. The client should never be trusted to report whether a Retribution succeeded or failed.
//
// using System;
// using Unity.Netcode;
// using ChessTheBetrayal.Core.Data;
// using ChessTheBetrayal.Core.Engine;
// using ChessTheBetrayal.Gameplay;
//
// namespace ChessTheBetrayal.Network
// {
//     public class NetworkMoveExecutor : NetworkBehaviour, IMoveExecutor
//     {
//         public event Action<MoveCommand> OnMoveConfirmed;
//         public event Action<Vector2Int, Vector2Int> OnMoveRejected;
//         public event Action<Vector2Int> OnPromotionRequired;
//
//         public void RequestMove(Vector2Int from, Vector2Int to)
//         {
//             // TODO: Serialize custom struct or pass primitives
//             // RequestMoveServerRpc(from.x, from.y, to.x, to.y);
//         }
//
//         public void RequestPromotion(ChessPieceType type)
//         {
//             // RequestPromotionServerRpc((int)type);
//         }
//
//         [ServerRpc(RequireOwnership = false)]
//         private void RequestMoveServerRpc(int fx, int fy, int tx, int ty, ServerRpcParams rpcParams = default) 
//         { 
//             // Server-side validation against Server's LiveBoard goes here
//         }
//
//         [ClientRpc]
//         private void ConfirmMoveClientRpc( /* MoveCommandNetData */ ) 
//         { 
//             // Rehydrate network struct back into MoveCommand and invoke
//             // OnMoveConfirmed?.Invoke(cmd);
//         }
//     }
// }
