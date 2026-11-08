using System.Collections.Generic;
using UnityEngine;

namespace SimpleOutline
{
    /// <summary>
    /// 一般 Outline 的目標與顏色設定，透過每個 Renderer 的屬性區塊控制顯示。
    /// </summary>
    [DisallowMultipleComponent]
    public class OutlineTarget : MonoBehaviour
    {
        internal static readonly HashSet<OutlineTarget> ActiveTargets = new();
        [Header("Outline Appearance")]
        public bool UseCustomOutlineColor;
        public Color OutlineColor = Color.white;
        public void SetOutlineColor(Color color)
        {
            OutlineColor = color;
            UseCustomOutlineColor = true;
        }

        public virtual Color GetOutlineColor(Color defaultColor)
        {
            return UseCustomOutlineColor ? OutlineColor : defaultColor;
        }

        public static readonly int OutlineId = Shader.PropertyToID("_Outlined");
        protected Renderer[] _renderers;
        protected MaterialPropertyBlock _properties;
        public Renderer[] Renderers => _renderers;
        public bool Outlined { get; private set; }
        public Camera Camera { get; private set; }

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _properties = new MaterialPropertyBlock();
        }

        // Omit camera for all game cameras, or pass one camera to scope the outline.
        public void SetOutlined(bool outlined, Camera camera = null)
        {
            Outlined = outlined;
            Camera = camera;
            if (outlined)
            {
                ActiveTargets.Add(this);
            }
            else
            {
                ActiveTargets.Remove(this);
            }

            foreach (var renderer in _renderers)
            {
                renderer.GetPropertyBlock(_properties);
                _properties.SetFloat(OutlineId, outlined ? 1 : 0);
                renderer.SetPropertyBlock(_properties);
            }
        }

        private void OnDisable() => SetOutlined(false);
    }
}
