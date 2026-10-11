namespace RatBot.Application.Common.Extensions;

public static class ErrorOrExtensions
{
    public static async Task<ErrorOr<T>> ToErrorOr<T>(this Task<T?> task, Error error)
        where T : class
    {
        T? value = await task;

        return value is not null ? value : error;
    }

    extension<T>(ErrorOr<T> source)
    {
        public ErrorOr<TResult> Select<TResult>(Func<T, TResult> selector)
        {
            return source.IsError ? source.Errors : selector(source.Value);
        }

        public ErrorOr<TResult> SelectMany<TInner, TResult>(Func<T, ErrorOr<TInner>> bind,
            Func<T, TInner, TResult> project)
        {
            if (source.IsError)
                return source.Errors;

            ErrorOr<TInner> inner = bind(source.Value);

            return inner.IsError ? inner.Errors : project(source.Value, inner.Value);
        }

        public async Task<ErrorOr<TResult>> SelectMany<TInner, TResult>(Func<T, Task<ErrorOr<TInner>>> bind,
            Func<T, TInner, TResult> project)
        {
            if (source.IsError)
                return source.Errors;

            ErrorOr<TInner> inner = await bind(source.Value).ConfigureAwait(false);

            return inner.IsError ? inner.Errors : project(source.Value, inner.Value);
        }
    }

    extension<T>(Task<ErrorOr<T>> sourceTask)
    {
        public async Task<ErrorOr<TResult>> Select<TResult>(Func<T, TResult> selector)
        {
            ErrorOr<T> source = await sourceTask.ConfigureAwait(false);

            return source.IsError ? source.Errors : selector(source.Value);
        }

        public async Task<ErrorOr<TResult>> SelectMany<TInner, TResult>(Func<T, ErrorOr<TInner>> bind,
            Func<T, TInner, TResult> project)
        {
            ErrorOr<T> source = await sourceTask.ConfigureAwait(false);

            if (source.IsError)
                return source.Errors;

            ErrorOr<TInner> inner = bind(source.Value);

            return inner.IsError ? inner.Errors : project(source.Value, inner.Value);
        }

        public async Task<ErrorOr<TResult>> SelectMany<TInner, TResult>(Func<T, Task<ErrorOr<TInner>>> bind,
            Func<T, TInner, TResult> project)
        {
            ErrorOr<T> source = await sourceTask.ConfigureAwait(false);

            if (source.IsError)
                return source.Errors;

            ErrorOr<TInner> inner = await bind(source.Value).ConfigureAwait(false);

            return inner.IsError ? inner.Errors : project(source.Value, inner.Value);
        }
    }
}