using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ImprovedBeamSearch
{
    public class ImprovedBeamSearch_Parallel_Optimized : BaseSolver
    {
        private readonly int beamWidth;
        private readonly int maxDepth;
        private readonly int minN;
        private readonly int? maxN;
        private readonly bool useHashPruning;
        private readonly int K_noImprove;
        private readonly int backupPoolSize;
        private readonly int restartBeamWidth;
        private readonly int maxDegreeOfParallelism;

        private int uidCounter = 0;

        public ImprovedBeamSearch_Parallel_Optimized(
            int beamWidth = 150,
            int maxDepth = 150,
            int maxOperations = 5000,
            double timeout = 400.0,
            int minN = 2,
            int? maxN = null,
            bool useHashPruning = true,
            int K_noImprove = 8,
            int backupPoolSize = 8000,
            int restartBeamWidth = 150,
            int maxDegreeOfParallelism = 16
        ) : base(maxOperations, timeout)
        {
            this.beamWidth = Math.Max(1, beamWidth);
            this.maxDepth = Math.Max(1, maxDepth);
            this.minN = Math.Max(2, minN);
            this.maxN = maxN;
            this.useHashPruning = useHashPruning;

            this.K_noImprove = Math.Max(1, K_noImprove);
            this.backupPoolSize = Math.Max(0, backupPoolSize);
            this.restartBeamWidth = Math.Max(1, restartBeamWidth);
            this.maxDegreeOfParallelism = (maxDegreeOfParallelism <= 0) ? Environment.ProcessorCount : maxDegreeOfParallelism;
        }

        // Minimal State class (in case you don't have one)
        public class State
        {
            public Field Field { get; set; }
            public List<Operation> Operations { get; set; }
            public int Pairs { get; set; }
            public int OpCount { get { return Operations?.Count ?? 0; } }
            public int Score { get { return Pairs * 100 - OpCount; } }

            public State(Field field, List<Operation> ops)
            {
                Field = field;
                Operations = ops ?? new List<Operation>();
                Pairs = field.CountPairs();
            }
        }

        // Node for pool set
        private class PoolNode
        {
            public int Score;
            public int Uid;
            public State S;
            public PoolNode(int score, int uid, State s) { Score = score; Uid = uid; S = s; }
        }

        // Comparer for pool sorted desc by score then uid asc
        private class PoolComparer : IComparer<PoolNode>
        {
            public int Compare(PoolNode a, PoolNode b)
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;
                int c = b.Score.CompareTo(a.Score); // desc
                if (c != 0) return c;
                return a.Uid.CompareTo(b.Uid);
            }
        }

        // small min-heap for top-K (by score)
        private class SmallMinHeap
        {
            private readonly List<(State s, int score)> data;
            private readonly int capacity;
            public SmallMinHeap(int capacity)
            {
                this.capacity = Math.Max(1, capacity);
                data = new List<(State, int)>(this.capacity);
            }
            public int Count { get { return data.Count; } }
            private void Swap(int i, int j) { var t = data[i]; data[i] = data[j]; data[j] = t; }
            private void HeapifyUp(int idx)
            {
                while (idx > 0)
                {
                    int p = (idx - 1) >> 1;
                    if (data[idx].score >= data[p].score) break;
                    Swap(idx, p);
                    idx = p;
                }
            }
            private void HeapifyDown(int idx)
            {
                int n = data.Count;
                while (true)
                {
                    int l = idx * 2 + 1;
                    int r = l + 1;
                    int smallest = idx;
                    if (l < n && data[l].score < data[smallest].score) smallest = l;
                    if (r < n && data[r].score < data[smallest].score) smallest = r;
                    if (smallest == idx) break;
                    Swap(idx, smallest);
                    idx = smallest;
                }
            }
            public void Offer((State s, int score) item)
            {
                if (data.Count < capacity)
                {
                    data.Add(item);
                    HeapifyUp(data.Count - 1);
                    return;
                }
                if (item.score <= data[0].score) return;
                data[0] = item;
                HeapifyDown(0);
            }
            public List<(State s, int score)> ToListDescending()
            {
                var arr = data.ToArray();
                Array.Sort(arr, (a, b) => b.score.CompareTo(a.score));
                return new List<(State s, int score)>(arr);
            }
        }

        // 64-bit hash for field (FNV-like)
        private ulong HashFieldU64(Field f)
        {
            ulong h = 1469598103934665603UL;
            int[,] arr = f.ToArray();
            int n = f.Size;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    h ^= (ulong)arr[i, j];
                    h *= 1099511628211UL;
                }
            }
            return h;
        }

        public override List<Operation> Solve(Field field)
        {
            ResetTimer();
            Log($"Start OptimizedParallel - size={field.Size}, beamWidth={beamWidth}, maxDepth={maxDepth}, threads={maxDegreeOfParallelism}, timeout={Timeout}s");

            int maxSubSize = maxN ?? field.Size;
            var initialState = new State(field.Copy(), new List<Operation>());
            var beam = new List<State> { initialState };
            State best = initialState;

            int maxPairs = field.GetMaxPossiblePairs();
            int initialPairs = initialState.Pairs;
            Log($"Initial pairs: {initialPairs}/{maxPairs}");

            var visited = new ConcurrentDictionary<ulong, int>();
            if (useHashPruning)
            {
                int ih = ComputeHeuristic(initialState.Field, initialState.OpCount);
                visited[HashFieldU64(initialState.Field)] = ih;
            }

            var pool = new SortedSet<PoolNode>(new PoolComparer());
            var poolLock = new object();

            int depthWithoutImprovement = 0;

            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (IsTimeout())
                {
                    Log("Timeout reached.");
                    break;
                }

                if (beam.Count == 0)
                {
                    Log("Beam empty - stopping.");
                    break;
                }

                // partition indices for workload
                var partitioner = System.Collections.Concurrent.Partitioner.Create(0, beam.Count, Math.Max(1, beam.Count / (maxDegreeOfParallelism * 2)));

                var localHeaps = new ConcurrentBag<SmallMinHeap>();
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };

                Parallel.ForEach(partitioner, parallelOptions, range =>
                {
                    var localHeap = new SmallMinHeap(beamWidth * 2); // keep some extra
                    for (int idx = range.Item1; idx < range.Item2; idx++)
                    {
                        var st = beam[idx];
                        if (st.OpCount >= MaxOperations) continue;
                        if (st.Pairs == maxPairs)
                        {
                            localHeap.Offer((st, ComputeHeuristic(st.Field, st.OpCount)));
                            continue;
                        }

                        int localMaxSub = Math.Min(maxSubSize, field.Size);

                        for (int n = localMaxSub; n >= minN; n--)
                        {
                            for (int y = 0; y <= field.Size - n; y++)
                            {
                                for (int x = 0; x <= field.Size - n; x++)
                                {
                                    var op = new Operation(x, y, n);
                                    var newField = st.Field.Copy();
                                    newField.ApplyOperation(op);
                                    var newOps = new List<Operation>(st.Operations) { op };
                                    var newState = new State(newField, newOps);

                                    int heu = ComputeHeuristicFast(newField, newState.OpCount);

                                    if (useHashPruning)
                                    {
                                        var hh = HashFieldU64(newField);
                                        int prev;
                                        if (visited.TryGetValue(hh, out prev) && prev >= heu) continue;
                                        visited.AddOrUpdate(hh, heu, (k, old) => Math.Max(old, heu));
                                    }

                                    localHeap.Offer((newState, heu));
                                }
                            }
                        }
                    }
                    localHeaps.Add(localHeap);
                }); // parallel expansion end

                // merge local heaps into a global heap (streaming top-K)
                var globalHeap = new SmallMinHeap(beamWidth + backupPoolSize);
                foreach (var lh in localHeaps)
                {
                    var list = lh.ToListDescending(); // descending
                    foreach (var item in list)
                        globalHeap.Offer(item);
                }

                var finalList = globalHeap.ToListDescending(); // descending order by score

                if (finalList.Count == 0)
                {
                    Log("No candidates generated - stopping.");
                    break;
                }

                // next beam
                var newBeam = finalList.Take(beamWidth).Select(t => t.s).ToList();

                // rest -> pool
                if (backupPoolSize > 0 && finalList.Count > beamWidth)
                {
                    lock (poolLock)
                    {
                        for (int i = beamWidth; i < finalList.Count; i++)
                        {
                            int uid = Interlocked.Increment(ref uidCounter);
                            var it = finalList[i];
                            pool.Add(new PoolNode(it.score, uid, it.s));
                            if (pool.Count > backupPoolSize)
                            {
                                var worst = pool.Last();
                                pool.Remove(worst);
                            }
                        }
                    }
                }

                beam = newBeam;

                if (beam.Count > 0)
                {
                    var top = beam[0];
                    if (top.Pairs > best.Pairs || (top.Pairs == best.Pairs && top.OpCount < best.OpCount))
                    {
                        best = top;
                        depthWithoutImprovement = 0;
                    }
                    else depthWithoutImprovement++;
                    Log($"Depth {depth + 1}: beam={beam.Count}, bestPairs={beam[0].Pairs}/{maxPairs}, ops={beam[0].OpCount}, depthNoImprove={depthWithoutImprovement}");
                }
                else
                {
                    depthWithoutImprovement++;
                    Log($"Depth {depth + 1}: beam empty.");
                }

                if (beam.Count > 0 && beam[0].Pairs == maxPairs)
                {
                    Log("Reached maximum pairs!");
                    best = beam[0];
                    break;
                }

                if (depthWithoutImprovement >= K_noImprove)
                {
                    bool has = false;
                    var restartList = new List<State>();
                    lock (poolLock)
                    {
                        if (pool.Count > 0)
                        {
                            has = true;
                            int taken = 0;
                            var toRemove = new List<PoolNode>();
                            foreach (var node in pool)
                            {
                                restartList.Add(node.S);
                                toRemove.Add(node);
                                taken++;
                                if (taken >= restartBeamWidth) break;
                            }
                            foreach (var r in toRemove) pool.Remove(r);
                        }
                    }
                    if (!has)
                    {
                        Log("No backup to restart from.");
                        depthWithoutImprovement = 0;
                    }
                    else
                    {
                        beam = restartList;
                        Log($"Restarted beam with {beam.Count} states; pool now={pool.Count}");
                        depthWithoutImprovement = 0;
                    }
                }
            } // end depth loop

            PostProcessing(best);

            Log($"Done. ops={best.OpCount}, pairs={best.Pairs}/{maxPairs}, gained={best.Pairs - initialPairs}, finalScore={best.Score}");
            return best.Operations;
        }

        // Heuristic (original)
        private int ComputeHeuristic(Field f, int opCount)
        {
            int pairs = f.CountPairs();
            int totalDistance = 0;
            var entityPairs = f.GetAllEntityPairs();
            for (int i = 0; i < entityPairs.Count; i++)
            {
                var p = entityPairs[i];
                if (!f.IsAdjacent(p.Item1, p.Item2))
                    totalDistance += f.ManhattanDistance(p.Item1, p.Item2);
            }
            return pairs * 100 - opCount - totalDistance / 2;
        }

        // fast variant (same as above but explicit)
        private int ComputeHeuristicFast(Field f, int opCount)
        {
            return ComputeHeuristic(f, opCount);
        }

        private void PostProcessing(State state)
        {
            var field = state.Field;
            for (int iter = 0; iter < 100; iter++)
            {
                var entityPairs = field.GetAllEntityPairs();
                bool any = false;
                for (int i = 0; i < entityPairs.Count; i++)
                {
                    var p = entityPairs[i];
                    if (!field.IsAdjacent(p.Item1, p.Item2))
                    {
                        any = true;
                        int x = Math.Max(0, Math.Min(field.Size - 2, (p.Item1.Item2 + p.Item2.Item2) / 2));
                        int y = Math.Max(0, Math.Min(field.Size - 2, (p.Item1.Item1 + p.Item2.Item1) / 2));
                        field.Rotate(x, y, 2);
                    }
                }
                if (!any) break;
            }

            state.Pairs = state.Field.CountPairs();
        }
    }
}

