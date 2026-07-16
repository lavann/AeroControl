namespace AeroControl.Services;

internal sealed class AsyncCloseGate
{
    private readonly object _gate = new();
    private Task<bool>? _activeOperation;

    public bool TryBegin(
        Func<Task<bool>> operationFactory,
        out Task<bool> operation)
    {
        lock (_gate)
        {
            if (_activeOperation is not null)
            {
                operation = _activeOperation;
                return false;
            }

            operation = operationFactory();
            _activeOperation = operation;
            return true;
        }
    }

    public void Reset(Task<bool> operation)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeOperation, operation))
            {
                _activeOperation = null;
            }
        }
    }
}
