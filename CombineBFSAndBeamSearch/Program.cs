using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using ImprovedBeamSearch;
using UltimateSolution;

namespace MatchmakingRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            // Thiết lập bộ đệm lớn để paste input dài
            Console.SetIn(new StreamReader(Console.OpenStandardInput(8192)));

            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("==================================================");
                Console.WriteLine("   MATCHMAKING SOLVER - DEBUG & CHECKPOINT MODE   ");
                Console.WriteLine("   SETTINGS: BEAMWIDTH=65 | CORE=10x10            ");
                Console.WriteLine("==================================================");
                Console.ResetColor();
                Console.WriteLine("Paste chuoi JSON input vao duoi day va nhan ENTER:");
                Console.Write("> ");

                string jsonInput = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(jsonInput))
                {
                    Console.WriteLine("Loi: Ban chua nhap du lieu!");
                    Console.ReadLine();
                    return;
                }

                Console.WriteLine("\nDang phan tich du lieu...");
                Field field = Field.FromJson(jsonInput);
                int maxPairs = field.GetMaxPossiblePairs();

                Console.WriteLine($"-> Kich thuoc: {field.Size}x{field.Size}");
                Console.WriteLine($"-> Max Pairs: {maxPairs}");
                Console.WriteLine(new string('-', 50));

                // [CẤU HÌNH] CoreSize = 10 theo yeu cau cua ban
                var solver = new HybridUltimateSolver(
    coreSize: 12,       // Kích thước vùng trung tâm (Center Size)
    maxOps: 20000,      // Số bước tối đa
    timeout: 50,       // Timeout (giây) - đặt sát giờ thi (ví dụ 1.8s cho limit 2s)
    beamWidth: 200      // ĐỘ RỘNG BEAM: Càng to càng mạnh nhưng càng chậm. 
                        // Máy khỏe (16 cores) có thể để 500-1000. Máy yếu để 200.
);

                Console.WriteLine("Dang chay Solver...");
                Console.WriteLine("(Chuong trinh se TAM DUNG sau khi xu ly xong vung ngoai de ban kiem tra)");
                
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Gọi hàm giải (Hàm này sẽ tự dừng chờ bạn nhấn Enter ở giữa chừng)
                var operations = solver.Solve(field);

                stopwatch.Stop();

                // Cập nhật kết quả cuối cùng vào field gốc
                Console.WriteLine("\nDang dong bo ket qua cuoi cung...");
                field.ApplyOperations(operations);

                int currentPairs = field.CountPairs();
                double score = currentPairs * 100 - operations.Count;

                Console.WriteLine(new string('=', 50));
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("KET QUA CUOI CUNG (FINAL RESULT)");
                Console.ResetColor();
                Console.WriteLine($"Thoi gian tong: {stopwatch.Elapsed.TotalSeconds:0.00}s");
                Console.WriteLine($"Moves: {operations.Count}");
                Console.WriteLine($"Pairs: {currentPairs} / {maxPairs} ({(double)currentPairs / maxPairs * 100:0.0}%)");
                Console.WriteLine($"Score: {score}");
                Console.WriteLine(new string('=', 50));

                Console.WriteLine("Ma tran hoan thien:");
                PrintMatrix(field);

                SaveOutput(operations, "output.json");
                Console.WriteLine($"\nDa luu ket qua vao: {Path.GetFullPath("output.json")}");

                Console.WriteLine("\n[JSON OUTPUT - COPY DE NOP BAI]:");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(File.ReadAllText("output.json"));
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nCO LOI XAY RA: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }

            Console.WriteLine("\nNhan Enter de thoat...");
            Console.ReadLine();
        }

        static void PrintMatrix(Field field)
        {
            int n = field.Size;
            var entities = field.Entities;

            Console.Write("   ");
            for (int j = 0; j < n; j++) Console.Write("----");
            Console.WriteLine();

            for (int i = 0; i < n; i++)
            {
                Console.Write(" | ");
                for (int j = 0; j < n; j++)
                {
                    int val = entities[i, j];

                    bool isPair = false;
                    if (j < n - 1 && entities[i, j + 1] == val) isPair = true;
                    if (j > 0 && entities[i, j - 1] == val) isPair = true;
                    if (i < n - 1 && entities[i + 1, j] == val) isPair = true;
                    if (i > 0 && entities[i - 1, j] == val) isPair = true;

                    if (isPair)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write($"{val,3}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.Write($"{val,3}");
                    }
                    Console.Write(" ");
                }
                Console.WriteLine("|");
            }

            Console.Write("   ");
            for (int j = 0; j < n; j++) Console.Write("----");
            Console.WriteLine();
        }

        static void SaveOutput(List<Operation> ops, string filename)
        {
            var outputData = new
            {
                ops = new List<Dictionary<string, int>>()
            };

            foreach (var op in ops)
            {
                outputData.ops.Add(new Dictionary<string, int>
                {
                    { "x", op.X },
                    { "y", op.Y },
                    { "n", op.N }
                });
            }

            string jsonOutput = JsonConvert.SerializeObject(outputData, Formatting.None);
            File.WriteAllText(filename, jsonOutput);
        }
    }
}