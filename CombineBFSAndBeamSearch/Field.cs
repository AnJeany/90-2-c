using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace ImprovedBeamSearch
{
    public class Field
    {
        public int Size { get; private set; }
        public int[,] Entities { get; private set; }

        // Constructor nhận vào một mảng 2D (entities)
        public Field(int[,] entities)
        {
            Size = entities.GetLength(0);
            Entities = (int[,])entities.Clone();

            if (Size % 2 != 0)
                throw new ArgumentException($"Size phải là số chẵn, nhận được: {Size}");
            if (Size < 4 || Size > 24)
                throw new ArgumentException($"Size phải từ 4–24, nhận được: {Size}");
        }

        // Phương thức để chuyển JSON thành đối tượng Field
        public static Field FromJson(string json)
        {
            json = json.Trim();

            // TRƯỜNG HỢP 1: Input là mảng thuần [[...]]
            if (json.StartsWith("["))
            {
                var matrixList = JsonConvert.DeserializeObject<List<List<int>>>(json);
                int size = matrixList.Count;

                // Validate sơ bộ
                if (size == 0) throw new ArgumentException("Input array is empty");
                if (matrixList[0].Count != size) throw new ArgumentException($"Matrix must be square. Rows={size}, Cols={matrixList[0].Count}");

                int[,] entities = new int[size, size];
                for (int i = 0; i < size; i++)
                {
                    for (int j = 0; j < size; j++)
                    {
                        entities[i, j] = matrixList[i][j];
                    }
                }
                return new Field(entities);
            }
            // TRƯỜNG HỢP 2: Input là Object cũ {"size":...}
            else
            {
                var input = JsonConvert.DeserializeObject<JsonInput>(json);
                int size = input.Size;
                int[,] entities = new int[size, size];
                for (int i = 0; i < size; i++)
                {
                    for (int j = 0; j < size; j++)
                    {
                        entities[i, j] = input.Entities[i][j];
                    }
                }
                return new Field(entities);
            }
        }

        // Các phương thức khác của Field giữ nguyên như trong mã gốc
        public Field Copy()
        {
            return new Field((int[,])Entities.Clone());
        }

        public void Rotate(int x, int y, int n)
        {
            if (x < 0 || y < 0 || x + n > Size || y + n > Size)
                throw new ArgumentException($"Thao tác không hợp lệ: x={x}, y={y}, n={n}, size={Size}");
            if (n < 2 || n > Size)
                throw new ArgumentException($"n phải từ 2 đến {Size}, nhận được: {n}");

            int[,] subgrid = new int[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    subgrid[i, j] = Entities[y + i, x + j];

            // Xoay 90° theo chiều kim đồng hồ: (i, j) → (j, n-1-i)
            int[,] rotated = new int[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    rotated[j, n - 1 - i] = subgrid[i, j];

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    Entities[y + i, x + j] = rotated[i, j];
        }

        public void ApplyOperation(Operation op)
        {
            Rotate(op.X, op.Y, op.N);
        }

        public void ApplyOperations(List<Operation> operations)
        {
            foreach (var op in operations)
                ApplyOperation(op);
        }

        public int CountPairs()
        {
            int pairs = 0;

            // Cặp ngang
            for (int i = 0; i < Size; i++)
                for (int j = 0; j < Size - 1; j++)
                    if (Entities[i, j] == Entities[i, j + 1])
                        pairs++;

            // Cặp dọc
            for (int i = 0; i < Size - 1; i++)
                for (int j = 0; j < Size; j++)
                    if (Entities[i, j] == Entities[i + 1, j])
                        pairs++;

            return pairs;
        }

        public List<(int, int)> FindEntityPositions(int value)
        {
            var positions = new List<(int, int)>();
            for (int i = 0; i < Size; i++)
                for (int j = 0; j < Size; j++)
                    if (Entities[i, j] == value)
                        positions.Add((i, j));
            return positions;
        }

        public List<((int, int), (int, int))> GetAllEntityPairs()
        {
            var pairs = new List<((int, int), (int, int))>();
            var seen = new HashSet<int>();

            for (int i = 0; i < Size; i++)
            {
                for (int j = 0; j < Size; j++)
                {
                    int value = Entities[i, j];
                    if (seen.Contains(value)) continue;

                    var positions = FindEntityPositions(value);
                    if (positions.Count == 2)
                    {
                        pairs.Add((positions[0], positions[1]));
                        seen.Add(value);
                    }
                }
            }
            return pairs;
        }

        public int ManhattanDistance((int, int) pos1, (int, int) pos2)
        {
            return Math.Abs(pos1.Item1 - pos2.Item1) + Math.Abs(pos1.Item2 - pos2.Item2);
        }

        public bool IsAdjacent((int, int) pos1, (int, int) pos2)
        {
            return ManhattanDistance(pos1, pos2) == 1;
        }

        public int EvaluateScore()
        {
            int pairsCount = CountPairs();
            int totalDistance = 0;
            var entityPairs = GetAllEntityPairs();

            foreach (var pair in entityPairs)
            {
                if (!IsAdjacent(pair.Item1, pair.Item2))
                    totalDistance += ManhattanDistance(pair.Item1, pair.Item2);
            }

            return pairsCount * 1000 - totalDistance;
        }

        public int GetMaxPossiblePairs()
        {
            return Size * Size / 2;
        }

        public bool IsSolved()
        {
            return CountPairs() == GetMaxPossiblePairs();
        }

        public int[,] ToArray()
        {
            return (int[,])Entities.Clone();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Field {Size}x{Size} - Pairs: {CountPairs()}/{GetMaxPossiblePairs()}");
            sb.AppendLine(new string('-', Size * 4));

            for (int i = 0; i < Size; i++)
            {
                for (int j = 0; j < Size; j++)
                    sb.Append($"{Entities[i, j],2} ");
                sb.AppendLine();
            }

            return sb.ToString();
        }
        public class JsonInput
        {
            public int Size { get; set; }
            public List<List<int>> Entities { get; set; }
        }

    }
}