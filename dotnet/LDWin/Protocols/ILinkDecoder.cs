using LDWin.Models;

namespace LDWin.Protocols;

/// <summary>
/// Decodes a single captured layer-2 frame into <see cref="LinkData"/>.
/// Implementations are protocol-specific (one for CDP, one for LLDP). The
/// capture engine offers each received frame to every decoder until one
/// reports a successful decode.
/// </summary>
public interface ILinkDecoder
{
    /// <summary>"CDP" or "LLDP".</summary>
    string Protocol { get; }

    /// <summary>
    /// Attempt to decode <paramref name="frame"/> (a full Ethernet frame).
    /// On success, returns true and sets <paramref name="data"/> with the
    /// neighbour fields populated (Protocol set to <see cref="Protocol"/>).
    /// </summary>
    bool TryDecode(byte[] frame, out LinkData? data);
}
