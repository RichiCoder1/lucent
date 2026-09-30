using Lucent.Core;

namespace Lucent.Core.Tests;

internal sealed class StockImagePreparer : IImagePreparer
{
    public ValueTask<PreparedImage> PrepareAsync(
        ImagePreparationRequest request,
        CancellationToken token
    ) => ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, new byte[] { 0, 0, 0, 255 }));
}
