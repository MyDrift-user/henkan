namespace Henkan.Core.Conversion;

/// <summary>
/// Keeps two jobs running at the same time from settling on the same output name.
/// </summary>
/// <remarks>
/// Two files converted together can render the same output path: picture.png and
/// picture.docx both become picture.zip. Each job resolves its own name before
/// either has written anything, so both see a free name and race for it. One wins
/// and the other fails on a file that appeared underneath it.
/// </remarks>
public interface IOutputReservation
{
    /// <summary>True when another job has already claimed this path.</summary>
    bool IsReserved(string path);

    /// <summary>Claims a path for the caller until it releases it.</summary>
    void Reserve(string path);

    /// <summary>Gives a claimed path back, whether or not it was used.</summary>
    void Release(string path);
}
