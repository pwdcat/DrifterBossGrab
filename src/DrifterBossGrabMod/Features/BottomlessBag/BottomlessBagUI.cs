#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using RoR2;
using RoR2.UI;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Balance;
using DrifterBossGrabMod.Networking;

namespace DrifterBossGrabMod.UI
{
    // ========================================================================================
    // BAG CAROUSEL UPDATER
    // ========================================================================================

    public static class BagCarouselUpdater
    {
        internal static readonly List<BaggedObjectCarousel> ActiveCarousels = new();

        private static bool IsValid(GameObject obj) => obj && (obj.GetComponent<HealthComponent>()?.alive ?? true) && (obj.GetComponent<SpecialObjectAttributes>()?.durability ?? 1) > 0;

        public static void UpdateCarousel(DrifterBagController ctrl, int direction = 0)
        {
            Log.DebugIfEnabled("[UpdateCarousel] Controller: {0} Dir: {1}", ctrl ? ctrl.name : "null", direction);
            for (int i = ActiveCarousels.Count - 1; i >= 0; i--)
            {
                if (ActiveCarousels[i] == null) { ActiveCarousels.RemoveAt(i); continue; }
                ActiveCarousels[i].PopulateCarousel(direction);
            }
        }

        public static void UpdateNetworkBagState(DrifterBagController? ctrl, int direction = 0)
        {
            if (!ctrl || (!NetworkServer.active && !ctrl!.hasAuthority)) return;

            if (ctrl!.GetComponent<BottomlessBagNetworkController>() is { } net)
            {
                var bagged = API.DrifterBagAPI.GetBaggedObjects(ctrl);
                bagged.RemoveAll(o => !o || !IsValid(o));

                var seats = new List<GameObject>();
                if (API.DrifterBagAPI.GetAdditionalSeats(ctrl) is { } sd)
                {
                    foreach (var s in sd.Values) if (s && s.gameObject && IsValid(s.gameObject)) seats.Add(s.gameObject);
                }

                int idx = -1;
                var main = API.DrifterBagAPI.GetMainPassenger(ctrl);
                if (main && !IsValid(main!)) { API.DrifterBagAPI.SetMainSeatObject(ctrl, null); main = null; }

                bool inMain = main && ctrl.vehicleSeat && ctrl.vehicleSeat.hasPassenger && ReferenceEquals(ctrl.vehicleSeat.NetworkpassengerBodyObject, main);
                bool tracked = !NetworkServer.active && ctrl.hasAuthority && main && !inMain;

                if (inMain || tracked)
                {
                    for (int i = 0; i < bagged.Count; i++) if (bagged[i] && main && bagged[i].GetInstanceID() == main!.GetInstanceID()) { idx = i; break; }
                }
                net.SetBagState(idx, bagged, seats, direction);
            }
        }
    }

    // ========================================================================================
    // MASS CAPACITY UI CONTROLLER
    // ========================================================================================

    public class MassCapacityUIController : MonoBehaviour
    {
        private GameObject? _instance;
        private DrifterBagController? _ctrl;
        private HGTextMeshProUGUI? _pctText;
        private Image? _fill;
        private Image? _overfill;
        private GameObject? _sepTemplate;
        private readonly List<GameObject> _seps = new();
        private float _cap, _used;

        private void Start()
        {
            foreach (var bc in FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None)) if (bc.hasAuthority) { _ctrl = bc; break; }
            if (!_ctrl) { enabled = false; return; }
            if (PluginConfig.Instance.EnableMassCapacityUI.Value) StartCoroutine(LoadUI());
        }

        private IEnumerator LoadUI()
        {
            var handle = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/Junk UI.prefab");
            yield return handle;
            if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && handle.Result) InstantiateUI(handle.Result);
            Addressables.Release(handle);
        }

        private void InstantiateUI(GameObject prefab)
        {
            if (FindFirstObjectByType<HUD>() is not { mainContainer: { } mc }) return;
            Transform? parent = mc.transform.Find("MainUIArea/CrosshairCanvas/CrosshairExtras") ?? mc.transform.Find("MainUIArea/CrosshairCanvas") ?? mc.transform.Find("MainUIArea") ?? mc.transform;

            _instance = Instantiate(prefab, parent);
            _instance.name = "CapacityUI";
            _pctText = _instance.GetComponentInChildren<HGTextMeshProUGUI>();

            foreach (var img in _instance.GetComponentsInChildren<Image>()) if (img.type == Image.Type.Filled && img.fillMethod == Image.FillMethod.Horizontal) { _fill = img; break; }

            if (_fill)
            {
                _fill!.color = PluginConfig.Instance.CapacityGradientColorMid.Value;
                _fill!.gameObject.AddComponent<CapacityUIGradient>();
                foreach (Transform child in _fill!.transform.parent) if (child.name.StartsWith("Threshold"))
                    {
                        if (!_sepTemplate) { child.gameObject.name = "SeparatorTemplate"; child.gameObject.SetActive(false); _sepTemplate = child.gameObject; }
                        else Destroy(child.gameObject);
                    }
                CreateOverfill();
            }
            UpdateConfig(); UpdateCapacityUI();
        }

        private void CreateOverfill()
        {
            if (!_fill) return;
            var obj = new GameObject("OverencumbranceFillImage");
            obj.transform.SetParent(_fill!.transform.parent, false);
            obj.transform.localPosition = _fill!.transform.localPosition;
            obj.transform.localRotation = _fill!.transform.localRotation;
            obj.transform.localScale = _fill!.transform.localScale;
            if (_fill!.transform is RectTransform fr)
            {
                var or = obj.AddComponent<RectTransform>();
                or.anchorMin = fr.anchorMin; or.anchorMax = fr.anchorMax; or.pivot = fr.pivot; or.sizeDelta = fr.sizeDelta; or.anchoredPosition = fr.anchoredPosition;
            }
            _overfill = obj.AddComponent<Image>();
            _overfill.type = Image.Type.Filled;
            _overfill.fillMethod = _fill!.fillMethod;
            _overfill.fillOrigin = _fill!.fillOrigin;
            _overfill.fillClockwise = _fill!.fillClockwise;
            _overfill.preserveAspect = _fill!.preserveAspect;
            _overfill.useSpriteMesh = _fill!.useSpriteMesh;
            _overfill.pixelsPerUnitMultiplier = _fill!.pixelsPerUnitMultiplier;
            _overfill.color = PluginConfig.Instance.OverencumbranceGradientColorMid.Value;
            _overfill.sprite = _fill!.sprite; _overfill.raycastTarget = false;
            obj.AddComponent<OverencumbranceUIGradient>();
        }

        public void UpdateCapacityUI()
        {
            if (!_instance || !PluginConfig.Instance.EnableMassCapacityUI.Value || !_ctrl) return;

            if (PluginConfig.Instance.EnableBalance.Value) { _cap = CapacityScalingSystem.CalculateMassCapacity(_ctrl!); _used = BagCapacityCalculator.GetBaggedObjectMass(_ctrl!); }
            else { _cap = BagCapacityCalculator.GetUtilityMaxStock(_ctrl!); _used = BagCapacityCalculator.GetCurrentBaggedCount(_ctrl!); }

            _instance!.SetActive(_cap >= 1f);
            if (!_instance!.activeSelf) return;

            float mPct = (PluginConfig.Instance.EnableBalance.Value && _cap > 0) ? _used / _cap : 0;
            int slots = BagCapacityCalculator.GetUtilityMaxStock(_ctrl!), count = API.DrifterBagAPI.GetBagCount(_ctrl!) + (API.DrifterBagAPI.GetIncomingObject(_ctrl!) != null ? 1 : 0);
            float sPct = slots > 0 ? (float)count / slots : 0;
            float pct = PluginConfig.Instance.EnableBalance.Value ? Mathf.Max(mPct, sPct) : sPct;

            if (_fill) _fill!.fillAmount = Mathf.Clamp01(pct);
            if (_overfill)
            {
                if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.OverencumbranceMax.Value > 0)
                {
                    float of = (pct > 1f) ? Mathf.Clamp01((pct - 1f) / (PluginConfig.Instance.OverencumbranceMax.Value / 100f)) : 0;
                    _overfill!.fillAmount = of;
                    _overfill!.gameObject.SetActive(of > 0);
                }
                else
                {
                    _overfill!.fillAmount = 0;
                    _overfill!.gameObject.SetActive(false);
                }
            }
            if (_pctText) _pctText!.text = $"{Mathf.RoundToInt(pct * 100)}%";
            UpdateGradient(pct); UpdateSeparators(pct);
        }

        private void UpdateGradient(float pct)
        {
            if (_fill) _fill!.color = PluginConfig.Instance.CapacityGradientColorMid.Value;
            if (_overfill) _overfill!.color = PluginConfig.Instance.OverencumbranceGradientColorMid.Value;
            if (_fill?.GetComponent<CapacityUIGradient>() is { } g) { g.Intensity = PluginConfig.Instance.GradientIntensity.Value; _fill!.SetVerticesDirty(); }
            if (_overfill?.GetComponent<OverencumbranceUIGradient>() is { } og) { og.Intensity = PluginConfig.Instance.GradientIntensity.Value; _overfill!.SetVerticesDirty(); }
        }

        private void UpdateSeparators(float pct)
        {
            if (!_instance || !PluginConfig.Instance.EnableSeparators.Value || _cap <= 0) { foreach (var s in _seps) s.SetActive(false); return; }
            List<float> fracs = new();
            if (PluginConfig.Instance.EnableBalance.Value && _ctrl)
            {
                float cur = 0; int slots = BagCapacityCalculator.GetUtilityMaxStock(_ctrl!), k = 1;
                bool uncap = PluginConfig.Instance.IsSlotScalingFormulaInfinite && PluginConfig.Instance.BottomlessBagEnabled.Value;
                var list = API.DrifterBagAPI.GetBaggedObjects(_ctrl!);
                if (list != null) foreach (var o in list) if (o && !ProjectileRecoveryPatches.IsInProjectileState(o))
                        {
                            cur += API.DrifterBagAPI.LoadObjectState(_ctrl!, o)?.baggedMass ?? _ctrl!.CalculateBaggedObjectMass(o);
                            float f = cur / _cap; if (!uncap && slots > 0) f = Mathf.Max((float)k / slots, f);
                            fracs.Add(f); k++;
                        }
                if (API.DrifterBagAPI.GetIncomingObject(_ctrl!) is { } inc)
                {
                    cur += API.DrifterBagAPI.LoadObjectState(_ctrl!, inc)?.baggedMass ?? _ctrl!.CalculateBaggedObjectMass(inc);
                    float f = cur / _cap; if (!uncap && slots > 0) f = Mathf.Max((float)k / slots, f);
                    fracs.Add(f); k++;
                }
                if (!uncap && slots > 1) for (int i = k; i < Mathf.Min(slots, 15); i++) if ((float)i / slots > cur / _cap) fracs.Add((float)i / slots);

                if (PluginConfig.Instance.OverencumbranceMax.Value > 0 && cur > _cap)
                {
                    float max = PluginConfig.Instance.OverencumbranceMax.Value / 100f;
                    fracs = fracs.Where(f => f > 1f).Select(f => Mathf.Clamp01((f - 1f) / max)).ToList();
                }
            }
            else
            {
                int slots = _ctrl ? BagCapacityCalculator.GetUtilityMaxStock(_ctrl!) : 3;
                if (slots > 1) for (int i = 1; i < Mathf.Min(slots, 15); i++) fracs.Add((float)i / Mathf.Min(slots, 15));
            }

            while (_seps.Count < fracs.Count && _sepTemplate) _seps.Add(Instantiate(_sepTemplate!, _sepTemplate!.transform.parent));
            for (int i = 0; i < _seps.Count; i++)
            {
                if (i < fracs.Count && fracs[i] is > 0.01f and < 0.99f)
                {
                    _seps[i].SetActive(true);
                    if (_seps[i].GetComponent<RectTransform>() is { } r)
                    {
                        float rad = (226.44f - fracs[i] * 98.88f) * Mathf.Deg2Rad;
                        r.localPosition = new Vector3(-37.76f + Mathf.Cos(rad) * 21.53f, 51.92f + Mathf.Sin(rad) * 21.53f, 0);
                        r.localEulerAngles = new Vector3(0, 0, (226.44f - fracs[i] * 98.88f) + 90.5f);
                    }
                }
                else _seps[i].SetActive(false);
            }
        }

        public void UpdateConfig()
        {
            if (!_instance) return;
            _instance!.SetActive(PluginConfig.Instance.EnableMassCapacityUI.Value);
            if (!_instance!.activeSelf) return;
            if (_instance!.GetComponent<RectTransform>() is { } r) r.anchoredPosition = new Vector2(PluginConfig.Instance.MassCapacityUIPositionX.Value, PluginConfig.Instance.MassCapacityUIPositionY.Value);
            _instance!.transform.localScale = Vector3.one * PluginConfig.Instance.MassCapacityUIScale.Value;
        }

        private void OnDestroy() { if (_instance) Destroy(_instance); }
    }

    public class CapacityUIGradient : BaseMeshEffect
    {
        public float Intensity = 1f;
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            List<UIVertex> v = new(); vh.GetUIVertexStream(v);
            if (v.Count == 0) return;
            Rect r = graphic.rectTransform.rect;
            Color s = PluginConfig.Instance.CapacityGradientColorStart.Value, m = PluginConfig.Instance.CapacityGradientColorMid.Value, e = PluginConfig.Instance.CapacityGradientColorEnd.Value;
            for (int i = 0; i < v.Count; i++)
            {
                UIVertex vt = v[i]; float nx = Mathf.Clamp01((vt.position.x - r.xMin) / r.width);
                Color tc = nx <= 0.5f ? Color.Lerp(e, m, nx * 2f) : Color.Lerp(m, s, (nx - 0.5f) * 2f);
                tc.a *= vt.color.a; vt.color = Color.Lerp(vt.color, tc, Intensity); v[i] = vt;
            }
            vh.Clear(); vh.AddUIVertexTriangleStream(v);
        }
    }

    public class OverencumbranceUIGradient : BaseMeshEffect
    {
        public float Intensity = 1f;
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            List<UIVertex> v = new(); vh.GetUIVertexStream(v);
            if (v.Count == 0) return;
            Rect r = graphic.rectTransform.rect;
            Color s = PluginConfig.Instance.OverencumbranceGradientColorStart.Value, m = PluginConfig.Instance.OverencumbranceGradientColorMid.Value, e = PluginConfig.Instance.OverencumbranceGradientColorEnd.Value;
            for (int i = 0; i < v.Count; i++)
            {
                UIVertex vt = v[i]; float nx = Mathf.Clamp01((vt.position.x - r.xMin) / r.width);
                Color tc = nx <= 0.5f ? Color.Lerp(e, m, nx * 2f) : Color.Lerp(m, s, (nx - 0.5f) * 2f);
                tc.a *= vt.color.a; vt.color = Color.Lerp(vt.color, tc, Intensity); v[i] = vt;
            }
            vh.Clear(); vh.AddUIVertexTriangleStream(v);
        }
    }
}
