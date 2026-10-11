#nullable enable
using EntityStates.Drifter.Bag;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.UI
{
    public class BreakoutWarningOverlay : MonoBehaviour
    {
        private GameObject? _passenger;
        private DrifterBagController? _controller;
        private BaggedObjectCarousel? _carousel;
        private PortraitStruggleEffect? _portrait;

        public void SetTarget(GameObject? passenger, DrifterBagController? controller, BaggedCardController card)
        {
            Hide();
            _passenger = passenger;
            _controller = controller;
            _carousel = GetComponentInParent<BaggedObjectCarousel>();
            _portrait = null;
            if (passenger == null || card.portraitIconImage == null) return;
            _portrait = card.portraitIconImage.GetComponent<PortraitStruggleEffect>();
            if (_portrait == null)
                _portrait = card.portraitIconImage.gameObject.AddComponent<PortraitStruggleEffect>();
        }

        private void LateUpdate()
        {
            if (!PluginConfig.Instance.EnableBreakoutWarning.Value || _passenger == null || _controller == null ||
                _portrait == null || !_portrait.gameObject.activeInHierarchy || DrifterBagController.bagDisableBreakout ||
                (_carousel != null && _carousel.IsSlotAnimating(gameObject)) ||
                !TryGetProgress(_passenger, _controller, out float progress, out float attempts, out float interval) || progress < 0.75f)
            {
                Hide();
                return;
            }
            float warningProgress = Mathf.InverseLerp(0.75f, 1f, progress);
            float strength = Mathf.SmoothStep(0f, 1f, warningProgress);
            int beats = Mathf.Clamp(Mathf.FloorToInt(interval * 0.25f / 0.4f), 1, 3);
            float phase = Mathf.Repeat(warningProgress * beats, 1f);
            float beat = phase < 0.15f
                ? Mathf.SmoothStep(0f, 1f, phase / 0.15f)
                : 1f - Mathf.SmoothStep(0f, 1f, (phase - 0.15f) / 0.85f);
            float amplitude = Mathf.Min(1.5f + Mathf.Max(0f, attempts) * 0.4f, 3f) * strength * PluginConfig.Instance.BreakoutWarningStrength.Value;
            _portrait.SetOffset(new Vector2(Mathf.Sin(phase * Mathf.PI * 2f) * amplitude * beat, 0f));
        }

        internal static bool TryGetProgress(GameObject? passenger, DrifterBagController? controller, out float progress, out float attempts, out float interval)
        {
            progress = 0f;
            float elapsed = 0f;
            attempts = 0f;
            interval = 0f;
            if (passenger == null || controller == null) return false;
            var body = passenger.GetComponent<CharacterBody>();
            if (body == null || body.healthComponent == null || !body.healthComponent.alive) return false;
            var attributes = passenger.GetComponent<SpecialObjectAttributes>();
            if (attributes != null && attributes.suppressBreakout) return false;
            if (!NetworkServer.active)
            {
                var network = controller.GetComponent<Networking.BottomlessBagNetworkController>();
                return network != null && network.TryGetBreakoutPresentation(passenger, out progress, out attempts, out interval);
            }
            var timer = passenger.GetComponent<AdditionalSeatBreakoutTimer>();
            if (timer != null && timer.isActiveAndEnabled && timer.controller == controller &&
                BagHelpers.GetAdditionalSeat(controller, passenger) != null && NetworkServer.active)
            {
                if (!AdditionalSeatBreakoutTimer.CanBreakout(passenger)) return false;
                elapsed = timer.GetElapsedBreakoutTime();
                interval = timer.breakoutTime;
                attempts = timer.breakoutAttempts;
            }
            else
            {
                var machine = EntityStateMachine.FindByCustomName(controller.gameObject, "Bag");
                if (!(machine?.state is BaggedObject state) || state.targetObject != passenger ||
                    ReflectionCache.BaggedObject.HoldsBreakoutBody?.Invoke(state, null) is not bool canBreakout || !canBreakout ||
                    ReflectionCache.BaggedObject.BreakoutTimer?.GetValue(state) is not float age ||
                    ReflectionCache.BaggedObject.BreakoutTime?.GetValue(state) is not float duration) return false;
                elapsed = age;
                interval = duration;
                if (ReflectionCache.BaggedObject.BreakoutAttempts?.GetValue(state) is float count)
                    attempts = count;
            }
            if (attributes != null && attributes.breakoutState.stateType != null) interval *= 0.5f;
            if (interval <= 0f || float.IsNaN(interval) || float.IsInfinity(interval)) return false;
            progress = Mathf.Clamp01(elapsed / interval);
            return true;
        }

        private void Hide()
        {
            if (_portrait != null) _portrait.SetOffset(Vector2.zero);
        }

        private void OnDisable() => Hide();

        private void OnDestroy() => Hide();
    }

    internal sealed class PortraitStruggleEffect : BaseMeshEffect
    {
        private Vector2 _offset;

        internal void SetOffset(Vector2 offset)
        {
            if (_offset == offset) return;
            _offset = offset;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || _offset == Vector2.zero) return;
            var vertex = new UIVertex();
            var offset = new Vector3(_offset.x, _offset.y, 0f);
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                vertex.position += offset;
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
}
