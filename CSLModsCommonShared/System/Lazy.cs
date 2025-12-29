using CSLModsCommon.Threading;

namespace System;

public sealed class Lazy<T> {
    private Func<T> _factory;
    private T _value;
    private readonly object _lock = new();
    private readonly LazyThreadSafetyMode _mode;
    private Exception _exception;

    public Lazy() : this(CreateDefaultConstructor()) { }

    public Lazy(Func<T> valueFactory, LazyThreadSafetyMode mode = LazyThreadSafetyMode.ExecutionAndPublication) {
        _factory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        _mode = mode;
    }

    public bool IsValueCreated { get; private set; }

    public T Value => IsValueCreated ? _value : CreateValue();

    private static Func<T> CreateDefaultConstructor() =>
        () => {
            var ctor = typeof(T).GetConstructor(Type.EmptyTypes);
            if (ctor == null) throw new MissingMethodException("Type " + typeof(T).FullName + " does not have a parameterless constructor.");
            return (T)ctor.Invoke(null);
        };

    private T CreateValue() => _mode switch {
        LazyThreadSafetyMode.None => CreateValueNone(),
        LazyThreadSafetyMode.PublicationOnly => CreateValuePublicationOnly(),
        _ => CreateValueExecutionAndPublication()
    };

    private T CreateValueNone() {
        if (IsValueCreated) return _value;
        if (_exception != null) throw _exception;
        try {
            var v = _factory();
            _value = v;
            IsValueCreated = true;
            _factory = null;
            return v;
        }
        catch (Exception ex) {
            _exception = ex;
            throw;
        }
    }

    private T CreateValueExecutionAndPublication() {
        lock (_lock) {
            if (IsValueCreated) return _value;
            if (_exception != null) throw _exception;
            try {
                var v = _factory();
                _value = v;
                IsValueCreated = true;
                _factory = null;
                return v;
            }
            catch (Exception ex) {
                _exception = ex;
                throw;
            }
        }
    }

    private T CreateValuePublicationOnly() {
        if (IsValueCreated) return _value;
        T v;
        try {
            v = _factory();
        }
        catch (Exception ex) {
            _exception = ex;
            throw;
        }

        lock (_lock) {
            if (!IsValueCreated) {
                _value = v;
                IsValueCreated = true;
                _factory = null;
            }

            return _value;
        }
    }
}