using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class Chessboard : MonoBehaviour
{
    [Header("Art Settings")]
    [SerializeField] private Material tileMaterial;

    [Header("Board Settings")]
    [SerializeField, Range(0.1f, 2f)] private float tileSize = 1.0f;
    [SerializeField] private int tileCountX = 8;
    [SerializeField] private int tileCountY = 8;

    private GameObject[,] _tiles;
    private Dictionary<GameObject, Vector2Int> _tileLookup;
    private Camera _mainCamera;
    private Vector2Int _currentHover = -Vector2Int.one;

    // Layer cache (initialized in Awake)
    private int _tileLayer;
    private int _highlightLayer;
    private int _combinedTileMask;

    #region Unity Lifecycle

    private void Awake()
    {
        CacheLayers();
        InitializeBoard();
    }

    private void Update()
    {
        // Ensure camera reference
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                Debug.LogError("[Chessboard] No Camera.main found in the scene!");
                return;
            }
        }

        // Ensure mouse input is available
        if (Mouse.current == null)
        {
            Debug.LogWarning("[Chessboard] Mouse not detected by Input System! Skipping hover update.");
            return;
        }

        HandleHover(Mouse.current.position.ReadValue());
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Reads and caches the Tile and Highlight layer IDs.
    /// </summary>
    private void CacheLayers()
    {
        _tileLayer = LayerMask.NameToLayer("Tile");
        _highlightLayer = LayerMask.NameToLayer("Highlight");

        if (_tileLayer == -1)
            Debug.LogWarning("[Chessboard] Layer 'Tile' not found. Defaulting to layer 0.");
        if (_highlightLayer == -1)
            Debug.LogWarning("[Chessboard] Layer 'Highlight' not found. Defaulting to layer 0.");

        _tileLayer = Mathf.Max(0, _tileLayer);
        _highlightLayer = Mathf.Max(0, _highlightLayer);

        // Combine both layers into one mask
        _combinedTileMask = (1 << _tileLayer) | (1 << _highlightLayer);
    }

    /// <summary>
    /// Initializes the entire chessboard grid.
    /// </summary>
    private void InitializeBoard()
    {
        if (tileMaterial == null)
        {
            Debug.LogError("[Chessboard] Missing tile material reference!");
            return;
        }

        _tiles = new GameObject[tileCountX, tileCountY];
        _tileLookup = new Dictionary<GameObject, Vector2Int>(tileCountX * tileCountY);

        for (int x = 0; x < tileCountX; x++)
        {
            for (int y = 0; y < tileCountY; y++)
            {
                GameObject tile = CreateTile(x, y);
                _tiles[x, y] = tile;
                _tileLookup[tile] = new Vector2Int(x, y);
            }
        }
    }

    /// <summary>
    /// Creates a single tile at the given coordinates.
    /// </summary>
    private GameObject CreateTile(int x, int y)
    {
        var tileObject = new GameObject($"Tile ({x},{y})")
        {
            layer = _tileLayer
        };
        tileObject.transform.parent = transform;
        tileObject.transform.localPosition = Vector3.zero;

        // Build mesh
        Mesh mesh = new Mesh
        {
            name = $"TileMesh_{x}_{y}"
        };

        Vector3[] vertices = new Vector3[4];
        vertices[0] = new Vector3(x * tileSize, 0, y * tileSize);
        vertices[1] = new Vector3(x * tileSize, 0, (y + 1) * tileSize);
        vertices[2] = new Vector3((x + 1) * tileSize, 0, y * tileSize);
        vertices[3] = new Vector3((x + 1) * tileSize, 0, (y + 1) * tileSize);

        int[] tris = { 0, 1, 2, 1, 3, 2 };

        mesh.vertices = vertices;
        mesh.triangles = tris;
        mesh.RecalculateNormals();

        tileObject.AddComponent<MeshFilter>().mesh = mesh;
        tileObject.AddComponent<MeshRenderer>().material = tileMaterial;
        tileObject.AddComponent<BoxCollider>();

        return tileObject;
    }

    #endregion

    #region Hover Handling

    /// <summary>
    /// Handles hover detection over tiles based on screen position.
    /// </summary>
    private void HandleHover(Vector2 mouseScreenPosition)
    {
        Ray ray = _mainCamera.ScreenPointToRay(mouseScreenPosition);

        // FIX: raycast now includes both Tile and Highlight layers
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, _combinedTileMask))
        {
            GameObject hitTile = hit.transform.gameObject;

            if (!_tileLookup.TryGetValue(hitTile, out Vector2Int hitIndex))
            {
                Debug.LogWarning("[Chessboard] Raycast hit object not found in tile lookup!");
                return;
            }

            if (_currentHover == -Vector2Int.one)
            {
                _currentHover = hitIndex;
                SetTileHighlight(hitIndex, true);
                return;
            }

            if (_currentHover != hitIndex)
            {
                SetTileHighlight(_currentHover, false);
                _currentHover = hitIndex;
                SetTileHighlight(hitIndex, true);
            }
        }
        else
        {
            if (_currentHover != -Vector2Int.one)
            {
                SetTileHighlight(_currentHover, false);
                _currentHover = -Vector2Int.one;
            }
        }
    }

    /// <summary>
    /// Toggles tile highlight visual.
    /// </summary>
    private void SetTileHighlight(Vector2Int index, bool highlight)
    {
        if (index.x < 0 || index.x >= tileCountX || index.y < 0 || index.y >= tileCountY)
        {
            Debug.LogWarning($"[Chessboard] Invalid tile index: {index}");
            return;
        }

        GameObject tile = _tiles[index.x, index.y];
        if (tile == null)
        {
            Debug.LogWarning($"[Chessboard] Tile at index {index} is null!");
            return;
        }

        tile.layer = highlight ? _highlightLayer : _tileLayer;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Returns the world position center of a given tile index.
    /// </summary>
    public Vector3 GetTileCenter(Vector2Int index)
    {
        return new Vector3(
            (index.x + 0.5f) * tileSize,
            0,
            (index.y + 0.5f) * tileSize
        );
    }

    /// <summary>
    /// Gets the tile GameObject at the specified board coordinate.
    /// </summary>
    public GameObject GetTile(Vector2Int index)
    {
        if (index.x < 0 || index.x >= tileCountX || index.y < 0 || index.y >= tileCountY)
            return null;

        return _tiles[index.x, index.y];
    }

    #endregion
}
