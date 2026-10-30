using System;

namespace CampusNav.Routing
{
    /// <summary>
    /// Binary min-heap of (item, key) pairs. Duplicate items are allowed ("lazy" decrease-key:
    /// push again and skip stale entries when popped).
    ///
    /// Exists because System.Collections.Generic.PriorityQueue is a .NET 6+ type and is NOT
    /// available in Unity's .NET Standard 2.1 / .NET Framework 4.8 profiles.
    /// </summary>
    public sealed class MinHeap
    {
        int[] items;
        float[] keys;
        int count;

        public MinHeap(int capacity = 64)
        {
            if (capacity < 1) capacity = 1;
            items = new int[capacity];
            keys = new float[capacity];
        }

        public int Count => count;

        public void Clear() => count = 0;

        public void Push(int item, float key)
        {
            if (count == items.Length)
            {
                Array.Resize(ref items, count * 2);
                Array.Resize(ref keys, count * 2);
            }
            int i = count++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (keys[parent] <= key) break;
                items[i] = items[parent];
                keys[i] = keys[parent];
                i = parent;
            }
            items[i] = item;
            keys[i] = key;
        }

        public float PeekKey()
        {
            if (count == 0) throw new InvalidOperationException("Heap is empty.");
            return keys[0];
        }

        public int Pop(out float key)
        {
            if (count == 0) throw new InvalidOperationException("Heap is empty.");
            int top = items[0];
            key = keys[0];
            count--;
            if (count > 0)
            {
                int item = items[count];
                float k = keys[count];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= count) break;
                    if (child + 1 < count && keys[child + 1] < keys[child]) child++;
                    if (keys[child] >= k) break;
                    items[i] = items[child];
                    keys[i] = keys[child];
                    i = child;
                }
                items[i] = item;
                keys[i] = k;
            }
            return top;
        }
    }
}
