namespace SylvanBot.Application.Common;

public sealed class Option<T>
{
    private Option(object value)
    {
        Value = value;
    }

    public object Value { get; }

    public static implicit operator Option<T>(None value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Option<T>(value);
    }

    public static implicit operator Option<T>(Some<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Option<T>(value);
    }
}