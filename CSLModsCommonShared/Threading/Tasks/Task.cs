using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CSLModsCommon.Threading.Tasks; 
public class Task : IAsyncResult, IDisposable {
    internal const int TaskStateStarted = 0x10000;
    internal const int TaskStateDelegateInvoked = 0x20000;
    internal const int TaskStateDisposed = 0x40000;
    internal const int TaskStateFaulted = 0x200000;
    internal const int TaskStateCanceled = 0x400000;
    internal const int TaskStateRanToCompletion = 0x1000000;
    internal const int TaskStateWaitingForActivation = 0x2000000;
    private const int TaskStateCompletedMask = TaskStateCanceled | TaskStateFaulted | TaskStateRanToCompletion;

    private static Task _completedTask;
    private static int _taskIdCounter;

    protected readonly Delegate _action;
    protected readonly object _stateObject;
    protected readonly ManualResetEvent _taskCompletedEvent;
    protected readonly List<Exception> _exceptions = new();
    protected readonly Task _continueSource;
    private Action _waitCallback;
    private bool _runSync;
    private int _taskId;
    private volatile int _stateFlags;

    #region Properties

    public static Task CompletedTask {
        get {
            var completedTask = _completedTask;
            if (completedTask == null)
                _completedTask = completedTask = new Task((Exception)null);
            return completedTask;
        }
    }

    public static TaskFactory Factory { get; } = new();

    public AggregateException Exception => _exceptions.Count > 0 ? new AggregateException(_exceptions) : null;

    public int Id {
        get {
            if (_taskId == 0) Interlocked.CompareExchange(ref _taskId, NewId(), 0);

            return _taskId;
        }
    }

    public bool IsCompleted {
        get {
            var stateFlags = _stateFlags;
            return IsCompletedMethod(stateFlags);
        }
    }

    public bool IsFaulted => _exceptions.Count > 0;

    public TaskStatus Status {
        get {
            TaskStatus status;
            var sf = _stateFlags;

            if ((sf & TaskStateFaulted) != 0)
                status = TaskStatus.Faulted;
            else if ((sf & TaskStateCanceled) != 0)
                status = TaskStatus.Canceled;
            else if ((sf & TaskStateRanToCompletion) != 0)
                status = TaskStatus.RanToCompletion;
            else if ((sf & TaskStateDelegateInvoked) != 0)
                status = TaskStatus.Running;
            else if ((sf & TaskStateStarted) != 0)
                status = TaskStatus.WaitingToRun;
            else if ((sf & TaskStateWaitingForActivation) != 0)
                status = TaskStatus.WaitingForActivation;
            else status = TaskStatus.Created;

            return status;
        }
    }

    #endregion

    #region Constructors and Destructor

    protected Task() {
        _stateFlags = 0;
        _taskCompletedEvent = new ManualResetEvent(false);
    }

    protected Task(Exception ex) {
        _stateFlags = TaskStateStarted | TaskStateRanToCompletion;
        _taskCompletedEvent = new ManualResetEvent(true);

        if (ex != null)
            _exceptions.Add(ex);
    }

    protected Task(Delegate action, object state, Task continueSource) {
        _stateFlags = 0;
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _stateObject = state;
        _taskCompletedEvent = new ManualResetEvent(false);
        _continueSource = continueSource;
    }

    public Task(Action action)
        : this(action, null, null) { }

    public Task(Action<object> action, object state)
        : this(action, state, null) { }

    ~Task() {
        Dispose(false);
    }

    #endregion

    #region Helper methods

    private bool AtomicStateUpdate(int newBits, int illegalBits) {
        var sw = new SpinWait();
        do {
            var oldFlags = _stateFlags;
            if ((oldFlags & illegalBits) != 0) return false;
            if (Interlocked.CompareExchange(ref _stateFlags, oldFlags | newBits, oldFlags) == oldFlags) return true;

            sw.SpinOnce();
        } while (true);
    }

    private bool MarkStarted() => AtomicStateUpdate(TaskStateStarted, TaskStateCanceled | TaskStateStarted);

    private static int NewId() {
        int newId;
        do {
            newId = Interlocked.Increment(ref _taskIdCounter);
        } while (newId == 0);

        return newId;
    }

    protected void EnsureStartOnce() {
        if (!MarkStarted()) throw new InvalidOperationException("Trying to start Task more than once");
    }

    private static bool IsCompletedMethod(int flags) => (flags & TaskStateCompletedMask) != 0;

    internal bool IsRanToCompletion => (_stateFlags & TaskStateCompletedMask) == TaskStateRanToCompletion;

    #endregion

    #region Start method

    public void Start() {
        EnsureStartOnce();
        _runSync = false;

        AtomicStateUpdate(TaskStateWaitingForActivation, TaskStateCompletedMask);
        if (_continueSource == null) {
            if (!ThreadPool.QueueUserWorkItem(TaskStartAction))
                throw new NotSupportedException("Could not enqueue task for execution");
        }
        else {
            AsyncCallback internalCallback = ar => {
                _continueSource.InternalEndWait(ar);
                TaskStartAction(ar.AsyncState);
            };
            _continueSource.BeginWait(false, internalCallback, null);
        }
    }

    #endregion

    #region Task thread execution

    protected void TaskStartAction(object stateObject) {
        try {
            AtomicStateUpdate(TaskStateDelegateInvoked, TaskStateDelegateInvoked);

            ExecuteTaskAction();
        }
        catch (AggregateException ex) {
            AtomicStateUpdate(TaskStateFaulted, TaskStateCompletedMask);
            _exceptions.AddRange(ex.InnerExceptions);
        }
        catch (Exception ex) {
            AtomicStateUpdate(TaskStateFaulted, TaskStateCompletedMask);
            _exceptions.Add(ex);
        }
        finally {
            var isActionLocked = Monitor.TryEnter(_action);
            if (isActionLocked && _waitCallback != null)
                _waitCallback();

            AtomicStateUpdate(TaskStateRanToCompletion, TaskStateCompletedMask);
            _taskCompletedEvent.Set();

            if (isActionLocked)
                Monitor.Exit(_action);
        }
    }

    protected virtual void ExecuteTaskAction() {
        switch (_action) {
            case Action action1:
                action1();
                break;
            case Action<object> action2:
                action2(_stateObject);
                break;
            case Action<Task> action3:
                action3(_continueSource);
                break;
            case Action<Task, object> action:
                action(_continueSource, _stateObject);
                break;
            default:
                throw new InvalidOperationException("Unexpected action type");
        }
    }

    #endregion

    #region Synchronous

    public void RunSynchronously() {
        EnsureStartOnce();

        _runSync = true;
        TaskStartAction(null);
    }

    #endregion

    #region Wait methods

    public void Wait() {
        _taskCompletedEvent.WaitOne();

        if (_exceptions.Count > 0)
            throw Exception;
    }

    public bool Wait(TimeSpan timeout) {
        var totalMilliseconds = (long)timeout.TotalMilliseconds;
        if (totalMilliseconds is < -1 or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(timeout));

        var success = _taskCompletedEvent.WaitOne((int)totalMilliseconds, false);

        return _exceptions.Count > 0 ? throw Exception : success;
    }

    public bool Wait(int millisecondsTimeout) {
        if (millisecondsTimeout < -1) throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));

        var success = _taskCompletedEvent.WaitOne(millisecondsTimeout, false);

        return _exceptions.Count > 0 ? throw Exception : success;
    }

    public IAsyncResult BeginWait(AsyncCallback callback, object stateObject) => BeginWait(true, callback, stateObject);

    public IAsyncResult BeginWait(bool continueOnCapturedContext, AsyncCallback callback, object stateObject) {
        SynchronizationContext syncContext = null;
        if (continueOnCapturedContext)
            syncContext = SynchronizationContext.Current;

        var ar = new WaitAsyncResult(stateObject);
        var waitCallback = () => {
            // Always true because lacks timeout
            ar.Result = true;
            ar.EventHandler.Set();

            if (callback == null) return;
            if (syncContext == null)
                callback(ar);
            else
                syncContext.Send(s => { callback((IAsyncResult)s); }, ar);
        };

        bool isTaskCompleted;
        lock (_action) {
            isTaskCompleted = _taskCompletedEvent.WaitOne(0, false);
            if (!isTaskCompleted)
                // Enqueue to execute when Task is finalizing
                _waitCallback = waitCallback;
        }

        if (isTaskCompleted)
            // Execute callback synchrounously
            waitCallback();

        return ar;
    }

    public IAsyncResult BeginWait(TimeSpan timeout, AsyncCallback callback, object stateObject) => BeginWait(timeout, true, callback, stateObject);

    public IAsyncResult BeginWait(TimeSpan timeout, bool continueOnCapturedContext, AsyncCallback callback, object stateObject) {
        var totalMilliseconds = (long)timeout.TotalMilliseconds;
        return totalMilliseconds is < -1 or > int.MaxValue ? throw new ArgumentOutOfRangeException(nameof(timeout)) : BeginWait((int)totalMilliseconds, continueOnCapturedContext, callback, stateObject);
    }

    public IAsyncResult BeginWait(int millisecondsTimeout, AsyncCallback callback, object stateObject) => BeginWait(millisecondsTimeout, true, callback, stateObject);

    public IAsyncResult BeginWait(int millisecondsTimeout, bool continueOnCapturedContext, AsyncCallback callback, object stateObject) {
        if (millisecondsTimeout < -1)
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));

        SynchronizationContext syncContext = null;
        if (continueOnCapturedContext)
            syncContext = SynchronizationContext.Current;

        var ar = new WaitAsyncResult(stateObject);
        WaitCallback internalCallback = state => {
            // Can be called synchronously or asynchronously
            var needWaitSignal = (bool)state;
            if (needWaitSignal)
                ar.Result = _taskCompletedEvent.WaitOne(millisecondsTimeout, false);
            else
                ar.Result = true;

            ar.EventHandler.Set();

            if (callback != null) {
                if (syncContext == null)
                    callback(ar);
                else
                    syncContext.Send(s => { callback((IAsyncResult)s); }, ar);
            }
        };

        bool isTaskCompleted;
        lock (_action) {
            isTaskCompleted = _taskCompletedEvent.WaitOne(0, false);
            if (!isTaskCompleted)
                // Enqueue callback for futher execution
                ThreadPool.QueueUserWorkItem(internalCallback, true);
        }

        if (isTaskCompleted)
            // Call callback synchronously
            internalCallback(false);

        return ar;
    }

    public bool EndWait(IAsyncResult asyncResult) {
        if (asyncResult == null)
            throw new ArgumentNullException(nameof(asyncResult));

        if (asyncResult is not WaitAsyncResult)
            throw new ArgumentException("asyncResult was not returned by a call to the BeginWait method", nameof(asyncResult));

        var result = InternalEndWait(asyncResult);

        return _exceptions.Count > 0 ? throw Exception : result;
    }

    // Avoid checking and exceptions
    private bool InternalEndWait(IAsyncResult asyncResult) {
        var waitHandle = (WaitAsyncResult)asyncResult;
        if (!waitHandle.EventHandler.WaitOne())
            throw new InvalidOperationException("Error waiting for wait handle signal");
        waitHandle.Dispose();

        return waitHandle.Result;
    }

    #endregion

    #region Dispose

    public virtual void Dispose() => Dispose(true);

    private void Dispose(bool disposing) {
        if (disposing)
            if (!IsCompleted)
                throw new InvalidOperationException(
                    "A task may only be disposed if it has completed its execution");

        _taskCompletedEvent?.Close();
        AtomicStateUpdate(TaskStateDisposed, 0);
    }

    #endregion

    #region IAsyncResult Members

    public object AsyncState => _stateObject;

    WaitHandle IAsyncResult.AsyncWaitHandle => (_stateFlags & TaskStateDisposed) != 0 ? throw new ObjectDisposedException("Task") : _taskCompletedEvent;

    bool IAsyncResult.CompletedSynchronously => _runSync;

    #endregion

    #region IAsyncResult Objects

    private class WaitAsyncResult : IAsyncResult, IDisposable {
        public WaitAsyncResult(object stateObject) {
            EventHandler = new ManualResetEvent(false);
            AsyncState = stateObject;
        }

        public object AsyncState { get; }

        public WaitHandle AsyncWaitHandle => EventHandler;

        public bool CompletedSynchronously { get; set; }

        public ManualResetEvent EventHandler { get; }

        public bool IsCompleted => EventHandler.WaitOne(0, false);

        public bool Result { get; set; }

        public void Dispose() => EventHandler?.Close();
    }

    #endregion

    #region Continuation Methods

    private Task InternalContinueWith(Delegate continuationAction, object state) {
        if (continuationAction == null)
            throw new ArgumentNullException(nameof(continuationAction));

        var continueTask = new Task(continuationAction, state, this);
        continueTask.Start();
        return continueTask;
    }

    public Task ContinueWith(Action<Task> continuationAction) => InternalContinueWith(continuationAction, null);

    public Task ContinueWith(Action<Task, object> continuationAction, object state) => InternalContinueWith(continuationAction, state);

    private Task<TResult> InternalContinueWith<TResult>(Delegate continuationFunction, object state) {
        if (continuationFunction == null)
            throw new ArgumentNullException(nameof(continuationFunction));

        var continueTask = new Task<TResult>(continuationFunction, state, this);
        continueTask.Start();
        return continueTask;
    }

    public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction) => InternalContinueWith<TResult>(continuationFunction, null);

    public Task<TResult> ContinueWith<TResult>(Func<Task, object, TResult> continuationFunction, object state) => InternalContinueWith<TResult>(continuationFunction, state);

    #endregion

    #region Static Wait methods

    public static void WaitAll(params Task[] tasks) => WaitAll(tasks, Timeout.Infinite);

    public static bool WaitAll(Task[] tasks, TimeSpan timeout) {
        var totalMilliseconds = (long)timeout.TotalMilliseconds;
        return totalMilliseconds is < -1 or > int.MaxValue ? throw new ArgumentOutOfRangeException(nameof(timeout)) : WaitAll(tasks, (int)totalMilliseconds);
    }

    public static bool WaitAll(Task[] tasks, int millisecondsTimeout) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));

        if (millisecondsTimeout < -1)
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));

        var exceptions = new List<Exception>();
        var waitDone = new ManualResetEvent(false);
        var completedCounter = 0;
        var totalSum = tasks.Length;

        foreach (var task in tasks)
            task.BeginWait(ar => {
                try {
                    task.EndWait(ar);
                }
                catch (Exception ex) {
                    exceptions.Add(ex);
                }

                var val = Interlocked.Increment(ref completedCounter);
                if (val >= totalSum)
                    waitDone.Set();
            }, null);

        var done = waitDone.WaitOne(millisecondsTimeout, false);
        waitDone.Close();

        return exceptions.Count > 0 ? throw new AggregateException(exceptions).Flatten() : done;
    }


    public static int WaitAny(params Task[] tasks) => WaitAny(tasks, Timeout.Infinite);

    public static int WaitAny(Task[] tasks, TimeSpan timeout) {
        var totalMilliseconds = (long)timeout.TotalMilliseconds;
        return totalMilliseconds is < -1 or > int.MaxValue ? throw new ArgumentOutOfRangeException(nameof(timeout)) : WaitAny(tasks, (int)totalMilliseconds);
    }

    public static int WaitAny(Task[] tasks, int millisecondsTimeout) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));

        if (millisecondsTimeout < -1)
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));

        var exceptions = new List<Exception>();
        var waitDone = new ManualResetEvent(false);
        var completedIndex = -1;

        for (var i = 0; i < tasks.Length; i++) {
            var i1 = i;
            tasks[i]
                .BeginWait(ar => {
                    try {
                        tasks[i1].EndWait(ar);
                    }
                    catch (Exception ex) {
                        exceptions.Add(ex);
                    }

                    completedIndex = i1;
                    waitDone.Set();
                }, null);

            if (waitDone.WaitOne(0, false))
                break;
        }

        var done = waitDone.WaitOne(millisecondsTimeout, false);
        waitDone.Close();

        if (exceptions.Count > 0)
            throw new AggregateException(exceptions).Flatten();

        if (done)
            return completedIndex;
        return -1;
    }

    #endregion

    #region FromResult / FromException

    public static Task<TResult> FromResult<TResult>(TResult result) => new Task<TResult>(result, null);

    public static Task FromException(Exception exception) => exception == null ? throw new ArgumentNullException(nameof(exception)) : new Task(exception);

    public static Task<TResult> FromException<TResult>(Exception exception) => exception == null ? throw new ArgumentNullException(nameof(exception)) : new Task<TResult>(default, exception);

    #endregion

    #region Run methods

    public static Task Run(Action action) => Factory.StartNew(action);

    public static Task<TResult> Run<TResult>(Func<TResult> function) => Factory.StartNew(function);

    public static Task Run(Func<Task> function) => Factory.StartNew(() => { function().Wait(); });

    public static Task<TResult> Run<TResult>(Func<Task<TResult>> function) => Factory.StartNew(() => {
        var task = function();
        task.Wait();
        return task.Result;
    });

    #endregion

    #region Delay methods

    public static Task Delay(TimeSpan delay) {
        var totalMilliseconds = (long)delay.TotalMilliseconds;
        return totalMilliseconds is < -1 or > int.MaxValue ? throw new ArgumentOutOfRangeException(nameof(delay)) : Delay((int)totalMilliseconds);
    }

    public static Task Delay(int millisecondsDelay) => millisecondsDelay switch {
        < -1 => throw new ArgumentOutOfRangeException(nameof(millisecondsDelay)),
        0 => new Task((Exception)null),
        _ => Run(() => Thread.Sleep(millisecondsDelay))
    };

    #endregion

    #region WhenAll

    public static Task WhenAll(IEnumerable<Task> tasks) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));

        // Take a more efficient path if tasks is actually an array
        var taskArray = tasks as Task[];
        return WhenAll(taskArray ?? tasks.ToArray());
    }

    public static Task WhenAll(params Task[] tasks) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));

        foreach (var task in tasks)
            if (task == null)
                throw new ArgumentException("One task from provided task array is null");

        if (tasks.Length == 0)
            return new Task((Exception)null);

        var resultTask = new Task(() => {
            var exceptions = new List<Exception>();
            foreach (var task in tasks)
                try {
                    task.Wait();
                }
                catch (Exception e) {
                    exceptions.Add(e);
                }

            if (exceptions.Any())
                throw new AggregateException(exceptions.ToArray());
        });
        resultTask.Start();
        return resultTask;
    }

    public static Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));

        var taskArray = tasks as Task<TResult>[];
        return WhenAll(taskArray ?? tasks.ToArray());
    }

    public static Task<TResult[]> WhenAll<TResult>(params Task<TResult>[] tasks) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));

        foreach (var task in tasks)
            if (task == null)
                throw new ArgumentException("One task from provided task array is null");

        if (tasks.Length == 0)
            return new Task<TResult[]>(new TResult[0], null);

        var resultTask = new Task<TResult[]>(() => {
            var exceptions = new List<Exception>();
            var results = new List<TResult>(tasks.Length);
            foreach (var task in tasks)
                try {
                    task.Wait();
                    results.Add(task.Result);
                }
                catch (Exception e) {
                    exceptions.Add(e);
                    results.Add(default);
                }

            return exceptions.Any() ? throw new AggregateException(exceptions.ToArray()) : results.ToArray();
        });
        resultTask.Start();
        return resultTask;
    }

    #endregion

    #region WhenAny

    public static Task<Task> WhenAny(IEnumerable<Task> tasks) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));

        var taskArray = tasks as Task[];
        return WhenAny(taskArray ?? tasks.ToArray());
    }

    public static Task<Task> WhenAny(params Task[] tasks) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));
        if (tasks.Length == 0)
            throw new ArgumentException("At least one task is required to wait for completion");

        foreach (var task in tasks)
            if (task == null)
                throw new ArgumentException("One task from provided task array is null");

        var resultTask = new Task<Task>(() => {
            while (true) {
                foreach (var task in tasks)
                    try {
                        if (task.Wait(0))
                            return task;
                    }
                    catch (Exception) {
                        return task;
                    }

                Thread.Sleep(1);
            }
        });
        resultTask.Start();
        return resultTask;
    }

    public static Task<Task<TResult>> WhenAny<TResult>(IEnumerable<Task<TResult>> tasks) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));

        // Take a more efficient path if tasks is actually an array
        var taskArray = tasks as Task<TResult>[];
        return WhenAny(taskArray ?? tasks.ToArray());
    }

    public static Task<Task<TResult>> WhenAny<TResult>(params Task<TResult>[] tasks) {
        if (tasks == null)
            throw new ArgumentNullException(nameof(tasks));
        if (tasks.Length == 0)
            throw new ArgumentException("At least one task is required to wait for completion");

        foreach (var task in tasks)
            if (task == null)
                throw new ArgumentException("One task from provided task array is null");

        var resultTask = new Task<Task<TResult>>(() => {
            while (true) {
                foreach (var task in tasks)
                    try {
                        if (task.Wait(0))
                            return task;
                    }
                    catch (Exception) {
                        return task;
                    }

                Thread.Sleep(1);
            }
        });
        resultTask.Start();
        return resultTask;
    }

    #endregion
}

public class Task<TResult> : Task {
    private TResult _result;

    #region Property

    public TResult Result {
        get {
            _taskCompletedEvent.WaitOne();

            return _exceptions.Count > 0 ? throw Exception : _result;
        }
    }

    #endregion

    #region Constructors

    internal Task(TResult result, Exception ex)
        : base(ex) => _result = result;

    public Task(Func<TResult> function)
        : base(function, null, null) { }

    public Task(Func<object, TResult> function, object state)
        : base(function, state, null) { }

    internal Task(Delegate function, object state, Task continueSource)
        : base(function, state, continueSource) { }

    #endregion

    #region Task thread execution

    protected override void ExecuteTaskAction() => _result = _action switch {
        Func<TResult> func => func(),
        Func<object, TResult> work => work(_stateObject),
        Func<Task, TResult> action => action(_continueSource),
        Func<Task, object, TResult> userWork => userWork(_continueSource, _stateObject),
        _ => throw new InvalidOperationException("Unexpected action type")
    };

    #endregion
}