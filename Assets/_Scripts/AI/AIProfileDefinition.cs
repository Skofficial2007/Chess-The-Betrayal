using UnityEngine;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Authoring-time mirror of <see cref="AIProfile"/>. One asset per tier. Not yet consulted by
    /// the runtime resolution path — <see cref="AIProfileTableProvider"/> reads the code-side
    /// <see cref="AIProfileTable.BuiltIn"/> roster this ticket. This asset type exists so a later
    /// ticket can redirect the provider at authored assets without shape changes.
    ///
    /// PROVISIONAL — every asset of this type mirrors a row in <see cref="AIProfileTable.BuiltIn"/>,
    /// which is itself pre-validation. See that class's doc comment for what "validated" means here.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/AI/AI Profile Definition", fileName = "AIProfileDefinition")]
    public sealed class AIProfileDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "normal";
        [SerializeField, Min(1)] private int _maxDepth = 5;
        [SerializeField, Min(1)] private int _softTimeBudgetMs = 1500;
        [SerializeField, Min(1)] private int _hardTimeBudgetMs = 2250;
        [SerializeField, Range(0f, 1f)] private float _blunderRate;
        [SerializeField, Min(0)] private int _blunderMarginCp;
        [SerializeField] private float _betrayalAggression;
        [SerializeField] private float _attackDefenseBias = 1f;
        [SerializeField, Min(0)] private int _tieBreakWindowCp;
        [SerializeField] private bool _useOpeningBook = true;

        public AIProfile ToProfile() => AIProfileGuardrails.Apply(new AIProfile(
            _id, _maxDepth, new AITimeBudget(_softTimeBudgetMs, _hardTimeBudgetMs), _blunderRate, _blunderMarginCp,
            _betrayalAggression, _attackDefenseBias, _tieBreakWindowCp, _useOpeningBook));

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
                Debug.LogError($"[{nameof(AIProfileDefinition)}] '{name}' has an empty Id.", this);

            if (_maxDepth < 1)
                Debug.LogError($"[{nameof(AIProfileDefinition)}] '{name}' MaxDepth must be >= 1.", this);

            if (_softTimeBudgetMs < 1)
                Debug.LogError($"[{nameof(AIProfileDefinition)}] '{name}' SoftTimeBudgetMs must be >= 1.", this);

            if (_hardTimeBudgetMs < 1)
                Debug.LogError($"[{nameof(AIProfileDefinition)}] '{name}' HardTimeBudgetMs must be >= 1.", this);

            if (_hardTimeBudgetMs < _softTimeBudgetMs)
                Debug.LogError($"[{nameof(AIProfileDefinition)}] '{name}' HardTimeBudgetMs ({_hardTimeBudgetMs}) must be >= SoftTimeBudgetMs ({_softTimeBudgetMs}).", this);

            if (AIProfileGuardrails.RequiresClamp(_maxDepth))
            {
                if (_attackDefenseBias < AIProfileGuardrails.MinClampedAttackDefenseBias ||
                    _attackDefenseBias > AIProfileGuardrails.MaxClampedAttackDefenseBias)
                {
                    Debug.LogError(
                        $"[{nameof(AIProfileDefinition)}] '{name}' has MaxDepth {_maxDepth} (< {AIProfileGuardrails.ShallowSearchDepthThreshold}) " +
                        $"with AttackDefenseBias {_attackDefenseBias}, outside the shallow-search range " +
                        $"[{AIProfileGuardrails.MinClampedAttackDefenseBias}, {AIProfileGuardrails.MaxClampedAttackDefenseBias}]. " +
                        "It will be clamped at resolution — a shallow search can't vet a strongly reshaped evaluator.", this);
                }

                if (_betrayalAggression < AIProfileGuardrails.MinClampedBetrayalAggression ||
                    _betrayalAggression > AIProfileGuardrails.MaxClampedBetrayalAggression)
                {
                    Debug.LogError(
                        $"[{nameof(AIProfileDefinition)}] '{name}' has MaxDepth {_maxDepth} (< {AIProfileGuardrails.ShallowSearchDepthThreshold}) " +
                        $"with BetrayalAggression {_betrayalAggression}, outside the shallow-search range " +
                        $"[{AIProfileGuardrails.MinClampedBetrayalAggression}, {AIProfileGuardrails.MaxClampedBetrayalAggression}]. " +
                        "It will be clamped at resolution — a shallow search can't vet a strongly reshaped evaluator.", this);
                }
            }
        }
    }
}
