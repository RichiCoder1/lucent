namespace Lucent.ComponentBrowser;

public static partial class Components
{
    public static string ComponentLabel(ComponentCatalogItem item) =>
        item.Title + ", " + item.Family;
}
