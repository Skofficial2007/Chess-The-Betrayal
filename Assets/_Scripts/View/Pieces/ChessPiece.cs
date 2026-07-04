using System;
using UnityEngine;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Purely visual component attached to piece prefabs. Contains ZERO game logic — doesn't know
    /// chess rules, move validation, or board state.
    ///
    /// Every animated call here is a thin forward into an IPieceAnimator: ChessPiece owns *what*
    /// visual state a piece is in (its team, type, whether it's highlighted), while the animator
    /// owns *how* it gets there. This split is what lets BoardVisuals orchestrate a move — capture,
    /// slide, castle, promote — without ever knowing whether that will be tweened, instant, or
    /// (for AI/headless play) skipped entirely.
    /// </summary>
    public class ChessPiece : MonoBehaviour
    {
        [Tooltip("Visual identifier for the inspector (set by BoardVisuals on spawn)")]
        public Team team;

        [Tooltip("Piece type for visual reference")]
        public ChessPieceType type;

        private Collider _col;
        private IPieceAnimator _animator;

        private void Awake()
        {
            _col = GetComponent<Collider>();

            // Real, tweened animation is the default for every piece spawned in a live scene.
            // Headless/AI/test contexts opt out via SetAnimator instead of this ever branching on
            // "are we in a test" — the seam, not a runtime flag, is what makes both paths possible.
            //
            // getType is a lazy lookup rather than passing `type` by value: BoardVisuals.
            // SpawnSinglePiece sets ChessPiece.type *after* AddComponent(), which runs Awake, so
            // `type` would still be ChessPieceType.None if we captured it right now.
            _animator = new PrimeTweenPieceAnimator(transform, GetComponentInChildren<Renderer>(), () => type);
        }

        /// <summary>
        /// Swaps the animator this piece uses — e.g. NullPieceAnimator for headless/EditMode
        /// tests that need an instant, tween-free Transform. Must be called before any of the
        /// methods below if the default PrimeTween-backed animator isn't wanted.
        /// </summary>
        public void SetAnimator(IPieceAnimator animator)
        {
            _animator = animator;
        }

        /// <summary>
        /// Sets the target world position for this piece to slide towards.
        /// force = true snaps instantly with no interpolation (illegal-move revert, promotion
        /// snap-to-square) — anywhere a visible tween would fight with what the player just did.
        /// </summary>
        public void SetPosition(Vector3 worldPos, bool force = false)
        {
            _animator.MoveTo(worldPos, force);
        }

        /// <summary>
        /// Same as SetPosition, but with an explicit MoveStyle (quiet slide, capture punch, knight
        /// arc, promotion glide) so a board move can carry its intended feel through to the
        /// animator without BoardVisuals needing to know how each style is actually tweened.
        /// </summary>
        public void SetPosition(Vector3 worldPos, MoveStyle style, bool force = false)
        {
            _animator.MoveTo(worldPos, style, force);
        }

        /// <summary>
        /// Sets the target local scale (used for death-pile shrinking and initial spawn sizing).
        /// force = true snaps instantly with no interpolation.
        /// </summary>
        public void SetScale(Vector3 scale, bool force = false)
        {
            _animator.ScaleTo(scale, force);
        }

        /// <summary>
        /// Instantly turns the piece to face lookDirection (used for death-pile facing).
        /// </summary>
        public void FaceDirection(Vector3 lookDirection)
        {
            _animator.FaceDirection(lookDirection);
        }

        /// <summary>
        /// Turns off the piece's collider so it can no longer be clicked — used once a piece is
        /// captured and moved to the graveyard, since it's no longer a legal selection target.
        /// </summary>
        public void DisableCollider()
        {
            if (_col != null)
            {
                _col.enabled = false;
            }
        }

        /// <summary>
        /// Toggles the temporary "Betrayer" glow applied while this piece is mid-Betrayal (Act
        /// stage through Retribution/Defection resolution).
        /// </summary>
        public void SetBetrayerGlow(bool active)
        {
            _animator.SetHighlighted(active);
        }

        /// <summary>
        /// Plays the "vanish" half of a promotion/defection swap, then invokes onComplete — the
        /// moment BoardVisuals should Destroy this GameObject and spawn its replacement. The
        /// callback may fire on a later frame (it's driven by a tween), so callers must not assume
        /// synchronous completion.
        /// </summary>
        public void PlayTransitionOut(PieceTransitionStyle style, Action onComplete)
        {
            _animator.PlayTransitionOut(style, onComplete);
        }

        /// <summary>
        /// Plays the "reveal" half of a promotion/defection swap on a freshly-spawned piece — the
        /// counterpart to PlayTransitionOut, called immediately after BoardVisuals spawns the
        /// replacement at the same square.
        /// </summary>
        public void PlayTransitionIn(PieceTransitionStyle style)
        {
            _animator.PlayTransitionIn(style);
        }

        /// <summary>
        /// Plays the tap-to-select "pick up" animation and starts the idle bob for as long as this
        /// piece stays selected.
        /// </summary>
        public void LiftSelect()
        {
            _animator.LiftSelect();
        }

        /// <summary>
        /// Stops the idle bob and eases the piece back down to where it was lifted from.
        /// </summary>
        public void LowerDeselect()
        {
            _animator.LowerDeselect();
        }

        /// <summary>
        /// PrimeTween already no-ops safely against a tween whose target was destroyed (see the
        /// class doc on PrimeTweenPieceAnimator), so this isn't strictly required for correctness —
        /// but the idle bob loop started by LiftSelect runs indefinitely (cycles: -1) while a piece
        /// is selected, and stopping it explicitly the instant this GameObject is destroyed (e.g.
        /// captured mid-lift by a fast Betrayal) is cheap, obviously correct, and avoids relying
        /// solely on library behavior for a tween with no natural end.
        /// </summary>
        private void OnDestroy()
        {
            _animator?.CancelSelectionAnimation();
        }
    }
}
