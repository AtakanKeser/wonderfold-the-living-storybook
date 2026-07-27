using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Game.Presentation
{
    /// <summary>
    /// The page turn.
    ///
    /// <para>Everything in the fold region is parented under a temporary pivot placed on the crease and
    /// rotated 180° about it. Halfway through — edge-on to the camera, when nothing is legible anyway —
    /// the model's new state is swapped in. That is the whole trick: the flip hides the cut, so the
    /// player reads one continuous physical motion rather than a board that changed while they blinked.
    /// </para>
    ///
    /// <para>A small per-cell delay away from the crease gives the paper a ripple instead of a rigid
    /// slab; the timings come from <see cref="SurfaceMapper"/> so the core and the view agree on how long
    /// a fold takes.</para>
    /// </summary>
    public sealed class FoldAnimator : MonoBehaviour
    {
        public Coroutine Play(FoldAnimationPlan plan, List<Transform> contents, BoardLayout layout,
            float duration, System.Action onHalfway) =>
            StartCoroutine(Routine(plan, contents, layout, duration, onHalfway));

        private IEnumerator Routine(FoldAnimationPlan plan, List<Transform> contents, BoardLayout layout,
            float duration, System.Action onHalfway)
        {
            var pivot = new GameObject("FoldPivot").transform;
            pivot.SetParent(transform, false);
            pivot.position = HingeWorldPosition(plan, layout);

            var originalParents = new List<Transform>(contents.Count);
            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i] == null)
                {
                    originalParents.Add(null);
                    continue;
                }

                originalParents.Add(contents[i].parent);
                contents[i].SetParent(pivot, true);
            }

            var axis = plan.FlipAxis == Axis.Vertical ? Vector3.up : Vector3.right;
            bool swapped = false;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, t);

                pivot.localRotation = Quaternion.AngleAxis(eased * 180f, axis);

                // A little lift so the page peels off the diorama instead of spinning inside it.
                pivot.localPosition = HingeWorldPosition(plan, layout) - transform.position
                                      + new Vector3(0f, 0f, -Mathf.Sin(eased * Mathf.PI) * layout.CellSize * 0.5f);

                if (!swapped && t >= 0.5f)
                {
                    swapped = true;
                    onHalfway?.Invoke();
                    // Re-parent whatever the resync just rebuilt so the second half carries the new face.
                    Reparent(contents, pivot);
                }

                yield return null;
            }

            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i] == null) continue;
                contents[i].SetParent(originalParents[i], true);
            }

            Destroy(pivot.gameObject);
            onHalfway?.Invoke();
        }

        private static void Reparent(List<Transform> contents, Transform pivot)
        {
            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i] == null || contents[i].parent == pivot) continue;
                contents[i].SetParent(pivot, true);
            }
        }

        private Vector3 HingeWorldPosition(FoldAnimationPlan plan, BoardLayout layout)
        {
            float x = plan.FlipAxis == Axis.Vertical
                ? plan.HingePosition - 0.5f
                : plan.Area.X + plan.Area.Width * 0.5f - 0.5f;

            float y = plan.FlipAxis == Axis.Horizontal
                ? plan.HingePosition - 0.5f
                : plan.Area.Y + plan.Area.Height * 0.5f - 0.5f;

            return layout.Origin + new Vector3(x * layout.CellSize, y * layout.CellSize, 0f);
        }
    }
}
