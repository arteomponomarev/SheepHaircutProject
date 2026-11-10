using System;
using System.Collections.Generic;
using ShearAndGrow.Economy;
using ShearAndGrow.Processing;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Farm
{
    /// <summary>Player-facing explanations of placement results.</summary>
    public static class PlacementText
    {
        public static string Reason(PlacementResult r, FarmBuildingDefinition def, long coins)
        {
            switch (r)
            {
                case PlacementResult.Ok: return "Fits here";
                case PlacementResult.OutOfBounds: return "Outside the farm";
                case PlacementResult.Reserved: return "Keep the road and paths clear";
                case PlacementResult.Occupied: return "Something is already here";
                case PlacementResult.Locked: return "Needs Farmer House Lv " + (def != null ? def.RequiredFarmerHouseLevel : 2);
                case PlacementResult.MaxInstances: return "Already built";
                case PlacementResult.ConstructionBusy: return "The builder is busy with another building";
                case PlacementResult.NotEnoughCoins: return "Need " + Math.Max(0, (def != null ? def.Price : 0) - coins) + " more coins";
                case PlacementResult.NotMovable: return "The trader stays by the road";
                case PlacementResult.UnderConstruction: return "Can't move while under construction";
                case PlacementResult.NotPurchasable: return "Not for sale";
                case PlacementResult.Busy: return "Please wait…";
                case PlacementResult.MaxLevel: return "Maximum level reached";
                case PlacementResult.RequirementsNotMet: return "Build all required buildings first";
                default: return "Can't place here";
            }
        }

        public static string Time(double seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            if (s < 60) return s + "s";
            if (s < 3600) return (s / 60) + "m " + (s % 60).ToString("00") + "s";
            return (s / 3600) + "h " + ((s / 60) % 60).ToString("00") + "m";
        }
    }

    /// <summary>
    /// Build-mode UI on the farm HUD canvas: round Build / Edit buttons, the Buildings / Decorations catalog sheet,
    /// the placement bar (hint · Cancel · price · Place), the active-construction chip and the Edit-mode banner.
    /// Event-driven (economy / building / placement changes); the chip text updates once per second.
    /// </summary>
    public sealed class BuildUI : MonoBehaviour
    {
        const float CatalogHeight = 900f;

        FarmHubController _hub;
        FarmBuildingDirector _dir;
        RectTransform _safe;
        GameObject _buildBtn, _editBtn, _catalog, _bar, _chip, _banner;
        RectTransform _cards;
        Text _barHint, _barPrice, _chipText, _confirmLabel;
        Image _chipFill, _barCoin;
        Button _confirm;
        Button _tabBuildings, _tabDecor;
        BuildingCategory _tab = BuildingCategory.Building;
        readonly List<FarmBuildingDefinition> _defs = new List<FarmBuildingDefinition>();
        readonly List<GameObject> _cardObjects = new List<GameObject>();
        readonly Dictionary<string, Button> _cardButtons = new Dictionary<string, Button>();
        int _chipShownSecond = -1;
        float _chipShownFill = -1f;

        public bool CatalogOpen => _catalog != null && _catalog.activeSelf;
        public bool BannerVisible => _banner != null && _banner.activeSelf;
        public Button ConfirmButton => _confirm;
        public string BarHint => _barHint != null ? _barHint.text : "";
        public string ChipText => _chipText != null && _chip.activeSelf ? _chipText.text : "";

        public void Build(FarmHubController hub, FarmBuildingDirector dir, RectTransform safe)
        {
            _hub = hub; _dir = dir; _safe = safe;
            _buildBtn = RoundButton("BuildButton", "Build", new Vector2(1f, 0f), new Vector2(-28f, 40f), UiKit.Accent, () => _hub.OpenCatalog());
            _editBtn = RoundButton("EditButton", "Edit", new Vector2(0f, 0f), new Vector2(28f, 40f), UiKit.PanelLight, () => _hub.SetEditMode(true));
            BuildCatalog();
            BuildBar();
            BuildChip();
            BuildBanner();
            _dir.Placement.Changed += RefreshBar;
            _dir.Service.Changed += OnDataChanged;
            if (WoolEconomy.IsReady) WoolEconomy.Instance.Changed += OnDataChanged;
            RefreshChip(true);
        }

        void OnDestroy()
        {
            if (_dir != null) { if (_dir.Placement != null) _dir.Placement.Changed -= RefreshBar; if (_dir.Service != null) _dir.Service.Changed -= OnDataChanged; }
            if (WoolEconomy.IsReady) WoolEconomy.Instance.Changed -= OnDataChanged;
        }

        void OnDataChanged()
        {
            if (CatalogOpen) RefreshCards();
            if (_dir.Placement.IsActive) { _dir.Placement.Revalidate(); RefreshBar(); }
            RefreshChip(true);
        }

        GameObject RoundButton(string name, string label, Vector2 anchor, Vector2 pos, Color color, Action click)
        {
            var b = UiKit.Button(_safe, name, label, 38, color, click, out var t);
            UiKit.Place((RectTransform)b.transform, anchor, anchor, pos, new Vector2(200f, 120f));
            t.fontStyle = FontStyle.Bold;
            UiKit.SoftShadow(t);
            return b.gameObject;
        }

        // ------------------------------------------------------------------ main buttons / banner

        public void SetMainButtonsVisible(bool on)
        {
            _buildBtn.SetActive(on);
            _editBtn.SetActive(on);
        }

        void BuildBanner()
        {
            var bg = UiKit.Image(_safe, "EditBanner", UiKit.Panel, true);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(16f, 24f); rt.offsetMax = new Vector2(-16f, 184f);
            var t = UiKit.Text(rt, "Text", "Edit mode · tap a building or decoration to move it", 34, TextAnchor.MiddleLeft);
            t.rectTransform.anchorMin = new Vector2(0f, 0f); t.rectTransform.anchorMax = new Vector2(1f, 1f);
            t.rectTransform.offsetMin = new Vector2(30f, 0f); t.rectTransform.offsetMax = new Vector2(-260f, 0f);
            var done = UiKit.Button(rt, "Done", "Done", 40, UiKit.Accent, () => _hub.SetEditMode(false), out _);
            UiKit.Place((RectTransform)done.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(220f, 116f));
            _banner = bg.gameObject;
            _banner.SetActive(false);
        }

        public void SetBannerVisible(bool on) => _banner.SetActive(on);

        // ------------------------------------------------------------------ catalog

        void BuildCatalog()
        {
            var sheet = UiKit.Image(_safe, "BuildCatalog", UiKit.Panel, true);
            var rt = sheet.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(16f, 0f); rt.offsetMax = new Vector2(-16f, CatalogHeight);
            var title = UiKit.Text(rt, "Title", "Build", 56, TextAnchor.MiddleLeft);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(500f, 80f));
            var close = UiKit.Button(rt, "Close", "✕", 50, UiKit.PanelLight, () => CloseCatalog(), out _);
            UiKit.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(100f, 100f));
            _tabBuildings = UiKit.Button(rt, "TabBuildings", "Buildings", 38, UiKit.Accent, () => SetTab(BuildingCategory.Building), out _);
            UiKit.Place((RectTransform)_tabBuildings.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -130f), new Vector2(330f, 96f));
            _tabDecor = UiKit.Button(rt, "TabDecorations", "Decorations", 38, UiKit.PanelLight, () => SetTab(BuildingCategory.Decoration), out _);
            UiKit.Place((RectTransform)_tabDecor.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(390f, -130f), new Vector2(330f, 96f));

            // horizontal scroll of cards
            var view = UiKit.Image(rt, "Viewport", new Color(0f, 0f, 0f, 0.001f), true);
            var vrt = view.rectTransform;
            vrt.anchorMin = new Vector2(0f, 0f); vrt.anchorMax = new Vector2(1f, 1f);
            vrt.offsetMin = new Vector2(24f, 30f); vrt.offsetMax = new Vector2(-24f, -250f);
            view.gameObject.AddComponent<RectMask2D>();
            _cards = UiKit.Rect("Cards", vrt);
            _cards.anchorMin = new Vector2(0f, 0f); _cards.anchorMax = new Vector2(0f, 1f); _cards.pivot = new Vector2(0f, 0.5f);
            _cards.offsetMin = Vector2.zero; _cards.offsetMax = Vector2.zero;
            var hl = _cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 20f; hl.padding = new RectOffset(8, 8, 8, 8);
            hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            var fit = _cards.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = _cards; scroll.horizontal = true; scroll.vertical = false; scroll.viewport = vrt;
            scroll.movementType = ScrollRect.MovementType.Elastic; scroll.scrollSensitivity = 30f;
            _catalog = sheet.gameObject;
            _catalog.SetActive(false);
        }

        public void OpenCatalog(BuildingCategory? tab = null)
        {
            if (tab.HasValue) _tab = tab.Value;
            _catalog.SetActive(true);
            SetTab(_tab);
        }

        public void CloseCatalog() { if (_catalog != null) _catalog.SetActive(false); _hub.OnCatalogClosed(); }

        public void SetTab(BuildingCategory tab)
        {
            _tab = tab;
            _tabBuildings.image.color = tab == BuildingCategory.Building ? UiKit.Accent : UiKit.PanelLight;
            _tabDecor.image.color = tab == BuildingCategory.Decoration ? UiKit.Accent : UiKit.PanelLight;
            _dir.Service.Catalog.ByCategory(tab, _defs);
            foreach (var c in _cardObjects) Destroy(c);
            _cardObjects.Clear(); _cardButtons.Clear();
            foreach (var d in _defs) _cardObjects.Add(Card(d));
            RefreshCards();
        }

        readonly Dictionary<string, (Text status, Text price, Text action, CanvasGroup group)> _cardParts = new Dictionary<string, (Text, Text, Text, CanvasGroup)>();

        GameObject Card(FarmBuildingDefinition d)
        {
            var bg = UiKit.Image(_cards, "Card_" + d.Id, UiKit.PanelLight, true);
            var le = bg.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 300f; le.minWidth = 300f;
            var group = bg.gameObject.AddComponent<CanvasGroup>();
            var icon = UiKit.Image(bg.transform, "Icon", Color.white);
            if (d.Icon != null) { icon.sprite = d.Icon; icon.preserveAspect = true; }
            else icon.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Check, UiKit.Accent);
            UiKit.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(240f, 200f));
            var name = UiKit.Text(bg.transform, "Name", d.DisplayName, 34, TextAnchor.UpperCenter);
            UiKit.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -218f), new Vector2(280f, 84f));
            name.fontStyle = FontStyle.Bold;
            var size = UiKit.Text(bg.transform, "Size", d.SafeFootprint.x + "×" + d.SafeFootprint.y + (d.NeedsConstruction ? "  ·  " + PlacementText.Time(d.ConstructionSeconds) : "  ·  instant"), 26, TextAnchor.UpperCenter, UiKit.TextMuted);
            UiKit.Place(size.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -296f), new Vector2(280f, 36f));
            var price = UiKit.Text(bg.transform, "Price", d.Price.ToString(), 36, TextAnchor.MiddleLeft, UiKit.Coin);
            UiKit.Place(price.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(-30f, -334f), new Vector2(160f, 48f));
            var coin = UiKit.Image(bg.transform, "Coin", Color.white);
            coin.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Coin, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -334f), new Vector2(46f, 46f));
            var status = UiKit.Text(bg.transform, "Status", "", 26, TextAnchor.UpperCenter, UiKit.TextMuted);
            UiKit.Place(status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(284f, 70f));
            var btn = UiKit.Button(bg.transform, "Action", "Buy", 36, UiKit.Accent, () => OnCard(d), out var action);
            UiKit.Place((RectTransform)btn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(260f, 88f));
            _cardButtons[d.Id] = btn;
            _cardParts[d.Id] = (status, price, action, group);
            return bg.gameObject;
        }

        void OnCard(FarmBuildingDefinition d)
        {
            var svc = _dir.Service;
            bool fromInventory = svc.StoredCount(d.Id) > 0;
            if (!fromInventory)
            {
                var r = svc.CheckPurchaseRules(d);
                if (r != PlacementResult.Ok) { _hub.Toast(PlacementText.Reason(r, d, WoolEconomy.Instance.Coins)); return; }
            }
            _hub.BeginPlacement(d, fromInventory);
        }

        public void RefreshCards()
        {
            var svc = _dir.Service;
            long coins = WoolEconomy.IsReady ? WoolEconomy.Instance.Coins : 0;
            foreach (var d in _defs)
            {
                if (!_cardParts.TryGetValue(d.Id, out var p)) continue;
                int stored = svc.StoredCount(d.Id);
                var rules = svc.CheckPurchaseRules(d);
                string status; string action = "Buy"; bool enabled;
                var running = FindRunning(d.Id);
                if (stored > 0) { status = "In inventory ×" + stored + " · place free"; action = "Place"; enabled = true; }
                else if (running != null) { status = "Building… " + PlacementText.Time(svc.RemainingSeconds(running)) + " left"; enabled = false; }
                else if (rules == PlacementResult.MaxInstances) { status = d.MaxInstances == 1 ? "Built" : "Max " + d.MaxInstances + " reached"; enabled = false; }
                else if (rules == PlacementResult.Ok) { status = d.IsDecoration ? "Appears instantly" : "Builder ready"; enabled = true; }
                else { status = PlacementText.Reason(rules, d, coins); enabled = false; }
                p.status.text = status;
                p.status.color = enabled ? UiKit.TextMuted : new Color(1f, 0.75f, 0.6f, 0.9f);
                p.price.text = stored > 0 ? "0" : d.Price.ToString();
                p.action.text = action;
                p.group.alpha = enabled ? 1f : 0.6f;
                // keep the button clickable when it can explain why (coins / busy) – OnCard shows the reason
                _cardButtons[d.Id].interactable = enabled || rules == PlacementResult.NotEnoughCoins || rules == PlacementResult.ConstructionBusy;
            }
        }

        BuildingInstanceState FindRunning(string typeId)
        {
            foreach (var b in _dir.Service.State.instances) if (b.typeId == typeId && b.status == BuildingStatus.UnderConstruction) return b;
            return null;
        }

        // ------------------------------------------------------------------ placement bar

        void BuildBar()
        {
            var bg = UiKit.Image(_safe, "PlacementBar", UiKit.Panel, true);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(16f, 24f); rt.offsetMax = new Vector2(-16f, 284f);
            _barHint = UiKit.Text(rt, "Hint", "", 34, TextAnchor.MiddleCenter);
            _barHint.rectTransform.anchorMin = new Vector2(0f, 1f); _barHint.rectTransform.anchorMax = new Vector2(1f, 1f);
            _barHint.rectTransform.pivot = new Vector2(0.5f, 1f);
            _barHint.rectTransform.offsetMin = new Vector2(24f, -84f); _barHint.rectTransform.offsetMax = new Vector2(-24f, -14f);
            var cancel = UiKit.Button(rt, "Cancel", "Cancel", 40, UiKit.PanelLight, () => _hub.CancelPlacement(), out _);
            UiKit.Place((RectTransform)cancel.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(300f, 130f));
            _confirm = UiKit.Button(rt, "Confirm", "Place", 42, UiKit.Accent, () => _hub.ConfirmPlacement(), out _confirmLabel);
            UiKit.Place((RectTransform)_confirm.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(300f, 130f));
            _confirmLabel.fontStyle = FontStyle.Bold;
            _barPrice = UiKit.Text(rt, "Price", "", 44, TextAnchor.MiddleLeft, UiKit.Coin);
            UiKit.Place(_barPrice.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(-10f, 54f), new Vector2(200f, 70f));
            _barCoin = UiKit.Image(rt, "Coin", Color.white);
            _barCoin.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Coin, UiKit.Coin);
            UiKit.Place(_barCoin.rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 58f), new Vector2(62f, 62f));
            _bar = bg.gameObject;
            _bar.SetActive(false);
        }

        void RefreshBar()
        {
            var p = _dir.Placement;
            bool on = p.IsActive;
            if (_bar.activeSelf != on) _bar.SetActive(on);
            if (!on) return;
            long coins = WoolEconomy.IsReady ? WoolEconomy.Instance.Coins : 0;
            bool ok = p.Result == PlacementResult.Ok;
            string verb = p.Mode == PlacementMode.Move ? "Drag to move" : "Drag to place";
            _barHint.text = ok ? verb + " · " + PlacementText.Reason(p.Result, p.Definition, coins) : PlacementText.Reason(p.Result, p.Definition, coins);
            _barHint.color = ok ? UiKit.TextColor : new Color(1f, 0.62f, 0.5f, 1f);
            bool showPrice = p.Mode == PlacementMode.PlaceNew;
            _barPrice.gameObject.SetActive(showPrice); _barCoin.gameObject.SetActive(showPrice);
            _barPrice.text = p.Price.ToString();
            _confirmLabel.text = p.Mode == PlacementMode.Move ? "Move" : p.Mode == PlacementMode.PlaceNew && p.Definition != null && p.Definition.NeedsConstruction ? "Build" : "Place";
            _confirm.interactable = ok;
        }

        // ------------------------------------------------------------------ construction chip

        void BuildChip()
        {
            var b = UiKit.Button(_safe, "ConstructionChip", "", 10, UiKit.Panel, () => _hub.FocusActiveConstruction(), out var lbl);
            lbl.gameObject.SetActive(false);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(16f, -236f); rt.offsetMax = new Vector2(-16f, -150f);
            _chipText = UiKit.Text(rt, "Text", "", 32, TextAnchor.MiddleLeft);
            _chipText.rectTransform.anchorMin = Vector2.zero; _chipText.rectTransform.anchorMax = Vector2.one;
            _chipText.rectTransform.offsetMin = new Vector2(28f, 0f); _chipText.rectTransform.offsetMax = new Vector2(-300f, 0f);
            var back = UiKit.Image(rt, "Bar", new Color(1f, 1f, 1f, 0.15f));
            UiKit.Place(back.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28f, 0f), new Vector2(250f, 22f));
            _chipFill = UiKit.Image(back.transform, "Fill", UiKit.Accent);
            _chipFill.rectTransform.anchorMin = Vector2.zero; _chipFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            _chipFill.rectTransform.offsetMin = _chipFill.rectTransform.offsetMax = Vector2.zero;
            _chip = b.gameObject;
            _chip.SetActive(false);
        }

        void RefreshChip(bool force)
        {
            var svc = _dir.Service;
            var b = svc.ActiveConstruction;
            bool on = b != null;
            if (_chip.activeSelf != on) _chip.SetActive(on);
            if (!on) { _chipShownSecond = -1; return; }
            int sec = Mathf.CeilToInt((float)svc.RemainingSeconds(b));
            float fill = svc.Progress(b);
            if (force || sec != _chipShownSecond)
            {
                _chipShownSecond = sec;
                var def = svc.Definition(b.typeId);
                _chipText.text = "Building " + (def != null ? def.DisplayName : "") + " · " + PlacementText.Time(sec);
            }
            if (Mathf.Abs(fill - _chipShownFill) > 0.004f)
            {
                _chipShownFill = fill;
                _chipFill.rectTransform.anchorMax = new Vector2(fill, 1f);
            }
        }

        void Update()
        {
            if (_dir == null || _dir.Service == null) return;
            if (_chip.activeSelf || _dir.Service.ActiveConstructionCount > 0) RefreshChip(false);
        }

        // QA
        public void QA_ClickCard(string typeId) { foreach (var d in _defs) if (d.Id == typeId) { OnCard(d); return; } }
        public bool QA_CardInteractable(string typeId) => _cardButtons.TryGetValue(typeId, out var b) && b.interactable;
        public string QA_CardStatus(string typeId) => _cardParts.TryGetValue(typeId, out var p) ? p.status.text : null;
    }
}