using System;
using System.Threading;
using API.Contracts;

namespace Sandbox;

public class SimpleThreadDispatcher : IThreadDispatcher, IDisposable
{
    private readonly Thread[] _workers;
    private readonly AutoResetEvent[] _workerSignals;
    private readonly CountdownEvent _finishedSignal;
    private Action<int>? _currentWorkerBody;
    private volatile bool _disposed;

    public int ThreadCount { get; }
    
    
    
    
    
    public SimpleThreadDispatcher(int threadCount)
    {
        if (threadCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(threadCount), "ThreadCount muss mindestens 1 sein.");

        ThreadCount = threadCount;
        int backgroundCount = threadCount - 1;

        _workers = new Thread[backgroundCount];
        _workerSignals = new AutoResetEvent[backgroundCount];
        _finishedSignal = new CountdownEvent(backgroundCount);

        for (int i = 0; i < backgroundCount; i++)
        {
            int workerIndex = i + 1; // Worker 0 ist immer der aufrufende Main-Thread!
            var signal = new AutoResetEvent(false);
            _workerSignals[i] = signal;

            var thread = new Thread(() => WorkerLoop(workerIndex, signal))
            {
                IsBackground = true,
                Name = $"RenderWorker_{workerIndex}"
            };
            _workers[i] = thread;
            thread.Start();
        }
    }

    private void WorkerLoop(int workerIndex, AutoResetEvent signal)
    {
        while (true)
        {
            signal.WaitOne();
            if (_disposed)
                return;

            _currentWorkerBody?.Invoke(workerIndex);
            _finishedSignal.Signal();
        }
    }

    public void DispatchWorkers(Action<int> workerBody)
    {
        if (ThreadCount == 1)
        {
            workerBody(0);
            return;
        }

        _currentWorkerBody = workerBody;
        _finishedSignal.Reset(_workers.Length);

        // Hintergrund-Worker 1..N aufwecken
        for (int i = 0; i < _workerSignals.Length; i++)
        {
            _workerSignals[i].Set();
        }

        // Main-Thread arbeitet selbst als Worker 0 mit (kein verschwendeter Kern!)
        workerBody(0);

        // Warten, bis alle Hintergrund-Worker fertig sind
        _finishedSignal.Wait();
        _currentWorkerBody = null;
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        for (int i = 0; i < _workerSignals.Length; i++)
        {
            _workerSignals[i].Set();
        }
        for (int i = 0; i < _workers.Length; i++)
        {
            _workers[i].Join();
            _workerSignals[i].Dispose();
        }
        _finishedSignal.Dispose();
    }
}