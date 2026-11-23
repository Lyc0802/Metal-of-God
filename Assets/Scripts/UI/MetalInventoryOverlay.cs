using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 左上角的「METALS」面板：每個金屬一個開關按鈕，顯示目前的開 / 關與剩餘量，狀態直接讀玩家的 PlayerMetalInventory。
/// （原本驅動這個面板的腳本遺失了，面板只剩建立時烘進去的靜態文字，所以顯示的開關狀態與實際不符。）
/// 面板的子物件命名為「Iron Toggle」「Tin Toggle」… 每個底下有 Label（文字）、State Bar（狀態條）。
/// </summary>
public class MetalInventoryOverlay : MonoBehaviour
{
    private static readonly Color OnBackground = new Color(.12f, .32f, .26f, 1f);
    private static readonly Color OffBackground = new Color(.12f, .15f, .2f, 1f);
    private static readonly Color OnBar = new Color(.25f, 1f, .72f, 1f);
    private static readonly Color OffBar = new Color(.4f, .45f, .52f, 1f);

    private sealed class Row
    {
        public MetalType Type;
        public Image Background, Bar;
        public TMP_Text Label;
        public string Name;
    }

    private readonly List<Row> rows = new List<Row>();
    private PlayerMetalInventory inventory;

    private void Awake()
    {
        foreach (MetalType type in System.Enum.GetValues(typeof(MetalType)))
        {
            if (!MetalAbilitySignals.IsRealMetal(type))
            {
                continue;
            }

            var toggle = Find(transform, type + " Toggle");
            if (!toggle)
            {
                continue;
            }

            var row = new Row
            {
                Type = type,
                Name = type.ToString().ToUpperInvariant(),
                Background = toggle.GetComponent<Image>(),
                Label = toggle.GetComponentInChildren<TMP_Text>(true)
            };
            var bar = Find(toggle, "State Bar");
            row.Bar = bar ? bar.GetComponent<Image>() : null;
            rows.Add(row);
            var button = toggle.GetComponent<Button>();
            if (button)
            {
                var captured = type;
                button.onClick.AddListener(() => Toggle(captured));
            }
        }
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }

        return null;
    }

    private void Toggle(MetalType type)
    {
        var reserve = inventory ? inventory.Get(type) : null;
        if (reserve != null)
        {
            reserve.IsEnabled = !reserve.IsEnabled;
        }
    }

    private void Update()
    {
        if (!inventory)
        {
            inventory = FindFirstObjectByType<PlayerMetalInventory>();
            if (!inventory)
            {
                return;
            }
        }

        foreach (var row in rows)
        {
            var reserve = inventory.Get(row.Type);
            if (reserve == null)
            {
                continue;
            }

            bool on = reserve.IsEnabled;
            if (row.Label)
            {
                row.Label.text = row.Name + "   " + (on ? "ON" : "OFF") + "\n" + Mathf.CeilToInt(reserve.CurrentAmount) + " / " + Mathf.CeilToInt(reserve.MaxAmount);
            }

            if (row.Background)
            {
                row.Background.color = on ? OnBackground : OffBackground;
            }

            if (row.Bar)
            {
                row.Bar.color = on ? OnBar : OffBar;
            }
        }
    }
}
