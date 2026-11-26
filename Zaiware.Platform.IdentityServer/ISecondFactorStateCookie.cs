using System.Diagnostics.CodeAnalysis;
using Duende.UserManagement;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Stores the interim authenticated subject id between the password step and the
/// second-factor step.
/// </summary>
public interface ISecondFactorStateCookie
{
    /// <summary>Writes the encrypted subject id for the pending second-factor step.</summary>
    void Write(UserSubjectId subjectId);

    /// <summary>
    /// Reads and decrypts the pending subject id. Returns <see langword="false"/> for a
    /// missing, stale, tampered, or malformed cookie.
    /// </summary>
    bool TryRead([NotNullWhen(true)] out UserSubjectId? subjectId);

    /// <summary>Deletes the pending second-factor state.</summary>
    void Clear();
}
