namespace Intune.Commander.Core.Models;

/// <summary>
/// Authentication methods supported by Intune Commander.
/// Certificate and ManagedIdentity are intentionally deferred;
/// add them here only when the credential code path is implemented.
/// </summary>
public enum AuthMethod
{
    Interactive,
    ClientSecret,
    DeviceCode
}
