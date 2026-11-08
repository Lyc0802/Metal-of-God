using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimpleOutline
{
    // A separate ownership channel: never sets _Outlined, renderer materials or the
    // legacy OutlineTarget.ActiveTargets set, so Bronze OFF cannot erase a metal outline.
    /// <summary>
    /// 保存單一觀察者與目標的 Outline 資料，渲染前重新檢查即時偵測條件。
    /// </summary>
    public sealed class BronzeOutlineEntry
    {
        public readonly BronzeDetection Observer;
        public readonly IMetalAbilityStatus Target;
        public readonly Renderer[] Renderers;
        public BronzeOutlineEntry(BronzeDetection observer, IMetalAbilityStatus target)
        {
            Observer = observer;
            Target = target;
            Renderers = target.MetalOwner.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer
                || r is SkinnedMeshRenderer).ToArray();
        }

        public bool IsVisibleTo(Camera camera) => Observer && Observer.ShowOutlines && Observer.ObserverCamera == camera
            && Observer.CanDetect(Target);
    }

    /// <summary>
    /// Bronze 專用的視覺登錄表，不改動一般金屬 Outline 的登錄與材質狀態。
    /// </summary>
    public static class BronzeOutlineRegistry
    {
        private static readonly List<BronzeOutlineEntry> entries = new List<BronzeOutlineEntry>();
        public static IReadOnlyList<BronzeOutlineEntry> Entries => entries;

        public static void Add(BronzeOutlineEntry entry)
        {
            if (!entries.Contains(entry))
            {
                entries.Add(entry);
            }
        }

        public static void Remove(BronzeOutlineEntry entry)
        {
            entries.Remove(entry);
        }

        public static bool HasTargets(Camera camera)
        {
            foreach (var e in entries)
            {
                if (e.IsVisibleTo(camera))
                {
                    return true;
                }
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            entries.Clear();
        }
    }
}
