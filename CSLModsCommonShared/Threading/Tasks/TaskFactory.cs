using System;

namespace CSLModsCommon.Threading.Tasks;

public sealed class TaskFactory {
    public TaskFactory() { }

    public Task StartNew(Action action) {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var task = new Task(action);
        task.Start();
        return task;
    }

    public Task StartNew(Action<object> action, object state) {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var task = new Task(action, state);
        task.Start();
        return task;
    }

    public Task<TResult> StartNew<TResult>(Func<TResult> function) {
        if (function == null)
            throw new ArgumentNullException(nameof(function));

        var task = new Task<TResult>(function);
        task.Start();
        return task;
    }

    public Task<TResult> StartNew<TResult>(Func<object, TResult> function, object state) {
        if (function == null)
            throw new ArgumentNullException(nameof(function));

        var task = new Task<TResult>(function, state);
        task.Start();
        return task;
    }
}