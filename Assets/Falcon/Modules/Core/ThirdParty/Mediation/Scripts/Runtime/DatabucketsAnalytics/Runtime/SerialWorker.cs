using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RealtimeEvents
{
    // Only this short queue lock is shared with callers. Work always runs outside it.
    internal sealed class SerialWorker
    {
        private sealed class Work
        {
            internal Action Action;
            internal int Bytes;
            internal readonly TaskCompletionSource<bool> Done = NewCompletion();
        }

        private readonly object gate = new object();
        private readonly Queue<Work> queue = new Queue<Work>();
        private readonly int maxOperations;
        private readonly int maxBytes;
        private int operations;
        private int bytes;
        private Action finish;
        private bool closing;
        private readonly TaskCompletionSource<bool> completion = NewCompletion();
        internal Task Completion => completion.Task;

        internal SerialWorker(int maxOperations, int maxBytes)
        {
            this.maxOperations = maxOperations;
            this.maxBytes = maxBytes;
            new Thread(Run) { IsBackground = true, Name = "Realtime Events storage" }.Start();
        }

        private static TaskCompletionSource<bool> NewCompletion() =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private static void Fail(TaskCompletionSource<bool> source, Exception error)
        {
            source.SetException(error);
            // Void convenience APIs still report failures through LastError and the next persistence barrier.
            // Observe only failed tasks instead of allocating a continuation for every frame's successful tick.
            var observed = source.Task.Exception;
        }

        internal Task Post(Action action, int cost = 0)
        {
            lock (gate)
            {
                if (closing) throw new ObjectDisposedException(nameof(SerialWorker));
                if (operations >= maxOperations || cost > maxBytes - bytes)
                    throw new IOException("Pending event work is full. Retry after storage catches up.");
                var work = new Work { Action = action, Bytes = cost };
                queue.Enqueue(work);
                operations++;
                bytes += cost;
                Monitor.Pulse(gate);
                return work.Done.Task;
            }
        }

        internal void Close(Action action)
        {
            lock (gate)
            {
                if (closing) return;
                closing = true;
                finish = action; // A reserved final slot, even when the application queue is full.
                Monitor.Pulse(gate);
            }
        }

        private void Run()
        {
            while (true)
            {
                Work work;
                lock (gate)
                {
                    while (queue.Count == 0 && !closing) Monitor.Wait(gate);
                    if (queue.Count == 0) break;
                    work = queue.Dequeue();
                }
                Exception failure = null;
                try { work.Action(); }
                catch (Exception error) { failure = error; }
                lock (gate)
                {
                    operations--;
                    bytes -= work.Bytes;
                }
                if (failure == null) work.Done.SetResult(true);
                else Fail(work.Done, failure);
            }
            try { finish(); completion.SetResult(true); }
            catch (Exception error) { Fail(completion, error); }
        }
    }
}
