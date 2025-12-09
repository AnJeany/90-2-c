using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImprovedBeamSearch
{
    public class Operation
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int N { get; set; }

        public Operation(int x, int y, int n)
        {
            X = x;
            Y = y;
            N = n;
        }

        public Dictionary<string, int> ToDict()
        {
            return new Dictionary<string, int>
            {
                { "x", X },
                { "y", Y },
                { "n", N }
            };
        }

        public override string ToString()
        {
            return $"Rotate({X}, {Y}, {N}x{N})";
        }
    }

    /// <summary>
    /// Đại diện cho một bài toán.
    /// </summary>
    public class Problem
    {
        public int Size { get; set; }
        public int[,] Entities { get; set; }

        public Problem(int size, int[,] entities)
        {
            Size = size;
            Entities = entities;
        }

        public static Problem FromDict(dynamic data)
        {
            int size = data.size;
            var list = data.entities as List<List<int>>;

            int[,] entities = new int[size, size];
            for (int i = 0; i < size; i++)
                for (int j = 0; j < size; j++)
                    entities[i, j] = list[i][j];

            return new Problem(size, entities);
        }
    }

    /// <summary>
    /// Đại diện cho một câu trả lời.
    /// </summary>
    public class Answer
    {
        public List<Operation> Ops { get; set; }
        public int PairCount { get; set; }
        public int OperationCount { get; set; }

        public Answer()
        {
            Ops = new List<Operation>();
            PairCount = 0;
            OperationCount = 0;
        }

        public Answer(List<Operation> ops, int pairCount = 0, int operationCount = 0)
        {
            Ops = ops;
            PairCount = pairCount;
            OperationCount = operationCount;
        }

        public Dictionary<string, object> ToDict()
        {
            var opList = new List<Dictionary<string, int>>();
            foreach (var op in Ops)
                opList.Add(op.ToDict());

            return new Dictionary<string, object>
            {
                { "ops", opList }
            };
        }
    }
}
