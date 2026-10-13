#nullable enable
using EntityStates.Drifter.Bag;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Networking;

namespace DrifterBossGrabMod.UI
{
    public class BreakoutWarningOverlay : MonoBehaviour
    {
        private const float SlamShakeDuration = 0.28f;
        private static readonly ConditionalWeakTable<GameObject, SlamFeedback> _slamFeedback = new();

        private sealed class SlamFeedback
        {
            internal DrifterBagController? controller;
            internal float startedAt;
            internal float amplitude;
        }

        internal static void ApplySlamFeedback(DrifterBagController controller, GameObject? passenger, float amplitude)
        {
            if (controller == null || passenger == null || float.IsNaN(amplitude) || float.IsInfinity(amplitude)) return;
            var feedback = _slamFeedback.GetValue(passenger, _ => new SlamFeedback());
            feedback.controller = controller;
            feedback.startedAt = Time.unscaledTime;
            feedback.amplitude = Mathf.Clamp(amplitude, 0.75f, 3f);
        }

        public readonly struct DamageFeedbackState
        {
            internal readonly GameObject? passenger;
            internal readonly NetworkInstanceId passengerId;
            internal readonly HealthComponent? health;
            internal readonly SpecialObjectAttributes? attributes;
            internal readonly float healthBefore;
            internal readonly int durabilityBefore;
            internal readonly float amplitude;

            internal DamageFeedbackState(GameObject target, float strength)
            {
                passenger = target;
                var identity = target.GetComponent<NetworkIdentity>();
                passengerId = identity != null ? identity.netId : NetworkInstanceId.Invalid;
                health = target.GetComponent<HealthComponent>();
                attributes = target.GetComponent<SpecialObjectAttributes>();
                healthBefore = health != null ? health.combinedHealth : 0f;
                durabilityBefore = attributes != null ? attributes.durability : 0;
                amplitude = strength;
            }
        }

        private static DamageFeedbackState CaptureDamageFeedback(DrifterBagController controller, GameObject? passenger, bool addsSmack)
        {
            if (!NetworkServer.active || controller == null || passenger == null) return default;
            float amplitude = 2f;
            if (passenger.GetComponent<CharacterBody>() != null)
            {
                int smacks;
                if (addsSmack)
                    smacks = ReflectionCache.DrifterBagController.Smacks?.GetValue(controller) is int count ? count : 0;
                else
                    smacks = BaggedObjectPatches.LoadObjectState(controller, passenger)?.smacks ?? 0;
                float progress = Mathf.Clamp01((Mathf.Max(0, smacks) + (addsSmack ? 1f : 0f)) / Mathf.Max(1, controller.maxSmacks));
                amplitude = Mathf.Lerp(0.75f, 3f, progress);
            }
            return new DamageFeedbackState(passenger, amplitude);
        }

        private static void CompleteDamageFeedback(DrifterBagController controller, DamageFeedbackState hit)
        {
            if (!NetworkServer.active || controller == null || hit.passenger == null) return;
            bool damaged = hit.health != null
                ? hit.health.combinedHealth < hit.healthBefore
                : hit.attributes != null && hit.attributes.durability < hit.durabilityBefore;
            if (!damaged) return;
            ApplySlamFeedback(controller, hit.passenger, hit.amplitude);
            if (controller.netId == NetworkInstanceId.Invalid || hit.passengerId == NetworkInstanceId.Invalid) return;
            NetworkMessageRegistry.SendToAll(Constants.Network.SlamFeedbackSubMessageType, new SlamFeedbackMessage
            {
                controllerNetId = controller.netId,
                passengerNetId = hit.passengerId,
                amplitude = hit.amplitude
            });
        }

        [HarmonyPatch(typeof(DrifterBagController), nameof(DrifterBagController.CmdDamageBaggedObject))]
        public static class DrifterBagController_CmdDamageBaggedObject_Feedback
        {
            [HarmonyPrefix]
            public static void Prefix(DrifterBagController __instance, out DamageFeedbackState __state)
            {
                __state = default;
                if (!NetworkServer.active || __instance == null) return;
                var body = __instance.baggedBody;
                var attributes = __instance.baggedAttributes;
                var passenger = body != null ? body.gameObject : attributes != null ? attributes.gameObject : null;
                __state = CaptureDamageFeedback(__instance, passenger, true);
            }

            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, DamageFeedbackState __state)
            {
                var hit = __state;
                CompleteDamageFeedback(__instance, hit);
            }
        }

        [HarmonyPatch(typeof(CmdDamageBaggedObject_AoE), "ApplyDamageToObject")]
        public static class AoE_ApplyDamageToObject_Feedback
        {
            [HarmonyPrefix]
            public static void Prefix(DrifterBagController controller, GameObject targetObject, out DamageFeedbackState __state)
            {
                __state = CaptureDamageFeedback(controller, targetObject, false);
            }

            [HarmonyPostfix]
            public static void Postfix(DrifterBagController controller, DamageFeedbackState __state)
            {
                var hit = __state;
                CompleteDamageFeedback(controller, hit);
            }
        }

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
            if (_passenger == null || _controller == null ||
                _portrait == null || !_portrait.gameObject.activeInHierarchy ||
                (_carousel != null && _carousel.IsSlotAnimating(gameObject)))
            {
                Hide();
                return;
            }

            float strengthSetting = PluginConfig.Instance.BreakoutWarningStrength.Value;
            var offset = Vector2.zero;
            if (PluginConfig.Instance.EnableCardDamageWiggle.Value &&
                _slamFeedback.TryGetValue(_passenger, out var slam) && slam.controller == _controller)
            {
                float age = (Time.unscaledTime - slam.startedAt) / SlamShakeDuration;
                if (age >= 0f && age < 1f)
                {
                    _carousel?.ResetInactivityTimer();
                    float decay = (1f - age) * (1f - age);
                    offset.x = Mathf.Sin(age * Mathf.PI * 6f) * slam.amplitude * decay * PluginConfig.Instance.CardDamageWiggleStrength.Value;
                }
            }

            if (PluginConfig.Instance.EnableBreakoutWarning.Value && !DrifterBagController.bagDisableBreakout &&
                TryGetProgress(_passenger, _controller, out float progress, out float attempts, out float interval) && progress >= 0.75f)
            {
                float warningProgress = Mathf.InverseLerp(0.75f, 1f, progress);
                float strength = Mathf.SmoothStep(0f, 1f, warningProgress);
                int beats = Mathf.Clamp(Mathf.FloorToInt(interval * 0.25f / 0.4f), 1, 3);
                float phase = Mathf.Repeat(warningProgress * beats, 1f);
                float beat = phase < 0.15f
                    ? Mathf.SmoothStep(0f, 1f, phase / 0.15f)
                    : 1f - Mathf.SmoothStep(0f, 1f, (phase - 0.15f) / 0.85f);
                float amplitude = Mathf.Min(1.5f + Mathf.Max(0f, attempts) * 0.4f, 3f) * strength * strengthSetting;
                offset.x += Mathf.Sin(phase * Mathf.PI * 2f) * amplitude * beat;
            }
            _portrait.SetOffset(offset);
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
