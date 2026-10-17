namespace Meridian.Core.TimeVault;

/// <summary>Read-only, cancellable discovery of snapshots accessible to the current user.</summary>
public interface IShadowCopyCatalog
{
    Task<IReadOnlyList<ShadowCopy>> ListAsync(CancellationToken cancellationToken = default);
}
