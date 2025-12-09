using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImprovedBeamSearch
{
    public abstract class BaseSolver
    {
        /// <summary>
        /// Số thao tác tối đa cho phép.
        /// </summary>
        protected int MaxOperations { get; set; }

        /// <summary>
        /// Thời gian tối đa (giây).
        /// </summary>
        protected double Timeout { get; set; }

        /// <summary>
        /// Đồng hồ đo thời gian chạy.
        /// </summary>
        private Stopwatch _stopwatch;

        protected BaseSolver(int maxOperations = 1000, double timeout = 300.0)
        {
            MaxOperations = maxOperations;
            Timeout = timeout;
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// Phương thức trừu tượng: Giải bài toán và trả về danh sách các thao tác.
        /// </summary>
        /// <param name="field">Đối tượng Field cần giải.</param>
        /// <returns>Danh sách các thao tác Operation.</returns>
        public abstract List<Operation> Solve(Field field);

        /// <summary>
        /// Kiểm tra xem đã hết thời gian chưa.
        /// </summary>
        public bool IsTimeout()
        {
            return _stopwatch.IsRunning && _stopwatch.Elapsed.TotalSeconds > Timeout;
        }

        /// <summary>
        /// Reset bộ đếm thời gian.
        /// </summary>
        public void ResetTimer()
        {
            _stopwatch.Reset();
            _stopwatch.Start();
        }

        /// <summary>
        /// Lấy thời gian đã trôi qua (giây).
        /// </summary>
        public double GetElapsedTime()
        {
            return _stopwatch.IsRunning ? _stopwatch.Elapsed.TotalSeconds : 0.0;
        }

        /// <summary>
        /// Ghi log với timestamp (giống như print trong Python).
        /// </summary>
        public void Log(string message)
        {
            Console.WriteLine($"[{GetElapsedTime():0.00}s] {message}");
        }
    }
}
