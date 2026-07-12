using UnityEngine;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Authoring-time mirror of <see cref="AIProfile"/>. One asset per tier. Not yet consulted by
    /// the runtime resolution path — <see cref="AIProfileTableProvider"/> reads the code-side
    /// <see cref="AIProfileTable.BuiltIn"/> roster this ticket. This asset type exists so a later
    /// ticket can redirect the provider at authored assets without shape changes.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/AI/AI Profile Definition", fileName = "AIProfileDefinition")]
    public sealed class AIProfileDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "normal";
        [SerializeField, Min(1)] private int _maxDepth = 5;
        [SerializeField, Min(1)] private int _softTimeBudgetMs = 1500;
        [SerializeField, Range(0f, 1f)] private float _blunderRate;
        [SerializeField, Min(0)] private int _blunderMarginCp;
        [SerializeField] private float _betrayalAggression;
        [SerializeField] private float _attackDefenseBias = 1f;
        [SerializeField, Min(0)] private int _tieBreakWindowCp;
        [SerializeField] private bool _useOpeningBook = true;

        public AIProfile ToProfile() => new AIProfile(
            _id, _maxDepth, _softTimeBudgetMs, _blunderRate, _blunderMarginCp,
            _betrayalAggression, _attackDefenseBias, _tieBreakWindowCp, _useOpeningBook);

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
                Debug.LogError($"[{nameof(AIProfileDefinition)}] '{name}' has an empty Id.", this);

            if (_maxDepth < 1)
                Debug.LogError($"[{nameof(AIProfileDefinition)}] '{name}' MaxDepth must be >= 1.", this);

            if (_softTimeBudgetMs < 1)
                Debug.LogError($"[{nameof(AIProfileDefinition)}] '{name}' SoftTimeBudgetMs must be >= 1.", this);
        }
    }
}
