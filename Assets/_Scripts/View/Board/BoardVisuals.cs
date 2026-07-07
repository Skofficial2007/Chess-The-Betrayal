using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay;
using ChessTheBetrayal.Infrastructure;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// The eyes of the game. Spawns and moves piece GameObjects, highlights tiles, and plays animations.
    /// It has no idea how chess rules work — it just listens to GameManager and reacts.
    /// </summary>
    public class BoardVisuals : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Board Geometry")]
        [SerializeField] private Material tileMaterial;
        [Tooltip("Shared inverted-hull outline material (Custom/PieceSelectionOutline.shader) applied to every piece's selection ring. Injected at spawn instead of loaded via Resources.Load.")]
        [SerializeField] private Material selectionOutlineMaterial;
        [SerializeField, Range(0.1f, 4f)] private float tileSize = 1f;
        [SerializeField] private float tilesYOffset = 0.0f;
        [SerializeField] private Vector3 boardCenter = Vector3.zero;

        [Header("Prefabs (Order: Pawn, Rook, Knight, Bishop, Queen, King)")]
        [SerializeField] private GameObject[] whiteTeamPrefabs;
        [SerializeField] private GameObject[] blackTeamPrefabs;

        [Tooltip("Extra Y-axis rotation (degrees) applied on top of the standard facing for each White prefab index (same Pawn/Rook/Knight/Bishop/Queen/King order as the arrays above). Only needed when a source mesh (e.g. a piece pack's model) wasn't authored facing the same default direction as the rest of the set — leave every entry at 0 unless a specific piece visibly faces the wrong way. Array length may be shorter than the prefab arrays; missing entries default to 0.")]
        [SerializeField] private float[] whiteMeshFacingCorrectionDegrees = new float[0];
        [Tooltip("Same as whiteMeshFacingCorrectionDegrees, but for Black prefabs. The Black Knight model in the current piece pack is authored facing a different default direction than its White counterpart, which is why this exists.")]
        [SerializeField] private float[] blackMeshFacingCorrectionDegrees = { 0f, 0f, 180f, 0f, 0f, 0f };

        [Header("Piece Visuals")]
        [SerializeField] private float pieceYOffset = 0.05f;
        [SerializeField] private float pieceScaleMultiplier = 0.9f;
        [SerializeField] private float deathSize = 0.45f;
        [SerializeField] private float deathSpacing = 0.35f;

        [Header("Move Indicator (Circle)")]
        [SerializeField, Range(0.05f, 0.5f)] private float moveIndicatorRadiusRatio = 0.45f;
        [SerializeField] private float moveIndicatorYOffset = 0.01f;
        [SerializeField, Range(8, 64)] private int moveIndicatorSegments = 24;

        [Header("Check Indicator (Frame)")]
        [Tooltip("Outer size of the check-warning frame as a fraction of the tile (1 = full tile). Slightly under 1 insets the frame so the king reads as sitting INSIDE it.")]
        [SerializeField, Range(0.5f, 1f)] private float checkFrameSizeRatio = 0.9f;
        [Tooltip("Border thickness of the check frame as a fraction of its outer size (0.1 = a border 10% of the frame's width on each edge).")]
        [SerializeField, Range(0.02f, 0.3f)] private float checkFrameThicknessRatio = 0.1f;

        [Header("Hierarchy Containers")]
        [SerializeField] private Transform tilesParent;
        [SerializeField] private Transform whitePiecesParent;
        [SerializeField] private Transform blackPiecesParent;

        [Header("Data Source")]
        [SerializeField] private ChessTheBetrayal.Events.SharedBoardStateSO _sharedBoardState;

        [Header("Event Channels")]
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _gameStartedChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _gameResetChannel;
        [SerializeField] private ChessTheBetrayal.Events.MoveExecutedEventChannel _moveExecutedChannel;
        [SerializeField] private ChessTheBetrayal.Events.MoveRejectedEventChannel _moveRejectedChannel;
        [SerializeField] private ChessTheBetrayal.Events.SelectionRejectedEventChannel _selectionRejectedChannel;
        [SerializeField] private ChessTheBetrayal.Events.PromotionRequiredEventChannel _promotionRequiredChannel;
        [SerializeField] private ChessTheBetrayal.Events.BetrayalEventChannel _betrayalChannel;

        #endregion

        #region Private Fields

        // Maps logical grid coordinates to visual GameObjects
        private Dictionary<Vector2Int, ChessPiece> _piecesByPosition = new Dictionary<Vector2Int, ChessPiece>();

        // Tracks a capture-stamp victim whose death is still waiting on the ATTACKER's
        // onDescentStart callback (see AnimateMove) — keyed by the attacker, since the attacker is
        // what a same-frame Betrayal Defection can destroy mid-leap (SwapPieceTeam spins and
        // destroys the Betrayer immediately after its own Act-stage capture stamp starts, before
        // the leap ever reaches its fall phase). Without this, the victim was already removed from
        // _piecesByPosition but never told to animate anywhere — visually stuck on the board,
        // overlapping the Betrayer's old square, forever. FlushPendingStampVictim (called from
        // SwapPieceTeam and anywhere else that can destroy an attacker mid-stamp) is the safety net:
        // if the attacker never delivers its callback, send the victim to the graveyard directly.
        private readonly Dictionary<ChessPiece, ChessPiece> _pendingStampVictimByAttacker = new Dictionary<ChessPiece, ChessPiece>();

        // Tracks a piece whose Defection spin (SwapPieceTeam) must wait for its OWN Act-stage
        // capture stamp to finish playing first — MatchDriver resolves the whole Retribution ->
        // Defection sub-sequence synchronously in one PlayMove call when no legal Retribution move
        // exists, so DefectionOccurred can arrive in the very same frame the capture stamp starts.
        // Spinning/destroying the Betrayer immediately would cut its own stamp off mid-leap (the
        // bug _pendingStampVictimByAttacker's flush guards against) AND visually skip straight from
        // "piece captured something" to "piece spins away" with no beat in between. Queuing the
        // pending team-swap here and firing it from the stamp's own completion (see AnimateMove)
        // instead makes the two animations play out one after another, in order.
        private readonly Dictionary<ChessPiece, Vector2Int> _pendingDefectionByAttacker = new Dictionary<ChessPiece, Vector2Int>();

        // Set the instant a Betrayal Act's Initiated phase arrives already knowing (via
        // BetrayalPayload.WillDefect) that no Retribution move exists — read by AnimateMove's Act
        // branch so it can skip glowing the Betrayer entirely. There's no Retribution choice for
        // the player to make in that case, so the glow would only flash on for a moment before the
        // piece immediately spins away — worse than not glowing at all.
        private bool _actWillDefect;

        // Tile meshes and lookup for raycasting
        private GameObject[,] _tiles;
        private Dictionary<Transform, Vector2Int> _tileByTransform = new Dictionary<Transform, Vector2Int>();

        // Circular move-indicator child object per tile (hidden until a move highlight targets it)
        private MeshRenderer[,] _moveIndicatorRenderers;

        // Square check-warning child object per tile (hidden until a king is in check on that
        // square). Kept as its own renderer/mesh rather than reusing the tile's own MeshRenderer so
        // the tile itself (base/hover layer) and the check warning (MoveHighlightCapture layer,
        // red material) can be toggled completely independently — the same separation of concerns
        // _moveIndicatorRenderers already uses for legal-move dots.
        private MeshRenderer[,] _checkIndicatorRenderers;

        // The square currently showing the check-warning highlight, so a later move that resolves
        // (or shifts) check can find and clear the old one before showing a new one. Invalid when
        // no king is currently in check.
        private Vector2Int _checkHighlightSquare = Vector2Int.Invalid;

        // Death piles
        private List<ChessPiece> _deadWhitePieces = new List<ChessPiece>();
        private List<ChessPiece> _deadBlackPieces = new List<ChessPiece>();

        // Highlighting state
        private Vector2Int _hoverIndex = Vector2Int.Invalid;
        private readonly List<Vector2Int> _highlightedSquares = new List<Vector2Int>(32);

        // Mirror of the list above, used for fast "is this square highlighted?" checks.
        private readonly HashSet<Vector2Int> _highlightedSquaresLookup = new HashSet<Vector2Int>(32);
        private readonly List<ChessPiece> _destroyQueue = new List<ChessPiece>(32);

        // Cached values
        private Vector3 _boardOrigin;
        private int _tileCountX, _tileCountY;
        private int _tileLayer, _highlightLayer, _moveHighlightLayer, _moveHighlightCaptureLayer, _checkHighlightLayer;

        // Squares among _highlightedSquares that currently hold a capturable piece
        private readonly HashSet<Vector2Int> _captureSquaresLookup = new HashSet<Vector2Int>(32);

        // Defensive Override threat-pulse: two quick red flashes on the player's own king when a
        // Forced Save activates, so the sudden forced move reads as "your king is in danger" rather
        // than an unexplained rules interruption.
        private const float KingThreatFlashIntensity = 2.5f;
        private const float KingThreatFlashDuration = 0.15f;
        private const int KingThreatFlashCycles = 2;

        #endregion

        #region Public Properties

        public float TileYOffset => tilesYOffset;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            ServiceLocator.Instance.Register(this);

            ValidateRequiredFields();

            CacheLayers();
            CreateContainers();
        }

        /// <summary>
        /// Loud-fails on any unassigned Inspector reference at Play-mode start. Container
        /// transforms (tilesParent, whitePiecesParent, blackPiecesParent) are intentionally
        /// excluded — CreateContainers() auto-populates them when left empty.
        /// </summary>
        private void ValidateRequiredFields()
        {
            InspectorGuard.Require(tileMaterial, nameof(tileMaterial), this);
            InspectorGuard.Require(selectionOutlineMaterial, nameof(selectionOutlineMaterial), this);
            InspectorGuard.Require(_sharedBoardState, nameof(_sharedBoardState), this);
            InspectorGuard.Require(_gameStartedChannel, nameof(_gameStartedChannel), this);
            InspectorGuard.Require(_gameResetChannel, nameof(_gameResetChannel), this);
            InspectorGuard.Require(_moveExecutedChannel, nameof(_moveExecutedChannel), this);
            InspectorGuard.Require(_moveRejectedChannel, nameof(_moveRejectedChannel), this);
            InspectorGuard.Require(_selectionRejectedChannel, nameof(_selectionRejectedChannel), this);
            InspectorGuard.Require(_promotionRequiredChannel, nameof(_promotionRequiredChannel), this);
            InspectorGuard.Require(_betrayalChannel, nameof(_betrayalChannel), this);

            if (whiteTeamPrefabs == null || whiteTeamPrefabs.Length == 0)
            {
                Debug.LogError($"[{nameof(BoardVisuals)}] Required field '{nameof(whiteTeamPrefabs)}' is empty on '{name}'.", this);
            }

            if (blackTeamPrefabs == null || blackTeamPrefabs.Length == 0)
            {
                Debug.LogError($"[{nameof(BoardVisuals)}] Required field '{nameof(blackTeamPrefabs)}' is empty on '{name}'.", this);
            }
        }

        private void Start()
        {
            if (!ServiceLocator.Instance.TryResolve<GameManager>(out _))
            {
                Debug.LogError("[BoardVisuals] GameManager was never registered!");
            }
        }

        /// <summary>
        /// Direct C# subscriptions replacing the *EventListener + UnityEvent Inspector wiring.
        /// A broken method reference here is a compile error; a broken UnityEvent target was a
        /// silent no-op.
        /// </summary>
        private void OnEnable()
        {
            _gameStartedChannel?.Register(HandleGameStarted);
            _gameResetChannel?.Register(ClearAllVisuals);
            _moveExecutedChannel?.Register(AnimateMove);
            _moveRejectedChannel?.Register(HandleMoveRejected);
            _selectionRejectedChannel?.Register(HandleSelectionRejected);
            _promotionRequiredChannel?.Register(HandlePromotionOptimisticGlide);
            _betrayalChannel?.Register(HandleBetrayalPhaseChanged);
        }

        private void OnDisable()
        {
            _gameStartedChannel?.Unregister(HandleGameStarted);
            _gameResetChannel?.Unregister(ClearAllVisuals);
            _moveExecutedChannel?.Unregister(AnimateMove);
            _moveRejectedChannel?.Unregister(HandleMoveRejected);
            _selectionRejectedChannel?.Unregister(HandleSelectionRejected);
            _promotionRequiredChannel?.Unregister(HandlePromotionOptimisticGlide);
            _betrayalChannel?.Unregister(HandleBetrayalPhaseChanged);
        }

        #endregion

        #region Setup & Mesh Generation

        /// <summary>
        /// Called when a new game starts. Sets up the board and spawns pieces.
        /// </summary>
        public void HandleGameStarted()
        {
            var initialBoard = _sharedBoardState?.Value;
            if (initialBoard == null) return;

            ClearAllVisuals();

            _tileCountX = initialBoard.TileCountX;
            _tileCountY = initialBoard.TileCountY;

            // Calculate board origin (bottom-left corner)
            float halfWidth = _tileCountX * tileSize * 0.5f;
            float halfHeight = _tileCountY * tileSize * 0.5f;
            _boardOrigin = boardCenter - new Vector3(halfWidth, 0f, halfHeight);

            // Generate tile meshes if they don't exist
            if (_tiles == null || _tiles.GetLength(0) != _tileCountX || _tiles.GetLength(1) != _tileCountY)
            {
                GenerateTileMeshes();
            }

            // Spawn all pieces based on board state
            SpawnAllPieces(initialBoard);
        }

        /// <summary>
        /// Generates the 3D tile meshes and colliders for the board.
        /// </summary>
        private void GenerateTileMeshes()
        {
            _tiles = new GameObject[_tileCountX, _tileCountY];
            _moveIndicatorRenderers = new MeshRenderer[_tileCountX, _tileCountY];
            _checkIndicatorRenderers = new MeshRenderer[_tileCountX, _tileCountY];
            _tileByTransform.Clear();

            for (int x = 0; x < _tileCountX; x++)
            {
                for (int y = 0; y < _tileCountY; y++)
                {
                    GameObject tileGO = new GameObject($"Tile_{x}_{y}");
                    tileGO.transform.SetParent(tilesParent, false);

                    // Position at tile center
                    Vector3 worldPos = _boardOrigin + new Vector3(
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
                    tileGO.layer = _tileLayer;

                    // Create the circular move-indicator child, hidden until a legal-move highlight targets it
                    GameObject indicatorGO = new GameObject($"MoveIndicator_{x}_{y}");
                    indicatorGO.transform.SetParent(tileGO.transform, false);
                    indicatorGO.transform.localPosition = new Vector3(0f, moveIndicatorYOffset, 0f);

                    Mesh circleMesh = GenerateCircleMesh(tileSize * moveIndicatorRadiusRatio, moveIndicatorSegments);
                    indicatorGO.AddComponent<MeshFilter>().sharedMesh = circleMesh;
                    MeshRenderer indicatorRenderer = indicatorGO.AddComponent<MeshRenderer>();
                    indicatorRenderer.sharedMaterial = tileMaterial;
                    indicatorRenderer.enabled = false;
                    indicatorGO.layer = _tileLayer;

                    // Create the check-warning FRAME child, hidden until a king in check occupies
                    // this tile. A hollow square ring (not a filled square, and not the move dot's
                    // circle) inset slightly inside the tile, so the king reads as sitting FRAMED
                    // inside a red border with the tile still visible through the middle — see the
                    // class doc on SetKingInCheckHighlight for why this is its own mesh/material/layer.
                    GameObject checkGO = new GameObject($"CheckIndicator_{x}_{y}");
                    checkGO.transform.SetParent(tileGO.transform, false);
                    checkGO.transform.localPosition = new Vector3(0f, moveIndicatorYOffset, 0f);

                    float frameOuter = tileSize * checkFrameSizeRatio;
                    Mesh checkFrameMesh = GenerateFrameMesh(frameOuter, frameOuter * checkFrameThicknessRatio);
                    checkGO.AddComponent<MeshFilter>().sharedMesh = checkFrameMesh;
                    MeshRenderer checkRenderer = checkGO.AddComponent<MeshRenderer>();
                    checkRenderer.sharedMaterial = tileMaterial;
                    checkRenderer.enabled = false;
                    checkGO.layer = _tileLayer;

                    // Store references
                    _tiles[x, y] = tileGO;
                    _moveIndicatorRenderers[x, y] = indicatorRenderer;
                    _checkIndicatorRenderers[x, y] = checkRenderer;
                    _tileByTransform[tileGO.transform] = new Vector2Int(x, y);
                }
            }
        }

        /// <summary>
        /// Builds a flat triangle-fan circle mesh, used for the move-highlight indicator.
        /// </summary>
        private static Mesh GenerateCircleMesh(float radius, int segments)
        {
            Mesh mesh = new Mesh { name = "MoveIndicatorMesh" };

            Vector3[] vertices = new Vector3[segments + 1];
            int[] triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            for (int i = 0; i < segments; i++)
            {
                int a = i + 1;
                int b = (i + 1) % segments + 1;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = b;
                triangles[i * 3 + 2] = a;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Builds a flat hollow square ring (frame) centered on the origin, used for the king's
        /// check-warning indicator: an outer square of side outerSize with an inner square of side
        /// (outerSize - 2*thickness) cut out of the middle, leaving a border thickness units wide on
        /// every edge and transparent nothing inside. Eight outer/inner corner vertices stitched
        /// into eight triangles (two per side) — cheap enough to build one per tile at setup.
        /// </summary>
        private static Mesh GenerateFrameMesh(float outerSize, float thickness)
        {
            Mesh mesh = new Mesh { name = "CheckFrameMesh" };

            float outer = outerSize * 0.5f;
            float inner = Mathf.Max(0f, outer - thickness);

            // 0..3 outer corners, 4..7 inner corners — same corner order (BL, TL, BR, TR) for both
            // rings so each side's quad is a simple outer-pair + matching inner-pair.
            mesh.vertices = new Vector3[]
            {
                new Vector3(-outer, 0f, -outer), // 0 outer BL
                new Vector3(-outer, 0f,  outer), // 1 outer TL
                new Vector3( outer, 0f, -outer), // 2 outer BR
                new Vector3( outer, 0f,  outer), // 3 outer TR
                new Vector3(-inner, 0f, -inner), // 4 inner BL
                new Vector3(-inner, 0f,  inner), // 5 inner TL
                new Vector3( inner, 0f, -inner), // 6 inner BR
                new Vector3( inner, 0f,  inner), // 7 inner TR
            };

            // Four border strips (left, right, bottom, top), two triangles each, wound so the
            // frame faces up (+Y) like every other board indicator.
            mesh.triangles = new int[]
            {
                // Left strip (outer BL,TL -> inner BL,TL)
                0, 1, 4,   1, 5, 4,
                // Right strip (inner BR,TR -> outer BR,TR)
                6, 7, 2,   7, 3, 2,
                // Bottom strip (outer BL,inner BL -> outer BR,inner BR)
                0, 4, 2,   4, 6, 2,
                // Top strip (inner TL,outer TL -> inner TR,outer TR)
                5, 1, 7,   1, 3, 7,
            };

            mesh.RecalculateNormals();
            return mesh;
        }

        // Board-setup dissolve-in: each side's own back rank (row 0 for White, top row for Black)
        // materializes first, its pawn rank a beat later — reads as the army assembling rank by
        // rank rather than the whole board popping into existence at once. SetupInitialDelay is a
        // beat of anticipation before the wave starts at all — an instant first-frame reform reads
        // as a pop-in glitch, while a brief held beat on the empty board makes the reform land as a
        // deliberate reveal instead.
        private const float SetupDissolveDuration = 0.45f;
        private const float SetupRowStagger = 0.12f;
        private const float SetupInitialDelay = 0.25f;

        /// <summary>
        /// Spawns visual piece GameObjects for all pieces in the board state, dissolving each one
        /// in with a back-rank-first stagger (see SetupRowStagger) rather than popping in instantly.
        /// </summary>
        private void SpawnAllPieces(BoardState board)
        {
            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData data = board.GetPiece(x, y);
                    if (!data.IsEmpty)
                    {
                        int rankDistance = data.Team == Team.White ? y : (board.TileCountY - 1 - y);
                        float delay = SetupInitialDelay + rankDistance * SetupRowStagger;
                        SpawnSinglePiece(data, new Vector2Int(x, y), spawnDissolveDelay: delay);
                    }
                }
            }
        }

        /// <summary>
        /// Instantiates a single piece GameObject at the specified position and keys it into
        /// _piecesByPosition. Returns the spawned ChessPiece (null on a bad prefab index) so
        /// callers that need to animate it in — promotion and defection's reveal transitions —
        /// don't have to look it back up.
        ///
        /// spawnDissolveDelay >= 0 makes the piece spawn fully dissolved (invisible) and reform via
        /// the dissolve shader after that delay — used for the board-setup wave (SpawnAllPieces).
        /// Pass a negative value (the default) to spawn instantly with no dissolve, for callers like
        /// promotion/defection that drive their own transition separately.
        /// </summary>
        private ChessPiece SpawnSinglePiece(PieceData data, Vector2Int pos, float spawnDissolveDelay = -1f)
        {
            // Select prefab array and parent based on team
            GameObject[] prefabs = data.Team == Team.White ? whiteTeamPrefabs : blackTeamPrefabs;
            Transform parent = data.Team == Team.White ? whitePiecesParent : blackPiecesParent;

            // Get prefab index (ChessPieceType enum starts at 1)
            int index = (int)data.Type - 1;

            if (index < 0 || index >= prefabs.Length || prefabs[index] == null)
            {
                Debug.LogError($"[BoardVisuals] Invalid prefab for {data.Team} {data.Type}");
                return null;
            }

            // Calculate world position
            Vector3 worldPos = GetTileCenter(pos.x, pos.y);
            worldPos.y += pieceYOffset;

            // Instantiate
            GameObject go = Instantiate(prefabs[index], worldPos, Quaternion.identity, parent);
            go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, pieceScaleMultiplier);

            // Rotate enemy pieces 180 degrees to face player, plus any per-mesh facing correction
            // for a specific prefab whose source model wasn't authored facing the same default
            // direction as the rest of its own team's set (see whiteMeshFacingCorrectionDegrees'
            // doc comment — currently only the Black Knight needs one). The correction is baked
            // into this same rotation assignment (not a separate child transform) so every
            // downstream consumer of this piece's rotation — the defection Spin transition,
            // castling, the check-shake — sees ONE correct "resting" rotation and never has to know
            // a correction was ever needed.
            float facingCorrection = GetMeshFacingCorrectionDegrees(data.Team, index);
            go.transform.rotation = data.MoveDirection == -1
                ? Quaternion.Euler(0f, 180f + facingCorrection, 0f)
                : Quaternion.Euler(0f, facingCorrection, 0f);

            // Configure visual component
            ChessPiece visualPiece = go.GetComponent<ChessPiece>();
            if (visualPiece == null)
            {
                visualPiece = go.AddComponent<ChessPiece>();
            }
            visualPiece.team = data.Team;
            visualPiece.type = data.Type;
            visualPiece.SetSelectionOutlineMaterial(selectionOutlineMaterial);

            // Store in lookup
            _piecesByPosition[pos] = visualPiece;

            if (spawnDissolveDelay >= 0f)
            {
                visualPiece.SetDissolveImmediate(1f);
                Tween.Delay(spawnDissolveDelay).OnComplete(() =>
                {
                    // Guards against a reset/game-restart destroying the piece while it was still
                    // waiting out its stagger delay.
                    if (visualPiece != null)
                    {
                        visualPiece.DissolveTo(0f, SetupDissolveDuration);
                    }
                });
            }

            return visualPiece;
        }

        /// <summary>
        /// Looks up the extra Y-axis facing correction (degrees) for a team/prefab-index pair from
        /// whiteMeshFacingCorrectionDegrees / blackMeshFacingCorrectionDegrees. Returns 0 for any
        /// index the array doesn't cover, so leaving an array short (or empty, as White's default
        /// is) is always safe rather than requiring every entry to be explicitly authored.
        /// </summary>
        private float GetMeshFacingCorrectionDegrees(Team team, int prefabIndex)
        {
            float[] corrections = team == Team.White ? whiteMeshFacingCorrectionDegrees : blackMeshFacingCorrectionDegrees;
            if (corrections == null || prefabIndex < 0 || prefabIndex >= corrections.Length) return 0f;
            return corrections[prefabIndex];
        }

        /// <summary>
        /// Destroys all piece GameObjects and resets all visual state.
        /// </summary>
        public void ClearAllVisuals()
        {
            _destroyQueue.Clear();

            // Collect first, then destroy — destroying a piece while iterating the dictionary would
            // break the enumeration mid-loop.
            foreach (var kv in _piecesByPosition)
            {
                _destroyQueue.Add(kv.Value);
            }

            for (int i = 0; i < _destroyQueue.Count; i++)
            {
                if (_destroyQueue[i] != null) Destroy(_destroyQueue[i].gameObject);
            }

            for (int i = 0; i < _deadWhitePieces.Count; i++)
            {
                if (_deadWhitePieces[i] != null) Destroy(_deadWhitePieces[i].gameObject);
            }

            for (int i = 0; i < _deadBlackPieces.Count; i++)
            {
                if (_deadBlackPieces[i] != null) Destroy(_deadBlackPieces[i].gameObject);
            }

            // Clear collections
            _piecesByPosition.Clear();
            _deadWhitePieces.Clear();
            _deadBlackPieces.Clear();
            _destroyQueue.Clear();

            // Clear highlights
            ClearLegalMoveHighlights();
            ClearHoverHighlight();
            ClearCheckHighlight();
        }

        #endregion

        #region Animations & Movement Execution

        /// <summary>
        /// Reads a completed MoveCommand and triggers all the necessary animations: captures
        /// (including en passant) resolve first, then the primary piece either slides to its
        /// destination or — on promotion — plays a vanish/reveal transition and swaps prefabs,
        /// then castling (if any) slides the rook in the same pass as the king.
        /// </summary>
        public void AnimateMove(ChessTheBetrayal.Events.Payloads.MoveExecutedPayload payload)
        {
            MoveCommand move = payload.Move;

            // Safety check for invalid commands
            if (move.PieceType == ChessPieceType.None) return;

            // 1. Handle Captures. A direct capture (attacker lands ON the victim's tile) plays the
            // full cartoon "stamp": the attacker lunges in and stomps, the victim gets crushed flat
            // at the exact instant of impact. En passant is captured on a DIFFERENT square than the
            // attacker's landing tile — there's no piece-on-piece contact to stomp, so it keeps the
            // plain squash-and-shrink death instead of a lunge onto empty air.
            bool isDirectCaptureStamp = move.IsCapture && !move.IsEnPassant;
            ChessPiece stampVictim = null;

            if (move.IsCapture)
            {
                Vector2Int capturePos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue
                    ? move.EnPassantCapturePosition.Value
                    : move.EndPosition;

                if (_piecesByPosition.TryGetValue(capturePos, out ChessPiece deadPiece))
                {
                    _piecesByPosition.Remove(capturePos);

                    if (isDirectCaptureStamp)
                    {
                        // Deferred to the attacker's onImpact below (see step 2) so the victim's
                        // squash lands on the exact same frame the attacker's lunge does, rather
                        // than starting immediately and running out of sync with the stomp.
                        stampVictim = deadPiece;
                    }
                    else
                    {
                        AnimateDeath(deadPiece);
                    }
                }
            }

            // 2. Move the Primary Piece
            if (_piecesByPosition.TryGetValue(move.StartPosition, out ChessPiece movingPiece))
            {
                _piecesByPosition.Remove(move.StartPosition);

                // Handle Promotion: play the pawn's vanish animation, then — once it completes —
                // destroy it and spawn the promoted piece with its own reveal animation. The swap
                // is deferred to PlayTransitionOut's callback (which may fire on a later frame)
                // rather than happening immediately, so the pawn is visibly seen collapsing into
                // its new form instead of jump-cutting. The fields we need are copied into locals
                // up front because `move` is a snapshot on this method's stack — by the time the
                // callback runs, only what the closure explicitly captured is still guaranteed to
                // hold the right values.
                if (move.IsPromotion)
                {
                    Vector2Int promotionPos = move.EndPosition;
                    Team promotedTeam = move.PieceTeam;
                    ChessPieceType promotedType = move.PromotedTo;
                    int promotedMoveDirection = move.PieceMoveDirection;

                    movingPiece.PlayTransitionOut(PieceTransitionStyle.PromotionMorph, () =>
                    {
                        // The pawn (or the whole scene) may have been destroyed while the
                        // transition was still playing — e.g. a game reset. Unity's overloaded
                        // == correctly reports true here even though the C# reference isn't null.
                        if (movingPiece == null) return;
                        Destroy(movingPiece.gameObject);

                        PieceData promotedData = new PieceData(
                            team: promotedTeam,
                            type: promotedType,
                            moveDirection: promotedMoveDirection,
                            startRow: 0,
                            hasMoved: true
                        );
                        ChessPiece promoted = SpawnSinglePiece(promotedData, promotionPos);
                        promoted?.PlayTransitionIn(PieceTransitionStyle.PromotionMorph);
                    });
                }
                else if (move.IsCastling)
                {
                    // Castling's king-side of the choreography: lead the rook by starting at
                    // delay 0, on the same PlayCastleMove path (rather than the plain
                    // SetPosition/Quiet used by every other non-capture move) so the king ends
                    // with the identical tiny settle bob the rook plays once IT arrives below —
                    // "two pieces settling into place together," not a simultaneous teleport with
                    // an uncoordinated landing.
                    _piecesByPosition[move.EndPosition] = movingPiece;

                    Vector3 kingTargetPos = GetTileCenter(move.EndPosition.x, move.EndPosition.y);
                    kingTargetPos.y += pieceYOffset;
                    movingPiece.PlayCastleMove(kingTargetPos, startDelay: 0f);
                }
                else if (isDirectCaptureStamp)
                {
                    // The stamp: attacker leaps above the victim's tile and stomps down onto it.
                    // onDescentStart fires the frame the attacker begins dropping — the victim
                    // starts cowering/shrinking under it right then (see PlayStompedDeath), so the
                    // two pieces never overlap at full size and the crush lands in sync with the
                    // attacker's touchdown. Driving both animators off this one shared beat keeps
                    // them visually synced without either needing to know the other exists.
                    _piecesByPosition[move.EndPosition] = movingPiece;

                    Vector3 stampTargetPos = GetTileCenter(move.EndPosition.x, move.EndPosition.y);
                    stampTargetPos.y += pieceYOffset;

                    ChessPiece victimForClosure = stampVictim;
                    ChessPiece attackerForClosure = movingPiece;

                    // Registered BEFORE the stamp starts so SwapPieceTeam knows to QUEUE (rather
                    // than immediately play) a Defection spin on this same piece if one arrives
                    // before the stamp finishes — see _pendingStampVictimByAttacker's field doc.
                    if (victimForClosure != null)
                    {
                        _pendingStampVictimByAttacker[attackerForClosure] = victimForClosure;
                    }

                    movingPiece.PlayCaptureStamp(
                        stampTargetPos,
                        onDescentStart: () =>
                        {
                            if (victimForClosure == null) return;
                            victimForClosure.PlayStompedDeath(() => SendToGraveyard(victimForClosure));
                        },
                        onSettled: () =>
                        {
                            // The attacker's whole capture animation has now fully finished. Only
                            // NOW is it safe to un-register and play a Defection spin that arrived
                            // for this same piece mid-stamp (see SwapPieceTeam/
                            // _pendingDefectionByAttacker) — "capture finishes, then Defection
                            // plays," never the two overlapping.
                            _pendingStampVictimByAttacker.Remove(attackerForClosure);

                            if (attackerForClosure != null &&
                                _pendingDefectionByAttacker.TryGetValue(attackerForClosure, out Vector2Int defectionPos))
                            {
                                _pendingDefectionByAttacker.Remove(attackerForClosure);
                                SwapPieceTeamNow(attackerForClosure, defectionPos);
                            }
                        });

                    // Skip the glow entirely when Defection is already locked in for this Act (see
                    // _actWillDefect's doc comment) — there's no Retribution choice to present, so
                    // glowing would just flash on for a beat before the piece spins away moments
                    // later once the stamp above finishes.
                    if (move.Stage == BetrayalStage.Act && !_actWillDefect)
                    {
                        movingPiece.SetBetrayerGlow(true);
                    }
                }
                else
                {
                    // Standard move: update dictionary and slide piece
                    _piecesByPosition[move.EndPosition] = movingPiece;

                    Vector3 targetPos = GetTileCenter(move.EndPosition.x, move.EndPosition.y);
                    targetPos.y += pieceYOffset;

                    MoveStyle style = move.IsCapture
                        ? MoveStyle.Capture
                        : (move.PieceType == ChessPieceType.Knight ? MoveStyle.Knight : MoveStyle.Quiet);
                    movingPiece.SetPosition(targetPos, style);

                    // A Betrayal Act's MoveExecutedPayload arrives after MatchDriver has already
                    // raised Initiated/RetributionPending on the BetrayalEventChannel, so the piece
                    // is still keyed at StartPosition when that handler runs. Glow it here instead,
                    // once it's guaranteed to be at EndPosition — unless Defection is already
                    // locked in for this Act (see _actWillDefect's doc comment), in which case
                    // there's no Retribution choice to present and the glow would just flash on
                    // for a beat before SwapPieceTeam spins the piece away.
                    if (move.Stage == BetrayalStage.Act && !_actWillDefect)
                    {
                        movingPiece.SetBetrayerGlow(true);
                    }
                }
            }
            else if (stampVictim != null)
            {
                // Defensive: the attacker wasn't found at StartPosition (shouldn't happen in normal
                // play) but a victim was already pulled off the board above — don't leave it stuck
                // invisible-but-alive in the dictionary. Falls back to the plain death animation
                // since there's no attacker to synchronize a stomp with.
                AnimateDeath(stampVictim);
            }

            // 3. Handle Castling (move the Rook) — starts CastleRookStartDelay seconds after the
            // king (see PrimeTweenPieceAnimator.MoveToForCastle) so the king visibly leads and the
            // rook tucks in right behind it, rather than both pieces sliding in lockstep.
            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                if (_piecesByPosition.TryGetValue(move.RookStartPosition.Value, out ChessPiece rook))
                {
                    _piecesByPosition.Remove(move.RookStartPosition.Value);
                    _piecesByPosition[move.RookEndPosition.Value] = rook;

                    Vector3 rookPos = GetTileCenter(move.RookEndPosition.Value.x, move.RookEndPosition.Value.y);
                    rookPos.y += pieceYOffset;
                    rook.PlayCastleMove(rookPos, PrimeTweenPieceAnimator.CastleRookStartDelay);
                }
            }

            // 4. Check warning: payload.IsCheck reports whether the side about to move NEXT (i.e.
            // the opponent of whoever just moved, move.PieceTeam) is now in check — see
            // MatchDriver.CheckForGameEnd. Frame their king in red and give it a startle
            // shake; clear any stale highlight otherwise (check can resolve, or the
            // highlighted king can change between moves, e.g. a discovered check on a different
            // turn). Deferred to the end of AnimateMove so it never races the king's own move/
            // castle animation still being set up above.
            if (payload.IsCheck)
            {
                Team defendingTeam = move.PieceTeam == Team.White ? Team.Black : Team.White;
                ShowKingInCheck(defendingTeam);
            }
            else
            {
                ClearCheckHighlight();
            }
        }

        /// <summary>
        /// Finds team's king among the currently spawned pieces and shows the check-warning frame
        /// under it plus a startle shake — the same lookup pattern ThreatPulseOwnKing already uses,
        /// kept as its own linear scan rather than a maintained king-position cache since it only
        /// runs once per move and the board never has more than one king per team.
        /// </summary>
        private void ShowKingInCheck(Team team)
        {
            foreach (var kv in _piecesByPosition)
            {
                ChessPiece piece = kv.Value;
                if (piece.team == team && piece.type == ChessPieceType.King)
                {
                    SetKingInCheckHighlight(kv.Key);
                    piece.Shake();
                    return;
                }
            }
        }

        /// <summary>
        /// Animates a captured piece moving to the death pile. Used for en passant (and the
        /// defensive fallback below) — the attacker never visually lands on this victim's square,
        /// so there's no impact beat to crush against. Instead the victim plays its own hop-and-
        /// shrink glide (PlayEnPassantDeath) straight to its reserved graveyard slot, then snaps to
        /// death-pile scale/facing on arrival. Direct captures use PlayStompedDeath -> SendToGraveyard
        /// instead (see AnimateMove), which crushes the piece flat in place first.
        /// </summary>
        private void AnimateDeath(ChessPiece victim)
        {
            victim.DisableCollider();
            victim.SetBetrayerGlow(false);

            (Vector3 deathPos, Vector3 lookDir) = ReserveGraveyardSlot(victim);

            victim.PlayEnPassantDeath(deathPos, onArrived: () =>
            {
                if (victim == null) return;
                victim.SetScale(Vector3.one * deathSize, force: true);
                victim.FaceDirection(lookDir);
            });
        }

        /// <summary>
        /// Claims the next open slot in victim's team's graveyard stack (adding it to the list, so
        /// the slot is permanently reserved the instant this is called) and returns its world
        /// position plus the look direction a piece should face once it's there. Pure bookkeeping —
        /// no visual mutation — so callers can compute WHERE a piece is headed before it starts
        /// animating toward the graveyard (PlayEnPassantDeath glides there) as well as after
        /// (SendToGraveyard teleports there once a stomp has already finished on the board).
        /// </summary>
        private (Vector3 position, Vector3 lookDir) ReserveGraveyardSlot(ChessPiece victim)
        {
            List<ChessPiece> graveyard = victim.team == Team.White ? _deadWhitePieces : _deadBlackPieces;
            graveyard.Add(victim);

            // Determine which side of the board for this team's graveyard
            int majorRowIndex = (victim.team == Team.White) ? 0 : _tileCountY - 1;
            float rowCenterZ = _boardOrigin.z + majorRowIndex * tileSize + tileSize * 0.5f;

            float stackSpacing = Mathf.Max(0.01f, deathSpacing);
            int stackIndex = graveyard.Count - 1;

            // Position off the side of the board
            float xPos = (victim.team == Team.White)
                ? _boardOrigin.x + _tileCountX * tileSize + (tileSize * 0.5f)
                : _boardOrigin.x - (tileSize * 0.5f);

            float zPos = (victim.team == Team.White)
                ? (rowCenterZ - (stackIndex * 0.5f * stackSpacing)) + (stackIndex * stackSpacing)
                : (rowCenterZ + (stackIndex * 0.5f * stackSpacing)) - (stackIndex * stackSpacing);

            Vector3 deathPos = new Vector3(xPos, _boardOrigin.y + tilesYOffset + pieceYOffset, zPos);

            // Calculate look direction toward board center
            Vector3 boardCenterPos = _boardOrigin + new Vector3(
                _tileCountX * tileSize * 0.5f,
                0f,
                _tileCountY * tileSize * 0.5f
            );
            Vector3 lookDir = (boardCenterPos - deathPos).normalized;
            lookDir.y = 0;

            return (deathPos, lookDir);
        }

        /// <summary>
        /// Places an already-vanished victim at its team's death pile: turns off its
        /// collider/glow, reserves the next open graveyard slot, and snaps position/facing/scale
        /// there instantly. Used once a capture "stamp" (PlayStompedDeath) has already finished
        /// collapsing the piece on the board — the graveyard placement itself is still a teleport,
        /// but it happens AFTER the crush animation, not instead of it. En passant's death instead
        /// travels to its slot directly (see AnimateMove/PlayEnPassantDeath) since there's no
        /// on-board crush to finish first.
        /// </summary>
        private void SendToGraveyard(ChessPiece victim)
        {
            victim.DisableCollider();
            victim.SetBetrayerGlow(false);

            (Vector3 deathPos, Vector3 lookDir) = ReserveGraveyardSlot(victim);

            // The stomp already shrank the victim to VanishedScale on the board; restore its
            // death-pile size here so it reads as a normal (if small) captured piece in the
            // graveyard rather than staying pinned at the stamp's near-zero scale.
            victim.SetScale(Vector3.one * deathSize, force: true);
            victim.SetPosition(deathPos, force: true);
            victim.FaceDirection(lookDir);
        }

        /// <summary>
        /// Snaps a piece back to its grid position (used when illegal move is attempted).
        /// </summary>
        public void SnapPieceBack(Vector2Int gridPos)
        {
            if (_piecesByPosition.TryGetValue(gridPos, out ChessPiece piece))
            {
                Vector3 worldPos = GetTileCenter(gridPos.x, gridPos.y);
                worldPos.y += pieceYOffset;
                piece.SetPosition(worldPos, force: true);
            }
        }

        /// <summary>
        /// Plays the tap-to-select "pick up" animation on the piece at the given grid position.
        /// No-op if nothing is there (e.g. a stale event after the piece moved). Lift height and
        /// the squash/rise/bob feel live entirely in IPieceAnimator now — BoardVisuals only
        /// resolves which piece to tell, not how the lift should look.
        /// </summary>
        public void LiftPieceAt(Vector2Int gridPos)
        {
            if (_piecesByPosition.TryGetValue(gridPos, out ChessPiece piece))
            {
                piece.LiftSelect();
            }
        }

        /// <summary>
        /// Plays the "set down" animation on the piece at the given grid position. Safe to call
        /// even if the piece already moved away from gridPos (no-op) — SelectionClearedEvent
        /// doesn't carry a position, so PieceLiftView must remember which tile it lifted.
        /// </summary>
        public void LowerPieceAt(Vector2Int gridPos)
        {
            if (_piecesByPosition.TryGetValue(gridPos, out ChessPiece piece))
            {
                piece.LowerDeselect();
            }
        }

        /// <summary>
        /// Handles move rejection events from GameManager.
        /// Called when a move is validated as illegal - snaps the piece back to its original position.
        /// This enables optimistic prediction for future networking.
        /// </summary>
        public void HandleMoveRejected(ChessTheBetrayal.Events.Payloads.MoveRejectedPayload payload)
        {
            // Extract the original coordinate and snap it back
            SnapPieceBack(payload.FromPosition);
        }

        /// <summary>
        /// Handles a tap on a piece that belongs to the current turn's team but has zero legal
        /// moves right now (pinned, stuck behind a forced Betrayal sub-phase, etc.) — see
        /// SelectionController.TrySelect, which raises this instead of silently declining the
        /// selection. Reuses the same startle Shake() the king's check-warning plays (see
        /// ShowKingInCheck) so "this piece can't move" reads with the same visual language as
        /// "this king is in danger" — a piece rattling in place. No-op if the piece can't be found
        /// (e.g. a stale event racing a game reset).
        /// </summary>
        public void HandleSelectionRejected(ChessTheBetrayal.Events.Payloads.SelectionRejectedPayload payload)
        {
            if (_piecesByPosition.TryGetValue(payload.Position, out ChessPiece piece))
            {
                piece.Shake();
            }
        }

        /// <summary>
        /// Optimistically glides the promoting pawn onto its destination square while the UI waits
        /// for the player's choice.
        ///
        /// If the promotion also captures, the captured piece is sent to the graveyard first, so
        /// the pawn never visually overlaps a still-standing enemy piece — the old behavior force-
        /// snapped onto the target square unconditionally, which meant a capturing promotion looked
        /// like the pawn teleported on top of the victim and then, moments later when the real
        /// MoveExecutedPayload finally arrived and AnimateMove ran the actual capture, the victim
        /// would vanish and the promotion swap would play — reading as a glitchy "snap back."
        ///
        /// The pawn is intentionally left keyed at FromPosition in _piecesByPosition (only its
        /// Transform moves here) rather than re-keyed to ToPosition. AnimateMove's promotion branch
        /// already looks the mover up by move.StartPosition and expects to find it there; leaving
        /// the keying alone means AnimateMove needs no special-casing to avoid redoing this glide —
        /// it just runs its normal promotion transition starting from wherever the pawn currently
        /// is, which by then is already ToPosition.
        /// </summary>
        public void HandlePromotionOptimisticGlide(ChessTheBetrayal.Events.Payloads.PromotionRequiredPayload payload)
        {
            if (payload.IsCapture && _piecesByPosition.TryGetValue(payload.ToPosition, out ChessPiece victim))
            {
                _piecesByPosition.Remove(payload.ToPosition);
                AnimateDeath(victim);
            }

            if (_piecesByPosition.TryGetValue(payload.FromPosition, out ChessPiece pawn))
            {
                Vector3 glidePos = GetTileCenter(payload.ToPosition.x, payload.ToPosition.y);
                glidePos.y += pieceYOffset;
                pawn.SetPosition(glidePos, MoveStyle.Promotion);
            }
        }

        /// <summary>
        /// Reacts to Betrayal phase transitions: glows the Betrayer while Retribution is
        /// genuinely pending, and swaps its prefab to the opposing team the moment Defection
        /// occurs — deferred until any in-flight capture stamp on that piece finishes first (see
        /// SwapPieceTeam).
        /// </summary>
        public void HandleBetrayalPhaseChanged(ChessTheBetrayal.Events.Payloads.BetrayalPayload payload)
        {
            switch (payload.Phase)
            {
                case ChessTheBetrayal.Events.Payloads.BetrayalPhase.Initiated:
                    // Cached for AnimateMove's Act branch, which runs moments later off the
                    // MoveExecutedPayload MatchDriver raises right after this: if Defection is
                    // already locked in (no legal Retribution move exists), skip the Betrayer glow
                    // entirely — there's no Retribution choice for the player to make, so the glow
                    // would only flash on for a beat before the piece spins away, which reads as a
                    // glitch rather than a deliberate cue.
                    _actWillDefect = payload.WillDefect;
                    break;

                case ChessTheBetrayal.Events.Payloads.BetrayalPhase.RetributionPending:
                    // The Betrayer's glow (when it happens at all) is applied by AnimateMove
                    // instead: this phase is raised by MatchDriver before the corresponding
                    // MoveExecutedPayload, so the piece is still keyed at its start square in
                    // _piecesByPosition at this point.
                    break;

                case ChessTheBetrayal.Events.Payloads.BetrayalPhase.Resolved:
                    // Betrayer was already removed by AnimateMove's normal capture path
                    // (Retribution stage raises a real MoveExecutedPayload) — nothing to do.
                    break;

                case ChessTheBetrayal.Events.Payloads.BetrayalPhase.DefectionOccurred:
                    SwapPieceTeam(payload.BetrayerPosition);
                    break;

                case ChessTheBetrayal.Events.Payloads.BetrayalPhase.ForcedSaveActive:
                    // The glow-off concern this case used to guard against no longer applies here
                    // (SwapPieceTeam's fresh-spawn piece never carries the Betrayer glow), but the
                    // race it describes — this phase firing synchronously back-to-back with
                    // DefectionOccurred, before SwapPieceTeam's transition-out callback has run —
                    // still applies to the king lookup below, which is why it tolerates a miss.
                    ThreatPulseOwnKing(payload.InitiatingTeam);
                    break;
            }
        }

        /// <summary>
        /// Plays the piece's defection spin, then destroys it and respawns it as the opposing
        /// team's prefab in the same square — the same transition-then-swap pattern AnimateMove
        /// uses for promotion. Routing the new GameObject through SpawnSinglePiece also sets its
        /// ChessPiece.team correctly, which is what keeps later graveyard routing accurate for
        /// pieces that defected.
        ///
        /// When no legal Retribution move exists, MatchDriver resolves Act -> Defection
        /// synchronously in one call, so this can be reached in the SAME FRAME the piece's own
        /// capture stamp just started — i.e. before that piece has finished playing "I just
        /// captured something." Rather than yanking the stamp mid-leap (which used to cut the
        /// animation off and, worse, silently strand the victim on the board), the spin is queued
        /// in _pendingDefectionByAttacker and fired from the stamp's own completion callback in
        /// AnimateMove instead — so the two animations always play in order: capture finishes,
        /// THEN the Defection spin begins.
        /// </summary>
        private void SwapPieceTeam(Vector2Int pos)
        {
            if (!_piecesByPosition.TryGetValue(pos, out ChessPiece piece)) return;

            if (_pendingStampVictimByAttacker.ContainsKey(piece))
            {
                _pendingDefectionByAttacker[piece] = pos;
                return;
            }

            SwapPieceTeamNow(piece, pos);
        }

        /// <summary>
        /// The actual spin-and-swap, split out from SwapPieceTeam so AnimateMove's stamp completion
        /// can invoke it directly once a queued Defection's capture animation has finished (see
        /// _pendingDefectionByAttacker's doc comment) without re-running the "is this piece mid-stamp"
        /// check that already applies here.
        /// </summary>
        private void SwapPieceTeamNow(ChessPiece piece, Vector2Int pos)
        {
            Team newTeam = piece.team == Team.White ? Team.Black : Team.White;
            PieceData flipped = new PieceData(
                team: newTeam,
                type: piece.type,
                moveDirection: newTeam == Team.White ? 1 : -1,
                startRow: 0,
                hasMoved: true
            );

            _piecesByPosition.Remove(pos);

            piece.PlayTransitionOut(PieceTransitionStyle.Spin, () =>
            {
                // Guards the same "destroyed while the transition was mid-flight" case documented
                // in AnimateMove's promotion branch above (e.g. a game reset during the spin).
                if (piece == null) return;
                Destroy(piece.gameObject);
                ChessPiece defected = SpawnSinglePiece(flipped, pos);
                defected?.PlayTransitionIn(PieceTransitionStyle.Spin);
            });
        }

        /// <summary>
        /// Flashes the given team's king red twice — the "Defensive Override" cue that tells the
        /// player why they're being forced into a Save move: their own defected piece just put
        /// their king in check. Silently no-ops if the king can't be found (defense-in-depth; see
        /// the ForcedSaveActive case above for why that can theoretically race).
        /// </summary>
        private void ThreatPulseOwnKing(Team team)
        {
            foreach (var kv in _piecesByPosition)
            {
                ChessPiece piece = kv.Value;
                if (piece.team == team && piece.type == ChessPieceType.King)
                {
                    piece.FlashGlow(Color.red, KingThreatFlashIntensity, KingThreatFlashDuration, KingThreatFlashCycles);
                    return;
                }
            }
        }

        #endregion

        #region Public Getters for Input Controller

        /// <summary>
        /// Converts a raycast hit transform into a grid coordinate.
        /// Walks up the hierarchy to find the tile.
        /// </summary>
        public Vector2Int GetTileIndexFromTransform(Transform t)
        {
            Transform cur = t;
            int safety = 0;

            while (cur != null && safety++ < 16)
            {
                if (_tileByTransform.TryGetValue(cur, out Vector2Int idx))
                {
                    return idx;
                }
                cur = cur.parent;
            }

            return Vector2Int.Invalid;
        }

        /// <summary>
        /// Returns the Transform of the piece at the given grid position.
        /// Used by input controller for dragging.
        /// </summary>
        public Transform GetPieceTransformAt(Vector2Int gridPos)
        {
            if (_piecesByPosition.TryGetValue(gridPos, out ChessPiece piece))
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
            return _boardOrigin + new Vector3(
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
        public void UpdateHoverHighlight(Vector2Int idx)
        {
            if (_hoverIndex == idx) return;

            // Clear old hover
            if (_hoverIndex != Vector2Int.Invalid)
            {
                SetTileLayer(_hoverIndex, isHover: false);
            }

            // Set new hover
            _hoverIndex = idx;
            if (_hoverIndex != Vector2Int.Invalid)
            {
                SetTileLayer(_hoverIndex, isHover: true);
            }
        }

        /// <summary>
        /// Clears the hover highlight.
        /// </summary>
        public void ClearHoverHighlight()
        {
            UpdateHoverHighlight(Vector2Int.Invalid);
        }

        /// <summary>
        /// Highlights all legal move destinations.
        /// </summary>
        public void HighlightLegalMoves(IReadOnlyList<MoveCommand> moves)
        {
            ClearLegalMoveHighlights();
            for (int i = 0; i < moves.Count; i++)
            {
                Vector2Int pos = moves[i].EndPosition;

                _highlightedSquares.Add(pos);
                _highlightedSquaresLookup.Add(pos);

                // A move that captures gets the capture-colored indicator instead. Checking
                // move.IsCapture (rather than whether the destination square holds a piece) is
                // required for en passant, whose destination is empty — the captured pawn sits on
                // a different square (MoveCommand.EnPassantCapturePosition).
                if (moves[i].IsCapture)
                {
                    _captureSquaresLookup.Add(pos);
                }

                SetTileLayer(pos, isHover: false);
            }
        }

        /// <summary>
        /// Clears all legal move highlights.
        /// </summary>
        public void ClearLegalMoveHighlights()
        {
            for (int i = 0; i < _highlightedSquares.Count; i++)
            {
                Vector2Int pos = _highlightedSquares[i];
                if (pos.x >= 0 && pos.x < _tileCountX && pos.y >= 0 && pos.y < _tileCountY && _tiles[pos.x, pos.y] != null)
                {
                    _tiles[pos.x, pos.y].layer = _tileLayer;

                    MeshRenderer indicator = _moveIndicatorRenderers[pos.x, pos.y];
                    if (indicator != null) indicator.enabled = false;
                }
            }
            _highlightedSquares.Clear();
            _highlightedSquaresLookup.Clear();
            _captureSquaresLookup.Clear();

            // Restore hover if active
            if (_hoverIndex != Vector2Int.Invalid)
            {
                SetTileLayer(_hoverIndex, isHover: true);
            }
        }

        /// <summary>
        /// Sets the appropriate layer for a tile based on its state. The square tile itself only
        /// ever shows the base or hover look; a legal-move destination is instead signalled by
        /// enabling the tile's circular MoveIndicator child on the move/capture layer, so hover
        /// and move-highlight can never fight over the same renderer's layer.
        /// </summary>
        private void SetTileLayer(Vector2Int pos, bool isHover)
        {
            if (pos.x < 0 || pos.x >= _tileCountX || pos.y < 0 || pos.y >= _tileCountY) return;

            GameObject tile = _tiles[pos.x, pos.y];
            if (tile == null) return;

            tile.layer = isHover ? _highlightLayer : _tileLayer;

            MeshRenderer indicator = _moveIndicatorRenderers[pos.x, pos.y];
            if (indicator == null) return;

            // Check the lookup set, not the list — it's much faster for this kind of check
            if (_highlightedSquaresLookup.Contains(pos))
            {
                indicator.enabled = true;
                indicator.gameObject.layer = _captureSquaresLookup.Contains(pos)
                    ? _moveHighlightCaptureLayer
                    : _moveHighlightLayer;
            }
            else
            {
                indicator.enabled = false;
            }
        }

        /// <summary>
        /// Shows the red check-warning frame under the king's tile at kingPos and clears any
        /// previous one (a check can shift squares between moves — e.g. king moves out of check on
        /// one turn, a discovered check lands on a different king the next). Uses the dedicated
        /// CheckHighlight URP layer/material (its own bright red frame look, separate from the
        /// capturing-move dot's red) on the hollow frame mesh, so "your king is in danger" reads as
        /// a red border framing the king rather than a colored dot or a filled tile.
        /// </summary>
        public void SetKingInCheckHighlight(Vector2Int kingPos)
        {
            ClearCheckHighlight();

            if (kingPos.x < 0 || kingPos.x >= _tileCountX || kingPos.y < 0 || kingPos.y >= _tileCountY) return;

            MeshRenderer indicator = _checkIndicatorRenderers[kingPos.x, kingPos.y];
            if (indicator == null) return;

            indicator.enabled = true;
            indicator.gameObject.layer = _checkHighlightLayer;
            _checkHighlightSquare = kingPos;
        }

        /// <summary>
        /// Hides the check-warning square, if one is currently showing. Safe to call unconditionally
        /// (e.g. every move) — a no-op when no king is in check.
        /// </summary>
        public void ClearCheckHighlight()
        {
            if (_checkHighlightSquare == Vector2Int.Invalid) return;

            if (_checkHighlightSquare.x >= 0 && _checkHighlightSquare.x < _tileCountX &&
                _checkHighlightSquare.y >= 0 && _checkHighlightSquare.y < _tileCountY)
            {
                MeshRenderer indicator = _checkIndicatorRenderers[_checkHighlightSquare.x, _checkHighlightSquare.y];
                if (indicator != null) indicator.enabled = false;
            }

            _checkHighlightSquare = Vector2Int.Invalid;
        }

        #endregion

        #region Initialization Helpers

        /// <summary>
        /// Looks up the layer IDs by name once at startup so we're not doing string lookups every frame.
        /// </summary>
        private void CacheLayers()
        {
            _tileLayer = LayerMask.NameToLayer("Tile");
            _highlightLayer = LayerMask.NameToLayer("Highlight");
            _moveHighlightLayer = LayerMask.NameToLayer("MoveHighlight");
            _moveHighlightCaptureLayer = LayerMask.NameToLayer("MoveHighlightCapture");
            _checkHighlightLayer = LayerMask.NameToLayer("CheckHighlight");

            if (_tileLayer == -1) _tileLayer = 0;
            if (_highlightLayer == -1) _highlightLayer = 0;
            if (_moveHighlightLayer == -1) _moveHighlightLayer = 0;
            if (_moveHighlightCaptureLayer == -1) _moveHighlightCaptureLayer = 0;
            // Falls back to the capture layer if the CheckHighlight layer hasn't been created yet,
            // so the check warning still shows (as the capture red) rather than silently landing on
            // the Default layer and rendering as an untinted frame.
            if (_checkHighlightLayer == -1) _checkHighlightLayer = _moveHighlightCaptureLayer;
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