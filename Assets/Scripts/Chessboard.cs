using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChessTheMasterPiece.ChessPiece
{
    [DisallowMultipleComponent]
    public class Chessboard : MonoBehaviour
    {
        #region Constants

        private const int ExpectedPiecePrefabCount = 6; // Pawn..King
        private const int DefaultBoardSize = 8;
        private const float RaycastMaxDistance = 200f;

        #endregion

        #region Inspector Fields

        [Header("Board")]
        [SerializeField] private Material tileMaterial;
        [SerializeField, Range(0.1f, 4f)] private float tileSize = 1f;
        [SerializeField, Min(2)] private int tileCountX = DefaultBoardSize;
        [SerializeField, Min(2)] private int tileCountY = DefaultBoardSize;
        [SerializeField] private float tilesYOffset = 0.0f; // Height offset of tiles from board origin
        [SerializeField] private Vector3 boardCenter = Vector3.zero;

        [Header("Prefabs (order: Pawn,Rook,Knight,Bishop,Queen,King)")]
        [SerializeField] private GameObject[] whiteTeamPrefabs;
        [SerializeField] private GameObject[] blackTeamPrefabs;

        [Header("Piece visuals")]
        [SerializeField] private float pieceYOffset = 0.05f; // Height offset of pieces from tiles (Not Used)
        [SerializeField] private float pieceScaleMultiplier = 1f;
        [SerializeField] private float dragHeight = 1f;
        [SerializeField] private float deathSize = 0.45f;
        [SerializeField] private float deathSpacing = 0.35f;

        [Header("Parents (auto-created if null)")]
        [SerializeField] private Transform tilesParent;
        [SerializeField] private Transform whitePiecesParent;
        [SerializeField] private Transform blackPiecesParent;

        #endregion

        #region Private Fields

        private ChessPiece[,] board;                     // occupancy
        private GameObject[,] tiles;                     // tile game objects
        private Dictionary<Transform, Vector2Int> tileLookup;

        private readonly List<ChessPiece> whiteCaptured = new();
        private readonly List<ChessPiece> blackCaptured = new();

        private List<Vector2Int> currentAvailableMoves = new List<Vector2Int>();

        // Stores the special move type available for the currently dragging piece
        private SpecialMove specialMove = SpecialMove.None;

        // History of moves: [startPos, endPos]
        private List<Vector2Int[]> moveList = new List<Vector2Int[]>();

        private Camera mainCamera;
        private ChessPiece draggingPiece;
        private Vector2Int hoverIndex = new(-1, -1);

        private Transform cachedTransform;
        private Vector3 boardOrigin;                          // bottom-left world corner of tile (0,0)

        private int tileLayer;
        private int highlightLayer;
        private int moveHighlightLayer;
        private int combinedLayerMask;

        private Team playerTeam = Team.White;    // player's chosen side (default to white)
        private Team currentTurn = Team.White;   // whose turn it is (white always starts)

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            cachedTransform = transform;

            CacheLayers();
            CreateParentContainersIfNeeded();

            if (!ValidateConfiguration())
            {
                Debug.LogError("[Chessboard] Validation failed. Initialization aborted.");
                enabled = false;
                return;
            }

            InitializeBoard();
            CreateTiles();

            // If the team selection UI is open, wait for the player to choose and
            // spawn pieces after the choice. Otherwise spawn immediately using the current selection.
            if (TeamSelectionUI.IsOpen)
            {
                // listen for user's selection
                TeamSelectionUI.OnTeamChosen += HandleTeamChosen;
            }
            else
            {
                // UI already closed (or absent) -> use chosen team immediately
                playerTeam = (Team)TeamSelectionUI.ChosenTeam;
                SpawnDefaultSetupForPlayer();
            }
        }

        private void Update()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    Debug.LogError("[Chessboard] Camera.main not found.");
                    return;
                }
            }

            Vector2 pointer = Vector2.zero;
            bool pointerAvailable = false;

            if (Mouse.current != null)
            {
                pointer = Mouse.current.position.ReadValue();
                pointerAvailable = true;
            }
            else
            {
                // fallback to legacy Input (rare). This deliberately avoids noisy warnings.
#pragma warning disable 618
                if (Input.mousePresent)
                {
                    pointer = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                    pointerAvailable = true;
                }
#pragma warning restore 618
            }

            if (!pointerAvailable)
            {
                return;
            }

            HandlePointer(pointer);
        }

        private void OnDestroy()
        {
            // unsubscribe from team selection event
            TeamSelectionUI.OnTeamChosen -= HandleTeamChosen;

            // cleanup generated meshes
            if (tiles == null)
            {
                return;
            }

            for (int x = 0; x < tiles.GetLength(0); x++)
            {
                for (int y = 0; y < tiles.GetLength(1); y++)
                {
                    GameObject tile = tiles[x, y];
                    if (tile == null)
                    {
                        continue;
                    }

                    MeshFilter mf = tile.GetComponent<MeshFilter>();
                    if (mf?.sharedMesh != null)
                    {
                        Destroy(mf.sharedMesh);
                    }
                }
            }
        }

        #endregion

        #region Initialization Helpers

        private void CacheLayers()
        {
            int a = LayerMask.NameToLayer("Tile");
            int b = LayerMask.NameToLayer("Highlight");
            int c = LayerMask.NameToLayer("MoveHighlight");

            if (a == -1) Debug.LogWarning("[Chessboard] Layer 'Tile' not found. Defaulting to layer 0.");
            if (b == -1) Debug.LogWarning("[Chessboard] Layer 'Highlight' not found. Defaulting to layer 0.");
            if (c == -1) Debug.LogWarning("[Chessboard] Layer 'MoveHighlight' not found. Defaulting to layer 0.");

            tileLayer = (a == -1) ? 0 : a;
            highlightLayer = (b == -1) ? 0 : b;
            moveHighlightLayer = (c == -1) ? 0 : c;

            // ensure raycasts will hit tiles, hover highlights and move highlights
            combinedLayerMask = (1 << tileLayer) | (1 << highlightLayer) | (1 << moveHighlightLayer);
        }

        private void CreateParentContainersIfNeeded()
        {
            if (tilesParent == null)
            {
                GameObject go = new GameObject("Tiles");
                go.transform.SetParent(cachedTransform, false);
                tilesParent = go.transform;
            }

            if (whitePiecesParent == null)
            {
                GameObject go = new GameObject("WhitePieces");
                go.transform.SetParent(cachedTransform, false);
                whitePiecesParent = go.transform;
            }

            if (blackPiecesParent == null)
            {
                GameObject go = new GameObject("BlackPieces");
                go.transform.SetParent(cachedTransform, false);
                blackPiecesParent = go.transform;
            }
        }

        /// <summary>
        /// Safe check of inspector configuration.
        /// </summary>
        /// <returns></returns>
        private bool ValidateConfiguration()
        {
            if (tileMaterial == null)
            {
                Debug.LogError("[Chessboard] tileMaterial is not assigned.");
                return false;
            }

            if (tileSize <= 0f)
            {
                Debug.LogError("[Chessboard] tileSize must be > 0.");
                return false;
            }

            if (tileCountX < 2 || tileCountY < 2)
            {
                Debug.LogError("[Chessboard] tileCountX/Y must be >= 2.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Initialize board data structures like (occupancy) board array, tile array and lookup dictionary.
        /// </summary>
        private void InitializeBoard()
        {
            board = new ChessPiece[tileCountX, tileCountY];
            tiles = new GameObject[tileCountX, tileCountY];
            tileLookup = new Dictionary<Transform, Vector2Int>(tileCountX * tileCountY);

            float halfWidth = tileCountX * tileSize * 0.5f;
            float halfHeight = tileCountY * tileSize * 0.5f;

            boardOrigin = boardCenter - new Vector3(halfWidth, 0f, halfHeight);
        }

        #endregion

        #region Tile Creation & Utilities

        /// <summary>
        /// Create tiles (mesh + renderer + collider) and populate lookup.
        /// </summary>
        private void CreateTiles()
        {
            for (int x = 0; x < tileCountX; x++)
            {
                for (int y = 0; y < tileCountY; y++)
                {
                    GameObject tile = CreateTile(x, y);
                    tiles[x, y] = tile;
                    tileLookup[tile.transform] = new Vector2Int(x, y);
                }
            }
        }

        /// <summary>
        /// Create a single tile at (x,y).
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private GameObject CreateTile(int x, int y)
        {
            GameObject tileGO = new GameObject($"Tile_{x}_{y}");
            tileGO.transform.SetParent(tilesParent, false);

            Vector3 worldPos = boardOrigin + new Vector3(x * tileSize + tileSize * 0.5f, tilesYOffset, y * tileSize + tileSize * 0.5f);
            tileGO.transform.position = worldPos;

            Mesh mesh = new Mesh { name = $"TileMesh_{x}_{y}" };
            Vector3 half = new Vector3(tileSize * 0.5f, 0f, tileSize * 0.5f);

            Vector3 v0 = new Vector3(-half.x, 0f, -half.z);
            Vector3 v1 = new Vector3(-half.x, 0f, half.z);
            Vector3 v2 = new Vector3(half.x, 0f, -half.z);
            Vector3 v3 = new Vector3(half.x, 0f, half.z);

            mesh.vertices = new Vector3[] { v0, v1, v2, v3 };
            mesh.triangles = new int[] { 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateNormals();

            MeshFilter mf = tileGO.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = tileGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = tileMaterial;

            BoxCollider bc = tileGO.AddComponent<BoxCollider>();
            bc.size = new Vector3(tileSize, 0.05f, tileSize);
            bc.center = Vector3.zero;

            tileGO.layer = tileLayer;

            return tileGO;
        }

        private Vector3 GetTileCenter(Vector2Int index)
        {
            if (!IsValidIndex(index))
            {
                Debug.LogWarning($"[Chessboard] GetTileCenter invalid index {index}. Returning Vector3.zero.");
                return Vector3.zero;
            }

            return boardOrigin + new Vector3(index.x * tileSize + tileSize * 0.5f, tilesYOffset, index.y * tileSize + tileSize * 0.5f);
        }

        public Vector3 GetTileCenter(int x, int y)
        {
            return GetTileCenter(new Vector2Int(x, y));
        }

        private bool IsValidIndex(Vector2Int idx)
        {
            return idx.x >= 0 && idx.x < tileCountX && idx.y >= 0 && idx.y < tileCountY;
        }

        #endregion

        #region Spawning / Placement

        /// <summary>
        /// Spawn default chess setup (standard 8x8) with white at bottom.
        /// </summary>
        [Obsolete("Use SpawnDefaultSetupForPlayer instead to respect player's team choice.")]
        private void SpawnDefaultSetup()
        {
            ClearBoardPieces();

            ChessPieceType[] majors = new ChessPieceType[]
            {
                ChessPieceType.Rook,
                ChessPieceType.Knight,
                ChessPieceType.Bishop,
                ChessPieceType.Queen,
                ChessPieceType.King,
                ChessPieceType.Bishop,
                ChessPieceType.Knight,
                ChessPieceType.Rook
            };

            int columns = Math.Min(tileCountX, majors.Length);

            // White major row (y = 0)
            for (int x = 0; x < columns; x++)
            {
                SpawnPieceAt(majors[x], Team.White, new Vector2Int(x, 0), whiteTeamPrefabs, whitePiecesParent);
            }

            // White pawns (y = 1)
            int whitePawnRank = Mathf.Clamp(1, 0, tileCountY - 1);
            for (int x = 0; x < tileCountX; x++)
            {
                SpawnPieceAt(ChessPieceType.Pawn, Team.White, new Vector2Int(x, whitePawnRank), whiteTeamPrefabs, whitePiecesParent);
            }

            // Black pawns (y = tileCountY - 2)
            int blackPawnRank = Mathf.Clamp(tileCountY - 2, 0, tileCountY - 1);
            for (int x = 0; x < tileCountX; x++)
            {
                SpawnPieceAt(ChessPieceType.Pawn, Team.Black, new Vector2Int(x, blackPawnRank), blackTeamPrefabs, blackPiecesParent);
            }

            // Black major row (y = tileCountY - 1)
            int blackMajorRank = Mathf.Clamp(tileCountY - 1, 0, tileCountY - 1);
            for (int x = 0; x < columns; x++)
            {
                SpawnPieceAt(majors[x], Team.Black, new Vector2Int(x, blackMajorRank), blackTeamPrefabs, blackPiecesParent);
            }
        }

        public void ClearBoardPieces()
        {
            if (board == null)
            {
                InitializeBoard();
            }

            for (int x = 0; x < board.GetLength(0); x++)
            {
                for (int y = 0; y < board.GetLength(1); y++)
                {
                    ChessPiece cp = board[x, y];

                    if (cp != null)
                    {
                        Destroy(cp.gameObject);
                        board[x, y] = null;
                    }
                }
            }

            whiteCaptured.Clear();
            blackCaptured.Clear();
            moveList.Clear();
        }

        private ChessPiece SpawnPieceAt(ChessPieceType type, Team team, Vector2Int index, GameObject[] prefabArray, Transform parent)
        {
            if (!IsValidIndex(index))
            {
                Debug.LogWarning($"[Chessboard] SpawnPieceAt: invalid index {index} for {type}.");
                return null;
            }

            if (prefabArray == null || prefabArray.Length < ExpectedPiecePrefabCount)
            {
                Debug.LogWarning($"[Chessboard] SpawnPieceAt: prefab array missing or incomplete for {type}.");
                return null;
            }

            int prefabIndex = (int)type - 1;

            if (prefabIndex < 0 || prefabIndex >= prefabArray.Length)
            {
                Debug.LogWarning($"[Chessboard] SpawnPieceAt: invalid prefab index for {type}.");
                return null;
            }

            GameObject prefab = prefabArray[prefabIndex];

            if (prefab == null)
            {
                Debug.LogWarning($"[Chessboard] SpawnPieceAt: prefab for {type} is null. Skipping.");
                return null;
            }

            Vector3 pos = GetTileCenter(index);
            pos.y = tilesYOffset + pieceYOffset;

            GameObject go = Instantiate(prefab, pos, Quaternion.identity, parent);

            go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, pieceScaleMultiplier);

            ChessPiece cp = go.GetComponent<ChessPiece>();

            if (cp == null)
            {
                Debug.LogWarning($"[Chessboard] SpawnPieceAt: prefab {go.name} missing ChessPiece. Destroying.");
                Destroy(go);
                return null;
            }

            cp.type = type;
            cp.team = (int)team;
            cp.currentX = index.x;
            cp.currentY = index.y;
            cp.initialY = index.y;
            cp.moveDirection = (team == playerTeam) ? +1 : -1;

            try
            {
                if (team == playerTeam)
                {
                    go.transform.rotation = Quaternion.identity;
                }
                else
                {
                    go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Chessboard] SpawnPieceAt: failed to set rotation for {go.name}: {ex.Message}");
            }

            go.name = $"{team}_{type}_{index.x}_{index.y}";

            board[index.x, index.y] = cp;

            return cp;
        }

        /// <summary>
        /// Event handler called when the player chooses a team from the UI.
        /// </summary>
        private void HandleTeamChosen(int chosenTeam)
        {
            // cache player's choice
            playerTeam = (Team)chosenTeam;

            // unsubscribe (we only need the event once)
            TeamSelectionUI.OnTeamChosen -= HandleTeamChosen;

            // spawn pieces with the chosen side on player's side
            SpawnDefaultSetupForPlayer();
        }

        /// <summary>
        /// Spawn default chess setup (standard 8x8) respecting player's chosen team.
        /// </summary>
        private void SpawnDefaultSetupForPlayer()
        {
            ClearBoardPieces();

            ChessPieceType[] majors = new ChessPieceType[]
            {
                ChessPieceType.Rook,
                ChessPieceType.Knight,
                ChessPieceType.Bishop,
                ChessPieceType.Queen,
                ChessPieceType.King,
                ChessPieceType.Bishop,
                ChessPieceType.Knight,
                ChessPieceType.Rook
            };

            int columns = Math.Min(tileCountX, majors.Length);

            Team bottomTeam = playerTeam;
            Team topTeam = (playerTeam == Team.White) ? Team.Black : Team.White;

            for (int x = 0; x < columns; x++)
            {
                SpawnPieceAt(
                    majors[x],
                    bottomTeam,
                    new Vector2Int(x, 0),
                    bottomTeam == Team.White ? whiteTeamPrefabs : blackTeamPrefabs,
                    bottomTeam == Team.White ? whitePiecesParent : blackPiecesParent);
            }

            int bottomPawnRank = Mathf.Clamp(1, 0, tileCountY - 1);

            for (int x = 0; x < tileCountX; x++)
            {
                SpawnPieceAt(
                    ChessPieceType.Pawn,
                    bottomTeam,
                    new Vector2Int(x, bottomPawnRank),
                    bottomTeam == Team.White ? whiteTeamPrefabs : blackTeamPrefabs,
                    bottomTeam == Team.White ? whitePiecesParent : blackPiecesParent);
            }

            int topPawnRank = Mathf.Clamp(tileCountY - 2, 0, tileCountY - 1);

            for (int x = 0; x < tileCountX; x++)
            {
                SpawnPieceAt(
                    ChessPieceType.Pawn,
                    topTeam,
                    new Vector2Int(x, topPawnRank),
                    topTeam == Team.White ? whiteTeamPrefabs : blackTeamPrefabs,
                    topTeam == Team.White ? whitePiecesParent : blackPiecesParent);
            }

            int topMajorRank = Mathf.Clamp(tileCountY - 1, 0, tileCountY - 1);

            for (int x = 0; x < columns; x++)
            {
                SpawnPieceAt(
                    majors[x],
                    topTeam,
                    new Vector2Int(x, topMajorRank),
                    topTeam == Team.White ? whiteTeamPrefabs : blackTeamPrefabs,
                    topTeam == Team.White ? whitePiecesParent : blackPiecesParent);
            }

            currentTurn = Team.White;
        }

        #endregion

        #region Input / Drag & Drop

        /// <summary>
        /// Handle pointer input for hover, drag start, drag end.
        /// </summary>
        /// <param name="screenPosition"></param>
        private void HandlePointer(Vector2 screenPosition)
        {
            // Block all pointer handling while team selection UI is open
            if (TeamSelectionUI.IsOpen)
            {
                return;
            }

            Ray ray = mainCamera.ScreenPointToRay(screenPosition);

            if (Physics.Raycast(ray, out RaycastHit hit, RaycastMaxDistance, combinedLayerMask))
            {
                Vector2Int hitIndex = FindTileIndexUsingHitInfo(hit.transform);
                if (hitIndex.x == -1)
                {
                    return;
                }

                UpdateHover(hitIndex);

                if (WasPointerPressed())
                {
                    TryStartDrag(hitIndex);
                }

                if (WasPointerReleased())
                {
                    TryDrop(hitIndex);
                }
            }
            else
            {
                ClearHover();

                if (WasPointerReleased())
                {
                    if (draggingPiece != null)
                    {
                        ReturnPieceToTile(draggingPiece);
                        draggingPiece = null;

                        // clear highlights
                        ClearAvailableMoveHighlights();
                    }
                }
            }

            if (draggingPiece != null)
            {
                Plane p = new Plane(Vector3.up, Vector3.up * tilesYOffset);
                if (p.Raycast(ray, out float enter))
                {
                    Vector3 world = ray.GetPoint(enter) + Vector3.up * dragHeight;
                    draggingPiece.SetPosition(world);
                }
            }
        }

        /// <summary>
        /// Find tile index from a hit Transform by traversing up the hierarchy.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private Vector2Int FindTileIndexUsingHitInfo(Transform t)
        {
            Transform cur = t;
            int safety = 0;

            while (cur != null && safety++ < 16)
            {
                if (tileLookup.TryGetValue(cur, out Vector2Int idx))
                {
                    return idx;
                }

                cur = cur.parent;
            }

            return new Vector2Int(-1, -1);
        }

        /// <summary>
        /// Update hover highlight to the given index.
        /// </summary>
        /// <param name="idx"></param>
        private void UpdateHover(Vector2Int idx)
        {
            if (hoverIndex == idx)
            {
                return;
            }

            if (IsValidIndex(hoverIndex))
            {
                SetTileHighlight(hoverIndex, false);
            }

            hoverIndex = idx;
            SetTileHighlight(hoverIndex, true);
        }

        private void ClearHover()
        {
            if (IsValidIndex(hoverIndex))
            {
                SetTileHighlight(hoverIndex, false);
            }

            hoverIndex = new Vector2Int(-1, -1);
        }

        private bool WasPointerPressed()
        {
            if (Mouse.current != null)
            {
                return Mouse.current.leftButton.wasPressedThisFrame;
            }

#pragma warning disable 618
            return Input.GetMouseButtonDown(0);
#pragma warning restore 618
        }

        private bool WasPointerReleased()
        {
            if (Mouse.current != null)
            {
                return Mouse.current.leftButton.wasReleasedThisFrame;
            }

#pragma warning disable 618
            return Input.GetMouseButtonUp(0);
#pragma warning restore 618
        }

        /// <summary>
        /// Attempt to start dragging the piece at the given index.
        /// </summary>
        /// <param name="idx"></param>
        private void TryStartDrag(Vector2Int idx)
        {
            if (!IsValidIndex(idx))
            {
                return;
            }

            ChessPiece cp = board[idx.x, idx.y];

            if (cp == null)
            {
                return;
            }

            if (TeamSelectionUI.IsOpen)
            {
                // Selection UI is open — block any interaction.
                return;
            }

            Team pieceTeam = (Team)cp.team;

            // Allow dragging only when it's the piece's team's turn.
            if (pieceTeam != currentTurn)
            {
                Debug.Log("[Chessboard] TryStartDrag: It is not this team's turn.");
                return;
            }

            // Clear any previous move highlights.
            ClearAvailableMoveHighlights();

            // Compute available moves while the piece is still present on the board.
            List<Vector2Int> avail = cp.GetAvailableMoves(board, tileCountX, tileCountY) ?? new List<Vector2Int>();

            // Get Special Moves
            specialMove = cp.GetSpecialMoves(board, moveList, avail);

            // Begin dragging: remove from board occupancy and store reference.
            draggingPiece = cp;
            board[idx.x, idx.y] = null;

            // Highlight available moves for this piece.
            HighlightMoves(avail);
        }

        /// <summary>
        /// Attempt to drop the currently dragging piece at the given index.
        /// </summary>
        /// <param name="idx"></param>
        private void TryDrop(Vector2Int idx)
        {
            if (draggingPiece == null)
            {
                return;
            }

            Vector2Int from = new Vector2Int(draggingPiece.currentX, draggingPiece.currentY);
            Vector2Int to = idx;

            // invalid target -> return piece and clear highlights
            if (!IsValidIndex(to))
            {
                ReturnPieceToTile(draggingPiece);

                draggingPiece = null;

                ClearAvailableMoveHighlights();

                return;
            }

            // Only allow dropping onto a valid move
            if (!ContainsValidMoves(currentAvailableMoves, to))
            {
                // invalid move -> revert
                if (IsValidIndex(from))
                {
                    board[from.x, from.y] = draggingPiece;
                }

                ReturnPieceToTile(draggingPiece);

                draggingPiece = null;

                ClearAvailableMoveHighlights();

                return;
            }

            // Try to perform the move (MoveTo handles captures)
            bool moved = MoveTo(draggingPiece, to.x, to.y);

            if (moved)
            {
                // Successfully moved: switch turn to the other side
                currentTurn = (currentTurn == Team.White) ? Team.Black : Team.White;
            }
            else
            {
                // Move failed (e.g., trying to capture own piece) -> revert safely
                if (IsValidIndex(from))
                {
                    board[from.x, from.y] = draggingPiece;
                }

                ReturnPieceToTile(draggingPiece);
            }

            draggingPiece = null;

            // clear available-move highlights now that drag ended
            ClearAvailableMoveHighlights();
        }

        /// <summary>
        /// Return a piece to its original tile on the board.
        /// </summary>
        /// <param name="piece"></param>
        private void ReturnPieceToTile(ChessPiece piece)
        {
            if (piece == null)
            {
                return;
            }

            Vector2Int idx = new(piece.currentX, piece.currentY);

            if (!IsValidIndex(idx))
            {
                Debug.LogWarning($"[Chessboard] ReturnPieceToTile: piece {piece.name} had invalid coords {idx}. Placing at (0,0).");
                idx = new Vector2Int(0, 0);
            }

            board[idx.x, idx.y] = piece;

            Vector3 center = GetTileCenter(idx);
            center.y = tilesYOffset + pieceYOffset;
            piece.SetPosition(center, force: true);
        }

        #endregion

        #region Movement & Capture

        /// <summary>
        /// Moves a piece to the target tile (x,y). 
        /// Handles updating the board array, visual position, and capturing opponents.
        /// </summary>
        public bool MoveTo(ChessPiece piece, int x, int y)
        {
            if (piece == null)
            {
                Debug.LogWarning("[Chessboard] MoveTo: Piece is null.");
                return false;
            }

            Vector2Int targetIndex = new Vector2Int(x, y);

            // Validate Target
            if (!IsValidIndex(targetIndex))
            {
                Debug.LogError($"[Chessboard] MoveTo: Target {targetIndex} is out of bounds.");
                return false;
            }

            // Check for Occupancy / Capture
            ChessPiece occupant = board[x, y];
            if (occupant != null)
            {
                // Prevent capturing own team
                if (occupant.team == piece.team)
                {
                    return false;
                }

                CapturePiece(occupant);
            }

            // Execute Move (Update Data)
            Vector2Int previousPosition = new Vector2Int(piece.currentX, piece.currentY);

            board[x, y] = piece;
            piece.currentX = x;
            piece.currentY = y;

            // Execute Move (Update Visuals)
            Vector3 worldPos = GetTileCenter(x, y);
            worldPos.y = tilesYOffset + pieceYOffset;
            piece.SetPosition(worldPos, force: true);

            // Record History
            moveList.Add(new Vector2Int[] { previousPosition, targetIndex });

            // Handle Special Mechanics (En Passant, etc.)
            if (specialMove != SpecialMove.None)
            {
                ProcessSpecialMove();
            }

            return true;
        }

        private void CapturePiece(ChessPiece victim)
        {
            if (victim == null)
            {
                return;
            }

            // Remove from board data
            if (board[victim.currentX, victim.currentY] == victim)
            {
                board[victim.currentX, victim.currentY] = null;
            }

            // Add to captured list
            bool isWhite = victim.team == (int)Team.White;
            List<ChessPiece> capturedList = isWhite ? whiteCaptured : blackCaptured;
            capturedList.Add(victim);

            // --- Calculate "Death Pile" Visual Position ---

            int majorRowIndex = (victim.team == (int)Team.White) ? 0 : tileCountY - 1;
            float rowCenterZ = boardOrigin.z + majorRowIndex * tileSize + tileSize * 0.5f;

            float outsideBoardMargin = tileSize * 0.5f;
            float stackSpacing = Mathf.Max(0.01f, deathSpacing);
            int stackIndex = capturedList.Count - 1;

            // Determine X position (Right side for White, Left side for Black)
            float xPos = isWhite
                ? boardOrigin.x + tileCountX * tileSize + outsideBoardMargin
                : boardOrigin.x - outsideBoardMargin;

            // Determine Z position (Stacking direction)
            // White pieces stack upwards (+Z), Black pieces stack downwards (-Z)
            float zPos;
            if (isWhite)
            {
                float startZ = rowCenterZ - (stackIndex * 0.5f * stackSpacing);
                zPos = startZ + (stackIndex * stackSpacing);
            }
            else
            {
                float startZ = rowCenterZ + (stackIndex * 0.5f * stackSpacing);
                zPos = startZ - (stackIndex * stackSpacing);
            }

            float yPos = tilesYOffset + pieceYOffset;
            Vector3 finalPos = new Vector3(xPos, yPos, zPos);
            Vector3 boardCenterPos = boardOrigin + new Vector3(tileCountX * tileSize * 0.5f, 0f, tileCountY * tileSize * 0.5f);

            // Apply visual changes
            victim.SetScale(Vector3.one * deathSize, force: true);
            PlaceCapturedPiece(victim, finalPos, boardCenterPos);
        }

        private void PlaceCapturedPiece(ChessPiece piece, Vector3 targetPosition, Vector3 faceTarget)
        {
            piece.SetPosition(targetPosition, force: true);

            // Calculate rotation to face the center of the board
            Vector3 direction = (faceTarget - targetPosition).normalized;
            direction.y = 0; // Keep piece upright

            if (direction == Vector3.zero) direction = Vector3.forward;

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            piece.transform.rotation = targetRotation;
        }

        #endregion

        #region Special Moves

        private void ProcessSpecialMove()
        {
            if (specialMove == SpecialMove.EnPassant)
            {
                ProcessEnPassant();
            }
        }

        private void ProcessEnPassant()
        {
            // En Passant has already happened physically in MoveTo (the pawn moved diagonally).
            // Now we must find and remove the enemy pawn that was "passed".

            Vector2Int[] myLastMove = moveList[moveList.Count - 1];
            Vector2Int from = myLastMove[0];
            Vector2Int to = myLastMove[1];

            // Confirm the move was diagonal (change in X) and vertical (change in Y)
            // This distinguishes it from a simple forward move.
            bool isDiagonalMove = Mathf.Abs(to.x - from.x) == 1 && Mathf.Abs(to.y - from.y) == 1;

            if (!isDiagonalMove)
            {
                return;
            }

            // The enemy pawn is located at the destination X, but the starting Y.
            // (e.g., if we moved from 3,4 to 2,5... the enemy is at 2,4)
            Vector2Int enemyPawnPos = new Vector2Int(to.x, from.y);

            if (!IsValidIndex(enemyPawnPos))
            {
                return;
            }

            ChessPiece enemyPawn = board[enemyPawnPos.x, enemyPawnPos.y];

            // Sanity check: Ensure we are actually capturing an enemy pawn
            if (enemyPawn != null &&
                enemyPawn.type == ChessPieceType.Pawn &&
                enemyPawn.team != (int)currentTurn) // Note: currentTurn has already flipped in TryDrop, so we check against "Not Current" or strictly "!= My Team"
            {
                // We access the piece at the destination (which is us) to check our own team
                ChessPiece myPiece = board[to.x, to.y];
                if (myPiece != null && enemyPawn.team != myPiece.team)
                {
                    CapturePiece(enemyPawn);
                }
            }
        }

        #endregion

        #region Chesspiece Available Moves

        /// <summary>
        /// Highlight the given moves by setting the corresponding tiles to the MoveHighlight layer.
        /// Existing hover/highlight state is respected: hover (Highlight layer) has priority.
        /// </summary>
        private void HighlightMoves(List<Vector2Int> moves)
        {
            if (moves == null || moves.Count == 0)
            {
                return;
            }

            // store available moves list (replace any previous)
            currentAvailableMoves = new List<Vector2Int>(moves);

            foreach (var idx in currentAvailableMoves)
            {
                if (!IsValidIndex(idx))
                    continue;

                // if this tile is currently hovered, leave it as hover highlight (do not override)
                if (hoverIndex == idx)
                {
                    SetTileHighlight(idx, true); // ensures hover layer is applied
                    continue;
                }

                GameObject tile = tiles[idx.x, idx.y];
                if (tile == null)
                {
                    Debug.LogWarning($"[Chessboard] HighlightMoves: tile at {idx} is null.");
                    continue;
                }

                tile.layer = moveHighlightLayer;
            }
        }

        private void ClearAvailableMoveHighlights()
        {
            if (currentAvailableMoves == null || currentAvailableMoves.Count == 0)
            {
                return;
            }

            foreach (var idx in currentAvailableMoves)
            {
                if (!IsValidIndex(idx))
                    continue;

                // if this index is currently hovered, keep the hover layer
                if (hoverIndex == idx)
                {
                    SetTileHighlight(idx, true);
                    continue;
                }

                GameObject tile = tiles[idx.x, idx.y];
                if (tile == null)
                    continue;

                tile.layer = tileLayer;
            }

            currentAvailableMoves.Clear();
        }

        /// <summary>
        /// Checks whether (x,y) exists inside the allowed moves list.
        /// </summary>
        private bool ContainsValidMoves(List<Vector2Int> moves, Vector2Int pos)
        {
            if (moves == null || moves.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].x == pos.x && moves[i].y == pos.y)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Utilities / Queries

        private void SetTileHighlight(Vector2Int idx, bool highlight)
        {
            if (!IsValidIndex(idx))
            {
                return;
            }

            GameObject tile = tiles[idx.x, idx.y];
            if (tile == null)
            {
                Debug.LogWarning($"[Chessboard] SetTileHighlight: tile at {idx} is null.");
                return;
            }

            if (highlight)
            {
                // hover highlight takes precedence
                tile.layer = highlightLayer;
                return;
            }

            // clearing hover: if this tile is part of currentAvailableMoves, restore move highlight,
            // otherwise revert to base tile layer.
            bool isMoveHighlighted = currentAvailableMoves != null && currentAvailableMoves.Contains(idx);
            tile.layer = isMoveHighlighted ? moveHighlightLayer : tileLayer;
        }

        public ChessPiece GetPieceAt(int x, int y)
        {
            Vector2Int idx = new Vector2Int(x, y);
            if (!IsValidIndex(idx))
            {
                return null;
            }

            return board[x, y];
        }

        public bool IsOccupied(int x, int y)
        {
            return GetPieceAt(x, y) != null;
        }

        #endregion

        #region Types

        private enum Team
        {
            White = 0,
            Black = 1
        }

        #endregion
    }
}