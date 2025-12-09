using System;
using System.Collections.Generic;
using System.Linq;
using System.Text; // Dùng cho StringBuilder
using System.Threading.Tasks;
using ImprovedBeamSearch;

namespace Solvers
{
    public class RuleBaseBFSSolver : BaseSolver
    {
        private readonly int _centerSize;

        public RuleBaseBFSSolver(int centerSize = 4, int maxOperations = 2000, double timeout = 300.0)
            : base(maxOperations, timeout)
        {
            _centerSize = centerSize;
        }

        public override List<Operation> Solve(Field field)
        {
            ResetTimer();
            Log($"RuleBaseBFS Solver - Size: {field.Size}x{field.Size}");
            Log("Strategy: Spiral processing + BFS pathfinding");

            // In trạng thái ban đầu
            LogMatrix(field, "INITIAL STATE");

            var operations = new List<Operation>();
            var workingField = field.Copy();
            int n = field.Size;

            // Điều chỉnh center_size
            int centerSize;
            if (n >= 12)
            {
                // Yêu cầu: với ma trận >=12, luôn dùng center 10x10
                centerSize = 10;
            }
            else
            {
                centerSize = Math.Min(_centerSize, n / 2);
                if (n <= 6) centerSize = 0; // xử lý hết bằng spiral nếu bảng nhỏ
            }

            // Position map: value -> list of (row, col)
            var positionMap = BuildPositionMap(workingField);

            // Spiral coordinates
            var spiralCoords = GenerateSpiralCoords(n);

            // Visited matrix (vùng đã cố định, không động vào)
            var visited = new bool[n][];
            for (int i = 0; i < n; i++) visited[i] = new bool[n];

            // Tổng số vị trí (bỏ phần center)
            int totalPositions = n * n - centerSize * centerSize;

            // --- Biến theo dõi hướng di chuyển để log matrix ---
            int lastDr = 0;
            int lastDc = 0;
            // Khởi tạo hướng đầu tiên (thường là sang phải: dr=0, dc=1) nếu có đủ điểm
            if (spiralCoords.Count > 2)
            {
                lastDr = Math.Sign(spiralCoords[2].Item1 - spiralCoords[0].Item1);
                lastDc = Math.Sign(spiralCoords[2].Item2 - spiralCoords[0].Item2);
            }
            // --------------------------------------------------

            for (int idx = 0; idx < totalPositions; idx += 2)
            {
                if (IsTimeout())
                {
                    Log("Timeout during spiral processing");
                    break;
                }

                var targetPos = spiralCoords[idx];
                (int targetR, int targetC) = targetPos;
                (int nextR, int nextC) = (-1, -1);
                bool hasNext = false;
                if (idx + 1 < spiralCoords.Count)
                {
                    var next = spiralCoords[idx + 1];
                    nextR = next.Item1; nextC = next.Item2;
                    hasNext = true;
                }

                // Lấy giá trị tại target
                int targetValue = workingField.Entities[targetR, targetC];

                // Tìm vị trí cặp còn lại (chọn vị trí chưa visited)
                var pairPositions = positionMap.ContainsKey(targetValue) ? positionMap[targetValue] : new List<(int, int)>();
                (int, int)? pairPos = null;
                foreach (var p in pairPositions)
                {
                    if (!(p.Item1 == targetR && p.Item2 == targetC) && !visited[p.Item1][p.Item2])
                    {
                        pairPos = p;
                        break;
                    }
                }

                if (pairPos == null)
                {
                    // Đã có sẵn cặp ở target hoặc không tìm thấy
                    visited[targetR][targetC] = true;
                    if (hasNext) visited[nextR][nextC] = true;
                    // Vẫn cần check log ở đây nếu bỏ qua
                    CheckAndLogDirectionChange(idx, spiralCoords, ref lastDr, ref lastDc, workingField, targetPos);
                    continue;
                }

                // Compute rotation_dir tương tự Python: (dx, -dy)
                (int, int) rotationDir = (0, 0);
                if (hasNext)
                {
                    rotationDir = (nextC - targetC, -(nextR - targetR));
                }

                // Mark target visited
                visited[targetR][targetC] = true;

                // BFS tìm path để di chuyển pairPos -> nextTarget (hoặc target nếu next null)
                var goals = new List<(int, int)>();
                if (hasNext) goals.Add((nextR, nextC));
                else goals.Add((targetR, targetC));

                var path = BFSSearchPath(
                    workingField,
                    pairPos.Value,
                    goals,
                    visited,
                    rotationDir,
                    (targetR, targetC)
                );

                if (path != null && path.Count > 0)
                {
                    foreach (var op in path)
                    {
                        // Áp dụng operation lên workingField
                        workingField.ApplyOperation(op);
                        operations.Add(op);

                        if (operations.Count >= MaxOperations)
                        {
                            Log("Reached max operations");
                            break;
                        }
                    }

                    // Cập nhật position map sau khi thực hiện path
                    positionMap = BuildPositionMap(workingField);
                }

                // Mark next target visited
                if (hasNext) visited[nextR][nextC] = true;

                // --- KIỂM TRA ĐỔI HƯỚNG ĐỂ IN MATRIX ---
                CheckAndLogDirectionChange(idx, spiralCoords, ref lastDr, ref lastDc, workingField, targetPos);
                // ----------------------------------------

                if (operations.Count >= MaxOperations) break;
            }

            // In lần cuối sau khi xong vỏ
            LogMatrix(workingField, "FINISHED SPIRAL SHELL");

            // Solve center
            if (centerSize > 0 && !IsTimeout())
            {
                Log($"Processing center {centerSize}x{centerSize}...");
                var centerOps = SolveCenter(workingField, centerSize);
                foreach (var op in centerOps)
                {
                    workingField.ApplyOperation(op);
                    operations.Add(op);
                }
            }

            // In kết quả cuối cùng
            LogMatrix(workingField, "FINAL RESULT");

            int finalPairs = workingField.CountPairs();
            int finalMax = workingField.GetMaxPossiblePairs();
            Log($"Completed: {operations.Count} operations, {finalPairs}/{finalMax} pairs ({finalPairs * 100 / finalMax}%)");

            return operations;
        }

        // --- Hàm logic kiểm tra đổi hướng ---
        private void CheckAndLogDirectionChange(int idx, List<(int, int)> spiralCoords, ref int lastDr, ref int lastDc, Field field, (int, int) currentPos)
        {
            // Kiểm tra điểm tiếp theo của vòng lặp (idx + 2)
            if (idx + 2 < spiralCoords.Count)
            {
                var nextLoopPos = spiralCoords[idx + 2];
                var currentLoopPos = spiralCoords[idx];

                // Tính hướng đi sang bước lặp kế tiếp
                int newDr = Math.Sign(nextLoopPos.Item1 - currentLoopPos.Item1);
                int newDc = Math.Sign(nextLoopPos.Item2 - currentLoopPos.Item2);

                // Nếu hướng thay đổi so với hướng trước đó (và không phải lần đầu tiên)
                if (idx > 0 && (newDr != lastDr || newDc != lastDc))
                {
                    LogMatrix(field, $"Finished Edge at Pos {currentPos}");
                }

                lastDr = newDr;
                lastDc = newDc;
            }
        }

        // --- Hàm in ma trận ---
        private void LogMatrix(Field field, string title)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== {title} ===");
            int n = field.Size;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    // In số căn lề phải 3 ký tự để thẳng hàng
                    sb.Append($"{field.Entities[i, j],3} ");
                }
                sb.AppendLine();
            }
            sb.AppendLine("=======================");
            Log(sb.ToString());
        }

        #region Helper functions (spiral, map, rotations, BFS, etc.)

        private List<(int, int)> GenerateSpiralCoords(int n)
        {
            var coords = new List<(int, int)>();
            int top = 0, left = 0;
            int bottom = n - 1, right = n - 1;

            while (top <= bottom && left <= right)
            {
                // Top row
                for (int col = left; col <= right; col++) coords.Add((top, col));
                top++;
                if (top > bottom) break;

                // Right column
                for (int row = top; row <= bottom; row++) coords.Add((row, right));
                right--;
                if (left > right) break;

                // Bottom row
                for (int col = right; col >= left; col--) coords.Add((bottom, col));
                bottom--;
                if (top > bottom) break;

                // Left column
                for (int row = bottom; row >= top; row--) coords.Add((row, left));
                left++;
            }
            return coords;
        }

        private Dictionary<int, List<(int, int)>> BuildPositionMap(Field field)
        {
            var map = new Dictionary<int, List<(int, int)>>();
            int n = field.Size;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    int v = field.Entities[i, j];
                    if (!map.ContainsKey(v)) map[v] = new List<(int, int)>();
                    map[v].Add((i, j));
                }
            return map;
        }

        private bool CanRotate(Operation op, bool[][] visited, int fieldSize)
        {
            for (int i = op.Y; i < op.Y + op.N; i++)
            {
                for (int j = op.X; j < op.X + op.N; j++)
                {
                    if (i < 0 || i >= fieldSize || j < 0 || j >= fieldSize) return false;
                    if (visited[i][j]) return false;
                }
            }
            return true;
        }

        private List<Operation> GeneratePossibleRotations((int, int) pos, int fieldSize)
        {
            var operations = new List<Operation>();
            int row = pos.Item1;
            int col = pos.Item2;

            for (int n = 2; n <= fieldSize; n++)
            {
                int startTop = Math.Max(0, row - n + 1);
                int endTopExclusive = Math.Min(row + 1, fieldSize - n + 1);
                for (int top = startTop; top < endTopExclusive; top++)
                {
                    int startLeft = Math.Max(0, col - n + 1);
                    int endLeftExclusive = Math.Min(col + 1, fieldSize - n + 1);
                    for (int left = startLeft; left < endLeftExclusive; left++)
                    {
                        operations.Add(new Operation(left, top, n));
                    }
                }
            }

            return operations;
        }

        private (int, int) CalculateNewPosition((int, int) pos, Operation op)
        {
            int row = pos.Item1;
            int col = pos.Item2;

            int relRow = row - op.Y;
            int relCol = col - op.X;

            int newRelRow = relCol;
            int newRelCol = op.N - 1 - relRow;

            int newRow = op.Y + newRelRow;
            int newCol = op.X + newRelCol;
            return (newRow, newCol);
        }

        private Operation GetFinalRotation((int, int) pos, (int, int) rotationDir, (int, int) targetPos, int fieldSize)
        {
            var (dx, dy) = rotationDir;
            var (tR, tC) = targetPos;
            var (r, c) = pos;

            if (dx == 1 && dy == 0) // Down
            {
                return new Operation(tC, tR, 2);
            }
            else if (dx == 0 && dy == -1) // Left
            {
                return new Operation(c, r, 2);
            }
            else if (dx == -1 && dy == 0) // Up
            {
                return new Operation(tC - 1, tR - 1, 2);
            }
            else if (dx == 0 && dy == 1) // Right
            {
                return new Operation(tC, tR, 2);
            }
            return null;
        }

        private List<Operation> BFSSearchPath(
            Field field,
            (int, int) startPos,
            List<(int, int)> goalPositions,
            bool[][] visited,
            (int, int) rotationDir,
            (int, int) targetPos
        )
        {
            int n = field.Size;
            var queue = new Queue<((int, int) pos, List<Operation> path)>();
            queue.Enqueue((startPos, new List<Operation>()));

            var visitedBfs = new Dictionary<(int, int), int>();
            visitedBfs[startPos] = 0;

            int maxDepth = Math.Min(20, n);

            while (queue.Count > 0)
            {
                var (currentPos, opsPath) = queue.Dequeue();
                if (goalPositions.Contains(currentPos))
                {
                    if (!(rotationDir == (0, 0)) && goalPositions.Count > 0)
                    {
                        var finalOp = GetFinalRotation(currentPos, rotationDir, targetPos, n);
                        if (finalOp != null && CanRotate(finalOp, visited, n))
                        {
                            var newList = new List<Operation>(opsPath) { finalOp };
                            return newList;
                        }
                    }
                    return new List<Operation>(opsPath);
                }

                if (opsPath.Count >= maxDepth) continue;

                var possible = GeneratePossibleRotations(currentPos, n);

                foreach (var op in possible)
                {
                    if (!CanRotate(op, visited, n)) continue;

                    var newPos = CalculateNewPosition(currentPos, op);

                    if (visitedBfs.TryGetValue(newPos, out int prevDepth) && prevDepth <= opsPath.Count + 1)
                        continue;

                    visitedBfs[newPos] = opsPath.Count + 1;

                    var newPath = new List<Operation>(opsPath) { op };
                    queue.Enqueue((newPos, newPath));
                }
            }

            return new List<Operation>();
        }

        private List<Operation> SolveCenter(Field field, int centerSize)
        {
            int n = field.Size;

            if (n >= 12) centerSize = Math.Min(centerSize, 10);

            int offset = (n - centerSize) / 2;

            int[,] centerEntities = new int[centerSize, centerSize];
            for (int i = 0; i < centerSize; i++)
                for (int j = 0; j < centerSize; j++)
                    centerEntities[i, j] = field.Entities[offset + i, offset + j];

            var centerField = new Field(centerEntities);

            double remaining = Timeout - GetElapsedTime();
            double centerTimeout = Math.Min(Math.Max(0.1, remaining), remaining);

            var beamSolver = new ImprovedBeamSearch_Parallel_Optimized(
                beamWidth: 65,
                maxDepth: 150,
                maxOperations: Math.Max(100, Math.Min(2000, this.MaxOperations / 4)),
                timeout: centerTimeout,
                minN: 2,
                maxN: Math.Min(centerSize, 10),
                useHashPruning: true,
                K_noImprove: 8,
                backupPoolSize: 2000,
                restartBeamWidth: 80,
                maxDegreeOfParallelism: 16
            );

            Log($"Center beam: size={centerSize}x{centerSize}, timeout={centerTimeout:0.00}s");

            var centerOps = beamSolver.Solve(centerField);

            var fullOps = new List<Operation>();
            foreach (var op in centerOps)
            {
                if (op.X < 0 || op.Y < 0 || op.X + op.N > centerSize || op.Y + op.N > centerSize) continue;
                fullOps.Add(new Operation(op.X + offset, op.Y + offset, op.N));
            }

            Log($"Center solved by beam: {fullOps.Count} operations");
            return fullOps;
        }

        #endregion
    }
}