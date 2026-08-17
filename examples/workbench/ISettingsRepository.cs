namespace Lucent.Examples.Workbench;

internal interface ISettingsRepository
{
    Task<WorkbenchSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(WorkbenchSettings value, CancellationToken cancellationToken);
}
