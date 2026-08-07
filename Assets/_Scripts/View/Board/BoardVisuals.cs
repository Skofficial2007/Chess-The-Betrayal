using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Infrastructure;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// The eyes of the game. Spawns and moves piece GameObjects, highlights tiles, and plays animations.
    /// It has no idea how chess rules work — it just listens to GameManager and reacts.
    /// </summary>
    public class BoardVisuals : MonoBehaviour, IBoardHitTest
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

        [Header("Board Highlights")]
        [Tooltip("Every colour and dimension the square markers use. Retune the board's look by editing that asset — nothing on this component decides it.")]
        [SerializeField] private BoardHighlightPalette highlightPalette;
        [Tooltip("Height of a square's tint above the tile surface.")]
        [SerializeField] private float tintYOffset = 0.01f;
        [Tooltip("Height of a square's marker above the tile surface. Keep this above tintYOffset — two transparent shapes at the same height flicker against each other as the camera moves.")]
        [SerializeField] private float markerYOffset = 0.02f;
        [Tooltip("Height of the capture reticle's grounding shadow above the tile surface. Keep this below tintYOffset, same reason as markerYOffset.")]
        [SerializeField] private float shadowYOffset = 0.005f;

        [Header("Hierarchy Containers")]
        [SerializeField] private Transform tilesParent;
        [SerializeField] private Transform whitePiecesParent;
        [SerializeField] private Transform blackPiecesParent;

        [Header("Data Source")]
        [SerializeField] private ChessTheBetrayal.Events.SharedBoardStateSO _sharedBoardState;

        [Header("Event Channels")]
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _gameStartedChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _gameResetChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _boardResyncRequiredChannel;
        [SerializeField] private ChessTheBetrayal.Events.MoveExecutedEventChannel _moveExecutedChannel;
        [Tooltip("Carries each ply of a takeback as it comes back off the board, so the move can be played in reverse. Must be the same asset GameManager raises on. Leave unassigned and undo falls back to rebuilding the position instead.")]
        [SerializeField] private ChessTheBetrayal.Events.MoveUndoneEventChannel _moveUndoneChannel;
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

        // Two overlay children per tile: a full-square tint underneath, and a marker shape on top.
        // Two rather than one because a square often needs both at once — a capture standing on the
        // square the last move landed on shows a ring over a faint tint — and a single renderer
        // could only ever say one of those two things.
        private MeshRenderer[,] _tintRenderers;
        private MeshFilter[,] _tintFilters;
        private MeshRenderer[,] _markerRenderers;
        private MeshFilter[,] _markerFilters;

        // A third overlay, below the tint, that only ever shows the capture reticle's grounding
        // shadow today. Independent of the tint slot because a capture square can already carry a
        // last-move or selection tint at the same time.
        private MeshRenderer[,] _shadowRenderers;

        // The selected square's four corner ticks are NOT drawn through the generic marker slot —
        // each one needs to travel toward its own corner from its own direction, which one shared
        // Transform can't do. Built once (only one square is ever selected at a time) and reparented
        // to whichever tile currently needs them. See ShowSelectionCornerTicks.
        private Transform _selectionTicksRoot;
        private readonly Mesh[] _selectionTickMeshes = new Mesh[4];

        // The Betrayer's corner brackets, likewise a single reparented instance rather than one per
        // tile. Separate from its marker because the marker rotates and these must not.
        private Transform _betrayerBracketsRoot;
        private Mesh _betrayerBracketsMesh;

        private static readonly float[] SelectionTickSignX = { -1f, 1f, 1f, -1f };
        private static readonly float[] SelectionTickSignZ = { -1f, -1f, 1f, 1f };

        // One mesh per shape, shared by all 64 squares, built once when the board is generated.
        private Mesh _tintMesh;
        private Mesh _lastMoveFromOutlineMesh;
        private Mesh _dotMesh;
        private Mesh _captureReticleMesh;
        private Mesh _captureShadowMesh;
        private Mesh _betrayalMesh;
        private Mesh _betrayerAtLargeMesh;
        private Mesh _checkFrameMesh;

        // One material per state, built from the palette at startup and shared across squares. These
        // are created at runtime, so OnDestroy has to clean them up.
        private Material[] _markerMaterials;
        private Material[] _tintMaterials;
        private Material _captureShadowMaterial;

        // Where every square's highlight is decided. This component only draws what it answers.
        private readonly BoardHighlightMap _highlights = new BoardHighlightMap();

        // The squares drawn on the last redraw, so the next one can clear exactly those instead of
        // walking all 64.
        private readonly List<Vector2Int> _drawnSquares = new List<Vector2Int>(48);
        private readonly List<Vector2Int> _squaresToDraw = new List<Vector2Int>(48);

        // Which marker each square is currently showing, so a redraw can tell a genuinely new marker
        // (which should animate in) from one that was already there (which should not restart).
        private SquareMarker[,] _shownMarkers;

        // Death piles, one per side. Ordered by when each piece was taken — see GraveyardStack.
        private readonly GraveyardStack<ChessPiece> _graveyard = new GraveyardStack<ChessPiece>();

        private readonly List<ChessPiece> _destroyQueue = new List<ChessPiece>(32);

        // Cached values
        private Vector3 _boardOrigin;
        private int _tileCountX, _tileCountY;
        private int _tileLayer;

        // Defensive Override threat-pulse: two quick red flashes on the player's own king when a
        // Forced Save activates, so the sudden forced move reads as "your king is in danger" rather
        // than an unexplained rules interruption.
        private const float KingThreatFlashIntensity = 2.5f;
        private const float KingThreatFlashDuration = 0.15f;
        private const int KingThreatFlashCycles = 2;

        // Resolved once rather than hashing the same two strings on every material write.
        private static readonly int BaseColourProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int GlowProperty = Shader.PropertyToID("_Glow");

        // How far a marker overshoots its final size on the way in. Matches the arrival overshoot
        // the pieces themselves use, so the board has one motion vocabulary rather than two.
        private const float MarkerAppearOvershoot = 1.7f;

        #endregion

        #region Public Properties

        public float TileYOffset => tilesYOffset;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            ServiceLocator.Instance.Register(this);
            ServiceLocator.Instance.Register<IBoardHitTest>(this);

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
            InspectorGuard.Require(highlightPalette, nameof(highlightPalette), this);
            InspectorGuard.Require(_sharedBoardState, nameof(_sharedBoardState), this);
            InspectorGuard.Require(_gameStartedChannel, nameof(_gameStartedChannel), this);
            InspectorGuard.Require(_gameResetChannel, nameof(_gameResetChannel), this);
            InspectorGuard.Require(_boardResyncRequiredChannel, nameof(_boardResyncRequiredChannel), this);
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
            if (!ServiceLocator.Instance.TryResolve<IBoardQuery>(out _))
            {
                Debug.LogError("[BoardVisuals] The match host (IBoardQuery) was never registered!");
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
            _boardResyncRequiredChannel?.Register(HandleBoardResync);
            _moveExecutedChannel?.Register(AnimateMove);
            _moveUndoneChannel?.Register(AnimateMoveUndone);
            _moveRejectedChannel?.Register(HandleMoveRejected);
            _selectionRejectedChannel?.Register(HandleSelectionRejected);
            _promotionRequiredChannel?.Register(HandlePromotionOptimisticGlide);
            _betrayalChannel?.Register(HandleBetrayalPhaseChanged);
        }

        private void OnDisable()
        {
            _gameStartedChannel?.Unregister(HandleGameStarted);
            _gameResetChannel?.Unregister(ClearAllVisuals);
            _boardResyncRequiredChannel?.Unregister(HandleBoardResync);
            _moveExecutedChannel?.Unregister(AnimateMove);
            _moveUndoneChannel?.Unregister(AnimateMoveUndone);
            _moveRejectedChannel?.Unregister(HandleMoveRejected);
            _selectionRejectedChannel?.Unregister(HandleSelectionRejected);
            _promotionRequiredChannel?.Unregister(HandlePromotionOptimisticGlide);
            _betrayalChannel?.Unregister(HandleBetrayalPhaseChanged);
        }

        #endregion

        #region Setup & Mesh Generation

        /// <summary>
        /// A new game is beginning: build the board and let the army materialize rank by rank.
        /// </summary>
        public void HandleGameStarted() => RebuildBoard(playSetupWave: true);

        /// <summary>
        /// Rebuild the board to match whatever the shared state currently holds, with no fanfare —
        /// pieces are simply there. This is the recovery path (a client reconnecting to a match
        /// already in progress needs the position, not a performance), which is why it is a
        /// separate signal from game-started rather than the same one reused.
        ///
        /// Kept distinct from HandleGameStarted specifically so the setup wave stays what it was
        /// written to be: the opening of a fresh game, and nothing else.
        /// </summary>
        public void HandleBoardResync() => RebuildBoard(playSetupWave: false);

        private void RebuildBoard(bool playSetupWave)
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

            _graveyard.SetLayout(new GraveyardLayout(
                _boardOrigin, tileSize, _tileCountX, _tileCountY, tilesYOffset, pieceYOffset, deathSpacing));

            SpawnAllPieces(initialBoard, playSetupWave);
        }

        /// <summary>
        /// Generates the 3D tile meshes and colliders for the board.
        /// </summary>
        private void GenerateTileMeshes()
        {
            _tiles = new GameObject[_tileCountX, _tileCountY];
            _tintRenderers = new MeshRenderer[_tileCountX, _tileCountY];
            _tintFilters = new MeshFilter[_tileCountX, _tileCountY];
            _markerRenderers = new MeshRenderer[_tileCountX, _tileCountY];
            _markerFilters = new MeshFilter[_tileCountX, _tileCountY];
            _shadowRenderers = new MeshRenderer[_tileCountX, _tileCountY];
            _shownMarkers = new SquareMarker[_tileCountX, _tileCountY];
            _tileByTransform.Clear();

            BuildHighlightMeshes();
            BuildHighlightMaterials();
            BuildSelectionCornerTicks();
            BuildBetrayerBrackets();

            // Every overlay is born holding a real material. The tint and marker slots overwrite
            // theirs the moment they show something, so which one they start with is irrelevant —
            // what matters is that no overlay can ever exist without one. A MeshRenderer with a null
            // material does not draw nothing, it draws Unity's magenta error shader.
            Material initialTint = _tintMaterials?[(int)SquareTint.Selected];
            Material initialMarker = _markerMaterials?[(int)SquareMarker.Selected];

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

                    // The shadow sits lowest of the three, then the tint, then the marker on top —
                    // its mesh and material never vary (only the capture reticle ever uses it), so
                    // both are fixed here rather than assigned per state.
                    _shadowRenderers[x, y] = CreateOverlay(tileGO.transform, $"Shadow_{x}_{y}", shadowYOffset, _captureShadowMesh, _captureShadowMaterial, out _);

                    // The square's tint sits above the shadow, so a marker always reads as being on
                    // top of both. Its mesh is null here and assigned per state in ApplyHighlight, the
                    // same as the marker slot, because the last-move origin now draws a hollow outline
                    // rather than the same filled quad every other tint uses.
                    _tintRenderers[x, y] = CreateOverlay(tileGO.transform, $"Tint_{x}_{y}", tintYOffset, null, initialTint, out MeshFilter tintFilter);
                    _tintFilters[x, y] = tintFilter;
                    _markerRenderers[x, y] = CreateOverlay(tileGO.transform, $"Marker_{x}_{y}", markerYOffset, null, initialMarker, out MeshFilter markerFilter);
                    _markerFilters[x, y] = markerFilter;

                    // Store references
                    _tiles[x, y] = tileGO;
                    _tileByTransform[tileGO.transform] = new Vector2Int(x, y);
                }
            }
        }

        /// <summary>
        /// Adds one flat overlay child to a tile, hidden until something asks for it. A null mesh
        /// means the caller will assign one per state (which is what the marker slot does).
        ///
        /// The material is a required argument rather than something the caller may set later,
        /// because a MeshRenderer left without one silently draws Unity's magenta error shader
        /// instead of drawing nothing — a mistake that is invisible in code review and glaring on
        /// screen. Passing it here makes forgetting it impossible.
        /// </summary>
        private MeshRenderer CreateOverlay(Transform tile, string overlayName, float yOffset, Mesh mesh, Material material, out MeshFilter filter)
        {
            GameObject overlay = new GameObject(overlayName);
            overlay.transform.SetParent(tile, false);
            overlay.transform.localPosition = new Vector3(0f, yOffset, 0f);
            overlay.layer = _tileLayer;

            filter = overlay.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = overlay.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = false;

            // These are flat decals lying on the board. Letting them cast or receive shadows would
            // make an unlit affordance pick up the scene's lighting through the back door.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return renderer;
        }

        /// <summary>
        /// Builds the one mesh per marker shape that every square shares. Sizes come from the
        /// palette, so retuning a shape is an Inspector edit rather than a code change.
        /// </summary>
        private void BuildHighlightMeshes()
        {
            int segments = highlightPalette.CircleSegments;

            float tintOuter = tileSize * highlightPalette.TintSizeRatio;
            _tintMesh = GenerateQuadMesh(tintOuter);
            _lastMoveFromOutlineMesh = GenerateFrameMesh(tintOuter, tintOuter * highlightPalette.LastMoveFromOutlineThicknessRatio);
            _dotMesh = GenerateCircleMesh(tileSize * highlightPalette.DotRadiusRatio, segments);
            // Shared by every marker that has to stay readable with a piece standing on its square.
            float bracketHalf = tileSize * highlightPalette.MarkerBracketSpanRatio * 0.5f;
            float bracketLength = tileSize * highlightPalette.MarkerBracketLengthRatio;
            float bracketThickness = tileSize * highlightPalette.MarkerBracketThicknessRatio;

            _captureReticleMesh = GenerateCaptureReticleMesh(
                tileSize * highlightPalette.CaptureRingRadiusRatio,
                tileSize * highlightPalette.CaptureRingThicknessRatio,
                bracketHalf, bracketLength, bracketThickness,
                segments);
            _captureShadowMesh = GenerateCircleMesh(tileSize * highlightPalette.CaptureShadowRadiusRatio, segments);
            _betrayalMesh = GenerateBetrayalMarkerMesh(
                tileSize * highlightPalette.BetrayalRingRadiusRatio,
                tileSize * highlightPalette.CaptureRingThicknessRatio,
                tileSize * highlightPalette.BetrayalDiamondRatio,
                bracketHalf, bracketLength, bracketThickness,
                segments);
            _betrayerAtLargeMesh = GenerateBetrayerMarkerMesh(
                tileSize * highlightPalette.BetrayalRingRadiusRatio,
                tileSize * highlightPalette.CaptureRingThicknessRatio,
                tileSize * highlightPalette.BetrayerChevronLengthRatio,
                tileSize * highlightPalette.BetrayerChevronHalfWidthRatio,
                segments);

            float frameOuter = tileSize * highlightPalette.CheckFrameSizeRatio;
            _checkFrameMesh = GenerateFrameMesh(frameOuter, frameOuter * highlightPalette.CheckFrameThicknessRatio);
        }

        /// <summary>
        /// Builds one material per highlight state from the palette. All of them share a single
        /// unlit shader, which is what lets 64 squares of markers still batch together.
        ///
        /// Palette colours are picked in the Inspector's ordinary sRGB picker, so they are converted
        /// to linear on the way in — the project renders in linear space, and handing a shader raw
        /// sRGB numbers is the classic way to end up with colours that look washed out for no
        /// visible reason.
        /// </summary>
        private void BuildHighlightMaterials()
        {
            Shader shader = Shader.Find("Chess/BoardHighlight");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(BoardVisuals)}] The board highlight shader is missing, so no square markers can be drawn.", this);
                return;
            }

            DestroyHighlightMaterials();

            _markerMaterials = new Material[System.Enum.GetValues(typeof(SquareMarker)).Length];
            _tintMaterials = new Material[System.Enum.GetValues(typeof(SquareTint)).Length];

            foreach (SquareMarker marker in System.Enum.GetValues(typeof(SquareMarker)))
            {
                if (marker == SquareMarker.None) continue;
                _markerMaterials[(int)marker] = CreateHighlightMaterial(shader, $"Marker_{marker}", highlightPalette.LookFor(marker));
            }

            foreach (SquareTint tint in System.Enum.GetValues(typeof(SquareTint)))
            {
                if (tint == SquareTint.None) continue;
                _tintMaterials[(int)tint] = CreateHighlightMaterial(shader, $"Tint_{tint}", highlightPalette.LookFor(tint));
            }

            _captureShadowMaterial = CreateHighlightMaterial(shader, "CaptureShadow", highlightPalette.CaptureShadow);
        }

        /// <summary>
        /// Builds the four corner-tick GameObjects the selected square uses, once — only one square
        /// is ever selected at a time, so there is nothing to gain from a copy per tile. Starts
        /// parented under tilesParent and hidden; ShowSelectionCornerTicks reparents the whole group
        /// onto whichever tile is picked up.
        /// </summary>
        private void BuildSelectionCornerTicks()
        {
            Shader shader = Shader.Find("Chess/BoardHighlight");
            if (shader == null) return;

            // Rebuilding the board rebuilds the materials these point at, so an earlier group would
            // be left holding destroyed ones.
            if (_selectionTicksRoot != null) Destroy(_selectionTicksRoot.gameObject);

            GameObject root = new GameObject("SelectionCornerTicks");
            root.transform.SetParent(tilesParent, false);
            root.SetActive(false);
            _selectionTicksRoot = root.transform;

            float half = tileSize * highlightPalette.TintSizeRatio * 0.5f;
            float length = tileSize * highlightPalette.CornerTickLengthRatio;
            float thickness = tileSize * highlightPalette.CornerTickThicknessRatio;
            Material material = _markerMaterials[(int)SquareMarker.Selected];

            for (int i = 0; i < 4; i++)
            {
                _selectionTickMeshes[i] = GenerateSingleCornerTickMesh(
                    SelectionTickSignX[i], SelectionTickSignZ[i], length, thickness, half);

                GameObject tick = new GameObject($"Tick_{i}");
                tick.transform.SetParent(_selectionTicksRoot, false);
                tick.layer = _tileLayer;

                tick.AddComponent<MeshFilter>().sharedMesh = _selectionTickMeshes[i];

                MeshRenderer renderer = tick.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        /// <summary>
        /// Builds the Betrayer marker's corner brackets as their own object, once, for the same
        /// reason the selection ticks are built this way: only one Betrayer is ever at large.
        ///
        /// They cannot live in the Betrayer's own marker mesh because that mesh rotates, and a
        /// rotating bracket leaves the corner it was placed in to survive. Keeping them on a
        /// separate, still transform is what lets the ring keep spinning while the corners stay put.
        /// </summary>
        private void BuildBetrayerBrackets()
        {
            if (_markerMaterials == null) return;

            if (_betrayerBracketsRoot != null) Destroy(_betrayerBracketsRoot.gameObject);

            GameObject root = new GameObject("BetrayerBrackets");
            root.transform.SetParent(tilesParent, false);
            root.layer = _tileLayer;
            root.SetActive(false);
            _betrayerBracketsRoot = root.transform;

            var vertices = new Vector3[CornerBracketVertexCount];
            var triangles = new int[CornerBracketIndexCount];
            int vertexCount = 0;
            int triangleCount = 0;

            AppendCornerBrackets(vertices, triangles, ref vertexCount, ref triangleCount,
                tileSize * highlightPalette.MarkerBracketSpanRatio * 0.5f,
                tileSize * highlightPalette.MarkerBracketLengthRatio,
                tileSize * highlightPalette.MarkerBracketThicknessRatio);

            _betrayerBracketsMesh = new Mesh { name = "BetrayerBracketsMesh" };
            _betrayerBracketsMesh.vertices = vertices;
            _betrayerBracketsMesh.triangles = triangles;
            _betrayerBracketsMesh.RecalculateNormals();

            root.AddComponent<MeshFilter>().sharedMesh = _betrayerBracketsMesh;

            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _markerMaterials[(int)SquareMarker.BetrayerAtLarge];
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material CreateHighlightMaterial(Shader shader, string materialName, BoardHighlightPalette.Look look)
        {
            Material material = new Material(shader) { name = materialName };
            material.SetColor(BaseColourProperty, look.Colour.linear);
            material.SetFloat(GlowProperty, look.Glow);
            return material;
        }

        private void DestroyHighlightMaterials()
        {
            DestroyMaterials(_markerMaterials);
            DestroyMaterials(_tintMaterials);
            _markerMaterials = null;
            _tintMaterials = null;

            if (_captureShadowMaterial != null) Destroy(_captureShadowMaterial);
            _captureShadowMaterial = null;
        }

        private static void DestroyMaterials(Material[] materials)
        {
            if (materials == null) return;

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null) Destroy(materials[i]);
            }
        }

        /// <summary>
        /// Builds a flat square centred on the tile, used for a square's tint.
        /// </summary>
        private static Mesh GenerateQuadMesh(float size)
        {
            Mesh mesh = new Mesh { name = "SquareTintMesh" };

            float half = size * 0.5f;
            mesh.vertices = new Vector3[]
            {
                new Vector3(-half, 0f, -half),
                new Vector3(-half, 0f,  half),
                new Vector3( half, 0f, -half),
                new Vector3( half, 0f,  half)
            };
            mesh.triangles = new int[] { 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Builds a flat annulus — the capture marker. A ring rather than a filled circle because a
        /// capture square always has a piece standing on it, and a disc would simply hide the thing
        /// the player is deciding whether to take.
        /// </summary>
        private static Mesh GenerateRingMesh(float outerRadius, float thickness, int segments)
        {
            Mesh mesh = new Mesh { name = "CaptureRingMesh" };

            float innerRadius = Mathf.Max(0f, outerRadius - thickness);
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                vertices[i * 2] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                vertices[i * 2 + 1] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
            }

            for (int i = 0; i < segments; i++)
            {
                int outerA = i * 2;
                int innerA = i * 2 + 1;
                int outerB = (i + 1) % segments * 2;
                int innerB = (i + 1) % segments * 2 + 1;

                int t = i * 6;
                triangles[t] = outerA;
                triangles[t + 1] = outerB;
                triangles[t + 2] = innerA;
                triangles[t + 3] = innerA;
                triangles[t + 4] = outerB;
                triangles[t + 5] = innerB;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Builds the capture marker: the capture ring plus a bracket hugging each corner of the
        /// tile, as one mesh.
        ///
        /// The brackets are not decoration. A capture square always has a piece standing on it, and
        /// the pieces are two to three times taller than a square is wide, so from a tilted camera
        /// the piece hides everything on the board behind it far past the ring's own radius — the
        /// whole far half of the ring is gone, and what is left reads as a smear at the piece's base
        /// rather than a ring around it. The tile corners are the furthest points from the piece and
        /// the occlusion travels up-screen rather than sideways, so a mark placed there survives
        /// whatever is standing on the square. The ring still carries the look when the piece is
        /// small; the corners are what guarantee the state is readable under a Queen.
        /// </summary>
        private static Mesh GenerateCaptureReticleMesh(float ringOuterRadius, float ringThickness, float bracketHalf, float bracketLength, float bracketThickness, int segments)
        {
            Mesh ring = GenerateRingMesh(ringOuterRadius, ringThickness, segments);

            Vector3[] ringVertices = ring.vertices;
            int[] ringTriangles = ring.triangles;

            var vertices = new Vector3[ringVertices.Length + CornerBracketVertexCount];
            var triangles = new int[ringTriangles.Length + CornerBracketIndexCount];

            ringVertices.CopyTo(vertices, 0);
            ringTriangles.CopyTo(triangles, 0);

            int vertexCount = ringVertices.Length;
            int triangleCount = ringTriangles.Length;

            AppendCornerBrackets(vertices, triangles, ref vertexCount, ref triangleCount,
                bracketHalf, bracketLength, bracketThickness);

            var mesh = new Mesh { name = "CaptureReticleMesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        // Four corners, two bars each, four vertices and six indices per bar.
        private const int CornerBracketVertexCount = 4 * 2 * 4;
        private const int CornerBracketIndexCount = 4 * 2 * 6;

        /// <summary>
        /// Appends an L-shaped bracket at each corner of a square of half-width <paramref name="half"/>,
        /// each one running inward along both edges from the corner it sits in.
        ///
        /// Shared by every marker whose square has a piece standing on it, because the corners are
        /// the only part of a tile a tall piece cannot hide (see GenerateCaptureReticleMesh). The
        /// selection marker draws the same shape, but builds its four corners as separate meshes on
        /// separate transforms so each can animate independently — here they are baked into one
        /// static mesh, which is all these states need.
        /// </summary>
        private static void AppendCornerBrackets(Vector3[] vertices, int[] triangles, ref int vertexCount, ref int triangleCount,
            float half, float length, float thickness)
        {
            length = Mathf.Min(length, half);

            for (int corner = 0; corner < 4; corner++)
            {
                float signX = SelectionTickSignX[corner];
                float signZ = SelectionTickSignZ[corner];

                float cornerX = signX * half;
                float cornerZ = signZ * half;

                // Bar running along the X edge.
                AddBar(vertices, triangles, ref vertexCount, ref triangleCount,
                    cornerX, cornerZ, cornerX - signX * length, cornerZ - signZ * thickness);

                // Bar running along the Z edge.
                AddBar(vertices, triangles, ref vertexCount, ref triangleCount,
                    cornerX, cornerZ - signZ * thickness, cornerX - signX * thickness, cornerZ - signZ * length);
            }
        }

        /// <summary>
        /// Builds the Betrayal marker: the capture ring with a diamond floating at its centre, as one
        /// mesh. A Betrayal is not an ordinary capture and must not look like one — so it gets a
        /// silhouette of its own rather than only a different colour, which keeps it distinguishable
        /// for a player who cannot separate the two hues.
        /// </summary>
        private static Mesh GenerateBetrayalMarkerMesh(float ringRadius, float ringThickness, float diamondRadius,
            float bracketHalf, float bracketLength, float bracketThickness, int segments)
        {
            Mesh ring = GenerateRingMesh(ringRadius, ringThickness, segments);

            Vector3[] ringVertices = ring.vertices;
            int[] ringTriangles = ring.triangles;

            var vertices = new Vector3[ringVertices.Length + 4 + CornerBracketVertexCount];
            var triangles = new int[ringTriangles.Length + 6 + CornerBracketIndexCount];

            ringVertices.CopyTo(vertices, 0);
            ringTriangles.CopyTo(triangles, 0);

            int baseIndex = ringVertices.Length;
            vertices[baseIndex] = new Vector3(0f, 0f, diamondRadius);      // north
            vertices[baseIndex + 1] = new Vector3(diamondRadius, 0f, 0f);   // east
            vertices[baseIndex + 2] = new Vector3(0f, 0f, -diamondRadius);  // south
            vertices[baseIndex + 3] = new Vector3(-diamondRadius, 0f, 0f);  // west

            int triangleIndex = ringTriangles.Length;
            triangles[triangleIndex] = baseIndex;
            triangles[triangleIndex + 1] = baseIndex + 1;
            triangles[triangleIndex + 2] = baseIndex + 2;
            triangles[triangleIndex + 3] = baseIndex;
            triangles[triangleIndex + 4] = baseIndex + 2;
            triangles[triangleIndex + 5] = baseIndex + 3;

            // The diamond sits dead centre, which is exactly where the victim piece stands — so it
            // is the first thing lost to occlusion and cannot be what carries the state. The corner
            // brackets are what actually make this readable on a real board.
            int vertexCount = baseIndex + 4;
            int triangleCount = triangleIndex + 6;
            AppendCornerBrackets(vertices, triangles, ref vertexCount, ref triangleCount,
                bracketHalf, bracketLength, bracketThickness);

            var mesh = new Mesh { name = "BetrayalMarkerMesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Builds the Betrayer-at-large marker: the same hazard ring family as the Betrayal target,
        /// with four chevrons pointing inward from its edge instead of a diamond floating inside it.
        /// Marks the square the piece stands on rather than a square it threatens, so it earns a
        /// silhouette of its own even though it shares the target's colour and ring.
        /// </summary>
        private static Mesh GenerateBetrayerMarkerMesh(float ringRadius, float ringThickness, float chevronLength, float chevronHalfWidth, int segments)
        {
            Mesh ring = GenerateRingMesh(ringRadius, ringThickness, segments);

            Vector3[] ringVertices = ring.vertices;
            int[] ringTriangles = ring.triangles;

            var vertices = new Vector3[ringVertices.Length + 12];
            var triangles = new int[ringTriangles.Length + 12];

            ringVertices.CopyTo(vertices, 0);
            ringTriangles.CopyTo(triangles, 0);

            int vertexCount = ringVertices.Length;
            int triangleCount = ringTriangles.Length;

            AddChevron(vertices, triangles, ref vertexCount, ref triangleCount, 0f, ringRadius, chevronLength, chevronHalfWidth);
            AddChevron(vertices, triangles, ref vertexCount, ref triangleCount, 90f, ringRadius, chevronLength, chevronHalfWidth);
            AddChevron(vertices, triangles, ref vertexCount, ref triangleCount, 180f, ringRadius, chevronLength, chevronHalfWidth);
            AddChevron(vertices, triangles, ref vertexCount, ref triangleCount, 270f, ringRadius, chevronLength, chevronHalfWidth);

            var mesh = new Mesh { name = "BetrayerMarkerMesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Appends one triangular chevron: a base straddling the ring's outer edge at the given
        /// angle, tapering to a point that reaches inward — toward the square's centre, not away
        /// from it, which is what reads as a hazard closing in rather than a badge sitting on top.
        /// </summary>
        private static void AddChevron(Vector3[] vertices, int[] triangles, ref int vertexCount, ref int triangleCount,
            float angleDegrees, float baseRadius, float length, float halfWidth)
        {
            float angle = angleDegrees * Mathf.Deg2Rad;
            Vector3 outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 tangent = new Vector3(outward.z, 0f, -outward.x);

            Vector3 baseCentre = outward * baseRadius;
            Vector3 apex = outward * (baseRadius - length);

            int start = vertexCount;
            vertices[vertexCount++] = baseCentre + tangent * halfWidth;
            vertices[vertexCount++] = baseCentre - tangent * halfWidth;
            vertices[vertexCount++] = apex;

            triangles[triangleCount++] = start;
            triangles[triangleCount++] = start + 1;
            triangles[triangleCount++] = start + 2;
        }

        /// <summary>
        /// Builds one L-shaped corner tick — the marker for the square whose piece is currently
        /// picked up, one instance per corner rather than a full outline so it reads as "this one is
        /// in your hand" without competing with the move markers it points at.
        ///
        /// Built local to the corner itself: the point where the two bars meet sits at local origin,
        /// and both bars run inward from there. The four corners are four separate instances of this
        /// mesh on their own Transforms rather than one shared mesh, because each one clamps in from
        /// outside the square toward its own corner independently — a single Transform's uniform
        /// scale can only make every point converge on one shared centre, which is what made the old
        /// combined version read as a pop rather than a grip.
        /// </summary>
        private static Mesh GenerateSingleCornerTickMesh(float signX, float signZ, float tickLength, float tickThickness, float half)
        {
            float length = Mathf.Min(tickLength, half);

            var vertices = new Vector3[8];
            var triangles = new int[12];
            int vertexCount = 0;
            int triangleCount = 0;

            // Bar running along the X edge.
            AddBar(vertices, triangles, ref vertexCount, ref triangleCount,
                0f, 0f, -signX * length, -signZ * tickThickness);

            // Bar running along the Z edge.
            AddBar(vertices, triangles, ref vertexCount, ref triangleCount,
                0f, -signZ * tickThickness, -signX * tickThickness, -signZ * length);

            var mesh = new Mesh { name = "SelectionCornerTickMesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Appends one axis-aligned flat rectangle spanning the two opposite corners given, winding
        /// it so it faces up whichever way round those corners were supplied.
        /// </summary>
        private static void AddBar(Vector3[] vertices, int[] triangles, ref int vertexCount, ref int triangleCount,
            float x0, float z0, float x1, float z1)
        {
            float minX = Mathf.Min(x0, x1);
            float maxX = Mathf.Max(x0, x1);
            float minZ = Mathf.Min(z0, z1);
            float maxZ = Mathf.Max(z0, z1);

            int start = vertexCount;
            vertices[vertexCount++] = new Vector3(minX, 0f, minZ);
            vertices[vertexCount++] = new Vector3(minX, 0f, maxZ);
            vertices[vertexCount++] = new Vector3(maxX, 0f, minZ);
            vertices[vertexCount++] = new Vector3(maxX, 0f, maxZ);

            triangles[triangleCount++] = start;
            triangles[triangleCount++] = start + 1;
            triangles[triangleCount++] = start + 2;
            triangles[triangleCount++] = start + 1;
            triangles[triangleCount++] = start + 3;
            triangles[triangleCount++] = start + 2;
        }

        /// <summary>
        /// Builds a flat triangle-fan circle mesh, used for the quiet-move dot.
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
        /// Spawns visual piece GameObjects for every piece on the board. With playSetupWave the
        /// pieces dissolve in with a back-rank-first stagger (see SetupRowStagger); without it they
        /// appear immediately, which is what a mid-match rebuild wants — there is no opening to
        /// announce, and replaying the reveal would read as the game restarting.
        /// </summary>
        private void SpawnAllPieces(BoardState board, bool playSetupWave)
        {
            for (int x = 0; x < board.TileCountX; x++)
            {
                for (int y = 0; y < board.TileCountY; y++)
                {
                    PieceData data = board.GetPiece(x, y);
                    if (!data.IsEmpty)
                    {
                        // Negative means "no dissolve" — see SpawnSinglePiece.
                        float delay = -1f;
                        if (playSetupWave)
                        {
                            int rankDistance = data.Team == Team.White ? y : (board.TileCountY - 1 - y);
                            delay = SetupInitialDelay + rankDistance * SetupRowStagger;
                        }

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

            GameObject go = Instantiate(prefabs[index], PieceWorldPosition(pos), Quaternion.identity, parent);
            go.transform.localScale = PieceRestScale;

            ApplyRestingRotation(go.transform, data.Team, data.Type, data.MoveDirection);

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
                if (_destroyQueue[i] != null)
                {
                    _destroyQueue[i].StopAllAnimations();
                    Destroy(_destroyQueue[i].gameObject);
                }
            }

            foreach (ChessPiece dead in _graveyard.All)
            {
                if (dead != null)
                {
                    dead.StopAllAnimations();
                    Destroy(dead.gameObject);
                }
            }

            // Clear collections
            _piecesByPosition.Clear();
            _graveyard.Clear();
            _destroyQueue.Clear();

            // Clear highlights
            _highlights.Clear();
            RedrawHighlights();
        }

        /// <summary>
        /// The highlight materials are built at runtime from the palette, so they belong to this
        /// component rather than to the project, and nothing else will collect them.
        /// </summary>
        private void OnDestroy()
        {
            DestroyHighlightMaterials();
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

            // A Defection ply never reaches here — MatchDriver resolves it through
            // HandleBetrayalPhaseChanged instead, never raising MoveExecutedPayload for that
            // stage — so every move seen by this method has a genuine "from" and "to".
            SetLastMove(move.StartPosition, move.EndPosition);

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

            // 4. Check warning: payload.IsCheck reports whether the side about to move NEXT is now
            // in check — see MatchDriver.CheckForGameEnd. Frame their king in red and give it a
            // startle shake; clear any stale highlight otherwise (check can resolve, or the
            // highlighted king can change between moves, e.g. a discovered check on a different
            // turn). Deferred to the end of AnimateMove so it never races the king's own move/
            // castle animation still being set up above.
            //
            // Read from _sharedBoardState.Value.CurrentTurn rather than flipping move.PieceTeam —
            // the same key the domain itself uses to decide who owes the next move, so this can
            // never drift from what MatchDriver actually meant by "the side about to move".
            if (payload.IsCheck)
            {
                ShowKingInCheck(_sharedBoardState.Value.CurrentTurn);
            }
            else
            {
                ClearCheckHighlight();
            }
        }

        /// <summary>
        /// Plays one ply of a takeback backwards: the piece returns to the square it came from, a
        /// captured piece comes back to the square it was taken on, and a piece that was replaced
        /// mid-move (a promoted pawn, a defector) becomes what it was again.
        ///
        /// The order below is the forward order in reverse, and it has to be. A piece captured by
        /// being landed on shares its square with the attacker, so the attacker has to vacate before
        /// the victim can be put back — do it the other way round and the square holds one piece
        /// while the board thinks it holds another.
        ///
        /// Plies arrive one at a time and newest first (see UndoPlaybackSequencer), so each of these
        /// runs against a board that already reflects every ply after it.
        /// </summary>
        public void AnimateMoveUndone(ChessTheBetrayal.Events.Payloads.MoveUndonePayload payload)
        {
            MoveCommand move = payload.Move;

            if (move.PieceType == ChessPieceType.None) return;

            // Whatever was highlighted described the position being left behind.
            ClearLegalMoveHighlights();
            ClearCheckHighlight();
            ClearLastMove();
            ClearBetrayerAtLarge();

            // Castling is two pieces; the rook goes home alongside the king, trailing it by the same
            // beat it followed on the way out.
            if (move.IsCastling && move.RookStartPosition.HasValue && move.RookEndPosition.HasValue)
            {
                if (_piecesByPosition.TryGetValue(move.RookEndPosition.Value, out ChessPiece rook))
                {
                    _piecesByPosition.Remove(move.RookEndPosition.Value);
                    _piecesByPosition[move.RookStartPosition.Value] = rook;
                    rook.PlayCastleMove(PieceWorldPosition(move.RookStartPosition.Value), PrimeTweenPieceAnimator.CastleRookStartDelay);
                }
            }

            if (move.Stage == BetrayalStage.Defection)
            {
                RestoreDefectedPiece(move);
            }
            else if (move.IsPromotion)
            {
                RestorePromotedPawn(move);
            }
            else if (_piecesByPosition.TryGetValue(move.EndPosition, out ChessPiece movingPiece))
            {
                _piecesByPosition.Remove(move.EndPosition);
                _piecesByPosition[move.StartPosition] = movingPiece;

                // An Act being taken back takes its Betrayer glow with it — there is no longer a
                // betrayal pending for it to be marking.
                movingPiece.SetBetrayerGlow(false);

                movingPiece.SetPosition(PieceWorldPosition(move.StartPosition), ReverseMoveStyle(move));
            }

            if (move.IsCapture)
            {
                RestoreCapturedPiece(move);
            }

            // Only the ply that ends the takeback describes a position anyone arrives at.
            if (payload.IsFinalPly && payload.LandsInCheck && _sharedBoardState?.Value != null)
            {
                ShowKingInCheck(_sharedBoardState.Value.CurrentTurn);
            }
        }

        /// <summary>
        /// How a piece travels back. The same feel it went out with, except that a capture returns
        /// as a slide rather than the leaping stomp it arrived as — there is nothing underneath it
        /// to land on any more, and stomping an empty square reads as a mistake.
        /// </summary>
        private static MoveStyle ReverseMoveStyle(MoveCommand move)
        {
            if (move.PieceType == ChessPieceType.Knight) return MoveStyle.Knight;
            return move.IsCapture ? MoveStyle.Capture : MoveStyle.Quiet;
        }

        /// <summary>
        /// Puts a captured piece back on the square it was taken on. The piece itself is still
        /// around — a capture sends it to the death pile rather than destroying it — so this is the
        /// same GameObject returning, not a replacement.
        ///
        /// It comes off the end of its side's pile, which is where the most recently captured piece
        /// always is (see GraveyardStack). That holds because a takeback runs backwards through
        /// history, so the capture being undone is always the newest one.
        /// </summary>
        private void RestoreCapturedPiece(MoveCommand move)
        {
            Vector2Int capturePos = move.IsEnPassant && move.EnPassantCapturePosition.HasValue
                ? move.EnPassantCapturePosition.Value
                : move.EndPosition;

            PieceData captured = move.CapturedPieceFullState;

            if (!_graveyard.TryPopLast(captured.Team, out ChessPiece victim) || victim == null)
            {
                // The pile has nothing to give back — a rebuild since the capture would empty it.
                // The move still records exactly what was taken, so put that back instead of
                // leaving a square the board believes is occupied looking empty.
                SpawnSinglePiece(captured, capturePos);
                return;
            }

            // Turned to face its own side's direction before it sets off, so it travels home facing
            // the way it will stand rather than spinning round on arrival.
            ApplyRestingRotation(victim.transform, captured.Team, captured.Type, captured.MoveDirection);

            // Claim the square immediately, not on arrival: the board already believes the piece is
            // there, and anything that looks in between (the next ply of the same takeback, a rebuild)
            // must find it rather than an empty square with a piece in flight toward it.
            _piecesByPosition[capturePos] = victim;

            victim.PlayGraveyardReturn(PieceWorldPosition(capturePos), PieceRestScale, onArrived: () =>
            {
                // The piece can be destroyed mid-flight — a new match, or a rebuild.
                if (victim == null) return;
                victim.EnableCollider();
            });
        }

        /// <summary>
        /// Turns a promoted piece back into the pawn that earned it, standing on the square the pawn
        /// pushed from. Promotion swapped one GameObject for another on the way out, so undoing it
        /// swaps back rather than moving anything.
        /// </summary>
        private void RestorePromotedPawn(MoveCommand move)
        {
            // PieceType is the piece that MOVED — the pawn — while PromotedTo is what it became.
            PieceData pawn = new PieceData(move.PieceTeam, move.PieceType, move.PieceMoveDirection, startRow: 0, hasMoved: move.PieceHadMoved);
            Vector2Int promotionSquare = move.EndPosition;
            Vector2Int pawnSquare = move.StartPosition;

            // Keyed straight onto the square the pawn came from, even though it reforms on the
            // promotion square and walks back afterwards. A promotion that also captured puts its
            // victim back on the promotion square moments later, so leaving the pawn recorded there
            // in the meantime would have the two fighting over it.
            PlaySwapBack(promotionSquare, PieceTransitionStyle.PromotionMorph, pawn, pawnSquare, promotionSquare,
                onRevealed: pawnAgain =>
                {
                    // Promotion undone first, then the move that earned it — in that order, so the
                    // piece is visibly a pawn again before it steps back off the last rank.
                    pawnAgain.SetPosition(PieceWorldPosition(pawnSquare), MoveStyle.Promotion);
                });
        }

        /// <summary>
        /// Returns a defector to the side it betrayed. A Defection neither moves nor captures — it
        /// changes whose piece it is, on the spot — so its start and end squares are the same one,
        /// and the move's own snapshot still describes the piece as it was before it turned.
        /// </summary>
        private void RestoreDefectedPiece(MoveCommand move)
        {
            PieceData loyalAgain = new PieceData(move.PieceTeam, move.PieceType, move.PieceMoveDirection, startRow: 0, hasMoved: true);

            // The same spin the defection turned on, turning back — the piece rotates away as one
            // side's and comes back round as the other's, which is how it changed sides to begin with.
            PlaySwapBack(move.StartPosition, PieceTransitionStyle.Spin, loyalAgain, move.StartPosition, move.StartPosition, onRevealed: null);
        }

        /// <summary>
        /// Replaces the piece standing on <paramref name="outgoingSquare"/> with a different one,
        /// playing the swap out and back in the given style — the same vanish-then-reveal promotion
        /// and defection use going forward, so undoing one looks like the thing it undoes.
        ///
        /// The board's own record is updated up front rather than when the swap completes: the
        /// square is already claimed for the incoming piece as far as everything else is concerned,
        /// and the transition can outlive the ply that started it.
        /// </summary>
        private void PlaySwapBack(Vector2Int outgoingSquare, PieceTransitionStyle style, PieceData incoming,
            Vector2Int incomingSquare, Vector2Int revealAtSquare, System.Action<ChessPiece> onRevealed)
        {
            _piecesByPosition.TryGetValue(outgoingSquare, out ChessPiece outgoing);
            _piecesByPosition.Remove(outgoingSquare);

            if (outgoing != null)
            {
                // A piece about to stop existing can never deliver the callback any queued Betrayal
                // follow-up is waiting on, so drop that work rather than leave it stranded.
                _pendingStampVictimByAttacker.Remove(outgoing);
                _pendingDefectionByAttacker.Remove(outgoing);
            }

            ChessPiece revealed = SpawnSinglePiece(incoming, incomingSquare);
            if (revealed == null) return;

            // Fully dissolved rather than shrunk, because a piece scaled to nothing is refused by
            // the animator's own guard. It stays invisible until the outgoing piece has finished
            // leaving — the two must never be on the square together, or the swap reads as a
            // duplicate rather than one becoming the other.
            revealed.SetDissolveImmediate(1f);
            revealed.SetPosition(PieceWorldPosition(revealAtSquare), force: true);

            if (outgoing == null)
            {
                RevealSwappedPiece(revealed, style, onRevealed);
                return;
            }

            outgoing.PlayTransitionOut(style, () =>
            {
                // Either piece can be gone by the time this lands — a new match, a rebuild.
                if (outgoing != null)
                {
                    outgoing.StopAllAnimations();
                    Destroy(outgoing.gameObject);
                }

                if (revealed == null) return;
                RevealSwappedPiece(revealed, style, onRevealed);
            });
        }

        private void RevealSwappedPiece(ChessPiece revealed, PieceTransitionStyle style, System.Action<ChessPiece> onRevealed)
        {
            revealed.SetDissolveImmediate(0f);
            revealed.PlayTransitionIn(style);
            onRevealed?.Invoke(revealed);
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

            GraveyardSlot slot = ReserveGraveyardSlot(victim);

            victim.PlayEnPassantDeath(slot.Position, onArrived: () =>
            {
                if (victim == null) return;
                victim.SetScale(Vector3.one * deathSize, force: true);
                victim.FaceDirection(slot.LookDirection);
            });
        }

        /// <summary>
        /// Claims the next open slot in the victim's own side's death pile and returns where it
        /// stands. Pure bookkeeping — no visual mutation — so callers can find out where a piece is
        /// headed before it starts animating toward the graveyard (PlayEnPassantDeath glides there)
        /// as well as after (SendToGraveyard teleports there once a stomp has already finished on
        /// the board). The slot is reserved the instant this is called, so nothing else can be sent
        /// to the same place while a victim is still on its way.
        /// </summary>
        private GraveyardSlot ReserveGraveyardSlot(ChessPiece victim) => _graveyard.Push(victim.team, victim);

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

            GraveyardSlot slot = ReserveGraveyardSlot(victim);

            // The stomp already shrank the victim to VanishedScale on the board; restore its
            // death-pile size here so it reads as a normal (if small) captured piece in the
            // graveyard rather than staying pinned at the stamp's near-zero scale.
            victim.SetScale(Vector3.one * deathSize, force: true);
            victim.SetPosition(slot.Position, force: true);
            victim.FaceDirection(slot.LookDirection);
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

            // The square a piece was lifted from is also where the move markers ripple out from, so
            // it has to be recorded whether or not a piece was actually found to animate.
            _highlights.Selected = gridPos;
            RedrawHighlights();
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

            // Only forget the selection if this is the square that was actually selected — a stale
            // deselect for a square the player has already moved on from must not clear the new one.
            if (_highlights.Selected == gridPos)
            {
                _highlights.Selected = Vector2Int.Invalid;
                RedrawHighlights();
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
        /// Reacts to Betrayal phase transitions: glows the Betrayer and marks its square while
        /// Retribution is genuinely pending, and swaps its prefab to the opposing team the moment
        /// Defection occurs — deferred until any in-flight capture stamp on that piece finishes
        /// first (see SwapPieceTeam).
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

                    // Same reasoning applies to the square marker: it stays off entirely when
                    // Defection is already locked in, rather than appearing for one frame with
                    // nothing for the player to react to. Persists through RetributionPending on
                    // its own — nothing else needs to touch it while that phase is active.
                    if (!payload.WillDefect)
                    {
                        SetBetrayerAtLarge(payload.BetrayerPosition);
                    }
                    break;

                case ChessTheBetrayal.Events.Payloads.BetrayalPhase.RetributionPending:
                    // The Betrayer's glow (when it happens at all) is applied by AnimateMove
                    // instead: this phase is raised by MatchDriver before the corresponding
                    // MoveExecutedPayload, so the piece is still keyed at its start square in
                    // _piecesByPosition at this point.
                    break;

                case ChessTheBetrayal.Events.Payloads.BetrayalPhase.Resolved:
                    // Betrayer was already removed by AnimateMove's normal capture path
                    // (Retribution stage raises a real MoveExecutedPayload) — nothing to do beyond
                    // taking its square marker down.
                    ClearBetrayerAtLarge();
                    break;

                case ChessTheBetrayal.Events.Payloads.BetrayalPhase.DefectionOccurred:
                    ClearBetrayerAtLarge();
                    SwapPieceTeam(payload.BetrayerPosition);
                    break;

                case ChessTheBetrayal.Events.Payloads.BetrayalPhase.ForcedSaveActive:
                    // The glow-off concern this case used to guard against no longer applies here
                    // (SwapPieceTeam's fresh-spawn piece never carries the Betrayer glow), but the
                    // race it describes — this phase firing synchronously back-to-back with
                    // DefectionOccurred, before SwapPieceTeam's transition-out callback has run —
                    // still applies to the king lookups below, which is why they tolerate a miss.
                    ThreatPulseOwnKing(payload.InitiatingTeam);

                    // A Defection that forces a Save never raises MoveExecutedPayload — MatchDriver
                    // resolves straight into this phase instead of the normal move pipeline — so the
                    // usual check-frame logic in AnimateMove never runs for it. Without this, the
                    // startle flash above was the only warning a self-inflicted check ever got: no
                    // persistent frame, nothing left once the flash finished. Framed on CurrentTurn,
                    // the same key MatchDriver uses to decide who owes the forthcoming save.
                    ShowKingInCheck(_sharedBoardState.Value.CurrentTurn);
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

        /// <summary>Where a piece standing on the given square sits — the tile's centre, lifted clear of the board surface.</summary>
        private Vector3 PieceWorldPosition(Vector2Int gridPos)
        {
            Vector3 worldPos = GetTileCenter(gridPos.x, gridPos.y);
            worldPos.y += pieceYOffset;
            return worldPos;
        }

        /// <summary>The size a piece stands at while it is on the board, as opposed to the smaller death-pile size.</summary>
        private Vector3 PieceRestScale => Vector3.one * Mathf.Max(0.0001f, pieceScaleMultiplier);

        /// <summary>
        /// Turns a piece to the direction its side faces, including any per-mesh correction for a
        /// model that wasn't authored facing the same way as the rest of its set (see
        /// whiteMeshFacingCorrectionDegrees). Baked into one rotation so everything downstream —
        /// the defection spin, castling, the check shake — sees a single correct resting rotation
        /// and never has to know a correction was needed.
        /// </summary>
        private void ApplyRestingRotation(Transform pieceTransform, Team team, ChessPieceType type, int moveDirection)
        {
            float facingCorrection = GetMeshFacingCorrectionDegrees(team, (int)type - 1);
            pieceTransform.rotation = moveDirection == -1
                ? Quaternion.Euler(0f, 180f + facingCorrection, 0f)
                : Quaternion.Euler(0f, facingCorrection, 0f);
        }

        #endregion

        #region Highlighting

        /// <summary>
        /// Moves the pointer's tint to a new square.
        /// </summary>
        public void UpdateHoverHighlight(Vector2Int idx)
        {
            if (_highlights.Hover == idx) return;

            _highlights.Hover = idx;
            RedrawHighlights();
        }

        /// <summary>
        /// Takes the pointer tint off the board.
        /// </summary>
        public void ClearHoverHighlight()
        {
            UpdateHoverHighlight(Vector2Int.Invalid);
        }

        /// <summary>
        /// Marks every square the picked-up piece may move to, sorting each destination into the kind
        /// of thing it would do.
        /// </summary>
        public void HighlightLegalMoves(IReadOnlyList<MoveCommand> moves)
        {
            _highlights.ClearDestinations();

            for (int i = 0; i < moves.Count; i++)
            {
                MoveCommand move = moves[i];

                // Asking the move whether it captures, rather than whether the destination square
                // holds a piece, is what makes en passant mark correctly — its destination is empty,
                // because the pawn being taken stands on a different square.
                _highlights.AddDestination(
                    move.EndPosition,
                    isCapture: move.IsCapture,
                    isBetrayal: move.Stage == BetrayalStage.Act);
            }

            RedrawHighlights();
        }

        /// <summary>
        /// Forgets the destinations shown for the piece that was picked up. Selection, check and the
        /// last move played all survive this — putting a piece back down changes none of them.
        /// </summary>
        public void ClearLegalMoveHighlights()
        {
            _highlights.ClearDestinations();
            RedrawHighlights();
        }

        /// <summary>
        /// Frames the king standing at kingPos as being in check, clearing any previous frame — a
        /// check can move between squares from one turn to the next, when a king steps out of one
        /// and a discovered check lands on the other side's king instead.
        /// </summary>
        public void SetKingInCheckHighlight(Vector2Int kingPos)
        {
            _highlights.CheckSquare = kingPos;
            RedrawHighlights();
        }

        /// <summary>
        /// Takes the check frame off the board. Safe to call whether or not one is showing.
        /// </summary>
        public void ClearCheckHighlight()
        {
            if (_highlights.CheckSquare == Vector2Int.Invalid) return;

            _highlights.CheckSquare = Vector2Int.Invalid;
            RedrawHighlights();
        }

        /// <summary>
        /// Marks the square a Betrayer is currently standing on while Retribution is still pending.
        /// </summary>
        private void SetBetrayerAtLarge(Vector2Int square)
        {
            _highlights.BetrayerSquare = square;
            RedrawHighlights();
        }

        /// <summary>
        /// Takes the Betrayer hazard marker off the board. Safe to call whether or not one is showing.
        /// </summary>
        private void ClearBetrayerAtLarge()
        {
            if (_highlights.BetrayerSquare == Vector2Int.Invalid) return;

            _highlights.BetrayerSquare = Vector2Int.Invalid;
            RedrawHighlights();
        }

        /// <summary>
        /// Records the move just played, so both its squares stay faintly tinted. This is the cheapest
        /// clarity win on the board: it answers "what just happened?" for a player who looked away
        /// during the animation, or who is picking the game back up after a pause.
        /// </summary>
        private void SetLastMove(Vector2Int from, Vector2Int to)
        {
            _highlights.LastMoveFrom = from;
            _highlights.LastMoveTo = to;
            RedrawHighlights();
        }

        /// <summary>
        /// Drops the last-move tint. Used when a takeback rewinds a ply: the move before it is the
        /// one that would now be "last", and the view has no record of what that was, so showing
        /// nothing is honest where showing the retracted move would be a lie.
        /// </summary>
        private void ClearLastMove()
        {
            _highlights.LastMoveFrom = Vector2Int.Invalid;
            _highlights.LastMoveTo = Vector2Int.Invalid;
            RedrawHighlights();
        }

        /// <summary>
        /// Brings every square in line with what the highlight map now says. The only method here
        /// that touches a renderer — everything else states a fact and calls this.
        ///
        /// Squares drawn last time are cleared first, so a square that has just stopped being
        /// highlighted goes dark without walking the whole board.
        /// </summary>
        private void RedrawHighlights()
        {
            if (_tiles == null || _markerRenderers == null) return;

            _highlights.CollectActiveSquares(_squaresToDraw);

            for (int i = 0; i < _drawnSquares.Count; i++)
            {
                if (!_squaresToDraw.Contains(_drawnSquares[i]))
                {
                    ApplyHighlight(_drawnSquares[i], default);
                }
            }

            for (int i = 0; i < _squaresToDraw.Count; i++)
            {
                Vector2Int square = _squaresToDraw[i];
                ApplyHighlight(square, _highlights.Resolve(square));
            }

            _drawnSquares.Clear();
            _drawnSquares.AddRange(_squaresToDraw);
        }

        /// <summary>
        /// Shows one square's tint and marker, animating a marker in only when it wasn't already
        /// there — a redraw triggered by the pointer moving somewhere else must not make every
        /// move dot on the board pop again.
        /// </summary>
        private void ApplyHighlight(Vector2Int square, SquareHighlight highlight)
        {
            if (!IsOnBoard(square)) return;

            MeshRenderer tint = _tintRenderers[square.x, square.y];
            MeshFilter tintFilter = _tintFilters[square.x, square.y];
            if (tint != null && tintFilter != null)
            {
                bool showTint = highlight.Tint != SquareTint.None && _tintMaterials != null;
                tint.enabled = showTint;
                if (showTint)
                {
                    tintFilter.sharedMesh = TintMeshFor(highlight.Tint);
                    tint.sharedMaterial = _tintMaterials[(int)highlight.Tint];
                }
            }

            MeshRenderer shadow = _shadowRenderers[square.x, square.y];
            if (shadow != null)
            {
                shadow.enabled = highlight.Marker == SquareMarker.Capture && _captureShadowMaterial != null;
            }

            MeshRenderer marker = _markerRenderers[square.x, square.y];
            MeshFilter filter = _markerFilters[square.x, square.y];
            if (marker == null || filter == null) return;

            SquareMarker previous = _shownMarkers[square.x, square.y];
            _shownMarkers[square.x, square.y] = highlight.Marker;

            // Selected never uses this generic slot — its four corner ticks travel toward their own
            // corners independently, which needs four Transforms rather than the one this slot has.
            if (highlight.Marker == SquareMarker.Selected)
            {
                marker.enabled = false;
                ShowSelectionCornerTicks(square, isNewSelection: previous != SquareMarker.Selected);
                return;
            }

            if (previous == SquareMarker.Selected)
            {
                HideSelectionCornerTicks();
            }

            // The Betrayer's brackets ride alongside its marker rather than inside it, so that the
            // marker can keep rotating while they hold their corners.
            if (highlight.Marker == SquareMarker.BetrayerAtLarge)
            {
                ShowBetrayerBrackets(square);
            }
            else if (previous == SquareMarker.BetrayerAtLarge)
            {
                HideBetrayerBrackets();
            }

            if (highlight.Marker == SquareMarker.None || _markerMaterials == null)
            {
                marker.enabled = false;
                return;
            }

            filter.sharedMesh = MeshFor(highlight.Marker);
            marker.sharedMaterial = _markerMaterials[(int)highlight.Marker];
            marker.enabled = true;

            // Every marker except the Betrayer hazard stays axis-aligned. That one drives its own
            // rotation continuously in Update() while shown, so it must not be reset out from under
            // it here — only snapped back once a square stops showing it.
            if (highlight.Marker != SquareMarker.BetrayerAtLarge)
            {
                marker.transform.localRotation = Quaternion.identity;
            }

            if (previous != highlight.Marker)
            {
                PlayMarkerAppear(marker.transform, square);
            }
        }

        /// <summary>
        /// Moves the selection corner ticks onto the given square, playing the clamp-in only when
        /// this square wasn't already showing them — a redraw triggered by something else on the
        /// board must not make an already-picked-up piece's ticks clamp in a second time.
        /// </summary>
        private void ShowSelectionCornerTicks(Vector2Int square, bool isNewSelection)
        {
            if (_selectionTicksRoot == null || !IsOnBoard(square)) return;

            Transform tile = _tiles[square.x, square.y].transform;
            if (_selectionTicksRoot.parent != tile)
            {
                _selectionTicksRoot.SetParent(tile, false);
                _selectionTicksRoot.localPosition = new Vector3(0f, markerYOffset, 0f);
            }

            _selectionTicksRoot.gameObject.SetActive(true);

            if (isNewSelection)
            {
                PlaySelectionClampIn();
            }
        }

        /// <summary>
        /// Takes the selection corner ticks off the board. Safe to call whether or not they're
        /// showing.
        /// </summary>
        private void HideSelectionCornerTicks()
        {
            if (_selectionTicksRoot == null) return;

            _selectionTicksRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Moves the Betrayer's corner brackets onto the given square.
        /// </summary>
        private void ShowBetrayerBrackets(Vector2Int square)
        {
            if (_betrayerBracketsRoot == null || !IsOnBoard(square)) return;

            Transform tile = _tiles[square.x, square.y].transform;
            if (_betrayerBracketsRoot.parent != tile)
            {
                _betrayerBracketsRoot.SetParent(tile, false);
                _betrayerBracketsRoot.localPosition = new Vector3(0f, markerYOffset, 0f);
            }

            _betrayerBracketsRoot.gameObject.SetActive(true);
        }

        /// <summary>
        /// Takes the Betrayer's corner brackets off the board. Safe to call whether or not they're
        /// showing.
        /// </summary>
        private void HideBetrayerBrackets()
        {
            if (_betrayerBracketsRoot == null) return;

            _betrayerBracketsRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Slides each of the four corner ticks in from outside the square to its resting corner,
        /// with a slight overshoot on arrival — a grip closing onto the square rather than a badge
        /// popping out of its centre, which is what a uniform scale-up of the whole group used to
        /// read as.
        /// </summary>
        private void PlaySelectionClampIn()
        {
            float half = tileSize * highlightPalette.TintSizeRatio * 0.5f;
            float duration = highlightPalette.CornerTickClampDuration;
            float startRatio = highlightPalette.CornerTickClampStartRatio;

            for (int i = 0; i < 4; i++)
            {
                Transform tick = _selectionTicksRoot.GetChild(i);
                Vector3 resting = new Vector3(SelectionTickSignX[i] * half, 0f, SelectionTickSignZ[i] * half);

                if (duration <= 0f)
                {
                    tick.localPosition = resting;
                    continue;
                }

                Vector3 start = resting * startRatio;
                Tween.LocalPosition(tick, start, resting, duration,
                    Easing.Overshoot(highlightPalette.CornerTickClampOvershoot), useUnscaledTime: true);
            }
        }

        /// <summary>
        /// Scales a marker up as it appears, delayed by how far its square is from the piece that was
        /// picked up, so the destinations ripple outward from the piece rather than all arriving at
        /// once. The overshoot on arrival is the same vocabulary the pieces themselves land with.
        /// </summary>
        private void PlayMarkerAppear(Transform markerTransform, Vector2Int square)
        {
            float duration = highlightPalette.AppearDuration;
            if (duration <= 0f)
            {
                markerTransform.localScale = Vector3.one;
                return;
            }

            float delay = 0f;
            Vector2Int origin = _highlights.Selected;
            if (origin != Vector2Int.Invalid && highlightPalette.AppearStaggerPerSquare > 0f)
            {
                int distance = Mathf.Max(Mathf.Abs(square.x - origin.x), Mathf.Abs(square.y - origin.y));
                delay = distance * highlightPalette.AppearStaggerPerSquare;
            }

            markerTransform.localScale = Vector3.zero;
            Tween.Scale(markerTransform, Vector3.one, duration, Easing.Overshoot(MarkerAppearOvershoot),
                startDelay: delay, useUnscaledTime: true);
        }

        /// <summary>
        /// Selected has no case here — it never reaches this generic marker slot. See
        /// ShowSelectionCornerTicks for why its four ticks are drawn a different way.
        /// </summary>
        private Mesh MeshFor(SquareMarker marker)
        {
            switch (marker)
            {
                case SquareMarker.QuietMove: return _dotMesh;
                case SquareMarker.Capture: return _captureReticleMesh;
                case SquareMarker.BetrayalTarget: return _betrayalMesh;
                case SquareMarker.BetrayerAtLarge: return _betrayerAtLargeMesh;
                case SquareMarker.Check: return _checkFrameMesh;
                default: return null;
            }
        }

        /// <summary>
        /// Every tint shares one filled quad except the last move's origin square, which draws a
        /// hollow outline instead — the destination gets the fill, the departure gets a trace of
        /// where the piece used to stand.
        /// </summary>
        private Mesh TintMeshFor(SquareTint tint)
        {
            switch (tint)
            {
                case SquareTint.LastMoveFrom: return _lastMoveFromOutlineMesh;
                case SquareTint.None: return null;
                default: return _tintMesh;
            }
        }

        private bool IsOnBoard(Vector2Int square) =>
            square.x >= 0 && square.x < _tileCountX && square.y >= 0 && square.y < _tileCountY;

        private void Update()
        {
            if (_markerMaterials == null) return;

            UpdateCheckPulse();
            UpdateCaptureBreathe();
            UpdateBetrayerHazard();
        }

        /// <summary>
        /// Breathes the check frame's glow in and out. A king in check is the one thing on the board
        /// that must not be possible to overlook, and motion catches the eye where a brighter static
        /// colour would just blend into the grade.
        /// </summary>
        private void UpdateCheckPulse()
        {
            Material checkMaterial = _markerMaterials[(int)SquareMarker.Check];
            if (checkMaterial == null) return;

            bool inCheck = _highlights.CheckSquare != Vector2Int.Invalid;
            if (!inCheck)
            {
                checkMaterial.SetFloat(GlowProperty, highlightPalette.Check.Glow);
                return;
            }

            float period = Mathf.Max(0.05f, highlightPalette.CheckPulsePeriod);
            // Unscaled time, because the warning has to keep pulsing while the game is paused.
            float phase = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / period) * 0.5f + 0.5f;
            checkMaterial.SetFloat(GlowProperty,
                highlightPalette.Check.Glow + phase * highlightPalette.CheckPulseGlowAmount);
        }

        /// <summary>
        /// Breathes the capture material's glow the same way the check frame does, only slower and
        /// gentler — a capture is an option on the table, not a warning.
        ///
        /// The marker deliberately does NOT rotate. Its corner brackets only work because they sit
        /// where a tall piece cannot hide them, and spinning the marker would sweep them straight
        /// back into the occluded zone.
        /// </summary>
        private void UpdateCaptureBreathe()
        {
            Material captureMaterial = _markerMaterials[(int)SquareMarker.Capture];
            if (captureMaterial == null) return;

            float period = Mathf.Max(0.05f, highlightPalette.CaptureBreathePeriod);
            float phase = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / period) * 0.5f + 0.5f;
            captureMaterial.SetFloat(GlowProperty,
                highlightPalette.Capture.Glow + phase * highlightPalette.CaptureBreatheGlowAmount);
        }

        /// <summary>
        /// Pulses the Betrayer hazard's glow and spins its four chevrons, the same techniques as the
        /// check pulse and the capture reticle's spin — but only ever one square at a time, since a
        /// second Betrayer can't be at large while the first is still unresolved, so there is no
        /// synchronisation to worry about.
        /// </summary>
        private void UpdateBetrayerHazard()
        {
            Material material = _markerMaterials[(int)SquareMarker.BetrayerAtLarge];
            Vector2Int square = _highlights.BetrayerSquare;
            bool active = square != Vector2Int.Invalid;

            if (material != null)
            {
                if (!active)
                {
                    material.SetFloat(GlowProperty, highlightPalette.BetrayerAtLarge.Glow);
                }
                else
                {
                    float period = Mathf.Max(0.05f, highlightPalette.BetrayerPulsePeriod);
                    float phase = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / period) * 0.5f + 0.5f;
                    material.SetFloat(GlowProperty,
                        highlightPalette.BetrayerAtLarge.Glow + phase * highlightPalette.BetrayerPulseGlowAmount);
                }
            }

            if (active && IsOnBoard(square))
            {
                float spin = Time.unscaledTime * highlightPalette.BetrayerChevronRotationSpeed;
                _markerRenderers[square.x, square.y].transform.localRotation = Quaternion.Euler(0f, spin, 0f);
            }
        }

        #endregion

        #region Initialization Helpers

        /// <summary>
        /// Resolves the Tile layer once, rather than looking the name up every time a square is built.
        /// Tiles and their overlays all sit on it, and the pointer's raycast mask is what needs them
        /// there — nothing about how a square looks depends on its layer any more.
        /// </summary>
        private void CacheLayers()
        {
            _tileLayer = LayerMask.NameToLayer("Tile");
            if (_tileLayer == -1) _tileLayer = 0;
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