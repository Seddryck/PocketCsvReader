namespace PocketCsvReader;

// A file batch shares this state across all of its input readers.
internal sealed class RecordWindow(int skip, int? take)
{
    private int _skipRemaining = skip;
    private int? _takeRemaining = take;

    internal static RecordWindow? Create(IProfile profile)
        => profile.Skip != 0 || profile.Take.HasValue ? new(profile.Skip, profile.Take) : null;

    internal bool IsComplete => _takeRemaining == 0;

    internal bool Accept()
    {
        if (_skipRemaining > 0)
        {
            _skipRemaining--;
            return false;
        }

        _takeRemaining--;
        return true;
    }
}
