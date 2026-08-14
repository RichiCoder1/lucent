namespace Lucent.Compiler.CodeGeneration;

internal sealed record BoundCounterModel(
    string NamespaceName,
    string ComponentName,
    string StateName,
    int InitialValue,
    string? RootClass,
    string CountTextExpression,
    string? CountTextClass,
    string ButtonTextLiteral,
    string? ButtonClass,
    int Increment);
