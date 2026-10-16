namespace RatBot.Common;

public record None;
public record Some<T>(T Value);

public union Option<T>(None, Some<T>);
