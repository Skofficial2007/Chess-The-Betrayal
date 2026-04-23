using System.Collections.Generic;
using UnityEngine;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.Controllers;

namespace ChessTheMasterPiece.View
{
    /// <summary>
    /// The "Eyes" of the game. Strictly handles rendering meshes, spawning prefabs,
    /// highlighting tiles, and playing smooth animations.
    /// Has ZERO understanding of chess rules - just listens to GameManager and animates.
    /// </summary>
    public class BoardVisuals : MonoBehaviour
    {
        #region Singleton

        public static BoardVisuals Instance { get; private set; }

        #endregion

        #region Inspector Fields

        [Header("Board Geometry")]
        [SerializeField] private Material tileMaterial;
        [SerializeField, Range(0.1f, 4f)] private float tileSize = 1f;
        [SerializeField] private float tilesYOffset = 0.0f;
        [SerializeField] private Vector3 boardCenter = Vector3.zero;

        [Header("Prefabs (Order: Pawn, Rook, Knight, Bishop, Queen, King)")]
        [SerializeField] private GameObject[] whiteTeamPrefabs;
        [SerializeField] private GameObject[] blackTeamPrefabs;

        [Header("Piece Visuals")]
        [SerializeField] private float pieceYOffset = 0.05f;
        [SerializeField] private float pieceScaleMultiplier = 1f;
        [SerializeField] private float deathSize = 0.45f;
        [SerializeField] private float deathSpacing = 0.35f;

        [Header("Hierarchy Containers")]
        [SerializeField] private Transform tilesParent;
        [SerializeField] private Transform whitePiecesParent;
        [SerializeField] private Transform blackPiecesParent;

        #endregion

        #region Private Fields

        // Maps logical grid coordinates to visual GameObjects
        private Dictionary<ChessTheMasterPiece.Data.Vector2Int, ChessPiece> visualPieces = new Dictionary<ChessTheMasterPiece.Data.Vector2Int, ChessPiece>();

        // Tile meshes and lookup for raycasting
        private GameObject[,] tiles;
        private Dictionary<Transform, ChessTheMasterPiece.Data.Vector2Int> tileLookup = new Dictionary<Transform, ChessTheMasterPiece.Data.Vector2Int>();

        // Death piles
        private List<ChessPiece> deadWhitePieces = new List<ChessPiece>();
        private List<ChessPiece> deadBlackPieces = new List<ChessPiece>();

        // Highlighting state
        private ChessTheMasterPiece.Data.Vector2Int hoverIndex = ChessTheMasterPiece.Data.Vector2Int.Invalid;
        private List<ChessTheMasterPiece.Data.Vector2Int> currentLegalHighlights = new List<ChessTheMasterPiece.Data.Vector2Int>();

        // Cached values
        private Vector3 boardOrigin;
        private int tileCountX, tileCountY;
        private int tileLayer, highlightLayer, moveHighlightLayer;

        #endregion

        #region Public Properties

        public float TileYOffset => tilesYOffset;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            CacheLayers();
            CreateContainers();
        }

        private void Start()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogError("[BoardVisuals] GameManager.Instance is null!");
                return;
            }

            // Subscribe to GameManager events
            GameManager.Instance.OnGameStarted += HandleGameStarted;
            GameManager.Instance.OnMoveExecuted += AnimateMove;
            GameManager.Instance.OnGameReset += ClearAllVisuals;
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStarted -= HandleGameStarted;
                GameManager.Instance.OnMoveExecuted -= AnimateMove;
                GameManager.Instance.OnGameReset -= ClearAllVisuals;
            }
        }

        #endregion

        #region Setup & Mesh Generation

        /// <summary>
        /// Called when a new game starts. Sets up the board and spawns pieces.
        /// </summary>
        private void HandleGameStarted(BoardState initialBoard)
        {
            ClearAllVisuals();

            tileCountX = initialBoard.TileCountX;
            tileCountY = initialBoard.TileCountY;

            // Calculate board origin (bottom-left corner)
            float halfWidth = tileCountX * tileSize * 0.5f;
            float halfHeight = tileCountY * tileSize * 0.5f;
            boardOrigin = boardCenter - new Vector3(halfWidth, 0f, halfHeight);

            // Generate tile meshes if they don't exist
            if (tiles == null || tiles.GetLength(0) != tileCountX || tiles.GetLength(1) != tileCountY)
            {
                GenerateTileMeshes();
            }

            // Spawn all pieces based on board state
            SpawnPiecesFromData(initialBoard);
        }

        /// <summary>
        /// Generates the 3D tile meshes and colliders for the board.
        /// </summary>
        private void GenerateTileMeshes()
        {
            tiles = new GameObject[tileCountX, tileCountY];
            tileLookup.Clear();

            for (int x = 0; x < tileCountX; x++)
            {
                for (int y = 0; y < tileCountY; y++)
                {
                    GameObject tileGO = new GameObject($"Tile_{x}_{y}");
                    tileGO.transform.SetParent(tilesParent, false);

                    // Position at tile center
                    Vector3 worldPos = boardOrigin + new Vector3(
                        x * tileSize + tileSize * 0.5f,
                        tilesYOffset,
                        y * tileSize + tileSize * 0.5f
                    );
                    tileGO.transform.position = worldPos;

                    // Create mesh
                    Mesh mesh = new Mesh { name = $"TileMesh_{x}_{y}" };
                    float half = tileSize * 0.5f;
                    mesh.vertices = new Vector3[]
                    {
                        new Vector3(-half, 0f, -half),
                        new Vector3(-half, 0f, half),
                        new Vector3(half, 0f, -half),
                        new Vector3(half, 0f, half)
                    };
                    mesh.triangles = new int[] { 0, 1, 2, 1, 3, 2 };
                    mesh.RecalculateNormals();

                    // Add mesh components
                    tileGO.AddComponent<MeshFilter>().sharedMesh = mesh;
                    tileGO.AddComponent<MeshRenderer>().sharedMaterial = tileMaterial;

                    // Add collider for raycasting
                    BoxCollider bc = tileGO.AddComponent<BoxCollider>();
                    bc.size = new Vector3(tileSize, 0.05f, tileSize);

                    // Set layer
                    tileGO.layer = tileLayer;

                    // Store references
                    tiles[x, y] = tileGO;
                    tileLookup[tileGO.transform] = new ChessTheMasterPiece.Data.Vector2Int(x, y);
                }
            }
        }

        /// <summary>
        /// Spawns visual piece GameObjects for all pieces in the board state.
        /// </summary>
        private void SpawnPiecesFromData(BoardState board)
        {
            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData data = board.GetPiece(x, y);
                    if (data != null)
                    {
                        SpawnSinglePiece(data, new ChessTheMasterPiece.Data.Vector2Int(x, y));
                    }
                }
            }
        }

        /// <summary>
        /// Instantiates a single piece GameObject at the specified position.
        /// </summary>
        private void SpawnSinglePiece(PieceData data, ChessTheMasterPiece.Data.Vector2Int pos)
        {
            // Select prefab array and parent based on team
            GameObject[] prefabs = data.Team == Team.White ? whiteTeamPrefabs : blackTeamPrefabs;
            Transform parent = data.Team == Team.White ? whitePiecesParent : blackPiecesParent;

            // Get prefab index (ChessPieceType enum starts at 1)
            int index = (int)data.Type - 1;

            if (index < 0 || index >= prefabs.Length || prefabs[index] == null)
            {
                Debug.LogError($"[BoardVisuals] Invalid prefab for {data.Team} {data.Type}");
                return;
            }

            // Calculate world position
            Vector3 worldPos = GetTileCenter(pos.x, pos.y);
            worldPos.y += pieceYOffset;

            // Instantiate
            GameObject go = Instantiate(prefabs[index], worldPos, Quaternion.identity, parent);
            go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, pieceScaleMultiplier);

            // Rotate enemy pieces 180 degrees to face player
            if (data.MoveDirection == -1)
            {
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            }

            // Configure visual component
            ChessPiece visualPiece = go.GetComponent<ChessPiece>();
            if (visualPiece == null)
            {
                visualPiece = go.AddComponent<ChessPiece>();
            }
            visualPiece.team = data.Team;
            visualPiece.type = data.Type;

            // Store in lookup
            visualPieces[pos] = visualPiece;
        }

        /// <summary>
        /// Destroys all visual pieces and clears state.
        /// </summary>
        private void ClearAllVisuals()
        {
            // Destroy active pieces
            foreach (var piece in visualPieces.Values)
            {
                if (piece != null) Destroy(piece.gameObject);
            }

            // Destroy dead pieces
            foreach (var dead in deadWhitePieces)
            {
                if (dead != null) Destroy(dead.gameObject);
            }
            foreach (var dead in deadBlackPieces)
            {
                if (dead != null) Destroy(dead.gameObject);
            }

            // Clear collections
            visualPieces.Clear();
            deadWhitePieces.Clear();
            deadBlackPieces.Clear();

            // Clear highlights
            ClearLegalMoveHighlights();
            ClearHoverHighlight();
        }

        #endregion

        #region Animations & Movement Execution

        /// <summary>
        /// Reads a MoveCommand and triggers all necessary visual updates.
        /// This is the heart of the visual layer - it just reads commands and animates.
        /// </summary>
        private void AnimateMove(MoveCommand move)
        {
            // Safety check for invalid commands
            if (move.PieceType == ChessPieceType.None) return;

            // 1. Handle Captures
            if (move.IsCapture)
            {
                ChessTheMasterPiece.Data.Vector2Int capturePos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue
                    ? move.EnPassantCapturePosition.Value
                    : move.EndPosition;

                if (visualPieces.TryGetValue(capturePos, out ChessPiece deadPiece))
                {
                    visualPieces.Remove(capturePos);
                    AnimateDeath(deadPiece);
                }
            }

            // 2. Move the Primary Piece
            if (visualPieces.TryGetValue(move.StartPosition, out ChessPiece movingPiece))
            {
                visualPieces.Remove(move.StartPosition);

                // Handle Promotion: destroy old piece and spawn new one
                if (move.IsPromotion)
                {
                    Destroy(movingPiece.gameObject);

                    PieceData promotedData = new PieceData(
                        move.PieceTeam,
                        move.PromotedTo,
                        move.EndPosition.x,
                        move.EndPosition.y,
                        move.PieceMoveDirection
                    );
                    SpawnSinglePiece(promotedData, move.EndPosition);
                }
                else
                {
                    // Standard move: update dictionary and slide piece
                    visualPieces[move.EndPosition] = movingPiece;

                    Vector3 targetPos = GetTileCenter(move.EndPosition.x, move.EndPosition.y);
                    targetPos.y += pieceYOffset;
                    movingPiece.SetPosition(targetPos);
                }
            }

            // 3. Handle Castling (move the Rook)
            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                if (visualPieces.TryGetValue(move.RookStartPosition.Value, out ChessPiece rook))
                {
                    visualPieces.Remove(move.RookStartPosition.Value);
                    visualPieces[move.RookEndPosition.Value] = rook;

                    Vector3 rookPos = GetTileCenter(move.RookEndPosition.Value.x, move.RookEndPosition.Value.y);
                    rookPos.y += pieceYOffset;
                    rook.SetPosition(rookPos);
                }
            }
        }

        /// <summary>
        /// Animates a captured piece moving to the death pile.
        /// </summary>
        private void AnimateDeath(ChessPiece victim)
        {
            Collider col = victim.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
            }

            List<ChessPiece> graveyard = victim.team == Team.White ? deadWhitePieces : deadBlackPieces;
            graveyard.Add(victim);

            // Determine which side of the board for this team's graveyard
            int majorRowIndex = (victim.team == Team.White) ? 0 : tileCountY - 1;
            float rowCenterZ = boardOrigin.z + majorRowIndex * tileSize + tileSize * 0.5f;

            float stackSpacing = Mathf.Max(0.01f, deathSpacing);
            int stackIndex = graveyard.Count - 1;

            // Position off the side of the board
            float xPos = (victim.team == Team.White)
                ? boardOrigin.x + tileCountX * tileSize + (tileSize * 0.5f)
                : boardOrigin.x - (tileSize * 0.5f);

            float zPos = (victim.team == Team.White)
                ? (rowCenterZ - (stackIndex * 0.5f * stackSpacing)) + (stackIndex * stackSpacing)
                : (rowCenterZ + (stackIndex * 0.5f * stackSpacing)) - (stackIndex * stackSpacing);

            Vector3 deathPos = new Vector3(xPos, boardOrigin.y + tilesYOffset + pieceYOffset, zPos);

            // Calculate look direction toward board center
            Vector3 boardCenterPos = boardOrigin + new Vector3(
                tileCountX * tileSize * 0.5f,
                0f,
                tileCountY * tileSize * 0.5f
            );
            Vector3 lookDir = (boardCenterPos - deathPos).normalized;
            lookDir.y = 0;

            // Apply visual changes
            victim.SetScale(Vector3.one * deathSize);
            victim.SetPosition(deathPos);
            victim.transform.rotation = Quaternion.LookRotation(lookDir == Vector3.zero ? Vector3.forward : lookDir);
        }

        /// <summary>
        /// Snaps a piece back to its grid position (used when illegal move is attempted).
        /// </summary>
        public void SnapPieceBack(ChessTheMasterPiece.Data.Vector2Int gridPos)
        {
            if (visualPieces.TryGetValue(gridPos, out ChessPiece piece))
            {
                Vector3 worldPos = GetTileCenter(gridPos.x, gridPos.y);
                worldPos.y += pieceYOffset;
                piece.SetPosition(worldPos, force: true);
            }
        }

        #endregion

        #region Public Getters for Input Controller

        /// <summary>
        /// Converts a raycast hit transform into a grid coordinate.
        /// Walks up the hierarchy to find the tile.
        /// </summary>
        public ChessTheMasterPiece.Data.Vector2Int GetTileIndexFromTransform(Transform t)
        {
            Transform cur = t;
            int safety = 0;

            while (cur != null && safety++ < 16)
            {
                if (tileLookup.TryGetValue(cur, out ChessTheMasterPiece.Data.Vector2Int idx))
                {
                    return idx;
                }
                cur = cur.parent;
            }

            return ChessTheMasterPiece.Data.Vector2Int.Invalid;
        }

        /// <summary>
        /// Returns the Transform of the piece at the given grid position.
        /// Used by input controller for dragging.
        /// </summary>
        public Transform GetPieceTransformAt(ChessTheMasterPiece.Data.Vector2Int gridPos)
        {
            if (visualPieces.TryGetValue(gridPos, out ChessPiece piece))
            {
                return piece.transform;
            }
            return null;
        }

        /// <summary>
        /// Calculates world position of a tile's center.
        /// </summary>
        private Vector3 GetTileCenter(int x, int y)
        {
            return boardOrigin + new Vector3(
                x * tileSize + tileSize * 0.5f,
                tilesYOffset,
                y * tileSize + tileSize * 0.5f
            );
        }

        #endregion

        #region Highlighting

        /// <summary>
        /// Updates the hover highlight on a tile.
        /// </summary>
        public void UpdateHoverHighlight(ChessTheMasterPiece.Data.Vector2Int idx)
        {
            if (hoverIndex == idx) return;

            // Clear old hover
            if (hoverIndex != ChessTheMasterPiece.Data.Vector2Int.Invalid)
            {
                SetTileLayer(hoverIndex, isHover: false);
            }

            // Set new hover
            hoverIndex = idx;
            if (hoverIndex != ChessTheMasterPiece.Data.Vector2Int.Invalid)
            {
                SetTileLayer(hoverIndex, isHover: true);
            }
        }

        /// <summary>
        /// Clears the hover highlight.
        /// </summary>
        public void ClearHoverHighlight()
        {
            UpdateHoverHighlight(ChessTheMasterPiece.Data.Vector2Int.Invalid);
        }

        /// <summary>
        /// Highlights all legal move destinations.
        /// GC-optimized to accept read-only buffer interface.
        /// </summary>
        public void HighlightLegalMoves(IReadOnlyList<MoveCommand> moves)
        {
            ClearLegalMoveHighlights();

            // IReadOnlyList still supports foreach perfectly!
            foreach (var move in moves)
            {
                currentLegalHighlights.Add(move.EndPosition);
                SetTileLayer(move.EndPosition, isHover: false);
            }
        }

        /// <summary>
        /// Clears all legal move highlights.
        /// </summary>
        public void ClearLegalMoveHighlights()
        {
            foreach (var pos in currentLegalHighlights)
            {
                if (pos.x >= 0 && pos.x < tileCountX && pos.y >= 0 && pos.y < tileCountY && tiles[pos.x, pos.y] != null)
                {
                    tiles[pos.x, pos.y].layer = tileLayer;
                }
            }
            currentLegalHighlights.Clear();

            // Restore hover if active
            if (hoverIndex != ChessTheMasterPiece.Data.Vector2Int.Invalid)
            {
                SetTileLayer(hoverIndex, isHover: true);
            }
        }

        /// <summary>
        /// Sets the appropriate layer for a tile based on its state.
        /// </summary>
        private void SetTileLayer(ChessTheMasterPiece.Data.Vector2Int pos, bool isHover)
        {
            if (pos.x < 0 || pos.x >= tileCountX || pos.y < 0 || pos.y >= tileCountY) return;

            GameObject tile = tiles[pos.x, pos.y];
            if (tile == null) return;

            if (isHover)
            {
                tile.layer = highlightLayer;
            }
            else if (currentLegalHighlights.Contains(pos))
            {
                tile.layer = moveHighlightLayer;
            }
            else
            {
                tile.layer = tileLayer;
            }
        }

        #endregion

        #region Initialization Helpers

        /// <summary>
        /// Caches layer indices to avoid string lookups every frame.
        /// </summary>
        private void CacheLayers()
        {
            tileLayer = LayerMask.NameToLayer("Tile");
            highlightLayer = LayerMask.NameToLayer("Highlight");
            moveHighlightLayer = LayerMask.NameToLayer("MoveHighlight");

            if (tileLayer == -1) tileLayer = 0;
            if (highlightLayer == -1) highlightLayer = 0;
            if (moveHighlightLayer == -1) moveHighlightLayer = 0;
        }

        /// <summary>
        /// Creates parent container GameObjects if not assigned in inspector.
        /// </summary>
        private void CreateContainers()
        {
            if (tilesParent == null)
            {
                tilesParent = new GameObject("Tiles").transform;
                tilesParent.SetParent(transform, false);
            }

            if (whitePiecesParent == null)
            {
                whitePiecesParent = new GameObject("WhitePieces").transform;
                whitePiecesParent.SetParent(transform, false);
            }

            if (blackPiecesParent == null)
            {
                blackPiecesParent = new GameObject("BlackPieces").transform;
                blackPiecesParent.SetParent(transform, false);
            }
        }

        #endregion
    }
}