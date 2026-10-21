using System.Text;
using Fin.Copy;

namespace Fin;

/// <summary>
/// Reads server-sent event lines one at a time. A blank line ends an event; comment lines (": ping") are skipped; several
/// <c>data:</c> lines join with a newline; an event without a name is a "message"; other fields (id, retry) are ignored.
/// </summary>
internal sealed class FinSseParser
{
    private string? _eventName;
    private readonly StringBuilder _data = new();
    private bool _hasData;

    /// <summary>Adds one line (without its line break). Returns the event the line completed, if any.</summary>
    public FinStreamEvent? Add(string line)
    {
        if (line.Length == 0) return Flush();
        if (line[0] == ':') return null;
        var separator = line.IndexOf(':');
        var field = separator < 0 ? line : line.Substring(0, separator);
        var value = separator < 0 ? "" : line.Substring(separator + 1);
        if (value.Length > 0 && value[0] == ' ') value = value.Substring(1);
        if (field == "event") _eventName = value;
        else if (field == "data")
        {
            if (_hasData) _data.Append('\n');
            _data.Append(value);
            _hasData = true;
        }
        return null;
    }

    /// <summary>Ends the stream: an event still being read is returned when it carries data.</summary>
    public FinStreamEvent? Complete() => _hasData ? Flush() : null;

    private FinStreamEvent? Flush()
    {
        // As in the EventSource standard, an event with no data line is not delivered.
        var item = _hasData ? new FinStreamEvent(_eventName ?? "message", _data.ToString()) : null;
        _eventName = null;
        _data.Clear();
        _hasData = false;
        return item;
    }
}
