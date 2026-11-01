namespace PocketCsvReader.Json;

// Transport-independent trivia state, retained across input chunks.
internal struct JsonCommentScanner
{
    private CommentState _state;
    private long _start;

    public bool TryConsume(char current, long position)
    {
        switch (_state)
        {
            case CommentState.None:
                if (current != '/') return false;
                _start = position;
                _state = CommentState.Slash;
                break;
            case CommentState.Slash:
                _state = current switch
                {
                    '/' => CommentState.Line,
                    '*' => CommentState.Block,
                    _ => throw new InvalidDataException($"Invalid JSON comment at position {_start}.")
                };
                break;
            case CommentState.Line:
                if (current is '\r' or '\n') _state = CommentState.None;
                break;
            case CommentState.Block:
                if (current == '*') _state = CommentState.Star;
                break;
            case CommentState.Star:
                _state = current == '/' ? CommentState.None
                    : current == '*' ? CommentState.Star : CommentState.Block;
                break;
        }
        return true;
    }

    public readonly void CompleteAtEof()
    {
        if (_state is CommentState.Slash or CommentState.Block or CommentState.Star)
            throw new InvalidDataException($"Incomplete JSON comment at position {_start}.");
    }

    private enum CommentState { None,
        Slash,
        Line,
        Block,
        Star }
}
