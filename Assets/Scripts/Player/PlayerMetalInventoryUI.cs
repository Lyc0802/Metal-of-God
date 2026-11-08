using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 將玩家金屬庫存呈現在 UI，並把介面操作回寫到同一份庫存。
/// </summary>
public class PlayerMetalInventoryUI : MonoBehaviour
{
    [Serializable]
    public class MetalButton
    {
        public MetalType Type;
        public Button Button;
        public Text Label;
        public Image StatusBar;
        [NonSerialized]
        public UnityAction Click;
        [NonSerialized]
        public bool Initialized, Enabled;
        [NonSerialized]
        public float Amount, Maximum;
    }

    public PlayerMetalInventory Inventory;
    public MetalButton[] Buttons = new MetalButton[0];
    public Color EnabledColor = new Color(.12f, .32f, .26f, 1);
    public Color DisabledColor = new Color(.12f, .15f, .2f, 1);
    public Color EnabledAccent = new Color(.25f, 1, .72f, 1);
    public Color DisabledAccent = new Color(.4f, .45f, .52f, 1);
    private void OnEnable()
    {
        if (!Inventory)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player)
            {
                Inventory = player.GetComponent<PlayerMetalInventory>();
            }
        }

        foreach (var entry in Buttons)
        {
            if (entry == null || !entry.Button)
            {
                continue;
            }

            var type = entry.Type;
            entry.Click = () => ToggleMetal(type);
            entry.Button.onClick.AddListener(entry.Click);
        }

        RefreshUI();
    }

    private void OnDisable()
    {
        foreach (var entry in Buttons)
        {
            if (entry != null && entry.Button && entry.Click != null)
            {
                entry.Button.onClick.RemoveListener(entry.Click);
                entry.Click = null;
            }
        }
    }

    public void ToggleMetal(MetalType type)
    {
        if (!Inventory)
        {
            return;
        }

        var reserve = Inventory.Get(type);
        if (reserve == null)
        {
            return;
        }

        reserve.IsEnabled = !reserve.IsEnabled;
        RefreshUI();
    }

    public void RefreshUI(bool force = true)
    {
        foreach (var entry in Buttons)
        {
            if (entry == null || !entry.Button)
            {
                continue;
            }

            var reserve = Inventory ? Inventory.Get(entry.Type) : null;
            entry.Button.interactable = reserve != null;
            bool enabled = reserve != null && reserve.IsEnabled;
            float amount = reserve != null ? reserve.CurrentAmount : 0, maximum = reserve != null
                ? reserve.MaxAmount
                : 0;
            if (!force && entry.Initialized && entry.Enabled == enabled && entry.Amount == amount
                && entry.Maximum == maximum)
            {
                continue;
            }

            entry.Initialized = true;
            entry.Enabled = enabled;
            entry.Amount = amount;
            entry.Maximum = maximum;
            string state = reserve == null ? "N/A" : !enabled ? "OFF" : amount > 0 ? "ON" : "EMPTY";
            if (entry.Label)
            {
                entry.Label.text = entry.Type.ToString().ToUpperInvariant() + "   " + state + "\n" + amount.ToString("0.#") + " / " + maximum.ToString("0.#");
            }

            if (entry.Button.image)
            {
                entry.Button.image.color = enabled ? EnabledColor : DisabledColor;
            }

            if (entry.StatusBar)
            {
                entry.StatusBar.color = enabled
                    ? (amount > 0 ? EnabledAccent : new Color(1, .7f, .25f))
                    : DisabledAccent;
            }
        }
    }

    private void Update()
    {
        RefreshUI(false);
    }
}
