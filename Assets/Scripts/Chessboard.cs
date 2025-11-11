using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChessTheMasterPiece.ChessPiece
{
    [DisallowMultipleComponent]
    public class Chessboard : MonoBehaviour
    {
        // constants
        private const int ExpectedPiecePrefabCount = 6; // Pawn..King
        private const int DefaultBoardSize = 8;
        private const float RaycastMaxDistance = 200f;

        [Header("Art / Board")]
        [SerializeField] private Material tileMaterial;
        [SerializeField, Range(0.1f, 4f)] private float tileSize = 1f;
        [SerializeField, Min(2)] private int tileCountX = DefaultBoardSize;
        [SerializeField, Min(2)] private int tileCountY = DefaultBoardSize;
        [SerializeField] private float tilesYOffset = 0.0f; // world Y for tile placement (tile top)
        [SerializeField] private Vector3 boardCenter = Vector3.zero;

        [Header("Piece Prefabs (order: Pawn,Rook,Knight,Bishop,Queen,King)")]
        [SerializeField] private GameObject[] whiteTeamPrefabs;
        [SerializeField] private GameObject[] blackTeamPrefabs;

        [Header("Piece visuals / placement")]
        [SerializeField] private float pieceYOffset = 0.05f;      // -> local vertical offset above tile
        [SerializeField] private float pieceScaleMultiplier = 1f;
        [SerializeField] private float dragHeight = 1f;          // -> how high piece floats while dragging
        [SerializeField] private float deathSize = 0.45f;        // -> scaled dead piece size
        [SerializeField] private float deathSpacing = 0.35f;     // -> spacing for placed captured pieces

        [Header("Parents (auto-created if null)")]
        [SerializeField] private Transform tilesParent;
        [SerializeField] private Transform whitePiecesParent;
        [SerializeField] private Transform blackPiecesParent;

        // runtime data
        private ChessPiece[,] board;                      // -> main board occupancy [x,y]
        private GameObject[,] tiles;                      // -> tile GameObjects
        private Dictionary<Transform, Vector2Int> tileLookup; // -> transform -> grid index
        private List<ChessPiece> whiteCaptured = new List<ChessPiece>();
        private List<ChessPiece> blackCaptured = new List<ChessPiece>();

        // input / camera
        private Camera mainCamera;
        private ChessPiece draggingPiece;
        private Vector2Int hoverIndex = new Vector2Int(-1, -1);

        // cached
        private Transform _transform;
        private Vector3 origin; // world position of board origin (0,0) tile corner

        // layers (optional)
        private int tileLayer = 0;
        private int highlightLayer = 0;
        private int combinedLayerMask = ~0;

        #region Unity lifecycle

        private void Awake()
        {
            _transform = transform;
            CacheLayers();
            CreateParentContainersIfNeeded();

            if (!ValidateConfig())
            {
                Debug.LogError("[Chessboard] Validation failed. Chessboard will not initialize.");
                enabled = false;
                return;
            }

            InitializeBoardData();
            CreateTiles();
            SpawnDefaultSetup();
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

            // Read pointer from new Input System if available, otherwise fallback
            Vector2 pointer = Vector2.zero;
            bool hasPointer = false;

            if (Mouse.current != null)
            {
                pointer = Mouse.current.position.ReadValue();
                hasPointer = true;
            }
            else
            {
                // fallback to legacy Input
#pragma warning disable 618
                if (Input.mousePresent)
                {
                    pointer = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                    hasPointer = true;
                }
#pragma warning restore 618
            }

            if (!hasPointer)
            {
                // no mouse, nothing to do
                return;
            }

            HandlePointer(pointer);
        }

        private void OnDestroy()
        {
            // cleanup generated meshes (to avoid leaking in editor play mode)
            if (tiles != null)
            {
                for (int x = 0; x < tiles.GetLength(0); x++)
                {
                    for (int y = 0; y < tiles.GetLength(1); y++)
                    {
                        if (tiles[x, y] == null) continue;
                        MeshFilter mf = tiles[x, y].GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            Destroy(mf.sharedMesh);
                        }
                    }
                }
            }
        }

        #endregion

        #region Initialization helpers

        private void CacheLayers()
        {
            int a = LayerMask.NameToLayer("Tile");
            int b = LayerMask.NameToLayer("Highlight");

            if (a == -1) Debug.LogWarning("[Chessboard] Layer 'Tile' not found. Using default layer 0.");
            if (b == -1) Debug.LogWarning("[Chessboard] Layer 'Highlight' not found. Using default layer 0.");

            tileLayer = Mathf.Max(0, a);
            highlightLayer = Mathf.Max(0, b);
            combinedLayerMask = (1 << tileLayer) | (1 << highlightLayer);
        }

        private void CreateParentContainersIfNeeded()
        {
            if (tilesParent == null)
            {
                var go = new GameObject("Tiles");
                go.transform.SetParent(_transform, false);
                tilesParent = go.transform;
            }

            if (whitePiecesParent == null)
            {
                var go = new GameObject("WhitePieces");
                go.transform.SetParent(_transform, false);
                whitePiecesParent = go.transform;
            }

            if (blackPiecesParent == null)
            {
                var go = new GameObject("BlackPieces");
                go.transform.SetParent(_transform, false);
                blackPiecesParent = go.transform;
            }
        }

        private bool ValidateConfig()
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
                Debug.LogError("[Chessboard] tile count must be >= 2.");
                return false;
            }

            if (whiteTeamPrefabs == null || whiteTeamPrefabs.Length < ExpectedPiecePrefabCount)
            {
                Debug.LogWarning($"[Chessboard] whiteTeamPrefabs should have {ExpectedPiecePrefabCount} prefabs.");
            }

            if (blackTeamPrefabs == null || blackTeamPrefabs.Length < ExpectedPiecePrefabCount)
            {
                Debug.LogWarning($"[Chessboard] blackTeamPrefabs should have {ExpectedPiecePrefabCount} prefabs.");
            }

            return true;
        }

        private void InitializeBoardData()
        {
            board = new ChessPiece[tileCountX, tileCountY];
            tiles = new GameObject[tileCountX, tileCountY];
            tileLookup = new Dictionary<Transform, Vector2Int>(tileCountX * tileCountY);

            // origin -> bottom-left corner of tile (0,0)
            float halfWidth = (tileCountX * tileSize) * 0.5f;
            float halfDepth = (tileCountY * tileSize) * 0.5f;
            origin = boardCenter - new Vector3(halfWidth, 0f, halfDepth);
        }

        #endregion

        #region Tile creation & utilities

        /// <summary>
        /// Create tile GameObjects with simple quad mesh and collider.
        /// Implementation:
        /// -> tile GameObject localPosition = origin + (x*tileSize, tilesYOffset, y*tileSize)
        /// -> mesh vertices in local space [0..tileSize] to keep colliders/simple math easy
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

        private GameObject CreateTile(int x, int y)
        {
            var tileGO = new GameObject($"Tile_{x}_{y}");
            tileGO.transform.SetParent(tilesParent, false);

            // place tile at correct world position
            Vector3 tileWorldPos = origin + new Vector3(x * tileSize + tileSize * 0.5f, tilesYOffset, y * tileSize + tileSize * 0.5f);
            tileGO.transform.position = tileWorldPos;

            // Mesh: simple quad in local space, origin at tile center
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

            // BoxCollider centered at tile center, thin height
            BoxCollider bc = tileGO.AddComponent<BoxCollider>();
            bc.size = new Vector3(tileSize, 0.05f, tileSize);
            bc.center = Vector3.zero;

            // set layer
            tileGO.layer = tileLayer;

            return tileGO;
        }

        /// <summary>
        /// Convert grid index to world-space center of tile.
        /// -> returns Vector3.zero with warning if invalid index.
        /// </summary>
        public Vector3 GetTileCenter(Vector2Int index)
        {
            if (!IsValidIndex(index))
            {
                Debug.LogWarning($"[Chessboard] GetTileCenter called with invalid index {index} -> returning Vector3.zero.");
                return Vector3.zero;
            }

            return origin + new Vector3(index.x * tileSize + tileSize * 0.5f, tilesYOffset, index.y * tileSize + tileSize * 0.5f);
        }

        public Vector3 GetTileCenter(int x, int y) => GetTileCenter(new Vector2Int(x, y));

        public bool IsValidIndex(Vector2Int idx) => idx.x >= 0 && idx.x < tileCountX && idx.y >= 0 && idx.y < tileCountY;

        #endregion

        #region Spawning / placement API

        /// <summary>
        /// Spawn standard chess starting setup.
        /// -> white at bottom (y=0, y=1), black at top (y=tileCountY-1, tileCountY-2)
        /// -> uses prefab arrays; missing prefabs are logged and skipped gracefully
        /// </summary>
        public void SpawnDefaultSetup()
        {
            // clear any existing pieces
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

            // White major row y=0
            for (int x = 0; x < columns; x++)
                SpawnPieceAt(majors[x], Team.White, new Vector2Int(x, 0), whiteTeamPrefabs, whitePiecesParent);

            // White pawns y=1
            int whitePawnRank = Mathf.Clamp(1, 0, tileCountY - 1);
            for (int x = 0; x < tileCountX; x++)
                SpawnPieceAt(ChessPieceType.Pawn, Team.White, new Vector2Int(x, whitePawnRank), whiteTeamPrefabs, whitePiecesParent);

            // Black pawns
            int blackPawnRank = Mathf.Clamp(tileCountY - 2, 0, tileCountY - 1);
            for (int x = 0; x < tileCountX; x++)
                SpawnPieceAt(ChessPieceType.Pawn, Team.Black, new Vector2Int(x, blackPawnRank), blackTeamPrefabs, blackPiecesParent);

            // Black majors top row
            int blackMajorRank = Mathf.Clamp(tileCountY - 1, 0, tileCountY - 1);
            for (int x = 0; x < columns; x++)
                SpawnPieceAt(majors[x], Team.Black, new Vector2Int(x, blackMajorRank), blackTeamPrefabs, blackPiecesParent);
        }

        /// <summary>
        /// Remove and destroy all spawned pieces on the board.
        /// -> used before respawn
        /// </summary>
        public void ClearBoardPieces()
        {
            if (board == null) InitializeBoardData();

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
        }

        private ChessPiece SpawnPieceAt(ChessPieceType type, Team team, Vector2Int index, GameObject[] prefabArray, Transform parent)
        {
            if (!IsValidIndex(index))
            {
                Debug.LogWarning($"[Chessboard] SpawnPieceAt invalid index {index} for {type}.");
                return null;
            }

            if (prefabArray == null || prefabArray.Length < ExpectedPiecePrefabCount)
            {
                Debug.LogWarning($"[Chessboard] SpawnPieceAt: prefabArray is missing or incomplete for {type}.");
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
                Debug.LogWarning($"[Chessboard] SpawnPieceAt: prefab for {type} is null. Skipping spawn.");
                return null;
            }

            Vector3 pos = GetTileCenter(index);
            pos.y = tilesYOffset + pieceYOffset;

            GameObject go = Instantiate(prefab, pos, Quaternion.identity, parent);
            go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, pieceScaleMultiplier);

            ChessPiece cp = go.GetComponent<ChessPiece>();
            if (cp == null)
            {
                Debug.LogWarning($"[Chessboard] Spawned prefab {go.name} missing ChessPiece component. Destroying instance.");
                Destroy(go);
                return null;
            }

            cp.type = type;
            cp.team = (int)team;
            cp.currentX = index.x;
            cp.currentY = index.y;

            if (team == Team.Black)
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            go.name = $"{team}_{type}_{index.x}_{index.y}";

            board[index.x, index.y] = cp;
            return cp;
        }

        #endregion

        #region Input / dragging / hover

        private void HandlePointer(Vector2 screenPos)
        {
            Ray ray = mainCamera.ScreenPointToRay(screenPos);

            // raycast against our tile mask
            if (Physics.Raycast(ray, out RaycastHit hit, RaycastMaxDistance, combinedLayerMask))
            {
                Vector2Int hitIndex = ResolveTileIndexFromHit(hit.transform);
                if (hitIndex.x == -1)
                {
                    // mesh hit but not a registered tile
                    return;
                }

                UpdateHover(hitIndex);

                // press -> start drag
                if (WasPointerPressed())
                {
                    TryStartDrag(hitIndex);
                }

                // release -> drop
                if (WasPointerReleased())
                {
                    TryDrop(hitIndex);
                }
            }
            else
            {
                // not hovering over board
                ClearHover();

                if (WasPointerReleased())
                {
                    // drop back if dragged off-board
                    if (draggingPiece != null)
                    {
                        ReturnPieceToTile(draggingPiece);
                        draggingPiece = null;
                    }
                }
            }

            // update dragging world position
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

        private Vector2Int ResolveTileIndexFromHit(Transform t)
        {
            // walk up the hierarchy to find a transform that is a key in tileLookup
            Transform cur = t;
            int safety = 0;
            while (cur != null && safety++ < 16)
            {
                if (tileLookup.TryGetValue(cur, out Vector2Int idx))
                    return idx;

                cur = cur.parent;
            }

            return new Vector2Int(-1, -1);
        }

        private void UpdateHover(Vector2Int idx)
        {
            if (hoverIndex == idx) return;

            if (IsValidIndex(hoverIndex))
                SetTileHighlight(hoverIndex, false);

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
            if (Mouse.current != null) return Mouse.current.leftButton.wasPressedThisFrame;
#pragma warning disable 618
            return Input.GetMouseButtonDown(0);
#pragma warning restore 618
        }

        private bool WasPointerReleased()
        {
            if (Mouse.current != null) return Mouse.current.leftButton.wasReleasedThisFrame;
#pragma warning disable 618
            return Input.GetMouseButtonUp(0);
#pragma warning restore 618
        }

        private void TryStartDrag(Vector2Int idx)
        {
            if (!IsValidIndex(idx)) return;
            ChessPiece cp = board[idx.x, idx.y];
            if (cp == null) return;

            draggingPiece = cp;
            // detach from array temporarily so MoveTo can overwrite target slot
            board[idx.x, idx.y] = null;
        }

        private void TryDrop(Vector2Int idx)
        {
            if (draggingPiece == null) return;

            Vector2Int from = new Vector2Int(draggingPiece.currentX, draggingPiece.currentY);
            Vector2Int to = idx;

            // if invalid drop index, return to original
            if (!IsValidIndex(to))
            {
                ReturnPieceToTile(draggingPiece);
                draggingPiece = null;
                return;
            }

            bool moved = MoveTo(draggingPiece, to.x, to.y);

            if (!moved)
            {
                // revert
                board[from.x, from.y] = draggingPiece;
                ReturnPieceToTile(draggingPiece);
            }

            draggingPiece = null;
        }

        private void ReturnPieceToTile(ChessPiece piece)
        {
            if (piece == null) return;
            Vector2Int idx = new Vector2Int(piece.currentX, piece.currentY);

            if (!IsValidIndex(idx))
            {
                // piece has invalid stored coords -> place at nearest tile center (0,0)
                Debug.LogWarning($"[Chessboard] ReturnPieceToTile: piece {piece.name} had invalid coords {idx}. Placing at (0,0).");
                idx = new Vector2Int(0, 0);
            }

            board[idx.x, idx.y] = piece;
            Vector3 center = GetTileCenter(idx);
            center.y = tilesYOffset + pieceYOffset;
            piece.SetPosition(center, force: true);
        }

        #endregion

        #region Movement / capture

        /// <summary>
        /// Move piece to x,y. Handles capture. Returns true if move applied.
        /// -> no chess-rule validation here
        /// </summary>
        public bool MoveTo(ChessPiece piece, int x, int y)
        {
            if (piece == null)
            {
                Debug.LogWarning("[Chessboard] MoveTo called with null piece.");
                return false;
            }

            Vector2Int targetIdx = new Vector2Int(x, y);
            if (!IsValidIndex(targetIdx))
            {
                Debug.LogWarning($"[Chessboard] MoveTo called with invalid target {targetIdx}.");
                return false;
            }

            // capture
            ChessPiece target = board[x, y];
            if (target != null)
            {
                if (target.team == piece.team)
                {
                    // cannot capture own piece
                    Debug.Log("[Chessboard] MoveTo rejected: target occupied by own piece.");
                    return false;
                }

                CapturePiece(target);
            }

            // update board and piece coords
            board[x, y] = piece;
            piece.currentX = x;
            piece.currentY = y;

            // position physically
            Vector3 center = GetTileCenter(x, y);
            center.y = tilesYOffset + pieceYOffset;
            piece.SetPosition(center, force: true);

            return true;
        }

        private void CapturePiece(ChessPiece victim)
        {
            if (victim == null)
            {
                Debug.LogWarning("[Chessboard] CapturePiece called with null victim. Ignoring.");
                return;
            }

            // remove from board array if still present
            Vector2Int stored = new Vector2Int(victim.currentX, victim.currentY);
            if (IsValidIndex(stored) && board[stored.x, stored.y] == victim)
                board[stored.x, stored.y] = null;

            // baseline Z = center of major row for the team
            int majorRow = GetMajorRowForTeam(victim.team);
            float rowCenterZ = origin.z + majorRow * tileSize + tileSize * 0.5f;
            float placeY = tilesYOffset + pieceYOffset;

            float outsideMargin = tileSize * 0.5f;               // how far outside board on X
            float zSpacing = Mathf.Max(0.01f, deathSpacing);     // spacing between pieces along Z

            // board center (for facing)
            Vector3 realBoardCenter = origin + new Vector3(tileCountX * tileSize * 0.5f, 0f, tileCountY * tileSize * 0.5f);

            if (victim.team == (int)Team.White)
            {
                whiteCaptured.Add(victim);
                victim.SetScale(Vector3.one * deathSize, force: true);

                int count = whiteCaptured.Count;
                float startZ = rowCenterZ - ((count - 1) * 0.5f * zSpacing);
                float xPos = origin.x + tileCountX * tileSize + outsideMargin;
                float zPos = startZ + (count - 1) * zSpacing;

                Vector3 pos = new Vector3(xPos, placeY, zPos);
                victim.SetPosition(pos, force: true);

                Vector3 lookDir = realBoardCenter - new Vector3(pos.x, realBoardCenter.y, pos.z);
                lookDir.y = 0f;
                victim.transform.rotation = Quaternion.LookRotation(lookDir.normalized);
            }
            else
            {
                blackCaptured.Add(victim);
                victim.SetScale(Vector3.one * deathSize, force: true);

                int count = blackCaptured.Count;
                // Centered start point like white but move in negative Z direction
                float startZ = rowCenterZ + ((count - 1) * 0.5f * zSpacing);
                float xPos = origin.x - outsideMargin;
                float zPos = startZ - (count - 1) * zSpacing;

                Vector3 pos = new Vector3(xPos, placeY, zPos);
                victim.SetPosition(pos, force: true);

                Vector3 lookDir = realBoardCenter - new Vector3(pos.x, realBoardCenter.y, pos.z);
                lookDir.y = 0f;
                victim.transform.rotation = Quaternion.LookRotation(lookDir.normalized);
            }
        }

        /// <summary>
        /// Returns major-row index for a given team.
        /// White majors at y=0. Black majors at y=tileCountY-1.
        /// </summary>
        private int GetMajorRowForTeam(int teamInt)
        {
            var team = (Team)teamInt;
            return team == Team.White ? 0 : Mathf.Clamp(tileCountY - 1, 0, tileCountY - 1);
        }

        #endregion

        #region Utilities / highlight / queries

        private void SetTileHighlight(Vector2Int idx, bool highlight)
        {
            if (!IsValidIndex(idx)) return;

            GameObject tile = tiles[idx.x, idx.y];
            if (tile == null)
            {
                Debug.LogWarning($"[Chessboard] SetTileHighlight: tile at {idx} is null.");
                return;
            }

            tile.layer = highlight ? highlightLayer : tileLayer;
        }

        public ChessPiece GetPieceAt(int x, int y)
        {
            if (!IsValidIndex(new Vector2Int(x, y))) return null;
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
