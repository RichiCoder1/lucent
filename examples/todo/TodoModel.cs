namespace Lucent.Examples.Todo;

internal enum TodoFilter
{
    All,
    Active,
    Completed,
}

internal sealed record TodoItem(
    int Id,
    string Title,
    bool Completed);
