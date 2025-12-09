using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ImprovedBeamSearch;

namespace UltimateSolution
{
    // [STRUCT FASTMOVE - GIỮ NGUYÊN]
    public readonly struct FastMove
    {
        public readonly byte X; public readonly byte Y; public readonly byte Size;
        public FastMove(int x, int y, int size) { X = (byte)x; Y = (byte)y; Size = (byte)size; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Operation ToClass() => new Operation(X, Y, Size);
    }

    // [CLASS FASTBOARD - GIỮ NGUYÊN]
    public class FastBoard
    {
        public short[] Data; public int N; public ulong Hash;
        private static readonly ulong[,] _zobristTable;
        static FastBoard()
        {
            var rng = new Random(1337); _zobristTable = new ulong[25 * 25, 600];
            for (int i = 0; i < 25 * 25; i++) for (int j = 0; j < 600; j++) { byte[] buf = new byte[8]; rng.NextBytes(buf); _zobristTable[i, j] = BitConverter.ToUInt64(buf, 0); }
        }
        public FastBoard(int[,] original)
        {
            N = original.GetLength(0); Data = new short[N * N]; Hash = 0;
            for (int r = 0; r < N; r++) for (int c = 0; c < N; c++) { int idx = r * N + c; short val = (short)original[r, c]; Data[idx] = val; if (val < 600) Hash ^= _zobristTable[idx, val]; }
        }
        public FastBoard Clone() { var b = new FastBoard(N); Buffer.BlockCopy(Data, 0, b.Data, 0, Data.Length * sizeof(short)); b.Hash = this.Hash; return b; }
        private FastBoard(int n) { N = n; Data = new short[n * n]; }
        public void ApplyMove(FastMove op)
        {
            int n = op.Size; int startRow = op.Y; int startCol = op.X; int N_local = N;
            for (int i = 0; i < n / 2; i++)
            {
                int first = i; int last = n - 1 - i;
                for (int j = first; j < last; j++)
                {
                    int offset = j - first;
                    int topR = startRow + first; int topC = startCol + j;
                    int rightR = startRow + j; int rightC = startCol + last;
                    int botR = startRow + last; int botC = startCol + last - offset;
                    int leftR = startRow + last - offset; int leftC = startCol + first;
                    int idxTop = topR * N_local + topC; int idxRight = rightR * N_local + rightC; int idxBot = botR * N_local + botC; int idxLeft = leftR * N_local + leftC;
                    short valTop = Data[idxTop]; short valRight = Data[idxRight]; short valBot = Data[idxBot]; short valLeft = Data[idxLeft];
                    if (valTop < 600) Hash ^= _zobristTable[idxTop, valTop]; if (valRight < 600) Hash ^= _zobristTable[idxRight, valRight]; if (valBot < 600) Hash ^= _zobristTable[idxBot, valBot]; if (valLeft < 600) Hash ^= _zobristTable[idxLeft, valLeft];
                    Data[idxTop] = valLeft; Data[idxRight] = valTop; Data[idxBot] = valRight; Data[idxLeft] = valBot;
                    if (valLeft < 600) Hash ^= _zobristTable[idxTop, valLeft]; if (valTop < 600) Hash ^= _zobristTable[idxRight, valTop]; if (valRight < 600) Hash ^= _zobristTable[idxBot, valRight]; if (valBot < 600) Hash ^= _zobristTable[idxLeft, valBot];
                }
            }
        }
        public int CountPairs() { int p = 0; for (int r = 0; r < N; r++) for (int c = 0; c < N - 1; c++) if (Data[r * N + c] == Data[r * N + c + 1]) p++; for (int r = 0; r < N - 1; r++) for (int c = 0; c < N; c++) if (Data[r * N + c] == Data[(r + 1) * N + c]) p++; return p; }
        public int CalculateManhattanHeuristic() { Span<int> p = stackalloc int[600]; p.Fill(-1); int d = 0; int len = Data.Length; for (int i = 0; i < len; i++) { int v = Data[i]; if (v >= 600) continue; if (p[v] == -1) p[v] = i; else { int p1 = p[v]; d += Math.Abs(p1 / N - i / N) + Math.Abs(p1 % N - i % N); } } return d; }
    }

    // [CLASS SOLVER CHÍNH - ĐÃ THÊM LOGIC GÓC DƯỚI PHẢI]
    public class HybridUltimateSolver : BaseSolver
    {
        private int _coreSize;
        private FastBoard _fastField;
        private bool[] _lockedMap;
        private Random _rng = new Random(42);
        private int _beamWidth;

        public HybridUltimateSolver(int coreSize = 10, int maxOps = 20000, double timeout = 100, int beamWidth = 250)
            : base(maxOps, timeout)
        {
            _coreSize = coreSize;
            _beamWidth = beamWidth;
        }

        public override List<Operation> Solve(Field field)
        {
            ResetTimer();
            _fastField = new FastBoard(field.Entities);
            _lockedMap = new bool[field.Size * field.Size];
            var allMoves = new List<Operation>();

            int currentN = field.Size;
            int targetN = Math.Max(2, _coreSize + (field.Size % 2 != _coreSize % 2 ? 1 : 0));

            // [PHASE 1] SHELL SOLVER
            int layer = 0;
            while (currentN > targetN)
            {
                if (IsTimeout()) break;
                SolveStrictLayer(layer, targetN, allMoves);
                layer++;
                currentN -= 2;
            }

            if (IsTimeout() || allMoves.Count >= MaxOperations) return allMoves;

            // [PHASE 2] CORE SOLVER
            int extractSize = Math.Min(targetN + 2, field.Size);
            int offset = (field.Size - extractSize) / 2;
            int[,] coreGrid = new int[extractSize, extractSize];
            for (int r = 0; r < extractSize; r++)
                for (int c = 0; c < extractSize; c++)
                    coreGrid[r, c] = _fastField.Data[(offset + r) * field.Size + (offset + c)];

            var coreBoard = new FastBoard(coreGrid);
            double remainingTime = Timeout - GetElapsedTime();
            if (remainingTime < 0.5) remainingTime = 0.5;

            var beamSolver = new FastBeamSolver(width: _beamWidth, timeMs: (int)(remainingTime * 1000));
            var coreMoves = beamSolver.Solve(coreBoard);

            foreach (var m in coreMoves)
            {
                var globalOp = new Operation(m.X + offset, m.Y + offset, m.Size);
                allMoves.Add(globalOp);
                _fastField.ApplyMove(new FastMove(m.X + offset, m.Y + offset, m.Size));
            }
            return allMoves;
        }

        private void SolveStrictLayer(int layer, int targetCoreSize, List<Operation> moves)
        {
            int N = _fastField.N;
            int min = layer;
            int max = N - 1 - layer;

            bool canUse5CellStrategy = (max - min + 1 >= 5);

            // 1. TOP EDGE (Ngang) -> Góc Trên-Phải (Giữ nguyên)
            var topPairs = new List<(int, int)>();
            for (int c = min; c < max - 1; c += 2) topPairs.Add((Idx(min, c), Idx(min, c + 1)));
            ProcessSegment(topPairs, targetCoreSize, moves);

            SolveCornerWithBuffer(
                p1Target: Idx(min, max - 1), p2Target: Idx(min, max),
                p1Setup: Idx(min, max - 1), p2Setup: Idx(min + 1, max - 1),
                rotateOp: new FastMove(max - 1, min, 2),
                coreSize: targetCoreSize, moves: moves
            );

            // =========================================================
            // 2. RIGHT EDGE -> GÓC DƯỚI-PHẢI (CẬP NHẬT MỚI)
            // =========================================================
            var rightPairs = new List<(int, int)>();
            if (canUse5CellStrategy)
            {
                // A. Giải cạnh phải bình thường đến max-5
                for (int r = min + 1; r <= max - 5; r += 2)
                    rightPairs.Add((Idx(r, max), Idx(r + 1, max)));
                ProcessSegment(rightPairs, targetCoreSize, moves);

                // B. SETUP: Giải 4 ô dọc (Green) tại (max-4, max) đến (max-1, max)
                int p1 = Idx(max - 4, max); int p2 = Idx(max - 3, max);
                int p3 = Idx(max - 2, max); int p4 = Idx(max - 1, max);

                ForceSolvePair(p1, p2, targetCoreSize, moves);
                ForceSolvePair(p3, p4, targetCoreSize, moves);

                // C. ROTATE 5x5: Biến cột dọc xanh thành hàng ngang đỏ
                // Vị trí xoay: (max-4, max-4)
                var cornerOp = new FastMove(max - 4, max - 4, 5);
                _fastField.ApplyMove(cornerOp);
                moves.Add(cornerOp.ToClass());

                // D. LOCK: Khóa 4 ô đỏ dưới đáy (23:23, 23:22, 23:21, 23:20)
                Lock(Idx(max, max));
                Lock(Idx(max, max - 1));
                Lock(Idx(max, max - 2));
                Lock(Idx(max, max - 3));

                // E. REPAIR: Mở khóa và sửa lại 4 ô dọc vừa bị hỏng
                Unlock(p1); Unlock(p2); Unlock(p3); Unlock(p4);

                ForceSolvePair(p1, p2, targetCoreSize, moves);
                ForceSolvePair(p3, p4, targetCoreSize, moves);

                // Khóa lại
                Lock(p1); Lock(p2); Lock(p3); Lock(p4);
            }
            else
            {
                // Fallback cho size nhỏ: Logic cũ
                for (int r = min + 1; r < max - 1; r += 2) rightPairs.Add((Idx(r, max), Idx(r + 1, max)));
                ProcessSegment(rightPairs, targetCoreSize, moves);

                SolveCornerWithBuffer(
                    p1Target: Idx(max - 1, max), p2Target: Idx(max, max),
                    p1Setup: Idx(max - 1, max), p2Setup: Idx(max - 1, max - 1),
                    rotateOp: new FastMove(max - 1, max - 1, 2),
                    coreSize: targetCoreSize, moves: moves
                );
            }

            // 3. BOTTOM EDGE (Ngang) -> Góc Dưới-Trái
            // Nếu dùng 5-Cell thì bắt đầu từ max-4 (bỏ qua 4 ô đã giải), ngược lại từ max-1
            int bottomLimit = canUse5CellStrategy ? (max - 4) : (max - 1);

            var bottomPairs = new List<(int, int)>();
            for (int c = bottomLimit; c > min + 1; c -= 2)
                bottomPairs.Add((Idx(max, c), Idx(max, c - 1)));
            ProcessSegment(bottomPairs, targetCoreSize, moves);

            SolveCornerWithBuffer(
                p1Target: Idx(max, min + 1), p2Target: Idx(max, min),
                p1Setup: Idx(max, min + 1), p2Setup: Idx(max - 1, min + 1),
                rotateOp: new FastMove(min, max - 1, 2),
                coreSize: targetCoreSize, moves: moves
            );

            // 4. LEFT EDGE (Dọc) -> Góc Trên-Trái (Giữ nguyên)
            var leftPairs = new List<(int, int)>();
            for (int r = max; r > min + 1; r -= 2) leftPairs.Add((Idx(r, min), Idx(r - 1, min)));
            ProcessSegment(leftPairs, targetCoreSize, moves);

            Unlock(Idx(min, min)); Unlock(Idx(min, min + 1));
            SolveCornerWithBuffer(
                p1Target: Idx(min + 1, min), p2Target: Idx(min, min),
                p1Setup: Idx(min + 1, min), p2Setup: Idx(min + 1, min + 1),
                rotateOp: new FastMove(min, min, 2),
                coreSize: targetCoreSize, moves: moves
            );
            if (min + 2 < max) ForceSolvePair(Idx(min, min + 1), Idx(min, min + 2), targetCoreSize, moves);
        }

        private void ProcessSegment(List<(int, int)> pairs, int coreSize, List<Operation> moves)
        {
            foreach (var pair in pairs) ForceSolvePair(pair.Item1, pair.Item2, coreSize, moves);
        }

        private void SolveCornerWithBuffer(int p1Target, int p2Target, int p1Setup, int p2Setup, FastMove rotateOp, int coreSize, List<Operation> moves)
        {
            int r = rotateOp.Y; int c = rotateOp.X; int sz = rotateOp.Size;
            UnlockRegion(r, c, sz);

            bool success = false;
            short targetVal = _fastField.Data[p1Setup];

            for (int i = 0; i < 5; i++)
            {
                if (BringPartnerTo(p1Setup, p2Setup, moves))
                {
                    success = true;
                    break;
                }
                EvictToCenter(p1Setup, coreSize, moves);
                targetVal = _fastField.Data[p1Setup];
            }

            if (success)
            {
                _fastField.ApplyMove(rotateOp);
                moves.Add(rotateOp.ToClass());
            }

            Lock(p1Target); Lock(p2Target);
        }

        private void ForceSolvePair(int t1, int t2, int coreSize, List<Operation> moves)
        {
            if (_fastField.Data[t1] == _fastField.Data[t2]) { Lock(t1); Lock(t2); return; }
            if (_lockedMap[t1] || _lockedMap[t2]) return;

            for (int i = 0; i < 5; i++)
            {
                if (BringPartnerTo(t1, t2, moves)) { Lock(t1); Lock(t2); return; }
                EvictToCenter(t1, coreSize, moves);
            }
            Lock(t1); Lock(t2);
        }

        private bool EvictToCenter(int shellIdx, int coreSize, List<Operation> moves)
        {
            int offset = (_fastField.N - coreSize) / 2;
            int centerIdx = (offset + _rng.Next(coreSize)) * _fastField.N + (offset + _rng.Next(coreSize));
            var path = BfsFindPath(centerIdx, shellIdx);
            if (path != null) { ApplyMoves(path, moves); return true; }
            return false;
        }

        private bool BringPartnerTo(int t1, int t2, List<Operation> moves)
        {
            short targetVal = _fastField.Data[t1];
            int bestBuddy = -1; int minD = 999999;
            for (int i = 0; i < _fastField.Data.Length; i++)
            {
                if (i == t1 || _lockedMap[i]) continue;
                if (_fastField.Data[i] == targetVal)
                {
                    int d = Dist(i, t2);
                    if (d < minD) { minD = d; bestBuddy = i; }
                }
            }
            if (bestBuddy == -1) return false;

            _lockedMap[t1] = true;
            var path = BfsFindPath(bestBuddy, t2);
            _lockedMap[t1] = false;

            if (path != null) { ApplyMoves(path, moves); return true; }
            return false;
        }

        private List<FastMove> BfsFindPath(int startIdx, int endIdx)
        {
            if (startIdx == endIdx) return new List<FastMove>();
            int N = _fastField.N;
            var parentMove = new Dictionary<int, int>();
            var prevIdx = new Dictionary<int, int>();
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>(256);

            q.Enqueue(startIdx); dist[startIdx] = 0;
            int limitDepth = 14;

            while (q.Count > 0)
            {
                int u = q.Dequeue();
                if (u == endIdx)
                {
                    var path = new List<FastMove>(); int curr = endIdx;
                    while (curr != startIdx) { int p = prevIdx[curr]; int code = parentMove[curr]; path.Add(new FastMove(code & 0xFF, (code >> 8) & 0xFF, code >> 16)); curr = p; }
                    path.Reverse(); return path;
                }
                if (dist[u] >= limitDepth) continue;

                int ur = u / N; int uc = u % N;
                for (int sz = 2; sz <= 3; sz++)
                {
                    int rStart = Math.Max(0, ur - sz + 1); int rEnd = Math.Min(N - sz, ur);
                    for (int r = rStart; r <= rEnd; r++)
                    {
                        int cStart = Math.Max(0, uc - sz + 1); int cEnd = Math.Min(N - sz, uc);
                        for (int c = cStart; c <= cEnd; c++)
                        {
                            bool locked = false;
                            if (_lockedMap[r * N + c] || _lockedMap[(r + sz - 1) * N + (c + sz - 1)]) locked = true;
                            else { for (int rr = 0; rr < sz; rr++) for (int cc = 0; cc < sz; cc++) if (_lockedMap[(r + rr) * N + (c + cc)]) { locked = true; break; } }
                            if (locked) continue;

                            int nlr = uc - c; int nlc = sz - 1 - (ur - r);
                            int v = (r + nlr) * N + (c + nlc);

                            if (!dist.ContainsKey(v))
                            {
                                dist[v] = dist[u] + 1; prevIdx[v] = u; parentMove[v] = (sz << 16) | (r << 8) | c;
                                q.Enqueue(v);
                            }
                        }
                    }
                }
            }
            return null;
        }

        private void ApplyMoves(List<FastMove> path, List<Operation> globalOps) { foreach (var m in path) { _fastField.ApplyMove(m); globalOps.Add(m.ToClass()); } }
        private void UnlockRegion(int r, int c, int sz) { for (int i = 0; i < sz; i++) for (int j = 0; j < sz; j++) _lockedMap[Idx(r + i, c + j)] = false; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] private int Idx(int r, int c) => r * _fastField.N + c;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] private void Lock(int idx) => _lockedMap[idx] = true;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] private void Unlock(int idx) => _lockedMap[idx] = false;
        private int Dist(int idx1, int idx2) { int r1 = idx1 / _fastField.N; int c1 = idx1 % _fastField.N; int r2 = idx2 / _fastField.N; int c2 = idx2 % _fastField.N; return Math.Abs(r1 - r2) + Math.Abs(c1 - c2); }
    }

    // [CLASS FASTBEAMSOLVER - GIỮ NGUYÊN]
    public class FastBeamSolver
    {
        class Node { public FastBoard Board; public Node Parent; public FastMove Move; public int Score; public ulong Hash; }
        private int _beamWidth; private int _timeMs;
        public FastBeamSolver(int width, int timeMs) { _beamWidth = width; _timeMs = timeMs; }
        public List<FastMove> Solve(FastBoard startBoard)
        {
            var sw = Stopwatch.StartNew();
            var root = new Node { Board = startBoard.Clone(), Score = startBoard.CountPairs() * 1000 - startBoard.CalculateManhattanHeuristic() / 2, Hash = startBoard.Hash };
            var beam = new List<Node> { root };
            Node bestGlobal = root;
            int N = startBoard.N; int depth = 0;
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            while (depth < 200 && sw.ElapsedMilliseconds < _timeMs && beam.Count > 0)
            {
                var candidates = new ConcurrentBag<Node>();
                Parallel.ForEach(beam, parallelOptions, node =>
                {
                    if (node.Score < bestGlobal.Score - 3000) return;
                    int maxSz = Math.Min(4, N);
                    for (int sz = 2; sz <= maxSz; sz++)
                    {
                        for (int r = 0; r <= N - sz; r++)
                        {
                            for (int c = 0; c <= N - sz; c++)
                            {
                                var nextBoard = node.Board.Clone();
                                var move = new FastMove(c, r, sz);
                                nextBoard.ApplyMove(move);
                                candidates.Add(new Node { Board = nextBoard, Parent = node, Move = move, Score = nextBoard.CountPairs() * 1000 - (depth + 1) - nextBoard.CalculateManhattanHeuristic() / 2, Hash = nextBoard.Hash });
                            }
                        }
                    }
                });
                beam = candidates.OrderByDescending(n => n.Score).DistinctBy(n => n.Hash).Take(_beamWidth).ToList();
                if (beam.Count > 0 && beam[0].Score > bestGlobal.Score) bestGlobal = beam[0];
                if (bestGlobal.Score >= N * N * 1000 / 2 - 500) break;
                depth++;
            }
            var result = new List<FastMove>(); var curr = bestGlobal; while (curr.Parent != null) { result.Add(curr.Move); curr = curr.Parent; }
            result.Reverse(); return result;
        }
    }
}