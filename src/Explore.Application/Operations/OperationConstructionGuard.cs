namespace Explore.Application.Operations;

/// <summary>Scope-local protection against synchronous native reentry through opaque DI aliases.</summary>
internal sealed class OperationConstructionGuard
{
    private readonly HashSet<Type> _constructing = [];

    internal void Enter(Type contract)
    {
        lock (_constructing)
        {
            if (!_constructing.Add(contract))
                throw OperationCompositionValidation.Error("construction reentry", contract);
        }
    }

    internal void Exit(Type contract)
    {
        lock (_constructing)
            _constructing.Remove(contract);
    }
}
