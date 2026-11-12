using System;
using System.Collections.Generic;
using Letsail.Core;
using Letsail.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Letsail.UI
{
    /// <summary>
    /// 탭으로 나뉜 설정 메뉴 (게임 / 화면 / 소리 / 조작). 바꾸는 즉시 적용되고 저장된다.
    /// 각 탭의 줄은 열 때마다 템플릿으로 새로 만든다. 항목을 추가하려면 Build… 메서드에 한 줄 넣으면 된다.
    /// Esc 로 열고 닫는다 (메뉴의 설정 버튼으로도 열 수 있다). 열려 있는 동안은 커서가 풀린다.
    /// </summary>
    public sealed class SettingsMenuUI : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] RectTransform tabBar;
        [SerializeField] RectTransform pageContainer;
        [SerializeField] Button closeButton;
        [SerializeField, Tooltip("아래쪽 안내 문구. 탭에 따라 내용이 바뀐다")] Text hintText;

        [Header("템플릿 (비활성 상태로 둔다)")]
        [SerializeField] Button tabTemplate;
        [SerializeField] GameObject dropdownRowTemplate;
        [SerializeField] GameObject sliderRowTemplate;
        [SerializeField] GameObject toggleRowTemplate;
        [SerializeField] GameObject infoRowTemplate;

        static readonly Color TabOn = new Color(0.95f, 0.62f, 0.2f);
        static readonly Color TabOff = new Color(1f, 1f, 1f, 0.15f);
        static readonly Color TabTextOn = new Color(0.12f, 0.08f, 0.04f);
        static readonly Color TabTextOff = new Color(0.98f, 0.96f, 0.9f);

        readonly List<(Button tab, RectTransform page)> tabs = new List<(Button, RectTransform)>();
        int currentTab;

        public static SettingsMenuUI Instance { get; private set; }
        public bool IsOpen => panel.activeSelf;

        void Awake()
        {
            Instance = this;
            closeButton.onClick.AddListener(Close);
            foreach (var t in new[] { tabTemplate.gameObject, dropdownRowTemplate, sliderRowTemplate, toggleRowTemplate, infoRowTemplate })
            {
                t.SetActive(false);
            }
            panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void OnDisable()
        {
            PlayerCamera.HoldCursor(this, false);
        }

        void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return;
            }
            if (IsOpen)
            {
                Close();
            }
            // 옷장의 Esc 는 옷장을 닫는 데 쓴다 (같은 프레임에 설정까지 열리지 않게).
            else if (!WardrobeUI.UsedEscapeThisFrame)
            {
                Open();
            }
        }

        public void Open()
        {
            Build();
            ShowTab(currentTab);
            panel.SetActive(true);
            PlayerCamera.HoldCursor(this, true);
        }

        /// <summary>닫는다. 플레이 중이면 바로 다시 조작할 수 있다.</summary>
        public void Close()
        {
            panel.SetActive(false);
            PlayerCamera.HoldCursor(this, false);
        }

        // ---------------- 탭 구성 ----------------

        void Build()
        {
            foreach (var (tab, page) in tabs)
            {
                Destroy(tab.gameObject);
                Destroy(page.gameObject);
            }
            tabs.Clear();

            BuildGame(AddTab("게임"));
            BuildDisplay(AddTab("화면"));
            BuildSound(AddTab("소리"));
            BuildControls(AddTab("조작"));

        }

        void BuildGame(RectTransform page)
        {
            float range = GameSettings.MaxMouseSensitivity - GameSettings.MinMouseSensitivity;
            AddSlider(page, "마우스 감도", 1f, 100f, true,
                1f + (GameSettings.MouseSensitivity - GameSettings.MinMouseSensitivity) / range * 99f,
                v => GameSettings.MouseSensitivity = GameSettings.MinMouseSensitivity + (v - 1f) / 99f * range,
                v => Mathf.RoundToInt(v).ToString());
            AddToggle(page, "마우스 상하 반전", GameSettings.InvertY, v => GameSettings.InvertY = v);
            AddDropdown(page, "웅크리기", new[] { "누르고 있기", "한 번 눌러 전환" }, GameSettings.ToggleCrouch ? 1 : 0,
                i => GameSettings.ToggleCrouch = i == 1);
        }

        void BuildDisplay(RectTransform page)
        {
            List<Vector2Int> resolutions = GameSettings.GetAvailableResolutions();
            var names = new string[resolutions.Count];
            int selected = resolutions.Count - 1;
            for (int i = 0; i < resolutions.Count; i++)
            {
                names[i] = $"{resolutions[i].x} × {resolutions[i].y}";
                if (resolutions[i] == GameSettings.Resolution)
                {
                    selected = i;
                }
            }
            AddDropdown(page, "해상도", names, selected, i => GameSettings.Resolution = resolutions[i]);

            var modeNames = new string[GameSettings.ScreenModes.Length];
            int modeIndex = 0;
            for (int i = 0; i < modeNames.Length; i++)
            {
                modeNames[i] = GameSettings.GetScreenModeName(GameSettings.ScreenModes[i]);
                if (GameSettings.ScreenModes[i] == GameSettings.ScreenMode)
                {
                    modeIndex = i;
                }
            }
            AddDropdown(page, "화면 모드", modeNames, modeIndex, i => GameSettings.ScreenMode = GameSettings.ScreenModes[i]);

            AddDropdown(page, "그래픽 품질", QualitySettings.names, GameSettings.QualityLevel, i => GameSettings.QualityLevel = i);
            AddToggle(page, "수직 동기화", GameSettings.VSync, v => GameSettings.VSync = v);

            var limits = GameSettings.FrameRateLimits;
            var limitNames = new string[limits.Length];
            int limitIndex = Array.IndexOf(limits, GameSettings.FrameRateLimit);
            for (int i = 0; i < limits.Length; i++)
            {
                limitNames[i] = limits[i] > 0 ? $"{limits[i]} FPS" : "제한 없음";
            }
            AddDropdown(page, "최대 프레임", limitNames, limitIndex < 0 ? limits.Length - 1 : limitIndex, i => GameSettings.FrameRateLimit = limits[i]);

        }

        void BuildSound(RectTransform page)
        {
            AddVolume(page, "전체 볼륨", GameSettings.MasterVolume, v => GameSettings.MasterVolume = v);
            AddVolume(page, "음악", GameSettings.MusicVolume, v => GameSettings.MusicVolume = v);
            AddVolume(page, "효과음", GameSettings.SfxVolume, v => GameSettings.SfxVolume = v);
        }

        void BuildControls(RectTransform page)
        {
            AddInfo(page, "이동", Binding("Move"));
            AddInfo(page, "달리기", Binding("Sprint"));
            AddInfo(page, "점프", Binding("Jump"));
            AddInfo(page, "웅크리기", Binding("Crouch"));
            AddInfo(page, "상호작용", Binding("Interact"));
            AddInfo(page, "카메라", "마우스");
            AddInfo(page, "선원 · 참가 코드", "Tab (누르고 있기)");
            AddInfo(page, "설정", "Esc");
            AddInfo(page, "", "키 변경 기능은 나중에 추가돼요.");
        }

        // ---------------- 줄 만들기 ----------------

        RectTransform AddTab(string title)
        {
            Button tab = Instantiate(tabTemplate, tabBar);
            tab.gameObject.SetActive(true);
            tab.name = "Tab_" + title;
            tab.GetComponentInChildren<Text>().text = title;
            int index = tabs.Count;
            tab.onClick.AddListener(() => ShowTab(index));

            var pageGo = new GameObject("Page_" + title, typeof(RectTransform));
            var page = (RectTransform)pageGo.transform;
            page.SetParent(pageContainer, false);
            page.anchorMin = new Vector2(0f, 1f);
            page.anchorMax = new Vector2(1f, 1f);
            page.pivot = new Vector2(0.5f, 1f);
            page.offsetMin = page.offsetMax = Vector2.zero;
            var layout = pageGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            pageGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tabs.Add((tab, page));
            return page;
        }

        void ShowTab(int index)
        {
            currentTab = Mathf.Clamp(index, 0, tabs.Count - 1);
            for (int i = 0; i < tabs.Count; i++)
            {
                bool on = i == currentTab;
                tabs[i].page.gameObject.SetActive(on);
                tabs[i].tab.GetComponent<Image>().color = on ? TabOn : TabOff;
                tabs[i].tab.GetComponentInChildren<Text>().color = on ? TabTextOn : TabTextOff;
            }
            if (hintText != null)
            {
                // 화면 탭은 에디터에서 해상도·화면 모드가 바뀌지 않는다는 걸 알려 준다.
                bool displayTab = currentTab == 1;
                hintText.text = displayTab && Application.isEditor
                    ? "에디터에서는 해상도와 화면 모드가 바뀌지 않아요 (빌드한 게임에서 적용)"
                    : "바꾸면 바로 적용되고 저장돼요 · Esc: 닫기";
            }
        }

        GameObject AddRow(GameObject template, RectTransform page, string label)
        {
            GameObject row = Instantiate(template, page);
            row.SetActive(true);
            row.name = "Row_" + label;
            row.transform.Find("Label").GetComponent<Text>().text = label;
            return row;
        }

        void AddDropdown(RectTransform page, string label, IReadOnlyList<string> options, int selected, Action<int> onChange)
        {
            var dropdown = AddRow(dropdownRowTemplate, page, label).GetComponentInChildren<Dropdown>();
            dropdown.ClearOptions();
            var list = new List<Dropdown.OptionData>();
            foreach (var o in options)
            {
                list.Add(new Dropdown.OptionData(o));
            }
            dropdown.AddOptions(list);
            dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, list.Count - 1));
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(i => onChange(i));
        }

        void AddSlider(RectTransform page, string label, float min, float max, bool wholeNumbers, float value, Action<float> onChange, Func<float, string> format)
        {
            GameObject row = AddRow(sliderRowTemplate, page, label);
            var slider = row.GetComponentInChildren<Slider>();
            var valueText = row.transform.Find("Value").GetComponent<Text>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.SetValueWithoutNotify(value);
            valueText.text = format(value);
            slider.onValueChanged.AddListener(v =>
            {
                onChange(v);
                valueText.text = format(v);
            });
        }

        void AddVolume(RectTransform page, string label, float value01, Action<float> onChange)
        {
            AddSlider(page, label, 0f, 100f, true, value01 * 100f, v => onChange(v / 100f), v => $"{Mathf.RoundToInt(v)}%");
        }

        void AddToggle(RectTransform page, string label, bool value, Action<bool> onChange)
        {
            var toggle = AddRow(toggleRowTemplate, page, label).GetComponentInChildren<Toggle>();
            toggle.SetIsOnWithoutNotify(value);
            toggle.onValueChanged.AddListener(v => onChange(v));
        }

        void AddInfo(RectTransform page, string label, string value)
        {
            GameObject row = AddRow(infoRowTemplate, page, label);
            row.transform.Find("Value").GetComponent<Text>().text = value;
            if (string.IsNullOrEmpty(label))
            {
                // 설명 한 줄: 이름 칸 없이 왼쪽부터 쓴다.
                row.transform.Find("Label").gameObject.SetActive(false);
                var text = row.transform.Find("Value").GetComponent<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.fontSize = 18;
                text.color = new Color(text.color.r, text.color.g, text.color.b, 0.7f);
            }
        }

        static string Binding(string action)
        {
            var a = InputSystem.actions?.FindAction("Player/" + action);
            if (a == null)
            {
                return "-";
            }
            var keys = new List<string>();
            for (int i = 0; i < a.bindings.Count; i++)
            {
                var b = a.bindings[i];
                if (b.isComposite || !(b.effectivePath.Contains("<Keyboard>") || b.effectivePath.Contains("<Mouse>")))
                {
                    continue;
                }
                string key = a.GetBindingDisplayString(i);
                if (!string.IsNullOrEmpty(key) && !keys.Contains(key))
                {
                    keys.Add(key);
                }
            }
            if (action == "Move" && keys.Count >= 4)
            {
                return "W A S D";
            }
            return keys.Count > 0 ? string.Join(" / ", keys) : "-";
        }
    }
}
